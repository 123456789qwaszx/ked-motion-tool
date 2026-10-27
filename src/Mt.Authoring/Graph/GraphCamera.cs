namespace Mt.Authoring.Graph;

/// <summary>
/// 그래프 공간(초 × 값) ↔ 플롯 픽셀. 팬·줌이 전부 이 값 넷이다.
///
/// 화면 y는 값과 반대로 자란다(값이 오르면 픽셀 y는 줄어든다) — 변환 양쪽에 그 반전이 있어야 한다.
/// 축별 배율이 따로라 시간만·값만 줌이 된다.
/// </summary>
public readonly record struct GraphCamera(double TimeOrigin, double ValueOrigin, double PixelsPerSecond, double PixelsPerValue)
{
    public const double MinPixelsPerSecond = 4;
    public const double MaxPixelsPerSecond = 200_000;
    public const double MinPixelsPerValue = 1e-3;
    public const double MaxPixelsPerValue = 1e6;

    /// <summary>플롯 왼쪽 아래가 (TimeOrigin, ValueOrigin)이다.</summary>
    public Pixel ToPixel(GraphPoint point, double plotHeight) => new(
        (point.T - TimeOrigin) * PixelsPerSecond,
        plotHeight - (point.V - ValueOrigin) * PixelsPerValue);

    public GraphPoint ToGraph(Pixel pixel, double plotHeight) => new(
        TimeOrigin + pixel.X / PixelsPerSecond,
        ValueOrigin + (plotHeight - pixel.Y) / PixelsPerValue);

    /// <summary>픽셀 변위를 그래프 변위로 — 드래그에 쓴다. 값은 부호가 뒤집힌다.</summary>
    public GraphPoint DeltaToGraph(double dxPixels, double dyPixels)
        => new(dxPixels / PixelsPerSecond, -dyPixels / PixelsPerValue);

    public GraphCamera Pan(double dxPixels, double dyPixels) => this with
    {
        TimeOrigin = TimeOrigin - dxPixels / PixelsPerSecond,
        ValueOrigin = ValueOrigin + dyPixels / PixelsPerValue,
    };

    /// <summary>커서 아래의 그래프 점이 그 자리에 남게 배율을 바꾼다. 축별 배율은 1이면 그대로다.</summary>
    public GraphCamera ZoomAt(Pixel anchor, double plotHeight, double timeFactor, double valueFactor)
    {
        GraphPoint under = ToGraph(anchor, plotHeight);

        double pps = Math.Clamp(PixelsPerSecond * timeFactor, MinPixelsPerSecond, MaxPixelsPerSecond);
        double ppv = Math.Clamp(PixelsPerValue * valueFactor, MinPixelsPerValue, MaxPixelsPerValue);

        return new GraphCamera(
            under.T - anchor.X / pps,
            under.V - (plotHeight - anchor.Y) / ppv,
            pps,
            ppv);
    }

    /// <summary>
    /// 범위를 플롯에 맞춘다(F·A). 범위가 점이면 둘레를 조금 벌린다 — 0 나눗셈과 "보이지 않는 그래프"를 막는다.
    /// </summary>
    public static GraphCamera Fit(
        double tMin, double tMax, double vMin, double vMax,
        double plotWidth, double plotHeight, double paddingPixels)
    {
        if (tMax - tMin < 1e-6)
        {
            tMin -= 0.5;
            tMax += 0.5;
        }

        if (vMax - vMin < 1e-6)
        {
            vMin -= 1;
            vMax += 1;
        }

        double innerWidth = Math.Max(1, plotWidth - paddingPixels * 2);
        double innerHeight = Math.Max(1, plotHeight - paddingPixels * 2);

        double pps = Math.Clamp(innerWidth / (tMax - tMin), MinPixelsPerSecond, MaxPixelsPerSecond);
        double ppv = Math.Clamp(innerHeight / (vMax - vMin), MinPixelsPerValue, MaxPixelsPerValue);

        return new GraphCamera(tMin - paddingPixels / pps, vMin - paddingPixels / ppv, pps, ppv);
    }

    /// <summary>처음 열 때의 카메라 — 0~1초, -1~1 값.</summary>
    public static GraphCamera Default(double plotWidth, double plotHeight)
        => Fit(0, 1, -1, 1, plotWidth, plotHeight, 24);
}
