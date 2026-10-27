namespace Mt.Authoring.Graph;

/// <summary>
/// 기울기(dv/dt) ↔ 화면 팔 벡터. VnTool <c>EaseCurveEditor</c>의 산수 그대로 — 화면 y는 값과 반대로 자라므로
/// 부호 반전이 두 방향 모두에 있어야 한다. 이게 빠지면 핸들이 곡선과 거울로 서고, 날개를 돌리면 반대로 굽는다.
/// </summary>
public static class HandleMath
{
    /// <summary>탄젠트 핸들 팔 길이(px). 축 배율과 무관하게 화면에서 같은 길이다 — 마야와 같다.</summary>
    public const double ArmPixels = 40;

    public static double HandleDyPixels(double slope, double dxPixels, double pixelsPerSecond, double pixelsPerValue)
        => -slope * dxPixels / pixelsPerSecond * pixelsPerValue;

    public static double SlopeFromHandleDelta(double dxPixels, double dyPixels, double pixelsPerSecond, double pixelsPerValue)
        => -dyPixels / pixelsPerValue * pixelsPerSecond / dxPixels;

    /// <summary>키의 in(왼쪽)·out(오른쪽) 핸들 끝. 팔은 화면에서 일정 길이이고 기울기 방향으로 눕는다.</summary>
    public static Pixel HandleEnd(Pixel origin, double slope, bool inward, GraphCamera camera)
    {
        // 기울기가 가파르면 팔이 화면 밖으로 뻗으므로 방향만 유지하고 길이를 ArmPixels로 정규화한다.
        double dx = inward ? -1 : 1;
        double dy = HandleDyPixels(slope, dx, camera.PixelsPerSecond, camera.PixelsPerValue);
        double length = Math.Sqrt(dx * dx + dy * dy);
        return new Pixel(origin.X + dx / length * ArmPixels, origin.Y + dy / length * ArmPixels);
    }

    /// <summary>핸들 끝 픽셀에서 기울기. 수직에 가까우면 급경사로 — 무한 탄젠트(계단)는 여기서 안 만든다.</summary>
    public static double SlopeFromHandle(Pixel origin, Pixel handle, bool inward, GraphCamera camera)
    {
        double dx = handle.X - origin.X;

        // 팔이 진행 방향을 거슬러 넘어가도 기울기는 그 쪽 부호로 — in 팔은 왼쪽, out 팔은 오른쪽이다.
        if (inward ? dx > -1 : dx < 1)
        {
            dx = inward ? -1 : 1;
        }

        double dy = handle.Y - origin.Y;
        return SlopeFromHandleDelta(dx, dy, camera.PixelsPerSecond, camera.PixelsPerValue);
    }
}
