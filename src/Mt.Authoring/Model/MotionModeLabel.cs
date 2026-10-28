using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 재생 기준(relative/absolute)의 화면 이름 — 상대값 · 절대값(소유자, 2026-10-28). 대본·파일에는 <see cref="MotionModeToken"/> 그대로 쓴다.
/// </summary>
public static class MotionModeLabel
{
    public static string Of(MotionChannelMode mode) => mode switch
    {
        MotionChannelMode.Absolute => "절대값",
        _ => "상대값",
    };
}
