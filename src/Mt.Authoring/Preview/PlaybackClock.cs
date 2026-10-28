namespace Mt.Authoring.Preview;

public enum PlaybackMode
{
    /// <summary>끝에서 멈춘다 — 끝 프레임이 restore/commit 규칙을 보여 주는 자리.</summary>
    Once,

    /// <summary>범위 끝에서 처음으로 감는다.</summary>
    Loop,

    /// <summary>범위 끝에서 되돌아온다(마야 Oscillate).</summary>
    Oscillate,
}

/// <summary>
/// 재생 시계 — 순수 상태 기계. 실제 시간은 호스트가 <see cref="Advance"/>로 넣는다(테스트가 시계를 쥔다).
/// 재생 범위(<see cref="RangeStart"/>~<see cref="RangeEnd"/>)는 길이 안의 구간이고 기본은 전체다.
/// 길이가 바뀌면 끝에 붙어 있던 범위 끝은 새 길이를 따라간다.
/// </summary>
public sealed class PlaybackClock
{
    private bool _rangeEndFollowsDuration = true;
    private int _direction = 1;

    public double Time { get; private set; }

    public double Duration { get; private set; } = 1;

    public double RangeStart { get; private set; }

    public double RangeEnd { get; private set; } = 1;

    public bool IsPlaying { get; private set; }

    /// <summary>거꾸로 재생 중인가(마야 Play Backwards). 왕복은 방향이 번갈아 바뀐다.</summary>
    public bool IsReversed => _direction < 0;

    public PlaybackMode Mode { get; set; } = PlaybackMode.Loop;

    /// <summary>호환용 — Loop ↔ Once. 왕복은 <see cref="Mode"/>로.</summary>
    public bool Loop
    {
        get => Mode != PlaybackMode.Once;
        set => Mode = value ? PlaybackMode.Loop : PlaybackMode.Once;
    }

    /// <summary>재생 속도 배율(0.25·0.5·1·2…). 양수.</summary>
    public double Speed { get; set; } = 1;

    /// <summary>
    /// 1프레임 단위 재생(마야 Real-time 24fps 표시) — 속도는 실제 시간을 따르되 보이는 시각은 정수 프레임(24fps)이다.
    /// 꺼져 있으면 흐른 시간 그대로(연속).
    /// </summary>
    public bool WholeFrames { get; set; }

    /// <summary>24fps 프레임 길이(초).</summary>
    public static double Frame => 1.0 / Ked.Presentation.Core.DurationToken.FramesPerSecond;

    // 1프레임 단위 재생일 때 실제로 흐른 시각 — Time은 이것을 프레임에 맞춘 값이다.
    private double _exact;

    public void SetDuration(double seconds)
    {
        Duration = Math.Max(1e-3, seconds);

        if (_rangeEndFollowsDuration)
        {
            RangeEnd = Duration;
        }

        RangeEnd = Math.Clamp(RangeEnd, 1e-3, Duration);
        RangeStart = Math.Clamp(RangeStart, 0, RangeEnd - 1e-3);
        Time = Math.Clamp(Time, 0, Duration);
    }

    /// <summary>재생 범위. 끝이 길이와 같으면 길이를 따라간다. 시작 ≥ 끝이면 거부한다.</summary>
    public void SetRange(double start, double end)
    {
        start = Math.Clamp(start, 0, Duration);
        end = Math.Clamp(end, 0, Duration);

        if (end - start < 1e-3)
        {
            throw new ArgumentException("재생 범위의 끝은 시작보다 커야 한다.");
        }

        RangeStart = start;
        RangeEnd = end;
        _rangeEndFollowsDuration = Math.Abs(end - Duration) < 1e-9;
    }

    public void ResetRange()
    {
        RangeStart = 0;
        RangeEnd = Duration;
        _rangeEndFollowsDuration = true;
    }

    public void Play()
    {
        _direction = 1;

        // 범위 밖이거나 끝에 서 있으면 범위 처음부터.
        if (Time < RangeStart - 1e-9 || Time > RangeEnd + 1e-9 || (Mode == PlaybackMode.Once && Time >= RangeEnd - 1e-9))
        {
            Time = RangeStart;
        }

        _exact = Time;
        IsPlaying = true;
    }

    /// <summary>거꾸로 재생(마야 ◀). 범위 밖이거나 시작에 서 있으면 범위 끝부터.</summary>
    public void PlayBackward()
    {
        _direction = -1;

        if (Time < RangeStart - 1e-9 || Time > RangeEnd + 1e-9 || (Mode == PlaybackMode.Once && Time <= RangeStart + 1e-9))
        {
            Time = RangeEnd;
        }

        _exact = Time;
        IsPlaying = true;
    }

    public void Pause() => IsPlaying = false;

    public void Toggle()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Stop()
    {
        IsPlaying = false;
        _direction = 1;
        Time = RangeStart;
    }

    public void ScrubTo(double seconds)
    {
        Time = Math.Clamp(seconds, 0, Duration);
        _exact = Time;
    }

    /// <summary>재생 중이면 흐른 시간 × 속도만큼 간다. 바뀌었으면 true.</summary>
    public bool Advance(double deltaSeconds)
    {
        if (!IsPlaying || deltaSeconds <= 0)
        {
            return false;
        }

        double span = RangeEnd - RangeStart;
        double next = (WholeFrames ? _exact : Time) + deltaSeconds * Math.Max(0, Speed) * _direction;

        switch (Mode)
        {
            case PlaybackMode.Once:
                if (_direction > 0 && next >= RangeEnd)
                {
                    next = RangeEnd;
                    IsPlaying = false;
                }
                else if (_direction < 0 && next <= RangeStart)
                {
                    next = RangeStart;
                    IsPlaying = false;
                }

                break;

            case PlaybackMode.Loop:
                if (next >= RangeEnd)
                {
                    next = RangeStart + (next - RangeStart) % span;
                }
                else if (next < RangeStart)
                {
                    next = RangeEnd - (RangeStart - next) % span;
                }

                break;

            case PlaybackMode.Oscillate:
                while (next > RangeEnd || next < RangeStart)
                {
                    if (next > RangeEnd)
                    {
                        next = RangeEnd - (next - RangeEnd);
                        _direction = -1;
                    }
                    else
                    {
                        next = RangeStart + (RangeStart - next);
                        _direction = 1;
                    }
                }

                break;
        }

        _exact = next;

        if (!WholeFrames || !IsPlaying)
        {
            Time = next;
            return true;
        }

        double snapped = Math.Clamp(Math.Round(next / Frame) * Frame, RangeStart, RangeEnd);
        bool changed = Math.Abs(snapped - Time) > 1e-12;
        Time = snapped;
        return changed;
    }
}
