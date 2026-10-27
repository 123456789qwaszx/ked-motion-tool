using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // 모션 검증 — 내보내기(에디터)와 런타임 로더와 저작 도구가 같은 규칙을 써야 한다.
    // 한쪽만 알면 "툴에서는 되는데 재생에서 사라지는" 모션이 생긴다(CurveKindRules와 같은 사정).
    //
    // 어긋난 모션은 호출부가 로그 + 무시한다 — 조용히 빠뜨리지 않는다.
    // ─────────────────────────────────────────────────────────────────
    public static class MotionRules
    {
        /// <summary>이름은 커맨드 토큰에 실린다 — 이징 곡선과 같은 규칙.</summary>
        public static readonly Regex NameRule = new("^[a-z0-9_]+$", RegexOptions.Compiled);

        /// <summary>시각·값 판정 허용 오차. JSON 왕복의 부동소수 잡음만 흡수한다.</summary>
        public const float Tolerance = 1e-4f;

        /// <summary>
        /// 키잉 금지 노드. Track_Focus는 저작값이 아니라 해(解)다 — focus 프리셋 × ScreenPoint × 정착 체인에서
        /// 풀려 나온다. 직접 키잉하면 place가 다음에 푸는 값과 싸운다.
        /// </summary>
        public static readonly IReadOnlyCollection<string> ForbiddenNodes = new[] { "CharSlot_Track_Focus" };

        /// <summary>
        /// 클립 전체를 검증한다. rigNodeIds를 주면 노드가 그 스키마에 있는지도 본다(null이면 생략 —
        /// 스키마가 없는 자리, 예: 단위 테스트).
        /// </summary>
        public static bool TryValidate(MotionClip clip, ICollection<string> rigNodeIds, out string reason)
        {
            if (clip == null)
            {
                reason = "클립이 null이다";
                return false;
            }

            if (string.IsNullOrEmpty(clip.Name) || !NameRule.IsMatch(clip.Name))
            {
                reason = $"이름은 [a-z0-9_]+ 여야 한다 ('{clip.Name}')";
                return false;
            }

            if (!(clip.Duration > 0f))
            {
                reason = $"길이가 0보다 커야 한다 ({clip.Duration})";
                return false;
            }

            if (clip.Channels == null || clip.Channels.Length == 0)
            {
                reason = "채널이 하나 이상이어야 한다";
                return false;
            }

            if (!Enum.IsDefined(typeof(MotionEnd), clip.End))
            {
                reason = $"end는 restore 또는 commit이어야 한다 ({(int)clip.End})";
                return false;
            }

            HashSet<(string, MotionChannelKind)> seen = new();

            for (int i = 0; i < clip.Channels.Length; i++)
            {
                if (!TryValidateChannel(clip.Channels[i], clip.Duration, clip.End, rigNodeIds, out string why))
                {
                    reason = $"채널 {i}: {why}";
                    return false;
                }

                if (!seen.Add((clip.Channels[i].Node, clip.Channels[i].Kind)))
                {
                    reason = $"채널 {i}: 같은 노드·종류의 채널이 둘이다 ({clip.Channels[i].Node} {clip.Channels[i].Kind})";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        public static bool TryValidateChannel(
            in MotionChannel channel, float duration, MotionEnd end, ICollection<string> rigNodeIds, out string reason)
        {
            if (string.IsNullOrEmpty(channel.Node))
            {
                reason = "노드가 비어 있다";
                return false;
            }

            foreach (string forbidden in ForbiddenNodes)
            {
                if (channel.Node == forbidden)
                {
                    reason = $"'{channel.Node}'는 키잉할 수 없다 — 저작값이 아니라 해(解)다";
                    return false;
                }
            }

            if (rigNodeIds != null && !rigNodeIds.Contains(channel.Node))
            {
                reason = $"리그 스키마에 없는 노드다 ('{channel.Node}')";
                return false;
            }

            if (!Enum.IsDefined(typeof(MotionChannelKind), channel.Kind))
            {
                reason = $"모르는 채널 종류다 ({(int)channel.Kind})";
                return false;
            }

            if (!Enum.IsDefined(typeof(MotionChannelMode), channel.Mode))
            {
                reason = $"mode는 offset 또는 absolute여야 한다 ({(int)channel.Mode})";
                return false;
            }

            if (channel.Kind != MotionChannelKind.Euler && channel.Z != null)
            {
                reason = $"{channel.Kind}에는 Z 성분이 없다";
                return false;
            }

            if (!channel.HasX && !channel.HasY && !channel.HasZ)
            {
                reason = "성분(X·Y·Z)이 하나는 있어야 한다";
                return false;
            }

            // restore는 "아무것도 남기지 않는다"는 보증이다 — absolute 채널은 끝에서 시작값으로 튈 수밖에 없으니 금지.
            if (end == MotionEnd.Restore && channel.Mode != MotionChannelMode.Relative)
            {
                reason = "restore 모션의 채널은 relative여야 한다 (absolute는 끝에서 튄다)";
                return false;
            }

            float neutral = MotionMath.Neutral(channel.Kind);

            return TryValidateComponent("X", channel.X, channel.Mode, end, neutral, duration, out reason)
                && TryValidateComponent("Y", channel.Y, channel.Mode, end, neutral, duration, out reason)
                && TryValidateComponent("Z", channel.Z, channel.Mode, end, neutral, duration, out reason);
        }

        private static bool TryValidateComponent(
            string label, CurveKey[] keys, MotionChannelMode mode, MotionEnd end, float neutral, float duration, out string reason)
        {
            reason = null;

            if (keys == null || keys.Length == 0)
                return true;

            if (keys.Length < 2)
            {
                reason = $"{label}: 키가 2개 이상이어야 한다";
                return false;
            }

            if (Math.Abs(keys[0].Time) > Tolerance)
            {
                reason = $"{label}: 첫 키 시각이 0이 아니다 ({keys[0].Time})";
                return false;
            }

            float lastTime = keys[keys.Length - 1].Time;

            if (Math.Abs(lastTime - duration) > Tolerance)
            {
                reason = $"{label}: 마지막 키 시각이 길이와 다르다 ({lastTime} ≠ {duration})";
                return false;
            }

            for (int i = 1; i < keys.Length; i++)
            {
                if (keys[i].Time <= keys[i - 1].Time)
                {
                    reason = $"{label}: 키 시각이 오름차순이 아니다 (index {i})";
                    return false;
                }
            }

            // relative는 "지금 자리에서" 시작한다 — 첫 키가 중립값(위치·회전 0, 배율 1)이 아니면 첫 프레임이 튄다.
            if (mode == MotionChannelMode.Relative && Math.Abs(keys[0].Value - neutral) > Tolerance)
            {
                reason = $"{label}: relative 채널의 첫 키 값이 중립값 {neutral}이 아니다 ({keys[0].Value})";
                return false;
            }

            // restore는 마지막 키도 중립값이어야 한다 — 선언과 키가 어긋나면 끝에서 튀고, 리듀서의 무변과도 갈린다.
            if (end == MotionEnd.Restore && Math.Abs(keys[keys.Length - 1].Value - neutral) > Tolerance)
            {
                reason = $"{label}: restore 모션의 마지막 키 값이 중립값 {neutral}이 아니다 ({keys[keys.Length - 1].Value}) — 남기려면 commit";
                return false;
            }

            return true;
        }
    }
}
