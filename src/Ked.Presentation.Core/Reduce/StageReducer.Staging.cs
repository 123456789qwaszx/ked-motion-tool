namespace Ked.Presentation.Core
{
    /// <summary>
    /// nudge · move · scale · rotate — 현재 값에 얹는 상대 변형.
    ///
    /// place/size와 달리 focus를 모른다. 표적 노드와 델타만 정하면 끝이라
    /// 폴드 본문은 전부 "토큰 파싱 → 표적 노드 → 리덕션 호출" 세 줄 모양이다.
    /// </summary>
    public static partial class StageReducer
    {
        private static bool ApplyNudge(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            float xSign, float ySign, string targetId, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string unitToken = cmd.Arg(1);

            if (!UnitToken.TryParsePixels(unitToken, tuning.ReferenceStageWidth, out float pixels))
            {
                reason = $"거리 토큰을 읽지 못했다: '{unitToken}'";
                return false;
            }

            ApplyMoveClaim(state, slotKey, targetId, relative: true, new Vec2(pixels * xSign, pixels * ySign));
            return true;
        }

        private static bool ApplyMoveBy(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!UnitToken.TryParseSignedPixels(cmd.Arg(1, "0u"), tuning.ReferenceStageWidth, out float x) ||
                !UnitToken.TryParseSignedPixels(cmd.Arg(2, "0u"), tuning.ReferenceStageWidth, out float y))
            {
                reason = $"거리 토큰을 읽지 못했다: '{cmd.Arg(1)}', '{cmd.Arg(2)}'";
                return false;
            }

            ApplyMoveClaim(state, slotKey, "CharSlot_Track", relative: true, new Vec2(x, y));
            return true;
        }

        /// <summary>
        /// 접는 것은 "위치를 바꾸지 않는다"는 사실 그 자체.
        /// 접지 않으면 프리뷰가 이 커맨드를 영원히 "반영 안 된 연출"로 짚음.
        /// </summary>
        private static bool ApplySlideIn(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
            => TryReadSlide(state, cmd, tuning, SlideMotion.DefaultInDirection, out _, out _, out reason);

        private static bool ApplySlideOut(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryReadSlide(state, cmd, tuning, SlideMotion.DefaultOutDirection,
                    out string slotKey, out Vec2 offset, out reason))
                return false;

            ApplyMoveClaim(state, slotKey, "CharSlot_Track", relative: true, offset);
            return true;
        }

        private static bool TryReadSlide(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            string defaultDirection, out string slotKey, out Vec2 offset, out string reason)
        {
            offset = Vec2.Zero;

            if (!TryGetSpawnedSlot(state, cmd, out slotKey, out reason))
                return false;

            string distanceToken = cmd.Arg(2, SlideMotion.DefaultDistanceToken);

            if (!UnitToken.TryParsePixels(distanceToken, tuning.ReferenceStageWidth, out float pixels))
            {
                reason = $"거리 토큰을 읽지 못했다: '{distanceToken}'";
                return false;
            }

            offset = SlideMotion.DirectionVector(cmd.Arg(1, defaultDirection)) * pixels;
            return true;
        }

        private static bool ApplyMoveReset(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            // 브리지는 Track과 Track_Focus 두 노드에 절대 0을 건다.
            ApplyMoveClaim(state, slotKey, "CharSlot_Track", relative: false, Vec2.Zero);
            ApplyMoveClaim(state, slotKey, "CharSlot_Track_Focus", relative: false, Vec2.Zero);
            return true;
        }

        private static void ApplyMoveClaim(
            StageState state, string slotKey, string targetId, bool relative, Vec2 delta)
        {
            string nodeKey = StageState.NodeKeyOf(slotKey, targetId);

            state.Apply(MoveByReduction.Reduce(
                nodeKey,
                new MoveByReduction.Args(!relative, delta),
                state.Nodes.GetState(nodeKey).AnchoredPosition));
        }

        private static bool ApplyScaleBy(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NumberToken.TryParseFloat(cmd.Arg(1), out float multiplier))
            {
                reason = $"배율을 읽지 못했다: '{cmd.Arg(1)}'";
                return false;
            }

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharSlot_Scale");

            state.Apply(ScaleToReduction.Reduce(
                nodeKey,
                new ScaleToReduction.Args(true, new Vec2(multiplier, multiplier)),
                state.Nodes.GetState(nodeKey).LocalScale.XY));

            return true;
        }

        private static bool ApplyScaleReset(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharSlot_Scale");

            state.Apply(ScaleToReduction.Reduce(
                nodeKey,
                new ScaleToReduction.Args(false, Vec2.One),
                state.Nodes.GetState(nodeKey).LocalScale.XY));

            return true;
        }


        // char_scale_to — 초상 축의 절대 배율(브리지: CharacterPortrait_ActingScale).
        // scale_by가 미는 CharSlot_Scale과는 다른 노드다 — 슬롯을 키우는 것과
        // 초상만 키우는 것은 다른 일이고, 겹쳐 써도 서로를 덮지 않는다.
        private static bool ApplyPortraitScaleTo(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NumberToken.TryParseFloat(cmd.Arg(1), out float scale))
            {
                reason = $"배율을 읽지 못했다: '{cmd.Arg(1)}'";
                return false;
            }

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharacterPortrait_ActingScale");

            // 브리지가 xy 하나를 두 축에 함께 넣는다(toScale = new Vector2(xy, xy)).
            state.Apply(ScaleToReduction.Reduce(
                nodeKey,
                new ScaleToReduction.Args(false, new Vec2(scale, scale)),
                state.Nodes.GetState(nodeKey).LocalScale.XY));

            return true;
        }

        /// <summary>
        /// gesture — 무변으로 접는 것이 정답이다.
        ///
        /// 변위(t) = 진폭 × 곡선(t)이고 곡선이 (0,0)→(1,0)이라 순변위가 0이다.
        /// 라인 시작과 끝의 무대가 같으므로 정지 프레임은 손대지 않는 것이 옳다 —
        /// 그래서 리듀서가 곡선 내용을 알 필요가 없고, "이징은 종점에 관여하지 않는다"는
        /// 불변식도 지켜진다(그게 이 커맨드를 move_by 위에 얹지 않은 이유다).
        ///
        /// 슬롯 존재 검사는 한다 — 없는 슬롯이면 다른 커맨드와 같은 규약으로 사유를 남긴다.
        /// </summary>
        private static bool ApplyGesture(StageState state, in StageCommand cmd, out string reason)
            => TryGetSpawnedSlot(state, cmd, out _, out reason);

        /// <summary>
        /// motion(slot, name, mode?) — restore면 무변(gesture와 같다), commit이면 채널마다 **마지막 키**를 그 노드의
        /// 클레임으로 접는다. 종점이 평가 결과가 아니라 키 값이라 샘플링이 필요 없다 — "이징은 종점에 관여하지
        /// 않는다"가 모션에서도 그대로 선다. mode는 세 번째 인자(생략 = relative): relative는 현재 상태 위에
        /// (위치·회전 덧셈, 배율 곱셈), absolute는 그 값으로. 클립은 mode를 모른다(motions/2).
        ///
        /// 재생(MotionCommandCharR.ClaimTarget)과 같은 함수(MotionMath.ClaimFor)를 지난다 —
        /// 장부 게시·트윈 종점·상태 폴드가 같은 값을 본다.
        /// </summary>
        private static bool ApplyMotion(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string name = cmd.Arg(1);

            if (string.IsNullOrEmpty(name))
            {
                reason = "모션 이름이 없다";
                return false;
            }

            if (tuning.Motions == null || !tuning.Motions.TryGetValue(name, out MotionClip clip))
            {
                reason = tuning.Motions == null
                    ? $"모션 '{name}' — 튜닝에 motions.json이 없다"
                    : $"모션 '{name}'을 모른다 (motions.json에 없거나 검증에서 걸렸다)";
                return false;
            }

            if (!MotionModeToken.TryParse(cmd.Arg(2), out MotionChannelMode mode))
            {
                reason = $"모션 '{name}': mode는 relative|absolute 여야 한다 ('{cmd.Arg(2)}')";
                return false;
            }

            // 채널이 가리키는 노드가 하나라도 없으면 통째로 거부한다 — 반만 접힌 모션은 거짓 정지 프레임이다.
            for (int i = 0; i < clip.Channels.Length; i++)
            {
                if (!state.Nodes.Contains(StageState.NodeKeyOf(slotKey, clip.Channels[i].Node)))
                {
                    reason = $"모션 '{name}' 채널 {i}: 리그에 노드 '{clip.Channels[i].Node}'가 없다";
                    return false;
                }
            }

            // restore — 남기는 것이 없다. 노드 검사는 위에서 했다(없는 노드를 가리키는 모션은 restore라도 거짓이다).
            if (clip.End == MotionEnd.Restore)
                return true;

            for (int i = 0; i < clip.Channels.Length; i++)
            {
                ref readonly MotionChannel channel = ref clip.Channels[i];
                string nodeKey = StageState.NodeKeyOf(slotKey, channel.Node);

                Vec3 start = MotionMath.StartOf(channel, state.Nodes.GetState(nodeKey));
                state.Apply(MotionMath.ClaimFor(channel, mode, nodeKey, start));
            }

            return true;
        }

        // char_rotate_to — 초상 축의 절대 회전(브리지: CharacterPortrait_SwayPivot).
        // rotate_by가 미는 CharSlot_SwayPivot과는 다른 노드다.
        private static bool ApplyPortraitRotateTo(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NumberToken.TryParseFloat(cmd.Arg(1), out float degree))
            {
                reason = $"각도를 읽지 못했다: '{cmd.Arg(1)}'";
                return false;
            }

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharacterPortrait_SwayPivot");

            state.Apply(RotateToReduction.Reduce(
                nodeKey,
                new RotateToReduction.Args(false, new Vec3(0f, 0f, degree)),
                state.Nodes.GetState(nodeKey).LocalEulerAngles));

            return true;
        }
        private static bool ApplyRotateBy(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NumberToken.TryParseFloat(cmd.Arg(1), out float degree))
            {
                reason = $"각도를 읽지 못했다: '{cmd.Arg(1)}'";
                return false;
            }

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharSlot_SwayPivot");

            state.Apply(RotateToReduction.Reduce(
                nodeKey,
                new RotateToReduction.Args(true, new Vec3(0f, 0f, degree)),
                state.Nodes.GetState(nodeKey).LocalEulerAngles));

            return true;
        }

        private static bool ApplyRotateReset(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharSlot_SwayPivot");

            state.Apply(RotateToReduction.Reduce(
                nodeKey,
                new RotateToReduction.Args(false, Vec3.Zero),
                state.Nodes.GetState(nodeKey).LocalEulerAngles));

            return true;
        }
    }
}
