using System.Collections.Generic;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// bg_* 폴드 골든 — 배경은 캐스팅하지 않은 캐릭터 슬롯이다(StageReducer.Background.cs).
    ///
    /// 고정하는 규율:
    ///   · 배경 슬롯은 캐릭터 슬롯과 **같은 리그·같은 노드**로 선다.
    ///   · bg_spawn은 캐릭터 slot처럼 숨은 채 선다(α 0) — bg_fade_in으로 들인다.
    ///   · 종횡비는 초상 DB가 아니라 배경 치수표(키 하나)에서 온다. 없으면 그 축만 Unhandled.
    ///   · 지운 어휘(bg_size·bg_place)는 Unhandled로 소리를 낸다 — 조용히 통과하지 않는다.
    /// 합성 리그는 StageReducerTests와 같은 부모 사슬이다.
    /// </summary>
    public sealed class BackgroundReductionTests
    {
        private const float Eps = 1e-3f;

        private static Float2Dto F2(float x, float y) => new() { x = x, y = y };
        private static Float3Dto F3(float x, float y, float z) => new() { x = x, y = y, z = z };

        private static RigSchemaNodeDto Node(
            string id, string parent, bool bottomPivot = false, float? canvasGroupAlpha = null)
        {
            return new RigSchemaNodeDto
            {
                id = id,
                parent = parent,
                anchoredPosition = F2(0f, 0f),
                anchorMin = F2(0f, 0f),
                anchorMax = F2(1f, 1f),
                pivot = bottomPivot ? F2(0.5f, 0f) : F2(0.5f, 0.5f),
                sizeDelta = F2(0f, 0f),
                localScale = F3(1f, 1f, 1f),
                localEulerAngles = F3(0f, 0f, 0f),
                measuredRectSize = F2(0f, 0f),
                hasCanvasGroup = canvasGroupAlpha.HasValue,
                canvasGroupAlpha = canvasGroupAlpha ?? 1f,
            };
        }

        private static RigSchemaRigDto CharacterRig()
        {
            return new RigSchemaRigDto
            {
                rigKind = "character",
                sourcePrefab = "",
                nodes = new List<RigSchemaNodeDto>
                {
                    Node("__root", ""),
                    Node("CharSlot_Track_Focus", "__root"),
                    Node("CharSlot_DepthY", "CharSlot_Track_Focus"),
                    Node("CharSlot_DepthScale", "CharSlot_DepthY", bottomPivot: true),
                    Node("CharSlot_Track_Idle", "CharSlot_DepthScale"),
                    Node("CharSlot_Track", "CharSlot_Track_Idle"),
                    Node("CharSlot_Track_X", "CharSlot_Track"),
                    Node("CharSlot_Track_Y", "CharSlot_Track_X"),
                    Node("CharSlot_Rotation", "CharSlot_Track_Y"),
                    Node("CharSlot_SwayPivot", "CharSlot_Rotation", bottomPivot: true),
                    Node("CharSlot_Scale", "CharSlot_SwayPivot", bottomPivot: true),
                    Node("CharacterPortrait_VisualOffset", "CharSlot_Scale", bottomPivot: true),
                    Node("CharacterPortrait_Track", "CharacterPortrait_VisualOffset"),
                    Node("CharacterPortrait_Rotation", "CharacterPortrait_Track"),
                    Node("CharacterPortrait_Track_Move", "CharacterPortrait_Rotation"),
                    Node("CharacterPortrait_Track_Move_X", "CharacterPortrait_Track_Move"),
                    Node("CharacterPortrait_Track_Move_Y", "CharacterPortrait_Track_Move_X"),
                    Node("CharacterPortrait_SwayPivot", "CharacterPortrait_Track_Move_Y", bottomPivot: true),
                    Node("CharacterPortrait_Shake", "CharacterPortrait_SwayPivot"),
                    Node("CharacterPortrait_ActingScale", "CharacterPortrait_Shake"),
                    Node("CharacterPortrait_ActingScale_X", "CharacterPortrait_ActingScale"),
                    Node("CharacterPortrait_ActingScale_Y", "CharacterPortrait_ActingScale_X"),
                    Node("CharacterPortraitSprite_Root", "CharacterPortrait_ActingScale_Y", canvasGroupAlpha: 0f),
                    Node("CharacterPortraitSprite_Image", "CharacterPortraitSprite_Root"),
                },
            };
        }

        private const int RigNodeCount = 24;

        private static StageReducerTuning NewTuning(bool withBackgroundDimensions = true)
        {
            return new StageReducerTuning
            {
                RigSchemas = new RigSchemasFileDto
                {
                    capturedUnderParentSize = F2(1920f, 1080f),
                    rigs = new List<RigSchemaRigDto> { CharacterRig() },
                },
                ReferenceStageWidth = 1920f,
                BaseResolution = new Vec2(1920f, 1080f),
                BackgroundDimensions = withBackgroundDimensions ? NewBackgroundDimensions() : null,
            };
        }

        /// <summary>
        /// 실제 예제 배경의 비율 둘 — 16:9(1920×1080)와 살짝 좁은 것(1026×580).
        /// 높이가 부모 rect(1080)와 다르므로 접힌 폭은 1080 × 종횡비다.
        /// </summary>
        private static BackgroundDimensionsFileDto NewBackgroundDimensions()
        {
            return new BackgroundDimensionsFileDto
            {
                sourceFolder = "Resources/Backgrounds",
                entries = new List<BackgroundDimensionDto>
                {
                    new() { key = "class_day", width = 1026f, height = 580f },
                    new() { key = "3_한옥_에피소드시작", width = 1920f, height = 1080f },
                    new() { key = "Class_Day", width = 100f, height = 100f }, // 대소문자만 다르다 — 먼저 온 class_day가 이긴다
                },
            };
        }

        private static float ImageWidthOf(StageState state, string slotKey)
            => state.Nodes.GetState(StageState.NodeKeyOf(slotKey, "CharacterPortraitSprite_Image")).SizeDelta.X;

        private static StageCommand Cmd(string name, params string[] args)
            => new(name, args, "test.yarn:1");

        private static StageState Fold(StageReducerTuning tuning, params StageCommand[] commands)
            => StageReducer.ApplyAll(StageReducer.CreateInitialState(tuning), commands, tuning);

        private static StageState Fold(params StageCommand[] commands)
            => Fold(NewTuning(), commands);

        // ── 스폰 ─────────────────────────────────────────────────────

        [Test]
        public void bg_spawn은_캐릭터_리그를_far에_숨긴_채_세운다()
        {
            StageState state = Fold(Cmd("bg_spawn", "bg_main", "class_day"));

            Assert.That(state.HasSlot("bg_main"), Is.True);
            Assert.That(state.Nodes.Count, Is.EqualTo(RigNodeCount), "배경 전용 노드는 없다 — 같은 리그다");
            Assert.That(state.Nodes.Contains("bg_main/CharacterPortraitSprite_Image"), Is.True);

            Assert.That(state.TryGetAttachment("bg_main", out SlotAttachment attachment), Is.True);
            Assert.That(attachment.StageKey, Is.EqualTo("stage00"));
            Assert.That(attachment.LayerKey, Is.EqualTo("far"));

            // 숨은 채 선다 — 캐릭터 slot과 같다. 대본은 bg_fade_in으로 들인다.
            Assert.That(state.GetAlpha("bg_main/CharacterPortraitSprite_Root"), Is.EqualTo(0f));

            // 폭 = 부모 높이 1080 × (1026/580).
            Assert.That(ImageWidthOf(state, "bg_main"), Is.EqualTo(1080f * 1026f / 580f).Within(Eps));
            Assert.That(state.Unhandled, Is.Empty);
        }

        [Test]
        public void bg_slotNN은_스테이지를_접미사로_레이어를_셋째_인자로_받는다()
        {
            StageState state = Fold(
                Cmd("bg_slot01", "bg_a", "class_day"),
                Cmd("bg_slot02", "bg_b", "class_day", "mid"));

            state.TryGetAttachment("bg_a", out SlotAttachment a);
            Assert.That(a.StageKey, Is.EqualTo("stage01"));
            Assert.That(a.LayerKey, Is.EqualTo("far"), "레이어 기본값은 far");

            state.TryGetAttachment("bg_b", out SlotAttachment b);
            Assert.That(b.StageKey, Is.EqualTo("stage02"));
            Assert.That(b.LayerKey, Is.EqualTo("mid"));
        }

        [Test]
        public void 배경은_캐스팅되지_않는다()
        {
            StageState state = Fold(Cmd("bg_spawn", "bg_main", "class_day"));

            Assert.That(state.TryGetCharacter("bg_main", out _), Is.False,
                "배경 = 캐스팅하지 않은 슬롯. 배역 기반 축(focus_on 등)이 여기서 갈린다");
        }

        // ── 사이징 ───────────────────────────────────────────────────

        [Test]
        public void bg_sprite는_폭만_바꾸고_가시성은_건드리지_않는다()
        {
            StageState state = Fold(
                Cmd("bg_spawn", "bg_main", "class_day"),
                Cmd("bg_fade_out", "bg_main"),
                Cmd("bg_sprite", "bg_main", "3_한옥_에피소드시작"));

            Assert.That(ImageWidthOf(state, "bg_main"), Is.EqualTo(1920f).Within(Eps), "1080 × 16/9");
            Assert.That(state.GetAlpha("bg_main/CharacterPortraitSprite_Root"), Is.EqualTo(0f),
                "그림 교체는 숨긴 배경을 드러내지 않는다");
            Assert.That(state.Unhandled, Is.Empty);
        }

        [Test]
        public void 배경_키는_대소문자를_가리지_않는다()
        {
            // Resources.Load가 그렇다 — 가리면 런타임은 그림을 찾아 크기를 맞추는데 폴드만 Unhandled가 된다.
            StageState state = Fold(Cmd("bg_spawn", "bg_main", "CLASS_DAY"));

            Assert.That(ImageWidthOf(state, "bg_main"), Is.EqualTo(1080f * 1026f / 580f).Within(Eps),
                "대소문자만 다른 두 엔트리 중 먼저 온 class_day");
            Assert.That(state.Unhandled, Is.Empty);
        }

        [Test]
        public void 치수에_없는_키는_슬롯은_서고_사이징만_Unhandled다()
        {
            StageState state = Fold(Cmd("bg_spawn", "bg_main", "missing_bg"));

            Assert.That(state.HasSlot("bg_main"), Is.True, "슬롯을 못 세웠다는 거짓말을 하지 않는다");
            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("배경 치수가 없다"));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("missing_bg"));
        }

        [Test]
        public void 치수_덤프가_없으면_사이징이_Unhandled로_소리를_낸다()
        {
            StageState state = Fold(NewTuning(withBackgroundDimensions: false),
                Cmd("bg_spawn", "bg_main", "class_day"));

            Assert.That(state.HasSlot("bg_main"), Is.True);
            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("background-dimensions.json"));
        }

        [Test]
        public void bg_sprite는_세우지_않은_슬롯이면_Unhandled다()
        {
            StageState state = Fold(Cmd("bg_sprite", "bg_main", "class_day"));

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("슬롯으로 풀 수 없다"));
        }

        // ── 가시성·변형 — 캐릭터 폴드와 같은 노드 ───────────────────

        [Test]
        public void bg_fade는_캐릭터_fade와_같은_노드를_접는다()
        {
            StageState hidden = Fold(
                Cmd("bg_spawn", "bg_main", "class_day"),
                Cmd("bg_fade_out", "bg_main", "20fr"));

            Assert.That(hidden.GetAlpha("bg_main/CharacterPortraitSprite_Root"), Is.EqualTo(0f));

            StageState shown = Fold(
                Cmd("bg_spawn", "bg_main", "class_day"),
                Cmd("bg_fade_out", "bg_main"),
                Cmd("bg_fade_in", "bg_main", "24fr"));

            Assert.That(shown.GetAlpha("bg_main/CharacterPortraitSprite_Root"), Is.EqualTo(1f));
            Assert.That(shown.Unhandled, Is.Empty);
        }

        [Test]
        public void bg_move는_CharSlot_Track을_상대로_민다()
        {
            // 1u = 1920/48 = 40px. 브리지도 move_by와 같은 축(CharSlot_Track)에 건다.
            StageState state = Fold(
                Cmd("bg_spawn", "bg_main", "class_day"),
                Cmd("bg_move", "bg_main", "2u", "-1u", "10fr"),
                Cmd("bg_move", "bg_main", "1u", "0u"));

            Assert.That(state.Nodes.GetState("bg_main/CharSlot_Track").AnchoredPosition,
                Is.EqualTo(new Vec2(120f, -40f)));
            Assert.That(state.Unhandled, Is.Empty);
        }

        [Test]
        public void bg_scale은_가운데_피벗의_ActingScale에_절대값으로_간다()
        {
            // CharSlot_Scale(발 피벗)이 아니다 — 배경은 화면 가운데에서 커져야 한다(D 대응표).
            StageState state = Fold(
                Cmd("bg_spawn", "bg_main", "class_day"),
                Cmd("bg_scale", "bg_main", "1.2", "10fr"));

            Assert.That(state.Nodes.GetState("bg_main/CharacterPortrait_ActingScale").LocalScale.XY,
                Is.EqualTo(new Vec2(1.2f, 1.2f)));
            Assert.That(state.Nodes.GetState("bg_main/CharSlot_Scale").LocalScale.XY,
                Is.EqualTo(Vec2.One));
            Assert.That(state.Unhandled, Is.Empty);
        }

        // ── 지운 어휘 ────────────────────────────────────────────────

        [Test]
        public void 지운_bg_size와_bg_place는_Unhandled로_드러난다()
        {
            StageState state = Fold(
                Cmd("bg_spawn", "bg_main", "class_day"),
                Cmd("bg_size", "bg_main", "1"),
                Cmd("bg_place", "bg_main", "0u", "0u"));

            Assert.That(state.Unhandled.Count, Is.EqualTo(2));
            Assert.That(state.Unhandled[0].Command.Name, Is.EqualTo("bg_size"));
            Assert.That(state.Unhandled[1].Command.Name, Is.EqualTo("bg_place"));
        }

        // ── 재선언 · 키 공간 ─────────────────────────────────────────

        [Test]
        public void bg_spawn_재선언은_리그를_새로_세운다()
        {
            // 같은 키로 bg_spawn을 다시 부르면 런타임은 새 리그를 짓고 옛 리그를 부순다 —
            // 앞서 민 위치·배율은 남지 않고, 새 리그는 숨은 채 선다. (그림만 바꾸는 것은 bg_sprite다.)
            StageState state = Fold(
                Cmd("bg_spawn", "bg0", "class_day"),
                Cmd("bg_move", "bg0", "2u", "0u"),
                Cmd("bg_scale", "bg0", "1.2"),
                Cmd("bg_spawn", "bg0", "3_한옥_에피소드시작"));

            Assert.That(state.Nodes.Count, Is.EqualTo(RigNodeCount));
            Assert.That(state.Nodes.GetState("bg0/CharSlot_Track").AnchoredPosition, Is.EqualTo(Vec2.Zero));
            Assert.That(state.Nodes.GetState("bg0/CharacterPortrait_ActingScale").LocalScale.XY, Is.EqualTo(Vec2.One));
            Assert.That(ImageWidthOf(state, "bg0"), Is.EqualTo(1920f).Within(Eps), "새 그림의 폭");
            Assert.That(state.GetAlpha("bg0/CharacterPortraitSprite_Root"), Is.EqualTo(0f),
                "새 리그는 숨은 채 선다 — 다시 bg_fade_in으로 들인다");
            Assert.That(state.Unhandled, Is.Empty);
        }

        [Test]
        public void 캐스팅된_캐릭터_키는_슬롯_키보다_먼저_풀린다()
        {
            // 런타임 CharacterRigTargetResolver와 같은 순서 — 캐릭터 키가 이긴다.
            // 배경 키를 캐릭터 키와 같게 지으면 bg_* 명령이 그 캐릭터의 슬롯으로 간다(런타임은 세울 때 경고한다).
            StageState state = Fold(
                Cmd("slot", "s1"),
                Cmd("cast", "s1", "tyrant"),
                Cmd("fade_in", "s1"),
                Cmd("bg_spawn", "tyrant", "class_day"),
                Cmd("bg_fade_in", "tyrant"),
                Cmd("fade_out", "tyrant"));

            Assert.That(state.HasSlot("tyrant"), Is.True, "슬롯은 그 키 그대로 선다");
            Assert.That(state.GetAlpha("s1/CharacterPortraitSprite_Root"), Is.EqualTo(0f),
                "'tyrant'는 캐릭터 키로 풀려 s1에 닿는다 — bg_fade_in이 s1을 켜고 fade_out이 끈다");
            Assert.That(state.GetAlpha("tyrant/CharacterPortraitSprite_Root"), Is.EqualTo(0f),
                "배경 슬롯에는 아무 명령도 닿지 않았다 — 숨은 그대로");
        }

        [Test]
        public void 배경이_배역_있는_슬롯을_덮으면_배역을_푼다()
        {
            // 배경 = 캐스팅하지 않은 슬롯. 배역이 남으면 fade_in harin이 배경을 페이드하고
            // focus_on이 배경을 초점면에 넣는다. 런타임도 같은 자리에서 배역을 풀고 경고한다.
            StageState state = Fold(
                Cmd("slot", "s1"),
                Cmd("cast", "s1", "tyrant"),
                Cmd("bg_spawn", "s1", "class_day"),
                Cmd("bg_fade_in", "s1"),
                Cmd("fade_out", "tyrant"));

            Assert.That(state.TryGetCharacter("s1", out _), Is.False);
            Assert.That(state.GetAlpha("s1/CharacterPortraitSprite_Root"), Is.EqualTo(1f),
                "배역 키로는 더 이상 배경에 닿지 않는다");
            // 이 픽스처에는 초상 치수가 없어 cast의 사이징 축이 하나 남는다 — 보는 것은 마지막 것이다.
            UnhandledCommand last = state.Unhandled[state.Unhandled.Count - 1];
            Assert.That(last.Command.Name, Is.EqualTo("fade_out"));
            Assert.That(last.Reason, Does.Contain("슬롯으로 풀 수 없다"));
        }
    }
}
