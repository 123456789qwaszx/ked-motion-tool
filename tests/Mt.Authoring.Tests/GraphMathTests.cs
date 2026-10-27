using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;

namespace Mt.Authoring.Tests;

public class GraphCameraTests
{
    [Fact]
    public void 픽셀_왕복은_항등이고_값은_위로_자란다()
    {
        var camera = new GraphCamera(-0.1, -20, 400, 3);
        var point = new GraphPoint(0.3, -18);

        Pixel px = camera.ToPixel(point, 300);
        GraphPoint back = camera.ToGraph(px, 300);

        Assert.Equal(point.T, back.T, 9);
        Assert.Equal(point.V, back.V, 9);

        Pixel higher = camera.ToPixel(new GraphPoint(0.3, 0), 300);
        Assert.True(higher.Y < px.Y, "값이 커졌는데 픽셀 y가 안 줄었다");
    }

    [Fact]
    public void 줌은_커서_아래_점을_그_자리에_둔다()
    {
        var camera = new GraphCamera(0, -1, 300, 100);
        var anchor = new Pixel(120, 80);
        GraphPoint before = camera.ToGraph(anchor, 300);

        GraphCamera zoomed = camera.ZoomAt(anchor, 300, 2, 0.5);
        GraphPoint after = zoomed.ToGraph(anchor, 300);

        Assert.Equal(before.T, after.T, 9);
        Assert.Equal(before.V, after.V, 9);
        Assert.Equal(600, zoomed.PixelsPerSecond, 9);
        Assert.Equal(50, zoomed.PixelsPerValue, 9);
    }

    [Fact]
    public void 팬은_픽셀만큼_옮긴다()
    {
        var camera = new GraphCamera(0, 0, 100, 10);
        GraphCamera panned = camera.Pan(50, -20);

        // 오른쪽으로 50px 끌면 원점은 0.5초 왼쪽으로, 위로 20px 끌면 값 원점은 2 내려간다.
        Assert.Equal(-0.5, panned.TimeOrigin, 9);
        Assert.Equal(-2, panned.ValueOrigin, 9);
    }

    [Fact]
    public void Fit은_범위를_안쪽_여백_안에_넣고_점_범위도_연다()
    {
        GraphCamera camera = GraphCamera.Fit(0, 0.6, -18, 0, 600, 300, 20);

        Pixel start = camera.ToPixel(new GraphPoint(0, -18), 300);
        Pixel end = camera.ToPixel(new GraphPoint(0.6, 0), 300);
        Assert.Equal(20, start.X, 6);
        Assert.Equal(280, start.Y, 6);
        Assert.Equal(580, end.X, 6);
        Assert.Equal(20, end.Y, 6);

        GraphCamera dot = GraphCamera.Fit(0.5, 0.5, 3, 3, 600, 300, 20);
        Assert.True(dot.PixelsPerSecond > 0 && double.IsFinite(dot.PixelsPerSecond));
        Assert.True(dot.PixelsPerValue > 0 && double.IsFinite(dot.PixelsPerValue));
    }
}

public class HandleMathTests
{
    private static readonly GraphCamera Camera = new(0, 0, 280, 81);

    [Fact]
    public void 오르는_기울기의_out_핸들은_화면에서_위를_향한다()
    {
        double dy = HandleMath.HandleDyPixels(1, 34, 280, 81);
        Assert.True(dy < 0);
        Assert.Equal(-dy, HandleMath.HandleDyPixels(1, -34, 280, 81), 9);
    }

    [Fact]
    public void 기울기_화면_왕복은_항등이다()
    {
        foreach (double slope in new[] { -3.5, -1, 0, 0.4, 1, 2.6 })
        {
            double dy = HandleMath.HandleDyPixels(slope, 34, 280, 81);
            Assert.Equal(slope, HandleMath.SlopeFromHandleDelta(34, dy, 280, 81), 9);
        }
    }

    [Fact]
    public void 핸들_끝은_팔_길이가_일정하고_방향은_기울기를_따른다()
    {
        var origin = new Pixel(100, 100);

        Pixel outEnd = HandleMath.HandleEnd(origin, 2, inward: false, Camera);
        Pixel inEnd = HandleMath.HandleEnd(origin, 2, inward: true, Camera);

        Assert.Equal(HandleMath.ArmPixels, origin.DistanceTo(outEnd), 6);
        Assert.Equal(HandleMath.ArmPixels, origin.DistanceTo(inEnd), 6);
        Assert.True(outEnd.X > origin.X && outEnd.Y < origin.Y);
        Assert.True(inEnd.X < origin.X && inEnd.Y > origin.Y);

        Assert.Equal(2, HandleMath.SlopeFromHandle(origin, outEnd, inward: false, Camera), 6);
        Assert.Equal(2, HandleMath.SlopeFromHandle(origin, inEnd, inward: true, Camera), 6);
    }

    [Fact]
    public void 팔을_거꾸로_넘겨도_수직_급경사로_막힌다()
    {
        var origin = new Pixel(100, 100);
        double slope = HandleMath.SlopeFromHandle(origin, new Pixel(90, 60), inward: false, Camera);
        Assert.True(slope > 0 && double.IsFinite(slope));
    }
}

public class TimeSnapTests
{
    [Theory]
    [InlineData(0.3, 1.0 / 60, 0.3)]
    [InlineData(0.3071, 1.0 / 60, 0.3)]
    [InlineData(0.3091, 1.0 / 60, 19.0 / 60)]
    [InlineData(0.3091, 0, 0.3091)]
    public void 스냅은_가까운_눈금이고_0이면_끈다(double seconds, double step, double expected)
    {
        Assert.Equal(expected, TimeSnap.Snap(seconds, step), 9);
    }

    [Fact]
    public void 눈금은_프레임_단계에서_고르고_밀도가_낮으면_커진다()
    {
        double dense = TimeSnap.TickStep(2000);
        double sparse = TimeSnap.TickStep(60);

        Assert.Equal(1 / 24.0, dense, 9);
        Assert.True(sparse > dense);
        Assert.Equal(0, (sparse * 24) % 1, 6);
        Assert.Equal(24, TimeSnap.FramesPerSecond);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(100, 0.5)]
    [InlineData(1, 50)]
    public void 값_눈금은_1_2_5_배수다(double pixelsPerValue, double expected)
    {
        Assert.Equal(expected, TimeSnap.ValueTickStep(pixelsPerValue), 9);
    }
}

public class TangentMathTests
{
    private static KeyAsset K(float t, float v, TangentMode mode) => new() { T = t, V = v, Tangent = mode };

    [Fact]
    public void flat은_0이고_linear는_이웃_할선이다()
    {
        var keys = new List<KeyAsset> { K(0, 0, TangentMode.Flat), K(1, 10, TangentMode.Linear), K(3, 0, TangentMode.Linear) };

        TangentMath.Recompute(keys);

        Assert.Equal(0f, keys[0].OutTangent);
        Assert.Equal(10f, keys[1].InTangent);
        Assert.Equal(-5f, keys[1].OutTangent);
        Assert.True(keys[1].Broken);
        Assert.Equal(-5f, keys[2].InTangent);
        Assert.Equal(-5f, keys[2].OutTangent);
    }

    [Fact]
    public void auto는_끝_키와_극값에서_0이고_가운데는_이웃_할선이다()
    {
        var rising = new List<KeyAsset> { K(0, 0, TangentMode.Auto), K(1, 1, TangentMode.Auto), K(2, 2, TangentMode.Auto) };
        TangentMath.Recompute(rising);
        Assert.Equal(0f, rising[0].OutTangent);
        Assert.Equal(1f, rising[1].InTangent);
        Assert.Equal(1f, rising[1].OutTangent);
        Assert.Equal(0f, rising[2].InTangent);

        var peak = new List<KeyAsset> { K(0, 0, TangentMode.Auto), K(0.3f, -18, TangentMode.Auto), K(0.6f, 0, TangentMode.Auto) };
        TangentMath.Recompute(peak);
        Assert.Equal(0f, peak[1].InTangent);
        Assert.Equal(0f, peak[1].OutTangent);
    }

    [Fact]
    public void auto는_가파른_쪽의_3배를_넘지_않는다()
    {
        var keys = new List<KeyAsset> { K(0, 0, TangentMode.Auto), K(1, 1, TangentMode.Auto), K(1.01f, 100, TangentMode.Auto) };
        TangentMath.Recompute(keys);

        Assert.True(Math.Abs(keys[1].OutTangent) <= 3f * 1f + 1e-4f);
    }

    [Fact]
    public void spline은_할선_그대로라_넘칠_수_있고_끝은_이웃과의_할선이다()
    {
        var keys = new List<KeyAsset> { K(0, 0, TangentMode.Spline), K(1, 1, TangentMode.Spline), K(1.01f, 100, TangentMode.Spline) };
        TangentMath.Recompute(keys);

        Assert.Equal(1f, keys[0].OutTangent, 3);          // 끝: (0,0)→(1,1)
        Assert.Equal(100f / 1.01f, keys[1].OutTangent, 2); // 가운데: 이웃 할선 그대로(auto면 3으로 눌렸다)
        Assert.Equal(99f / 0.01f, keys[2].InTangent, 0);   // 끝: (1,1)→(1.01,100)
        Assert.False(keys[1].Broken);
    }

    [Fact]
    public void plateau는_이웃을_넘지_않고_같은_값이면_평평하다()
    {
        var rising = new List<KeyAsset> { K(0, 0, TangentMode.Plateau), K(1, 1, TangentMode.Plateau), K(2, 10, TangentMode.Plateau) };
        TangentMath.Recompute(rising);
        Assert.Equal(1f, rising[1].OutTangent, 3); // 할선 5를 작은 쪽 구간(1)으로 눌렀다
        Assert.Equal(0f, rising[0].OutTangent);

        var flatNeighbor = new List<KeyAsset> { K(0, 0, TangentMode.Plateau), K(1, 1, TangentMode.Plateau), K(2, 1, TangentMode.Plateau), K(3, 5, TangentMode.Plateau) };
        TangentMath.Recompute(flatNeighbor);
        Assert.Equal(0f, flatNeighbor[1].OutTangent);
        Assert.Equal(0f, flatNeighbor[2].OutTangent);

        var peak = new List<KeyAsset> { K(0, 0, TangentMode.Plateau), K(1, 5, TangentMode.Plateau), K(2, 0, TangentMode.Plateau) };
        TangentMath.Recompute(peak);
        Assert.Equal(0f, peak[1].InTangent);
    }

    [Fact]
    public void 새_키_탄젠트를_정하면_끼운_키가_그_모드다()
    {
        var keys = new List<KeyAsset> { K(0, 0, TangentMode.Flat), K(1, 10, TangentMode.Flat) };

        Assert.Equal(TangentMode.Free, KeyOps.WithKeyOnCurve(keys, 0.5f).Single(k => k.T == 0.5f).Tangent);
        Assert.Equal(TangentMode.Plateau, KeyOps.WithKeyOnCurve(keys, 0.5f, TangentMode.Plateau).Single(k => k.T == 0.5f).Tangent);
        Assert.Equal(TangentMode.Spline, KeyOps.WithKeyOnCurve(new List<KeyAsset>(), 0.5f, TangentMode.Spline).Single().Tangent);
    }

    [Fact]
    public void free는_손대지_않는다()
    {
        var keys = new List<KeyAsset> { K(0, 0, TangentMode.Free), new() { T = 1, V = 1, InTangent = 7, OutTangent = -7, Tangent = TangentMode.Free, Broken = true } };
        TangentMath.Recompute(keys);
        Assert.Equal(7f, keys[1].InTangent);
        Assert.Equal(-7f, keys[1].OutTangent);
    }

    [Fact]
    public void 곡선_위_값과_접선은_코어_평가기에서_온다()
    {
        var keys = new List<KeyAsset> { K(0, 0, TangentMode.Linear), K(1, 10, TangentMode.Linear) };
        TangentMath.Recompute(keys);

        (float value, float slope) = TangentMath.OnCurve(keys, 0.5f);

        Assert.Equal(5f, value, 3);
        Assert.Equal(10f, slope, 2);
        Assert.Equal(CurveFunctions.Evaluate(TangentMath.ToCurveKeys(keys), 0.5f), value);
    }
}

public class CurveSamplerTests
{
    [Fact]
    public void 샘플은_키_시각을_반드시_포함하고_범위_밖은_평평하다()
    {
        var keys = new List<KeyAsset>
        {
            new() { T = 0, V = 0 }, new() { T = 0.3f, V = -18 }, new() { T = 0.6f, V = 0 },
        };

        List<GraphPoint> points = CurveSampler.Sample(keys, -0.2, 1.0, 7);

        Assert.Contains(points, p => Math.Abs(p.T - 0.3) < 1e-6 && Math.Abs(p.V + 18) < 1e-6);
        Assert.Equal(0, points[0].V, 6);
        Assert.Equal(0, points[^1].V, 6);
        Assert.True(points.Zip(points.Skip(1), (a, b) => a.T < b.T).All(x => x));
    }

    [Fact]
    public void 범위는_키에서_나온다()
    {
        var keys = new List<KeyAsset> { new() { T = 0.1f, V = 2 }, new() { T = 0.5f, V = -3 } };

        Assert.True(CurveSampler.TryBounds(keys, out double t0, out double t1, out double v0, out double v1));
        Assert.Equal((0.1, 0.5, -3.0, 2.0), (Math.Round(t0, 6), Math.Round(t1, 6), v0, v1));
        Assert.False(CurveSampler.TryBounds(Array.Empty<KeyAsset>(), out _, out _, out _, out _));
    }
}

public class GraphHitTestTests
{
    [Fact]
    public void 가장_가까운_것_겹치면_뒤에_그린_것()
    {
        var candidates = new List<(string, Pixel)> { ("a", new Pixel(10, 10)), ("b", new Pixel(12, 10)), ("c", new Pixel(12, 10)) };

        Assert.True(GraphHitTest.TryNearest(candidates, new Pixel(13, 10), 7, out string hit));
        Assert.Equal("c", hit);
        Assert.False(GraphHitTest.TryNearest(candidates, new Pixel(40, 40), 7, out _));
    }

    [Fact]
    public void 박스는_모서리_순서와_무관하다()
    {
        var candidates = new List<(int, Pixel)> { (1, new Pixel(5, 5)), (2, new Pixel(50, 50)), (3, new Pixel(20, 30)) };

        Assert.Equal(new[] { 1, 3 }, GraphHitTest.InBox(candidates, new Pixel(30, 40), new Pixel(0, 0)));
    }

    [Fact]
    public void 폴리라인_거리()
    {
        var line = new List<Pixel> { new(0, 0), new(10, 0), new(10, 10) };
        Assert.Equal(3, GraphHitTest.DistanceToPolyline(line, new Pixel(5, 3)), 9);
        Assert.Equal(2, GraphHitTest.DistanceToPolyline(line, new Pixel(12, 5)), 9);
    }
}

public class CurveViewTransformTests
{
    private static KeyAsset K(float t, float v) => new() { T = t, V = v };

    [Fact]
    public void 절대는_항등이고_정규화는_최소_최대를_0_1로_누른다()
    {
        var keys = new List<KeyAsset> { K(0, -18), K(1, 0), K(2, 18) };

        Assert.Equal(CurveViewTransform.Identity, CurveViewTransform.For(keys, GraphViewMode.Absolute, 3));

        CurveViewTransform n = CurveViewTransform.For(keys, GraphViewMode.Normalized, 3);
        Assert.Equal(0, n.ToView(-18), 9);
        Assert.Equal(0.5, n.ToView(0), 9);
        Assert.Equal(1, n.ToView(18), 9);
        Assert.Equal(18, n.ToValue(1), 9);

        CurveViewTransform s = CurveViewTransform.For(keys, GraphViewMode.Stacked, 2);
        Assert.Equal(2, s.ToView(-18), 9);
        Assert.Equal(3, s.ToView(18), 9);
    }

    [Fact]
    public void 평평한_커브는_1대1로_가운데_둔다_0으로_나누지_않는다()
    {
        var keys = new List<KeyAsset> { K(0, 5), K(1, 5) };
        CurveViewTransform n = CurveViewTransform.For(keys, GraphViewMode.Normalized, 0);

        Assert.Equal(1, n.Scale, 9);
        Assert.Equal(0.5, n.ToView(5), 9);
        Assert.Equal(6, n.ToValue(1.5), 9);
        Assert.Equal(1.5, CurveViewTransform.For(new List<KeyAsset>(), GraphViewMode.Stacked, 1).ToView(0), 9); // 빈 커브: 0이 띠 가운데
    }
}

public class CurveFlagsTests
{
    [Fact]
    public void 잠금_뮤트는_바뀔_때만_알린다()
    {
        var flags = new CurveFlags();
        var y = new CurveRef("N", MotionChannelKind.Position, Axis.Y);
        int changes = 0;
        flags.Changed += (_, _) => changes++;

        flags.SetLocked(y, true);
        flags.SetLocked(y, true);
        Assert.True(flags.IsLocked(y));
        Assert.Equal(1, changes);

        flags.SetMuted(y, true);
        flags.SetMuted(y, false);
        Assert.False(flags.IsMuted(y));
        Assert.Equal(3, changes);

        flags.Clear();
        Assert.False(flags.IsLocked(y));
        Assert.Equal(4, changes);
        flags.Clear();
        Assert.Equal(4, changes);
    }
}

public class TimeTextTests
{
    [Fact]
    public void 프레임_표시는_정수면_정수로_아니면_소수_둘로_초는_s_꼬리다()
    {
        Assert.Equal("12", TimeText.Format(0.5, TimeDisplay.Frames));
        Assert.Equal("7.2", TimeText.Format(0.3, TimeDisplay.Frames));
        Assert.Equal("0.3s", TimeText.Format(0.3, TimeDisplay.Seconds));
        Assert.Equal("0", TimeText.Format(0, TimeDisplay.Frames));
    }

    [Fact]
    public void 읽기는_꼬리_단위가_우선이고_없으면_표시_단위다()
    {
        Assert.True(TimeText.TryParse("12", TimeDisplay.Frames, out double a));
        Assert.Equal(0.5, a, 9);
        Assert.True(TimeText.TryParse("12", TimeDisplay.Seconds, out double b));
        Assert.Equal(12, b, 9);
        Assert.True(TimeText.TryParse("0.5s", TimeDisplay.Frames, out double c));
        Assert.Equal(0.5, c, 9);
        Assert.True(TimeText.TryParse(" 24f ", TimeDisplay.Seconds, out double d));
        Assert.Equal(1, d, 9);
        Assert.False(TimeText.TryParse("", TimeDisplay.Frames, out _));
        Assert.False(TimeText.TryParse("abc", TimeDisplay.Frames, out _));
    }

    [Fact]
    public void 프레임_눈금은_라벨이_겹치지_않는_첫_단계다()
    {
        Assert.Equal(1, TimeText.FrameTickStep(40));
        Assert.Equal(2, TimeText.FrameTickStep(20));
        Assert.Equal(5, TimeText.FrameTickStep(10));
        Assert.Equal(10, TimeText.FrameTickStep(4));
        Assert.Equal(100, TimeText.FrameTickStep(0.4));
    }
}
