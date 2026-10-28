using System.Text.Json;
using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Tests;

/// <summary>컨트롤러 표 — 이름 → (노드, 종류, 성분). 애니메이터가 보는 것이고, 파일(motions/2)은 노드 채널 그대로다.</summary>
public class ControllersFileTests
{
    [Fact]
    public void 내장_표는_character_리그의_세_묶음과_열_컨트롤러이고_노드가_전부_덤프에_있다()
    {
        ControllersFile file = BuiltinTuning.Controllers;

        Assert.True(file.TryGetRig("character", out ControllerRig rig));
        Assert.Equal(new[] { "placement", "depth", "acting" }, rig.Groups.Select(g => g.Id));
        Assert.Equal(MotionChannelMode.Absolute, rig.Groups[0].Mode);
        Assert.Equal(MotionChannelMode.Absolute, rig.Groups[1].Mode);
        Assert.Equal(MotionChannelMode.Relative, rig.Groups[2].Mode);
        Assert.Equal(10, rig.Controllers.Count);
        Assert.Empty(rig.Validate(BuiltinTuning.RigSchemas.NodeIds["character"]));
        Assert.Null(file.Rig("background")); // 표가 없는 리그는 노드로 다룬다
    }

    [Fact]
    public void 커브는_주_노드로도_별칭으로도_컨트롤러_성분으로_풀리고_주_노드가_별칭을_이긴다()
    {
        ControllerRig rig = BuiltinTuning.Controllers.Rig("character")!;

        // 견본 nod는 Track_Move_Y에 있다 — 별칭으로 "몸 Y".
        Assert.True(rig.TryResolve(new CurveRef("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y), out Controller body, out Axis axis));
        Assert.Equal("body", body.Id);
        Assert.Equal(Axis.Y, axis);
        Assert.Equal("연기 · 몸 Y", rig.LabelOf(new CurveRef("CharacterPortrait_Track_Move", MotionChannelKind.Position, Axis.Y)));
        Assert.Equal("연기 · 숙임", rig.LabelOf(new CurveRef("CharacterPortrait_SwayPivot", MotionChannelKind.Euler, Axis.Z)));
        Assert.Equal("자리 · 자리 X", rig.LabelOf(new CurveRef("CharSlot_Track", MotionChannelKind.Position, Axis.X)));

        // ActingScale_X의 scale X는 facing의 주 노드다 — squash의 별칭이 아니다.
        Assert.True(rig.TryResolve(new CurveRef("CharacterPortrait_ActingScale_X", MotionChannelKind.Scale, Axis.X), out Controller facing, out _));
        Assert.Equal("facing", facing.Id);
        Assert.True(rig.TryResolve(new CurveRef("CharacterPortrait_ActingScale_Y", MotionChannelKind.Scale, Axis.Y), out Controller squash, out _));
        Assert.Equal("squash", squash.Id);

        // 표에 없는 커브는 null — 노드 이름으로 보인다.
        Assert.Null(rig.LabelOf(new CurveRef("CharSlot_Track_X", MotionChannelKind.Position, Axis.X)));
        Assert.Equal(new[] { "CharacterPortrait_Track_Move", "CharacterPortrait_Track_Move_Y" }, body.CurvesOf(Axis.Y).Select(c => c.Node));
    }

    [Fact]
    public void W_E_R의_기본_손잡이는_몸_숙임_크기다()
    {
        ControllerRig rig = BuiltinTuning.Controllers.Rig("character")!;

        Assert.Equal("body", rig.DefaultFor(MotionChannelKind.Position)!.Id);
        Assert.Equal("bow", rig.DefaultFor(MotionChannelKind.Euler)!.Id);
        Assert.Equal("squash", rig.DefaultFor(MotionChannelKind.Scale)!.Id);
        Assert.Equal(ControllerBake.DepthLevel, rig.Find("depth")!.Bake);
    }

    [Fact]
    public void 깊이_level은_배율로_굽고_배율은_level로_되돌아온다()
    {
        DepthTuningFile depth = BuiltinTuning.Depth;

        foreach (float level in new[] { 0f, 5f, 10f, 14f, 16f, 20f })
        {
            Assert.True(depth.TryScaleOf(level, out float scale));
            Assert.True(depth.TryLevelOf(scale, out float back));
            Assert.Equal(level, back, 2);
        }

        Assert.True(depth.TryScaleOf(DepthLevelLabels.Mid, out float mid));
        Assert.Equal(1.708f, mid, 2); // 게임 프리팹 Stage00 Mid의 DepthScale과 같다(PLAN-scene §7.7)
        Assert.True(depth.TryLevelOf(0f, out float clampedLow) && clampedLow < 0.01f, "범위 밖은 끝으로");
        Assert.True(depth.TryLevelOf(9f, out float clampedHigh) && clampedHigh > 19.99f);
    }

    [Fact]
    public void 틀린_표는_거부한다_모양을_조용히_받지_않는다()
    {
        Assert.Throws<JsonException>(() => ControllersFile.Parse("""{ "schema": "controllers/0", "rigs": [] }"""));
        Assert.Throws<JsonException>(() => ControllersFile.Parse("""
            { "schema": "controllers/1", "rigs": [ { "rigKind": "character", "groups": [ { "id": "acting", "mode": "relative" } ],
              "controllers": [ { "id": "a", "group": "acting", "node": "N", "kind": "position", "axes": ["z"] } ] } ] }
            """)); // position에는 Z가 없다
        Assert.Throws<JsonException>(() => ControllersFile.Parse("""
            { "schema": "controllers/1", "rigs": [ { "rigKind": "character", "groups": [ { "id": "acting", "mode": "relative" } ],
              "controllers": [ { "id": "a", "group": "nope", "node": "N", "kind": "position", "axes": ["x"] } ] } ] }
            """)); // 모르는 묶음

        ControllerRig rig = ControllersFile.Parse("""
            { "schema": "controllers/1", "rigs": [ { "rigKind": "character", "groups": [ { "id": "acting", "mode": "relative" } ],
              "controllers": [ { "id": "a", "group": "acting", "node": "NoSuchNode", "kind": "position", "axes": ["x"] } ] } ] }
            """).Rig("character")!;
        Assert.Single(rig.Validate(BuiltinTuning.RigSchemas.NodeIds["character"]));
    }
}
