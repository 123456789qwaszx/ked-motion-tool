using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;

namespace Mt.Authoring.Tests;

public class MotionEditorGraphTests
{
    private static readonly CurveRef Y = new("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);

    [Fact]
    public void 여러_성분을_한_번에_바꾸면_되돌리기도_한_번이다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        var z = new CurveRef("CharacterPortrait_SwayPivot", MotionChannelKind.Euler, Axis.Z);
        long before = editor.Revision;

        editor.SetKeysMany("bow", new[]
        {
            (z, (IEnumerable<KeyAsset>)new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.4f, 20f) }),
            (new CurveRef("CharacterPortrait_SwayPivot", MotionChannelKind.Euler, Axis.X), Array.Empty<KeyAsset>()),
        });

        Assert.Equal(before + 1, editor.Revision);
        MotionAsset bow = editor.Document.Find("bow")!;
        Assert.Equal(20f, z.Resolve(bow)!.Keys[1].V);
        Assert.Null(bow.Channels[0].X);

        editor.Undo();
        bow = editor.Document.Find("bow")!;
        Assert.Equal(8f, z.Resolve(bow)!.Keys[1].V);
        Assert.NotNull(bow.Channels[0].X);
    }

    [Fact]
    public void 키를_옮기면_auto_탄젠트가_따라온다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        editor.SetTangentMode("nod", new[] { new KeyRef(Y, 0), new KeyRef(Y, 1), new KeyRef(Y, 2) }, TangentMode.Auto);

        // 가운데가 극값이라 0.
        Assert.Equal(0f, Y.Resolve(editor.Document.Find("nod")!)!.Keys[1].OutTangent);

        // 가운데 키를 더 내리고 끝 키를 올리면 더는 극값이 아니다 → 기울기가 생긴다.
        editor.UpdateKey("nod", Y.Node, Y.Kind, Y.Axis, 2, new KeyAsset { T = 0.6f, V = -40f, Tangent = TangentMode.Auto });
        KeyAsset middle = Y.Resolve(editor.Document.Find("nod")!)!.Keys[1];
        Assert.Equal(TangentMode.Auto, middle.Tangent);
        Assert.True(middle.OutTangent < 0f);
        Assert.Equal(middle.InTangent, middle.OutTangent);
    }

    [Fact]
    public void 핸들을_끌면_free가_되고_끊기면_양쪽이_따로다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        var key = new KeyRef(Y, 1);

        editor.SetTangents("nod", key, 30f, 30f);
        KeyAsset k = key.Resolve(editor.Document.Find("nod")!)!;
        Assert.Equal(TangentMode.Free, k.Tangent);
        Assert.False(k.Broken);

        editor.SetBroken("nod", new[] { key }, true);
        editor.SetTangents("nod", key, 30f, -30f);
        k = key.Resolve(editor.Document.Find("nod")!)!;
        Assert.True(k.Broken);
        Assert.Equal(-30f, k.OutTangent);

        editor.SetBroken("nod", new[] { key }, false);
        k = key.Resolve(editor.Document.Find("nod")!)!;
        Assert.False(k.Broken);
        Assert.Equal(30f, k.OutTangent);
    }

    [Fact]
    public void 여러_키_삭제는_한_번이고_빈_성분은_없음이_된다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        long before = editor.Revision;

        editor.RemoveKeys("nod", new[] { new KeyRef(Y, 0), new KeyRef(Y, 2), new KeyRef(Y, 9) });

        Assert.Equal(before + 1, editor.Revision);
        List<KeyAsset> keys = Y.Resolve(editor.Document.Find("nod")!)!.Keys;
        Assert.Single(keys);
        Assert.Equal(-18f, keys[0].V);

        editor.RemoveKeys("nod", new[] { new KeyRef(Y, 0) });
        Assert.Null(Y.Resolve(editor.Document.Find("nod")!));

        editor.RemoveKeys("nod", new[] { new KeyRef(Y, 0) });
        Assert.Equal(before + 2, editor.Revision);
    }
}

public class TimeSliderOpsTests
{
    private static readonly CurveRef Y = new("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);
    private const float Frame = 1f / 24f;

    [Fact]
    public void 현재_시각의_키를_모든_커브에서_모은다()
    {
        MotionAsset bow = Fixtures.NodBowDocument().Find("bow")!;

        List<KeyRef> at = TimeSliderOps.KeysAt(bow, 0.4);

        Assert.Equal(3, at.Count); // x·y·z 끝 키
        Assert.All(at, k => Assert.Equal(1, k.Index));
        Assert.Empty(TimeSliderOps.KeysAt(bow, 0.2));
    }

    [Fact]
    public void 인비트윈_넣기는_뒤의_키만_한_프레임_밀고_되돌리기_한_번이다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        long before = editor.Revision;

        Assert.True(editor.ShiftKeysAfter("nod", 0.3f, Frame));

        Assert.Equal(before + 1, editor.Revision);
        List<float> times = Y.Resolve(editor.Document.Find("nod")!)!.Keys.Select(k => k.T).ToList();
        Assert.Equal(0f, times[0]);
        Assert.Equal(0.3f, times[1], 4);
        Assert.Equal(0.6f + Frame, times[2], 4);
        editor.Undo();
        Assert.Equal(0.6f, Y.Resolve(editor.Document.Find("nod")!)!.Keys[2].T, 4);
    }

    [Fact]
    public void 인비트윈_빼기는_닿는_키가_이기고_현재_시각_아래로_안_내려간다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        editor.SetKeys("nod", Y.Node, Y.Kind, Y.Axis, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.3f, -18f), Fixtures.Key(0.3f + Frame, 7f), Fixtures.Key(0.6f, 0f) });

        Assert.True(editor.ShiftKeysAfter("nod", 0.3f, -Frame));

        IReadOnlyList<KeyAsset> keys = Y.Resolve(editor.Document.Find("nod")!)!.Keys;
        Assert.Equal(3, keys.Count);
        Assert.Equal(0.3f, keys[1].T, 4);
        Assert.Equal(7f, keys[1].V); // 움직인 키가 이긴다
        Assert.Equal(0.6f - Frame, keys[2].T, 4);

        Assert.False(editor.ShiftKeysAfter("nod", 1f, -Frame)); // 뒤에 키가 없다
    }

    [Fact]
    public void 현재_시각의_키_지우기는_모든_커브에서_한_번에_지운다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        long before = editor.Revision;

        Assert.True(editor.RemoveKeysAt("bow", 0.4));

        Assert.Equal(before + 1, editor.Revision);
        MotionAsset bow = editor.Document.Find("bow")!;
        Assert.All(new[] { Axis.X, Axis.Y, Axis.Z }, a => Assert.Single(bow.Channels[0].Get(a)!.Keys));
        Assert.False(editor.RemoveKeysAt("bow", 0.4));
    }
}
