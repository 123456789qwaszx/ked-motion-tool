namespace Mt.Authoring.Preview;

/// <summary>
/// 재생 시계 — 순수 상태 기계. 실제 시간은 호스트가 <see cref="Advance"/>로 넣는다(테스트가 시계를 쥔다).
/// 길이에 닿으면 루프면 감고, 아니면 끝에서 멈춘다 — 끝 프레임이 restore/commit 규칙을 보여 주는 자리다.
/// </summary>
public sealed class PlaybackClock
{
    public double Time { get; private set; }

    public double Duration { get; private set; } = 1;

    public bool IsPlaying { get; private set; }

    public bool Loop { get; set; } = true;

    public void SetDuration(double seconds)
    {
        Duration = Math.Max(1e-3, seconds);
        Time = Math.Clamp(Time, 0, Duration);
    }

    public void Play()
    {
        // 끝에 서 있으면 처음부터.
        if (!Loop && Time >= Duration - 1e-9)
        {
            Time = 0;
        }

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
        Time = 0;
    }

    public void ScrubTo(double seconds) => Time = Math.Clamp(seconds, 0, Duration);

    /// <summary>재생 중이면 흐른 시간만큼 간다. 바뀌었으면 true.</summary>
    public bool Advance(double deltaSeconds)
    {
        if (!IsPlaying || deltaSeconds <= 0)
        {
            return false;
        }

        double next = Time + deltaSeconds;

        if (next >= Duration)
        {
            if (Loop)
            {
                next %= Duration;
            }
            else
            {
                next = Duration;
                IsPlaying = false;
            }
        }

        Time = next;
        return true;
    }
}
