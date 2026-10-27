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
