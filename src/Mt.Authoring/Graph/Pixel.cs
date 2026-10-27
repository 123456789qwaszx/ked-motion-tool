namespace Mt.Authoring.Graph;

/// <summary>화면 픽셀 좌표. Avalonia의 Point를 도메인에 들이지 않으려고 따로 둔다 — Mt.Authoring은 Avalonia를 모른다.</summary>
public readonly record struct Pixel(double X, double Y)
{
    public double DistanceTo(Pixel other)
        => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));
}

/// <summary>그래프 공간의 점 — 초와 값.</summary>
public readonly record struct GraphPoint(double T, double V);
