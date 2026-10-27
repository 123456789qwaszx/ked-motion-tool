namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // 모션 샘플링과 합성 — 정본은 여기다.
    //
    // 재생(호스트의 샘플러)과 툴 프리뷰와 정지 프레임(리듀서)이 전부 이 함수를 지난다.
    // 성분마다 CurveFunctions.Evaluate를 부르므로 AnimationCurve.Evaluate와 같은 값이다
    // (CurveFunctionsAnimationCurveParityTests).
    //
    // relative의 뜻은 종류마다 다르다: 위치·회전은 start + v, 배율은 start × v.
    // 그래서 중립값(Neutral)이 위치·회전은 0, 배율은 1이고, 없는 성분은 중립값으로 친다 — 시작값이 그대로 남는다.
    //
    // 규칙(EaseFunctions·CurveFunctions와 같다): 순수 함수, UnityEngine 타입 금지.
    // ─────────────────────────────────────────────────────────────────
    public static class MotionMath
    {
        /// <summary>relative 채널의 "아무 변화 없음" 값. 위치·회전 0, 배율 1. 첫 키(그리고 restore의 마지막 키)가 이 값이어야 한다.</summary>
        public static float Neutral(MotionChannelKind kind)
            => kind == MotionChannelKind.Scale ? 1f : 0f;

        /// <summary>
        /// 채널의 원시 값. 없는 성분은 중립값이다 — 합성(Compose)이 "건드리지 않는다"로 읽는다.
        /// seconds는 키 범위 밖이면 끝값으로 클램프된다(CurveFunctions 규칙).
        /// </summary>
        public static Vec3 Sample(in MotionChannel channel, float seconds)
        {
            float neutral = Neutral(channel.Kind);

            return new Vec3(
                channel.HasX ? CurveFunctions.Evaluate(channel.X, seconds) : neutral,
                channel.HasY ? CurveFunctions.Evaluate(channel.Y, seconds) : neutral,
                channel.HasZ ? CurveFunctions.Evaluate(channel.Z, seconds) : neutral);
        }

        /// <summary>
        /// 시작값 위에 채널을 얹은 실제 값. 한 함수가 모션의 모양을 전부 정한다.
        ///   Relative : 위치·회전 start + v · 배율 start × v (없는 성분은 중립값이라 start 그대로)
        ///   Absolute : v, 없는 성분은 start 유지
        /// </summary>
        public static Vec3 Compose(in MotionChannel channel, Vec3 start, float seconds)
            => Apply(channel, start, Sample(channel, seconds));

        /// <summary>
        /// 채널의 종점 — 마지막 키를 합성한 값. 규칙이 "마지막 키 Time = 길이"를 보장하므로
        /// Compose(duration)과 같지만, 길이를 몰라도 되게 키에서 직접 읽는다.
        /// </summary>
        public static Vec3 EndValue(in MotionChannel channel, Vec3 start)
        {
            float neutral = Neutral(channel.Kind);

            Vec3 last = new(
                channel.HasX ? channel.X[channel.X.Length - 1].Value : neutral,
                channel.HasY ? channel.Y[channel.Y.Length - 1].Value : neutral,
                channel.HasZ ? channel.Z[channel.Z.Length - 1].Value : neutral);

            return Apply(channel, start, last);
        }

        private static Vec3 Apply(in MotionChannel channel, Vec3 start, Vec3 v)
        {
            if (channel.Mode == MotionChannelMode.Relative)
                return channel.Kind == MotionChannelKind.Scale ? Vec3.Scale(start, v) : start + v;

            return new Vec3(
                channel.HasX ? v.X : start.X,
                channel.HasY ? v.Y : start.Y,
                channel.HasZ ? v.Z : start.Z);
        }

        /// <summary>
        /// 채널의 클레임 — 장부 게시·트윈 종점·상태 폴드 세 갈래가 같은 값을 본다. commit 모션에서만 뜻이 있다
        /// (restore는 남기는 것이 없다 — 호출부가 End를 보고 부르지 않는다).
        /// 종류가 StageNodeClaimKind로 가는 자리는 여기 하나다.
        /// </summary>
        public static StageNodeClaim ClaimFor(in MotionChannel channel, string nodeKey, Vec3 start)
        {
            Vec3 end = EndValue(channel, start);

            return channel.Kind switch
            {
                MotionChannelKind.Position => StageNodeClaim.AnchoredPosition(nodeKey, end.XY),
                MotionChannelKind.Scale => StageNodeClaim.LocalScaleXY(nodeKey, end.XY),
                MotionChannelKind.Euler => StageNodeClaim.LocalEuler(nodeKey, end),
                _ => throw new System.ArgumentOutOfRangeException(nameof(channel), channel.Kind, "모르는 채널 종류"),
            };
        }

        /// <summary>채널 종류에 맞는 시작값을 노드 상태에서 읽는다 — Position은 z=0, Scale은 z를 그대로 든다.</summary>
        public static Vec3 StartOf(in MotionChannel channel, in RectNodeState state)
            => channel.Kind switch
            {
                MotionChannelKind.Position => new Vec3(state.AnchoredPosition, 0f),
                MotionChannelKind.Scale => state.LocalScale,
                MotionChannelKind.Euler => state.LocalEulerAngles,
                _ => throw new System.ArgumentOutOfRangeException(nameof(channel), channel.Kind, "모르는 채널 종류"),
            };
    }
}
