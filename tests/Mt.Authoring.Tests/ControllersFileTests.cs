using System.Text.Json;
using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Editing;
using Mt.Authoring.Rig;
using Mt.Authoring.Validation;

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

        // 견본 nod는 Track_Move_Y에 있다 — 별칭으로 몸 Y, 표의 axisLabels로 "상하".
        Assert.True(rig.TryResolve(new CurveRef("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y), out Controller body, out Axis axis));
        Assert.Equal("body", body.Id);
        Assert.Equal(Axis.Y, axis);
        Assert.Equal("연기 · 상하", rig.LabelOf(new CurveRef("CharacterPortrait_Track_Move", MotionChannelKind.Position, Axis.Y)));
        Assert.Equal("연기 · 발 기준 회전", rig.LabelOf(new CurveRef("CharacterPortrait_SwayPivot", MotionChannelKind.Euler, Axis.Z)));
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

        // 깊이 손잡이는 DepthY도 맡는다 — 커브·노드·라벨이 깊이로 풀린다.
        Assert.Equal("CharSlot_DepthY", rig.Find("depth")!.DepthYNode);
        Assert.True(rig.TryResolve(new CurveRef("CharSlot_DepthY", MotionChannelKind.Position, Axis.Y), out Controller depth, out Axis depthAxis));
        Assert.Equal("depth", depth.Id);
        Assert.Equal(Axis.Y, depthAxis);
        Assert.Equal("depth", rig.OfNode("CharSlot_DepthY")!.Id);
        Assert.Equal("깊이 · 깊이 Y", DopeSheetRows.NodeLabel("CharSlot_DepthY", rig));
    }

    [Fact]
    public void 깊이_level은_배율로_굽고_배율은_level로_되돌아온다()
    {
        DepthTuningFile depth = BuiltinTuning.Depth;

        foreach (float level in new[] { 0f, 5f, 10f, 14f, 16f, 20f, 30f, 40f }) // 20 너머는 커브 할선 연장(슬라이더 끝 40)
        {
            Assert.True(depth.TryScaleOf(level, out float scale));
            Assert.True(depth.TryLevelOf(scale, out float back));
            Assert.Equal(level, back, 2);
        }

        Assert.Equal(40f, DepthTuningFile.MaxLevel);
        Assert.True(depth.TryScaleOf(40f, out float far) && depth.TryScaleOf(20f, out float close) && far > close); // 연장은 계속 커진다

        Assert.True(depth.TryScaleOf(DepthLevelLabels.Mid, out float mid));
        Assert.Equal(1.708f, mid, 2); // 게임 프리팹 Stage00 Mid의 DepthScale과 같다(PLAN-scene §7.7)

        // 깊이 Y = −bust × (배율 − 1): 런타임 size의 focus 보존이 정지 자세에서 남기는 값(yCurve는 상쇄된다). Mid = 프리팹의 −580.56.
        float bust = DepthTuningFile.FocusHeightOf(BuiltinTuning.Focus);
        Assert.Equal(820f, bust, 2);
        Assert.True(depth.TryDepthOf(DepthLevelLabels.Mid, bust, out float midY, out float midScale));
        Assert.Equal(-580.56f, midY, 1);
        Assert.Equal(mid, midScale, 4);
        Assert.True(depth.TryDepthOf(DepthLevelLabels.Far, bust, out float farY, out _));
        Assert.True(farY > 100f, "멀면 발이 위로(+)"); // 0.86 배율 → +114.8
        Assert.True(depth.TryDepthOf(DepthLevelLabels.Close, bust, out float closeY, out _));
        Assert.Equal(-984f, closeY, 1);
        Assert.Equal(0f, DepthTuningFile.FocusHeightOf(null));
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
        Assert.Throws<JsonException>(() => ControllersFile.Parse("""
            { "schema": "controllers/1", "rigs": [ { "rigKind": "character", "groups": [ { "id": "acting", "mode": "relative" } ],
              "controllers": [ { "id": "a", "group": "acting", "node": "N", "kind": "position", "axes": ["y"], "bakeY": "M" } ] } ] }
            """)); // bakeY는 depthLevel 굽기에만

        ControllerRig rig = ControllersFile.Parse("""
            { "schema": "controllers/1", "rigs": [ { "rigKind": "character", "groups": [ { "id": "acting", "mode": "relative" } ],
              "controllers": [ { "id": "a", "group": "acting", "node": "NoSuchNode", "kind": "position", "axes": ["x"] } ] } ] }
            """).Rig("character")!;
        Assert.Single(rig.Validate(BuiltinTuning.RigSchemas.NodeIds["character"]));
    }

    [Fact]
    public void 자리_깊이_컨트롤러가_든_relative_모션은_경고이고_두_모션으로_나누면_사라진다()
    {
        ControllersFile controllers = BuiltinTuning.Controllers;
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("step", "character", 0.5f, MotionEnd.Commit);
        editor.AddChannel("step", "CharSlot_Track", MotionChannelKind.Position);
        editor.SetKeys("step", "CharSlot_Track", MotionChannelKind.Position, Axis.X, new[] { Fixtures.Key(0f, -200f), Fixtures.Key(0.5f, 400f) });
        editor.AddChannel("step", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position);
        editor.SetKeys("step", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.5f, -18f) });

        // 컨트롤러 표 없이는 경고가 없다. 표를 주면 relative 미리보기 + 자리 컨트롤러 = 경고(막지 않는다).
        Assert.Empty(MotionValidator.Validate(editor.Document));
        MotionProblem warning = Assert.Single(MotionValidator.Validate(editor.Document, null, controllers));
        Assert.False(warning.Blocking);
        Assert.Contains("자리 · 자리", warning.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { MotionFix.PreviewModeToAbsolute, MotionFix.SplitByGroup }, MotionFixes.Suggest(warning));

        // absolute로 돌리면 경고가 사라진다.
        Assert.True(MotionFixes.Apply(editor, "step", MotionFix.PreviewModeToAbsolute, controllers));
        Assert.Empty(MotionValidator.Validate(editor.Document, null, controllers));
        editor.Undo();

        // 두 모션으로 나누기 — 자리는 step_place(absolute), 연기는 step(relative)에 남는다. 되돌리기 하나.
        long revision = editor.Revision;
        Assert.True(MotionFixes.Apply(editor, "step", MotionFix.SplitByGroup, controllers));
        Assert.Equal(revision + 1, editor.Revision);
        MotionAsset place = editor.Document.Find("step_place")!;
        Assert.Equal(MotionChannelMode.Absolute, place.PreviewMode);
        Assert.Equal(0.5f, place.Duration);
        Assert.Single(place.Channels);
        Assert.Equal("CharSlot_Track", place.Channels[0].Node);
        Assert.Single(editor.Document.Find("step")!.Channels);
        Assert.Empty(MotionValidator.Validate(editor.Document, null, controllers));
        Assert.Equal(new[] { "step", "step_place" }, editor.Document.Motions.Select(m => m.Name));

        editor.Undo();
        Assert.Null(editor.Document.Find("step_place"));
        Assert.Equal(2, editor.Document.Find("step")!.Channels.Count);

        // 전부 자리거나 전부 연기면 나눌 것이 없다.
        editor.RemoveChannel("step", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position);
        Assert.False(MotionFixes.Apply(editor, "step", MotionFix.SplitByGroup, controllers));
    }
}
