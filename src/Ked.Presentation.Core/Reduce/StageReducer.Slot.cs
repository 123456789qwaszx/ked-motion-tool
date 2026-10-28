namespace Ked.Presentation.Core
{
    /// <summary>
    /// 슬롯 스폰 · 배역/별칭 · 구조 축 — 다른 모든 폴드의 전제다.
    /// 슬롯이 서야 노드가 생기고, 배역/별칭이 있어야 커맨드가 대상을 푼다.
    /// </summary>
    public static partial class StageReducer
    {
        // ── 슬롯 ─────────────────────────────────────────────────────

        private static bool ApplySlot(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            string stageKey, string layerKey, out string reason)
        {
            if (!TryGetSlotKey(cmd, out string slotKey, out reason))
                return false;

            return SpawnSlot(state, slotKey, tuning, stageKey, layerKey, out reason);
        }

        /// <summary>
        /// 슬롯 하나를 세운다 — slot 계열과 bg_* 계열(StageReducer.Background.cs)이 같은 자리를 지난다.
        /// 배경도 캐릭터 리그다: 배역을 캐스팅하지 않은 슬롯일 뿐이다.
        ///
        /// **같은 키 재선언은 리그를 새로 세운다.** 런타임 SetupCharRigCommand는 매번 새 리그를 짓고
        /// CharacterRigRegistry.Register가 옛 리그를 부순다 — 노드는 스키마 초기값으로, 가시성은 스키마 α로
        /// 돌아간다. 배역·변형·별칭은 CastRegistry 쪽이라 남는다. 그래서 여기서도 노드와 α만 되돌린다.
        /// (예전 주석은 "런타임은 기존 리그를 재사용한다"였다 — 사실이 아니었다. 배경을 합치며 같은 키의
        ///  `bg_spawn` 재선언을 검토하다 드러나 바로잡았다, 2026-10-29. 배경 그림만 바꾸는 것은 `bg_sprite`다.)
        /// </summary>
        private static bool SpawnSlot(
            StageState state, string slotKey, StageReducerTuning tuning,
            string stageKey, string layerKey, out string reason)
        {
            reason = null;

            if (tuning.RigSchemas == null)
            {
                reason = "tuning에 리그 스키마가 없어 슬롯을 세울 수 없다";
                return false;
            }

            if (!TryFindCharacterRig(tuning.RigSchemas, out RigSchemaRigDto rig))
            {
                reason = "리그 스키마 덤프에 character 리그가 없다";
                return false;
            }

            if (state.HasSlot(slotKey))
            {
                ResetSlotToSchema(state, slotKey, rig);
            }
            else
            {
                // v1 가정 1: 컨테이너는 항등이므로 리그를 루트 공간 직속으로 세운다.
                RigSchemaLoader.AddRigTo(state.Nodes, rig, keyPrefix: slotKey + "/");
                state.RegisterSlot(slotKey);
            }

            state.SetAttachment(slotKey, new SlotAttachment(stageKey, layerKey));

            // 초기 가시성: 덤프의 CanvasGroup alpha.
            // 초상·오버레이·이모지 루트는 0으로 태어난다 — 기본값 1로 두면
            // show 전 구간이 전부 어긋난다(첫 리포트의 최대 불일치 클래스였다).
            for (int i = 0; i < rig.nodes.Count; i++)
            {
                RigSchemaNodeDto node = rig.nodes[i];

                if (node != null && node.hasCanvasGroup)
                    state.SetAlpha(StageState.NodeKeyOf(slotKey, node.id), node.canvasGroupAlpha);
            }

            return true;
        }

        /// <summary>재선언 — 같은 키·같은 부모의 노드를 스키마 초기 상태로 덮는다(새로 세운 것과 같다).</summary>
        private static void ResetSlotToSchema(StageState state, string slotKey, RigSchemaRigDto rig)
        {
            for (int i = 0; i < rig.nodes.Count; i++)
            {
                RigSchemaNodeDto node = rig.nodes[i];

                if (node == null)
                    continue;

                string key = StageState.NodeKeyOf(slotKey, node.id);

                if (state.Nodes.Contains(key))
                    state.Nodes.SetState(key, RigSchemaLoader.ToState(node, key));
            }
        }

        private static bool TryFindCharacterRig(RigSchemasFileDto file, out RigSchemaRigDto rig)
        {
            rig = null;

            if (file.rigs == null)
                return false;

            for (int i = 0; i < file.rigs.Count; i++)
            {
                if (file.rigs[i]?.rigKind == "character")
                    rig = file.rigs[i];
            }

            return rig != null;
        }

        // ── 배역·별칭 ────────────────────────────────────────────────

        /// <summary>
        /// cast (slot, characterKey, variant="a", emotion="1").
        ///
        /// 브리지는 이걸 셋으로 팬아웃한다: 배역 → pose(변형) → face(표정+사이징).
        /// 그 순서를 그대로 따른다 — 배역이 변형을 비우므로 pose가 먼저여야 한다.
        /// </summary>
        private static bool ApplyCast(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string characterKey = cmd.Arg(1);

            if (string.IsNullOrEmpty(characterKey))
            {
                reason = "cast에 캐릭터 키가 없다";
                return false;
            }

            state.SetCast(slotKey, characterKey);
            state.SetVariant(slotKey, cmd.Arg(2, PortraitDimensionsFileDto.DefaultVariantKey));

            // 사이징이 실패해도 배역은 접혔다 — 커맨드 전체를 Unhandled로 돌리면
            // "cast를 못 접었다"는 거짓말이 된다. 못 접은 축만 기록한다.
            if (!ApplyPortraitSizing(state, slotKey, cmd.Arg(3, "1"), tuning, out string sizingReason))
                state.AddUnhandled(cmd, sizingReason);

            return true;
        }

        private static bool ApplyActor(StageState state, in StageCommand cmd, out string reason)
        {
            reason = null;

            string aliasSymbol = cmd.Arg(0);
            string targetKey = cmd.Arg(1);

            if (string.IsNullOrEmpty(aliasSymbol) || string.IsNullOrEmpty(targetKey))
            {
                reason = "actor 인자가 모자란다 (별칭, 대상)";
                return false;
            }

            state.SetAlias(aliasSymbol, targetKey);
            return true;
        }

        // ── 구조 ─────────────────────────────────────────────────────

        private static bool ApplyCharTo(
            StageState state, in StageCommand cmd, string stageKey, string layerKey, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            state.SetAttachment(slotKey, new SlotAttachment(stageKey, layerKey));
            return true;
        }
    }
}
