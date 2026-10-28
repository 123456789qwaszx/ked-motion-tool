namespace Ked.Presentation.Core
{
    /// <summary>
    /// bg_* — 배경은 **캐스팅하지 않은 캐릭터 슬롯**이다.
    ///
    /// 별도 배경 리그가 있던 시절(2026-05-21 ~ 10)의 흔적이 `bg_` 접두사다. 리그를 하나로 합친 뒤에도
    /// 어휘는 남겼다 — 대본·문서·외부 도구가 그대로이고, "이건 배경"이라는 저작 의도가 대본에 남는다.
    /// 그래서 여기 폴드는 새로 발명한 것이 없다: 슬롯 스폰 · 사이징 · fade · move · scale을
    /// 캐릭터 폴드와 **같은 노드, 같은 리덕션**으로 접는다. 다른 것은 종횡비의 출처 하나다
    /// (초상 DB가 아니라 background-dimensions.json).
    ///
    /// 파서(StageCommandParser.Background)와의 대응(PLAN_background-as-slot "명령 대응표"):
    ///   bg_spawn / bg_slotNN → SetupCharRig(far, 배경) + 스프라이트            → 여기: SpawnSlot + 배역 풀기 + 사이징 (숨은 채)
    ///   bg_sprite            → 스프라이트 키 Spec(HeightFit)                 → 사이징
    ///   bg_fade_in/out       → FadeIn/Out(CharacterPortraitSprite_Root)       → ApplyFade (디스패치에서 직접)
    ///   bg_move              → MoveBy(CharSlot_Track)                         → ApplyMoveBy (디스패치에서 직접)
    ///   bg_scale             → ScaleTo(CharacterPortrait_ActingScale)         → ApplyPortraitScaleTo (디스패치에서 직접)
    /// </summary>
    public static partial class StageReducer
    {
        /// <summary>
        /// bg_spawn / bg_slotNN (rigKey, spriteKey[, layer]).
        ///
        /// 슬롯을 세우고 스프라이트 크기를 맞춘다. **숨은 채 선다**(스프라이트 루트 α 0) — 캐릭터 slot과 같다.
        /// 대본은 bg_fade_in으로 들인다. 옛 배경 리그는 세우면 바로 보여서, 대본마다 뒤에 쓴 bg_fade_in 24fr이
        /// 한 번도 보이지 않았다 — 리그를 합치면서 작가가 쓴 대로 동작하게 했다(PLAN D7).
        ///
        /// 사이징이 실패해도 슬롯은 섰다 — 커맨드 전체를 Unhandled로 돌리면 "슬롯을 못 세웠다"는
        /// 거짓말이 된다. cast와 같은 규율로 못 접은 축만 기록한다.
        ///
        /// **배역 있는 슬롯을 덮으면 배역을 푼다.** 배경은 캐스팅하지 않은 슬롯이라는 정의를 지키려는 것이다 —
        /// 배역이 남으면 `fade_in harin`이 배경을 페이드하고, 배역 기반 축(focus_on)이 배경을 초점면에 넣는다.
        /// 런타임 SetupCharRigCommand(asBackground)도 같은 자리에서 배역을 풀고 경고를 남긴다.
        /// 그림만 바꾸려면 `bg_sprite`를 쓴다. 같은 키로 `bg_spawn`을 다시 부르면 SpawnSlot의 재선언 규칙을 탄다 —
        /// 새 리그가 숨은 채 서므로(노드·α가 스키마로) 다시 `bg_fade_in`해야 보인다.
        /// </summary>
        private static bool ApplyBackgroundSpawn(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            string stageKey, string layerKey, out string reason)
        {
            if (!TryGetSlotKey(cmd, out string slotKey, out reason))
                return false;

            if (!SpawnSlot(state, slotKey, tuning, stageKey, layerKey, out reason))
                return false;

            state.ClearCast(slotKey);

            if (!ApplyBackgroundSizing(state, slotKey, cmd.Arg(1), tuning, out string sizingReason))
                state.AddUnhandled(cmd, sizingReason);

            return true;
        }

        /// <summary>bg_sprite (rigKey, spriteKey) — 그림만 간다. 가시성은 건드리지 않는다.</summary>
        private static bool ApplyBackgroundSprite(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            return ApplyBackgroundSizing(state, slotKey, cmd.Arg(1), tuning, out reason);
        }

        /// <summary>
        /// 배경 사이징 — 초상과 같은 리덕션(폭 = 부모 높이 × 종횡비), 종횡비만 배경 치수표에서.
        /// 치수표가 없거나 키가 없으면 false — 짐작으로 잇지 않는다.
        /// </summary>
        private static bool ApplyBackgroundSizing(
            StageState state, string slotKey, string spriteKey,
            StageReducerTuning tuning, out string reason)
        {
            if (tuning.BackgroundDimensions == null)
            {
                reason = "배경 치수 덤프가 tuning에 없다 (background-dimensions.json)";
                return false;
            }

            if (!tuning.BackgroundDimensions.TryGetAspect(spriteKey, out float aspect, out reason))
                return false;

            float parentHeight = state.Nodes
                .GetRectSize(StageState.NodeKeyOf(slotKey, PortraitSizingStageReduction.ParentNodeId))
                .Y;

            state.Apply(PortraitSizingReduction.Reduce(
                StageState.NodeKeyOf(slotKey, PortraitSizingStageReduction.ImageNodeId),
                parentHeight,
                aspect));

            reason = null;
            return true;
        }
    }
}
