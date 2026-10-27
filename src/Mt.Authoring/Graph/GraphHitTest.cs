namespace Mt.Authoring.Graph;

/// <summary>픽셀 히트 테스트 — 키·핸들·박스. 그리기와 같은 좌표를 써야 하므로 호출부가 픽셀을 만든다.</summary>
public static class GraphHitTest
{
    public const double KeyRadius = 7;
    public const double HandleRadius = 7;

    /// <summary>반지름 안에서 가장 가까운 것. 겹치면 뒤에 그린(목록 뒤쪽) 것이 이긴다 — 화면에서 위에 보이는 것.</summary>
    public static bool TryNearest<T>(IReadOnlyList<(T Item, Pixel At)> candidates, Pixel at, double radius, out T hit)
    {
        double best = radius;
        bool found = false;
        hit = default!;

        for (int i = 0; i < candidates.Count; i++)
        {
            double d = candidates[i].At.DistanceTo(at);

            if (d <= best)
            {
                best = d;
                hit = candidates[i].Item;
                found = true;
            }
        }

        return found;
    }

    /// <summary>두 모서리로 주어진 박스 안의 것들(모서리 순서 무관).</summary>
    public static List<T> InBox<T>(IReadOnlyList<(T Item, Pixel At)> candidates, Pixel a, Pixel b)
    {
        double left = Math.Min(a.X, b.X);
        double right = Math.Max(a.X, b.X);
        double top = Math.Min(a.Y, b.Y);
        double bottom = Math.Max(a.Y, b.Y);
        var hits = new List<T>();

        foreach ((T item, Pixel at) in candidates)
        {
            if (at.X >= left && at.X <= right && at.Y >= top && at.Y <= bottom)
            {
                hits.Add(item);
            }
        }

        return hits;
    }

    /// <summary>점과 폴리라인의 거리 — 더블클릭으로 "이 커브 위에" 키를 끼울 때 어느 커브인지 고른다.</summary>
    public static double DistanceToPolyline(IReadOnlyList<Pixel> polyline, Pixel at)
    {
        if (polyline.Count == 0)
        {
            return double.PositiveInfinity;
        }

        double best = polyline[0].DistanceTo(at);

        for (int i = 1; i < polyline.Count; i++)
        {
            best = Math.Min(best, DistanceToSegment(polyline[i - 1], polyline[i], at));
        }

        return best;
    }

    private static double DistanceToSegment(Pixel a, Pixel b, Pixel p)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;

        if (lengthSquared < 1e-12)
        {
            return a.DistanceTo(p);
        }

        double u = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return new Pixel(a.X + u * dx, a.Y + u * dy).DistanceTo(p);
    }
}
