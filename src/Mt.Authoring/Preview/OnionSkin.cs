using Mt.Authoring.Graph;

namespace Mt.Authoring.Preview;

/// <summary>어니언 스킨 — 현재 시각 앞뒤 몇 프레임(24fps)의 반투명 포즈. 멀수록 옅다.</summary>
public static class OnionSkin
{
    public static List<(double Time, double Opacity)> Frames(double now, double duration, int framesEachSide)
    {
        var frames = new List<(double, double)>();

        if (framesEachSide <= 0 || duration <= 0)
        {
            return frames;
        }

        double frame = 1.0 / TimeSnap.FramesPerSecond;

        for (int i = -framesEachSide; i <= framesEachSide; i++)
        {
            if (i == 0)
            {
                continue;
            }

            double t = now + i * frame;

            if (t < 0 || t > duration)
            {
                continue;
            }

            double opacity = 0.35 * (1.0 - (Math.Abs(i) - 1) / (double)framesEachSide);
            frames.Add((t, opacity));
        }

        return frames;
    }
}
