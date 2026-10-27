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
