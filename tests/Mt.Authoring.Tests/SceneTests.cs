using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

public class SceneTests
{
    private static readonly RigSchemaFile RigFile = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));

    [Fact]
    public void 새_문서는_캐릭터_하나짜리_기본_씬이고_씬은_저작본에_왕복한다()
    {
        var editor = new MotionEditor(new MotionDocument());
        SceneAsset scene = Assert.Single(editor.Document.Scenes);
        Assert.Equal(scene.Name, editor.Document.ActiveScene);
        SceneObject c1 = Assert.Single(scene.Objects);
        Assert.Equal("c1", c1.Id);
        Assert.Equal("character", c1.Rig);
        Assert.Equal(SceneLayers.Mid, c1.Layer);

        editor.AddSceneObject(null, "bg", "background", SceneLayers.Far);
        editor.SetObjectPlacement(null, "c1", 120f, -30f, 5f, 1.2f, 0.9f);
        editor.SetObjectPortrait(null, "c1", "assets/harin.png");
        editor.SetObjectVisible(null, "bg", false);

        string json = MotionDocumentJson.Write(editor.Document);
        MotionDocument read = MotionDocumentJson.Read(json);
        Assert.Equal(json, MotionDocumentJson.Write(read));

        SceneAsset back = read.ActiveSceneAsset!;
        Assert.Equal(new[] { "c1", "bg" }, back.Objects.Select(o => o.Id));
        Assert.Equal(120f, back.Objects[0].PositionX);
        Assert.Equal(0.9f, back.Objects[0].ScaleY);
        Assert.Equal("assets/harin.png", back.Objects[0].Portrait);
        Assert.False(back.Objects[1].Visible);
        Assert.Contains("\"layer\": \"far\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"prefix\"", json, StringComparison.Ordinal); // 계산 전용 속성은 파일에 없다
        Assert.DoesNotContain("\"activeSceneAsset\"", json, StringComparison.Ordinal);
        Assert.Equal(new[] { "bg", "c1" }, back.DrawOrder().Select(o => o.Id)); // far 먼저
    }

    [Fact]
    public void 씬_명령은_규칙을_지키고_각각_되돌리기_한_번이다()
    {
        var editor = new MotionEditor(new MotionDocument());
        Assert.Throws<InvalidOperationException>(() => editor.AddSceneObject(null, "c1", "character")); // 겹침
        Assert.Throws<InvalidOperationException>(() => editor.AddSceneObject(null, "C2", "character")); // 대문자
        Assert.Throws<InvalidOperationException>(() => editor.AddSceneObject(null, "c2", "character", "sky")); // 모르는 레이어

        long before = editor.Revision;
        editor.AddSceneObject(null, "c2", "character", SceneLayers.Close);
        editor.SetObjectPlacement(null, "c2", 10f, 0f, 0f, 1f, 1f);
        editor.SetObjectPlacement(null, "c2", 10f, 0f, 0f, 1f, 1f); // 같은 값은 조작이 아니다
        editor.SetObjectLayer(null, "c2", SceneLayers.Back);
        Assert.Equal(before + 3, editor.Revision);

        editor.Undo();
        Assert.Equal(SceneLayers.Close, editor.Document.ActiveSceneAsset!.Find("c2")!.Layer);
        editor.Undo();
        editor.Undo();
        Assert.Null(editor.Document.ActiveSceneAsset!.Find("c2"));
    }

    [Fact]
    public void 씬_트리는_오브젝트마다_접두사로_서고_배치가_배치_축_노드에_들어간다()
    {
        var scene = new SceneAsset();
        scene.Objects.Add(new SceneObject { Id = "c1", Rig = "character", PositionX = 100f, PositionY = 50f, RotationZ = 10f, ScaleX = 1.5f, ScaleY = 2f });
        scene.Objects.Add(new SceneObject { Id = "bg", Rig = "background", Layer = SceneLayers.Far });
        scene.Objects.Add(new SceneObject { Id = "nope", Rig = "robot" });
        var missing = new List<SceneObject>();

        RectNodeTree tree = ScenePreview.Build(RigFile, scene, _ => 0.8f, missing);

        Assert.True(tree.Contains("c1/CharacterPortraitSprite_Image"));
        Assert.True(tree.Contains("bg/__root"));
        Assert.False(tree.Contains("CharacterPortraitSprite_Image")); // 접두사 없는 키는 없다
        Assert.Equal("nope", Assert.Single(missing).Id);

        RectNodeState track = tree.GetState("c1/CharSlot_Track");
        Assert.Equal(100f, track.AnchoredPosition.X);
        Assert.Equal(50f, track.AnchoredPosition.Y);
        Assert.Equal(10f, tree.GetState("c1/CharSlot_SwayPivot").LocalEulerAngles.Z);
        Assert.Equal(1.5f, tree.GetState("c1/CharSlot_Scale").LocalScale.X);
        Assert.Equal(2f, tree.GetState("c1/CharSlot_Scale").LocalScale.Y);
        Assert.Equal(1f, tree.GetState("c1/CharSlot_Scale").LocalScale.Z); // z는 그대로

        // 초상 사이징(종횡비 0.8)은 접두사 밑에서도 입혀진다.
        Vec2 image = tree.GetRectSize("c1/CharacterPortraitSprite_Image");
        Assert.True(image.X > 0 && image.Y > 0);
    }

    [Fact]
    public void 모션은_선택_오브젝트에만_얹히고_공통_스키마면_다른_리그도_입는다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        var editor = new MotionEditor(document);
        editor.AddSceneObject(null, "c2", "character");
        editor.AddSceneObject(null, "bg", "background", SceneLayers.Far);
        SceneAsset scene = document.ActiveSceneAsset!;
        RectNodeTree template = ScenePreview.Build(RigFile, scene);
        MotionAsset nod = document.Find("nod")!;

        RectNodeTree pose = ScenePreview.Pose(template, scene.Find("c2")!, nod, 0.3);

        Assert.Equal(-18f, pose.GetState("c2/CharacterPortrait_Track_Move_Y").AnchoredPosition.Y, 3);
        Assert.Equal(template.GetState("c1/CharacterPortrait_Track_Move_Y").AnchoredPosition.Y, pose.GetState("c1/CharacterPortrait_Track_Move_Y").AnchoredPosition.Y, 3);

        Assert.True(ScenePreview.CanWear(RigFile, scene.Find("c1")!, nod));
        Assert.False(ScenePreview.CanWear(RigFile, scene.Find("bg")!, nod)); // 배경 리그에는 그 노드가 없다
    }

    [Fact]
    public void 깊이_커브가_있으면_레이어가_런타임_size_폴드와_같은_깊이가_된다()
    {
        string depthPath = DepthTuningFile.TryFindBeside(Fixtures.PathOf("rig-schemas.json")) ?? throw new InvalidOperationException("presets/depth.json이 없다");
        var depth = DepthTuningFile.Load(depthPath);
        Assert.True(depth.TryResolveLayer("mid", out float midY, out float midScale));
        Assert.True(depth.TryResolveLayer("far", out float farY, out float farScale));
        Assert.NotEqual(midScale, farScale);
        Assert.False(depth.TryResolveLayer("sky", out _, out _));

        var scene = new SceneAsset();
        scene.Objects.Add(new SceneObject { Id = "c1", Rig = "character", Layer = SceneLayers.Mid });
        scene.Objects.Add(new SceneObject { Id = "c2", Rig = "character", Layer = SceneLayers.Far });
        RectNodeTree tree = ScenePreview.Build(RigFile, scene, null, null, depth);

        Assert.Equal(midScale, tree.GetState("c1/CharSlot_DepthScale").LocalScale.X, 4);
        Assert.Equal(farScale, tree.GetState("c2/CharSlot_DepthScale").LocalScale.X, 4);
        // DepthY는 focus 보존이 정한다 — 초점(VisualOffset)이 배율 중심과 겹치면 레이어가 달라도 0일 수 있다. 값은 아래에서 코어와 대조한다.

        // 깊이 축에도 focus 보존이 들어가므로 날 커브 값과는 다를 수 있다 — 같은 코어 함수로 다시 접은 값과 같다.
        var state = new StageState(new RectSpace(RigFile.Dto.capturedUnderParentSize.ToVec2(), Vec2.Half));
        RigSchemaLoader.AddRigTo(state.Nodes, RigFile.Dto.rigs.First(r => r.rigKind == "character"), "c1/");
        state.RegisterSlot("c1");
        PortraitLayout.ApplySizing(state.Nodes, PortraitLayout.PlaceholderAspect, "c1/");
        Assert.True(SetDepthStageReduction.TryReduce(state, "c1", "", "mid", "bust", depth.Level, null, out StageNodeClaim y, out _, out _));
        Assert.Equal(y.Value.Y, tree.GetState("c1/CharSlot_DepthY").AnchoredPosition.Y, 3);

        // 커브가 없으면 깊이 축은 덤프 그대로다.
        RectNodeTree plain = ScenePreview.Build(RigFile, scene);
        Assert.Equal(1f, plain.GetState("c1/CharSlot_DepthScale").LocalScale.X);
    }

    [Fact]
    public void 내보내기_필터는_고른_모션만_검증하고_쓴다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        var editor = new MotionEditor(document);
        editor.AddMotion("robot_wave", "robot", 1f);
        editor.AddChannel("robot_wave", "Robot_Arm", MotionChannelKind.Position);

        Assert.False(MotionExport.TryExport(document, RigFile.NodeIds, out _, out IReadOnlyList<MotionProblem> all));
        Assert.Contains(all, p => p.MotionName == "robot_wave");

        Assert.True(MotionExport.TryExport(document, RigFile.NodeIds, out string json, out IReadOnlyList<MotionProblem> problems, m => m.Rig == "character"));
        Assert.Empty(problems);
        Assert.Contains("\"nod\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("robot_wave", json, StringComparison.Ordinal);
    }

    [Fact]
    public void 카메라는_씬에_남고_되돌리기_한_번이다()
    {
        var editor = new MotionEditor(new MotionDocument());
        long before = editor.Revision;
        editor.SetCamera(null, 20f, 100f, -50f);
        editor.SetCamera(null, 20f, 100f, -50f);
        Assert.Equal(before + 1, editor.Revision);
        CameraSettings camera = editor.Document.ActiveSceneAsset!.Camera;
        Assert.Equal(20f, camera.Zoom);
        Assert.Equal(-50f, camera.PanY);
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Equal(100f, MotionDocumentJson.Read(json).ActiveSceneAsset!.Camera.PanX);
    }
}
