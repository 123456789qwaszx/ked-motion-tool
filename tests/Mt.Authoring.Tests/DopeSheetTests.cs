using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;

namespace Mt.Authoring.Tests;

public class KeySelectionTests
{
    private static readonly CurveRef Y = new("N", MotionChannelKind.Position, Axis.Y);

    [Fact]
    public void 바뀔_때만_한_번_알린다()
    {
        var selection = new KeySelection();
        int changes = 0;
        selection.Changed += (_, _) => changes++;

        Assert.True(selection.Add(new KeyRef(Y, 0)));
        Assert.False(selection.Add(new KeyRef(Y, 0)));
        selection.AddRange(new[] { new KeyRef(Y, 1), new KeyRef(Y, 2) });
        selection.Set(new[] { new KeyRef(Y, 0), new KeyRef(Y, 1), new KeyRef(Y, 2) }); // 같은 집합 — 조용
        Assert.Equal(2, changes);

        Assert.False(selection.Toggle(new KeyRef(Y, 1)));
        Assert.True(selection.Toggle(new KeyRef(Y, 1)));
        Assert.Equal(4, changes);

        selection.Clear();
        selection.Clear();
        Assert.Equal(5, changes);
        Assert.Empty(selection);
    }

    [Fact]
    public void 문서가_바뀌면_없는_키를_턴다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        var y = new CurveRef("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);
        var selection = new KeySelection();
        selection.Set(new[] { new KeyRef(y, 2), new KeyRef(y, 9) });

        Assert.Equal(1, selection.Prune(document.Find("nod")));
        Assert.Equal(new[] { new KeyRef(y, 2) }, selection);
        Assert.Equal(1, selection.Prune(null));
        Assert.Empty(selection);
    }
}

public class KeyOpsTests
{
    private static readonly CurveRef Y = new("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);

    private static MotionAsset Nod() => Fixtures.NodBowDocument().Find("nod")!;

    [Fact]
    public void 이동은_선택만_옮기고_0_아래로_내려가지_않는다()
    {
        var edits = KeyOps.Move(Nod(), new[] { new KeyRef(Y, 1) }, 0.1f);

        (CurveRef curve, IEnumerable<KeyAsset> keys) = Assert.Single(edits);
        Assert.Equal(Y, curve);
        Assert.Equal(new[] { 0f, 0.4f, 0.6f }, keys.OrderBy(k => k.T).Select(k => (float)Math.Round(k.T, 4)));

        var clamped = KeyOps.Move(Nod(), new[] { new KeyRef(Y, 0) }, -5f);
        Assert.Contains(clamped[0].Keys, k => k.T == 0f);
    }

    [Fact]
    public void 움직인_키가_닿은_자리의_키는_사라진다_움직인_키가_이긴다()
    {
        var edits = KeyOps.Move(Nod(), new[] { new KeyRef(Y, 1) }, 0.3f);
        List<KeyAsset> keys = edits[0].Keys.OrderBy(k => k.T).ToList();

        Assert.Equal(2, keys.Count);
        Assert.Equal(-18f, keys[1].V); // 0.6의 끝 키(0)가 아니라 옮겨 온 키(-18)
    }

    [Fact]
    public void 스냅은_옮긴_뒤_시각에_적용된다()
    {
        var edits = KeyOps.Move(Nod(), new[] { new KeyRef(Y, 1) }, 0.011f, t => (float)TimeSnap.Snap(t, TimeSnap.DefaultStep));
        Assert.Contains(edits[0].Keys, k => Math.Abs(k.T - 19f / 60f) < 1e-4);
    }

    [Fact]
    public void 시간_스케일은_기준점을_두고_늘리고_양수여야_한다()
    {
        var edits = KeyOps.Scale(Nod(), new[] { new KeyRef(Y, 0), new KeyRef(Y, 1), new KeyRef(Y, 2) }, 0f, 2f);
        Assert.Equal(new[] { 0f, 0.6f, 1.2f }, edits[0].Keys.OrderBy(k => k.T).Select(k => (float)Math.Round(k.T, 4)));

        Assert.Throws<ArgumentOutOfRangeException>(() => KeyOps.Scale(Nod(), new[] { new KeyRef(Y, 1) }, 0f, 0f));
    }

    [Fact]
    public void 복제는_원본을_두고_사본을_옮겨_둔다()
    {
        var edits = KeyOps.Duplicate(Nod(), new[] { new KeyRef(Y, 1) }, 0.1f);
        List<KeyAsset> keys = edits[0].Keys.OrderBy(k => k.T).ToList();

        Assert.Equal(4, keys.Count);
        Assert.Equal(-18f, keys[1].V);
        Assert.Equal(-18f, keys[2].V);
        Assert.Equal(0.4f, keys[2].T, 4);
    }

    [Fact]
    public void 결과_시각은_선택을_다시_푸는_데_쓴다()
    {
        List<(CurveRef Curve, float T)> result = KeyOps.ResultingTimes(Nod(), new[] { new KeyRef(Y, 1), new KeyRef(Y, 9) }, t => t + 0.1f);
        (CurveRef curve, float t) = Assert.Single(result);
        Assert.Equal(Y, curve);
        Assert.Equal(0.4f, t, 4);
    }

    [Fact]
    public void 편집기_명령은_한_번의_조작이고_선택_시각을_돌려준다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        long before = editor.Revision;

        List<(CurveRef Curve, float T)> moved = editor.MoveKeys("nod", new[] { new KeyRef(Y, 1), new KeyRef(Y, 2) }, 0.2f);
        Assert.Equal(before + 1, editor.Revision);
        Assert.Equal(new[] { 0.5f, 0.8f }, moved.Select(m => (float)Math.Round(m.T, 4)).OrderBy(t => t));
        Assert.Equal(0.8f, Y.Resolve(editor.Document.Find("nod")!)!.Keys[^1].T, 4);

        editor.Undo();
        Assert.Equal(0.6f, Y.Resolve(editor.Document.Find("nod")!)!.Keys[^1].T, 4);

        editor.ScaleKeys("nod", new[] { new KeyRef(Y, 1), new KeyRef(Y, 2) }, 0f, 0.5f);
        Assert.Equal(new[] { 0f, 0.15f, 0.3f }, Y.Resolve(editor.Document.Find("nod")!)!.Keys.Select(k => (float)Math.Round(k.T, 4)));

        List<(CurveRef Curve, float T)> copies = editor.DuplicateKeys("nod", new[] { new KeyRef(Y, 1) }, 0.5f);
        Assert.Equal(0.65f, Assert.Single(copies).T, 4);
        Assert.Equal(4, Y.Resolve(editor.Document.Find("nod")!)!.Keys.Count);
    }
}

public class DopeSheetRowsTests
{
    [Fact]
    public void 노드_채널_성분_트리와_요약_블록()
    {
        MotionAsset bow = Fixtures.NodBowDocument().Find("bow")!;

        List<DopeRow> rows = DopeSheetRows.Build(bow);

        Assert.Equal(5, rows.Count); // 노드 · 채널 · x · y · z
        Assert.Equal(DopeRowLevel.Node, rows[0].Level);
        Assert.Equal("CharacterPortrait_SwayPivot", rows[0].Label);
        Assert.Equal(DopeRowLevel.Channel, rows[1].Level);
        Assert.Equal("euler · abs", rows[1].Label);
        Assert.Equal(new[] { "x", "y", "z" }, rows.Skip(2).Select(r => r.Label));
        Assert.All(rows.Skip(2), r => Assert.NotNull(r.Curve));

        // 요약 행: 같은 시각의 키 셋이 블록 하나.
        Assert.Equal(2, rows[0].Blocks.Count);
        Assert.Equal(3, rows[0].Blocks[0].Keys.Count);
        Assert.Equal(0.4f, rows[0].Blocks[1].Time, 4);
        Assert.Single(rows[2].Blocks[0].Keys);
    }

    [Fact]
    public void 접힌_노드는_요약_행만_남고_Covers는_계층대로다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        List<DopeRow> rows = DopeSheetRows.Build(document.Find("nod")!, node => true);

        DopeRow only = Assert.Single(rows);
        Assert.Equal(DopeRowLevel.Node, only.Level);
        var y = new CurveRef("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);
        Assert.True(only.Covers(new KeyRef(y, 1)));
        Assert.False(only.Covers(new KeyRef(new CurveRef("Other", MotionChannelKind.Position, Axis.Y), 1)));

        List<DopeRow> open = DopeSheetRows.Build(document.Find("nod")!);
        Assert.Equal(3, open.Count);
        Assert.False(open[2].Covers(new KeyRef(new CurveRef(y.Node, y.Kind, Axis.X), 0)));
    }

    [Fact]
    public void 채널이_없으면_행이_없다()
    {
        Assert.Empty(DopeSheetRows.Build(new MotionAsset { Name = "a", Rig = "character", Duration = 1f }));
    }
}
