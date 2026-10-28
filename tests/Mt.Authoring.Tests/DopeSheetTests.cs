using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

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
        var edits = KeyOps.Move(Nod(), new[] { new KeyRef(Y, 1) }, 0.011f, t => (float)TimeSnap.Snap(t, 1.0 / 60));
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
        Assert.Equal("rotate", rows[1].Label); // 화면 이름은 rotate, 파일 규약은 euler
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
    public void 컨트롤러_표가_있으면_노드_행은_묶음_컨트롤러_라벨이고_별칭_노드는_성분까지다()
    {
        ControllerRig rig = BuiltinTuning.Controllers.Rig("character")!;
        MotionDocument document = Fixtures.NodBowDocument();

        Assert.Equal("연기 · 숙임", DopeSheetRows.Build(document.Find("bow")!, null, rig)[0].Label);
        Assert.Equal("연기 · 몸 Y", DopeSheetRows.Build(document.Find("nod")!, null, rig)[0].Label); // 견본 nod는 별칭 노드 Track_Move_Y
        Assert.Equal("CharacterPortrait_SwayPivot", DopeSheetRows.Build(document.Find("bow")!)[0].Label); // 표 없이는 노드 이름
        Assert.Equal("Other", DopeSheetRows.NodeLabel("Other", rig));
    }

    [Fact]
    public void 채널이_없으면_행이_없다()
    {
        Assert.Empty(DopeSheetRows.Build(new MotionAsset { Name = "a", Rig = "character", Duration = 1f }));
    }
}

public class RelativeInputTests
{
    [Theory]
    [InlineData("5", 2f, 5f)]
    [InlineData(" +=3 ", 2f, 5f)]
    [InlineData("-=0.5", 2f, 1.5f)]
    [InlineData("*=2", 2f, 4f)]
    [InlineData("/=4", 2f, 0.5f)]
    [InlineData("/=0", 2f, 2f)]
    public void 절대와_상대(string text, float current, float expected)
    {
        Assert.True(RelativeInput.TryParse(text, out Func<float, float> apply));
        Assert.Equal(expected, apply(current), 5);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("+=x")]
    [InlineData("^=2")]
    public void 못_읽으면_false(string text)
    {
        Assert.False(RelativeInput.TryParse(text, out _));
    }
}

public class KeyValueOpsTests
{
    private static readonly CurveRef Y = new("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);

    [Fact]
    public void 값만_바꾸고_시각은_그대로라_선택이_유효하다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        long before = editor.Revision;

        editor.SetKeyValues("nod", new[] { new KeyRef(Y, 1), new KeyRef(Y, 9) }, k => k.V * 2f);

        Assert.Equal(before + 1, editor.Revision);
        List<KeyAsset> keys = Y.Resolve(editor.Document.Find("nod")!)!.Keys;
        Assert.Equal(new[] { 0f, 0.3f, 0.6f }, keys.Select(k => (float)Math.Round(k.T, 4)));
        Assert.Equal(-36f, keys[1].V);
        Assert.Equal(0f, keys[0].V);
    }

    [Fact]
    public void 라소는_다각형_안만_고른다()
    {
        var candidates = new List<(int, Pixel)> { (1, new Pixel(5, 5)), (2, new Pixel(50, 50)), (3, new Pixel(20, 8)) };
        var triangle = new List<Pixel> { new(0, 0), new(30, 0), new(0, 30) };

        Assert.Equal(new[] { 1, 3 }, GraphHitTest.InPolygon(candidates, triangle));
        Assert.Empty(GraphHitTest.InPolygon(candidates, new List<Pixel> { new(0, 0), new(1, 1) }));
    }
}

public class KeyClipboardTests
{
    private static readonly CurveRef Y = new("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);
    private static readonly CurveRef Z = new("CharacterPortrait_SwayPivot", MotionChannelKind.Euler, Axis.Z);

    [Fact]
    public void 복사는_사본이고_기준은_가장_이른_키다()
    {
        MotionAsset nod = Fixtures.NodBowDocument().Find("nod")!;

        KeyClipboard clipboard = KeyClipboard.Copy(nod, new[] { new KeyRef(Y, 2), new KeyRef(Y, 1), new KeyRef(Y, 9) })!;

        Assert.Equal(2, clipboard.Count);
        Assert.Equal(0.3f, clipboard.Origin, 4);
        (CurveRef curve, IReadOnlyList<KeyAsset> keys) = Assert.Single(clipboard.Items);
        Assert.Equal(Y, curve);
        Assert.Equal(new[] { 0.3f, 0.6f }, keys.Select(k => (float)Math.Round(k.T, 4)));
        Assert.NotSame(Y.Resolve(nod)!.Keys[1], keys[0]);
        Assert.Null(KeyClipboard.Copy(nod, new[] { new KeyRef(Y, 9) }));
    }

    [Fact]
    public void 붙여넣기는_현재_시각에_기준을_맞추고_같은_시각은_바꾸며_없는_채널은_만든다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        MotionAsset nod = editor.Document.Find("nod")!;
        KeyClipboard clipboard = KeyClipboard.Copy(nod, new[] { new KeyRef(Y, 1), new KeyRef(Y, 2) })!; // 0.3(-18) · 0.6(0)
        long before = editor.Revision;

        List<KeyRef> pasted = editor.PasteKeys("nod", clipboard, 0.6f); // 0.6 ← -18 (덮어씀) · 0.9 ← 0
        Assert.Equal(before + 1, editor.Revision);
        Assert.Equal(2, pasted.Count);
        List<KeyAsset> keys = Y.Resolve(editor.Document.Find("nod")!)!.Keys;
        Assert.Equal(new[] { 0f, 0.3f, 0.6f, 0.9f }, keys.Select(k => (float)Math.Round(k.T, 4)));
        Assert.Equal(-18f, keys[2].V);

        // bow에 nod의 y 채널을 붙이면 채널·성분이 생긴다.
        List<KeyRef> intoBow = editor.PasteKeys("bow", clipboard, 0f);
        ChannelAsset created = editor.Document.Find("bow")!.FindChannel(Y.Node, Y.Kind)!;
        Assert.Equal(2, created.Y!.Keys.Count);
        Assert.Equal(0f, created.Y.Keys[0].T);
        Assert.Equal(2, intoBow.Count);

        // 스냅.
        editor.PasteKeys("bow", KeyClipboard.Copy(editor.Document.Find("bow")!, new[] { new KeyRef(Z, 1) })!, 0.311f, t => (float)TimeSnap.Snap(t, 1.0 / 60));
        Assert.Contains(Z.Resolve(editor.Document.Find("bow")!)!.Keys, k => Math.Abs(k.T - 19f / 60f) < 1e-4);

        // 대상 커브를 주면 복사한 커브가 아니라 그 커브에 붙는다 — y의 키(-18)를 x에.
        var x = new CurveRef(Y.Node, Y.Kind, Axis.X);
        List<KeyRef> ontoX = editor.PasteKeys("nod", KeyClipboard.Copy(editor.Document.Find("nod")!, new[] { new KeyRef(Y, 1) })!, 0.5f, null, new[] { x });
        Assert.Single(ontoX);
        Assert.Equal(x, ontoX[0].Curve);
        Assert.Contains(x.Resolve(editor.Document.Find("nod")!)!.Keys, k => Math.Abs(k.T - 0.5f) < 1e-4 && k.V == -18f);
    }
}

public class CycleSamplerTests
{
    [Fact]
    public void 사이클은_길이로_감아_반복한다()
    {
        var keys = new List<KeyAsset> { new() { T = 0, V = 0 }, new() { T = 0.3f, V = -18 }, new() { T = 0.6f, V = 0 } };

        List<GraphPoint> points = CurveSampler.SampleCycled(keys, 0.6, -0.6, 1.2, 18);

        Assert.Equal(19, points.Count);
        Assert.Contains(points, p => Math.Abs(p.T - 0.9) < 1e-9 && Math.Abs(p.V + 18) < 1e-3);
        Assert.Contains(points, p => Math.Abs(p.T + 0.3) < 1e-9 && Math.Abs(p.V + 18) < 1e-3);
        Assert.Empty(CurveSampler.SampleCycled(keys, 0, 0, 1, 4));
    }
}

public class InfinitySamplerTests
{
    private static List<KeyAsset> Ramp() => new()
    {
        new() { T = 0, V = 0, InTangent = 1, OutTangent = 1, Tangent = TangentMode.Free },
        new() { T = 1, V = 1, InTangent = 1, OutTangent = 1, Tangent = TangentMode.Free },
    };

    private static double At(InfinityMode pre, InfinityMode post, double t)
    {
        List<GraphPoint> points = CurveSampler.SampleInfinity(Ramp(), pre, post, t, t + 1, 1);
        return points[0].V;
    }

    [Fact]
    public void 뒤_무한은_상수_사이클_오프셋_왕복_직선이다()
    {
        Assert.Equal(1, At(InfinityMode.Constant, InfinityMode.Constant, 1.5), 3);
        Assert.Equal(0.5, At(InfinityMode.Constant, InfinityMode.Cycle, 1.5), 3);
        Assert.Equal(1.5, At(InfinityMode.Constant, InfinityMode.CycleOffset, 1.5), 3);
        Assert.Equal(0.5, At(InfinityMode.Constant, InfinityMode.Oscillate, 1.5), 3);
        Assert.Equal(0.25, At(InfinityMode.Constant, InfinityMode.Oscillate, 2.25), 3);
        Assert.Equal(1.5, At(InfinityMode.Constant, InfinityMode.Linear, 1.5), 3);
    }

    [Fact]
    public void 앞_무한도_같고_구간_안은_보통_평가다()
    {
        Assert.Equal(0, At(InfinityMode.Constant, InfinityMode.Constant, -0.5), 3);
        Assert.Equal(0.5, At(InfinityMode.Cycle, InfinityMode.Constant, -0.5), 3);
        Assert.Equal(-0.5, At(InfinityMode.CycleOffset, InfinityMode.Constant, -0.5), 3);
        Assert.Equal(0.5, At(InfinityMode.Oscillate, InfinityMode.Constant, -0.5), 3);
        Assert.Equal(-0.5, At(InfinityMode.Linear, InfinityMode.Constant, -0.5), 3);
        Assert.Equal(0.5, At(InfinityMode.Cycle, InfinityMode.Cycle, 0.5), 3);
        Assert.Empty(CurveSampler.SampleInfinity(new List<KeyAsset> { new() { T = 0, V = 1 } }, InfinityMode.Cycle, InfinityMode.Cycle, -1, 1, 4));
    }
}
