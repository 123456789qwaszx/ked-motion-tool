using System.Globalization;

namespace Mt.Authoring.Graph;

/// <summary>시간을 화면에 어떻게 보이나 — 마야처럼 프레임이 기본이다. 내부 값은 언제나 초다.</summary>
public enum TimeDisplay
{
    Frames,
    Seconds,
}

/// <summary>
/// 초 ↔ 화면 글자. 프레임은 24fps(<see cref="TimeSnap.FramesPerSecond"/>). 읽을 때는 꼬리 글자로 단위를 고를 수 있다:
/// <c>12</c>(표시 단위) · <c>12f</c>(프레임) · <c>0.5s</c>(초).
/// </summary>
public static class TimeText
{
    public static string Format(double seconds, TimeDisplay display)
    {
        if (Math.Abs(seconds) < 1e-9)
        {
            seconds = 0; // 스냅이 남긴 -0.0이 "-0"으로 보이지 않게
        }

        if (display == TimeDisplay.Seconds)
        {
            return seconds.ToString("0.###", CultureInfo.InvariantCulture) + "s";
        }

        double frames = TimeSnap.ToFrames(seconds);
        double rounded = Math.Round(frames);
        return Math.Abs(frames - rounded) < 1e-3
            ? rounded.ToString("0", CultureInfo.InvariantCulture)
            : frames.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>숫자 하나를 초로. 단위 꼬리(f·s)가 있으면 그것을, 없으면 <paramref name="display"/>를 따른다.</summary>
    public static bool TryParse(string? text, TimeDisplay display, out double seconds)
    {
        seconds = 0;
        string s = (text ?? "").Trim();

        if (s.Length == 0)
        {
            return false;
        }

        TimeDisplay unit = display;
        char tail = char.ToLowerInvariant(s[^1]);

        if (tail == 'f')
        {
            unit = TimeDisplay.Frames;
            s = s[..^1];
        }
        else if (tail == 's')
        {
            unit = TimeDisplay.Seconds;
            s = s[..^1];
        }

        if (!double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return false;
        }

        seconds = unit == TimeDisplay.Frames ? value / TimeSnap.FramesPerSecond : value;
        return true;
    }

    /// <summary>표시 단위의 값 → 초. 상대 입력(<c>+=3</c>)이 표시 단위로 적힌 것을 초로 옮길 때 쓴다.</summary>
    public static double ToSeconds(double shown, TimeDisplay display)
        => display == TimeDisplay.Frames ? shown / TimeSnap.FramesPerSecond : shown;

    public static double FromSeconds(double seconds, TimeDisplay display)
        => display == TimeDisplay.Frames ? TimeSnap.ToFrames(seconds) : seconds;

    /// <summary>프레임 눈금 간격(프레임 수) — 라벨이 겹치지 않는 1·2·5·10·20·50·100… 중 첫 것.</summary>
    public static int FrameTickStep(double pixelsPerFrame, double minPixelsPerTick = 40)
    {
        int[] steps = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 };

        foreach (int step in steps)
        {
            if (step * pixelsPerFrame >= minPixelsPerTick)
            {
                return step;
            }
        }

        return steps[^1];
    }
}
