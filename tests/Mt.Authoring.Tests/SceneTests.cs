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
        SceneObject s1 = Assert.Single(scene.Objects);
        Assert.Equal("s1", s1.Id);
        Assert.Equal("character", s1.Rig);
        Assert.Equal(SceneLayers.Mid, s1.Layer);
        Assert.Equal("stage00", s1.Stage); // 게임·코어 slot 기본과 같다

        editor.AddSceneObject(null, "bg", "background", SceneLayers.Far);
        editor.SetObjectPlacement(null, "s1", 120f, -30f, 5f, 1.2f, 0.9f);
        editor.SetObjectPortrait(null, "s1", "assets/harin.png");
        editor.SetObjectVisible(null, "bg", false);

        string json = MotionDocumentJson.Write(editor.Document);
        MotionDocument read = MotionDocumentJson.Read(json);
        Assert.Equal(json, MotionDocumentJson.Write(read));

        SceneAsset back = read.ActiveSceneAsset!;
        Assert.Equal(new[] { "s1", "bg" }, back.Objects.Select(o => o.Id));
        Assert.Equal(120f, back.Objects[0].PositionX);
        Assert.Equal(0.9f, back.Objects[0].ScaleY);
        Assert.Equal("assets/harin.png", back.Objects[0].Portrait);
        Assert.False(back.Objects[1].Visible);
        Assert.Contains("\"layer\": \"far\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"prefix\"", json, StringComparison.Ordinal); // 계산 전용 속성은 파일에 없다
        Assert.DoesNotContain("\"activeSceneAsset\"", json, StringComparison.Ordinal);
        Assert.Equal(new[] { "bg", "s1" }, back.DrawOrder().Select(o => o.Id)); // far 먼저
    }

    [Fact]
    public void 씬_명령은_규칙을_지키고_각각_되돌리기_한_번이다()
    {
        var editor = new MotionEditor(new MotionDocument());
        Assert.Throws<InvalidOperationException>(() => editor.AddSceneObject(null, "s1", "character")); // 겹침
        Assert.Throws<InvalidOperationException>(() => editor.AddSceneObject(null, "C2", "character")); // 대문자
        Assert.Throws<InvalidOperationException>(() => editor.AddSceneObject(null, "s2", "character", "sky")); // 모르는 레이어

        long before = editor.Revision;
        editor.AddSceneObject(null, "s2", "character", SceneLayers.Close);
        editor.SetObjectPlacement(null, "s2", 10f, 0f, 0f, 1f, 1f);
        editor.SetObjectPlacement(null, "s2", 10f, 0f, 0f, 1f, 1f); // 같은 값은 조작이 아니다
        editor.SetObjectLayer(null, "s2", SceneLayers.Back);
        Assert.Equal(before + 3, editor.Revision);

        editor.Undo();
        Assert.Equal(SceneLayers.Close, editor.Document.ActiveSceneAsset!.Find("s2")!.Layer);
        editor.Undo();
        editor.Undo();
        Assert.Null(editor.Document.ActiveSceneAsset!.Find("s2"));
    }

    [Fact]
    public void 씬_트리는_오브젝트마다_접두사로_서고_배치가_배치_축_노드에_들어간다()
    {
        var scene = new SceneAsset();
        scene.Objects.Add(new SceneObject { Id = "s1", Rig = "character", PositionX = 100f, PositionY = 50f, RotationZ = 10f, ScaleX = 1.5f, ScaleY = 2f });
        scene.Objects.Add(new SceneObject { Id = "bg", Rig = "background", Layer = SceneLayers.Far });
        scene.Objects.Add(new SceneObject { Id = "nope", Rig = "robot" });
        var missing = new List<SceneObject>();

        RectNodeTree tree = ScenePreview.Build(RigFile, scene, _ => 0.8f, missing);

        Assert.True(tree.Contains("s1/CharacterPortraitSprite_Image"));
        Assert.True(tree.Contains("bg/__root"));
        Assert.False(tree.Contains("CharacterPortraitSprite_Image")); // 접두사 없는 키는 없다
        Assert.Equal("nope", Assert.Single(missing).Id);

        RectNodeState track = tree.GetState("s1/CharSlot_Track");
        Assert.Equal(100f, track.AnchoredPosition.X);
        Assert.Equal(50f, track.AnchoredPosition.Y);
        Assert.Equal(10f, tree.GetState("s1/CharSlot_SwayPivot").LocalEulerAngles.Z);
        Assert.Equal(1.5f, tree.GetState("s1/CharSlot_Scale").LocalScale.X);
        Assert.Equal(2f, tree.GetState("s1/CharSlot_Scale").LocalScale.Y);
        Assert.Equal(1f, tree.GetState("s1/CharSlot_Scale").LocalScale.Z); // z는 그대로

        // 초상 사이징(종횡비 0.8)은 접두사 밑에서도 입혀진다.
        Vec2 image = tree.GetRectSize("s1/CharacterPortraitSprite_Image");
        Assert.True(image.X > 0 && image.Y > 0);
    }

    [Fact]
    public void 모션은_선택_오브젝트에만_얹히고_공통_스키마면_다른_리그도_입는다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        var editor = new MotionEditor(document);
        editor.AddSceneObject(null, "s2", "character");
        editor.AddSceneObject(null, "bg", "background", SceneLayers.Far);
        SceneAsset scene = document.ActiveSceneAsset!;
        RectNodeTree template = ScenePreview.Build(RigFile, scene);
        MotionAsset nod = document.Find("nod")!;

        RectNodeTree pose = ScenePreview.Pose(template, scene.Find("s2")!, nod, 0.3);

        Assert.Equal(-18f, pose.GetState("s2/CharacterPortrait_Track_Move_Y").AnchoredPosition.Y, 3);
        Assert.Equal(template.GetState("s1/CharacterPortrait_Track_Move_Y").AnchoredPosition.Y, pose.GetState("s1/CharacterPortrait_Track_Move_Y").AnchoredPosition.Y, 3);

        Assert.True(ScenePreview.CanWear(RigFile, scene.Find("s1")!, nod));
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
        scene.Objects.Add(new SceneObject { Id = "s1", Rig = "character", Layer = SceneLayers.Mid });
        scene.Objects.Add(new SceneObject { Id = "s2", Rig = "character", Layer = SceneLayers.Far });
        RectNodeTree tree = ScenePreview.Build(RigFile, scene, null, null, depth);

        Assert.Equal(midScale, tree.GetState("s1/CharSlot_DepthScale").LocalScale.X, 4);
        Assert.Equal(farScale, tree.GetState("s2/CharSlot_DepthScale").LocalScale.X, 4);
        // DepthY는 focus 보존이 정한다 — 초점(VisualOffset)이 배율 중심과 겹치면 레이어가 달라도 0일 수 있다. 값은 아래에서 코어와 대조한다.

        // 깊이 축에도 focus 보존이 들어가므로 날 커브 값과는 다를 수 있다 — 같은 코어 함수로 다시 접은 값과 같다.
        var state = new StageState(new RectSpace(RigFile.Dto.capturedUnderParentSize.ToVec2(), Vec2.Half));
        RigSchemaLoader.AddRigTo(state.Nodes, RigFile.Dto.rigs.First(r => r.rigKind == "character"), "s1/");
        state.RegisterSlot("s1");
        PortraitLayout.ApplySizing(state.Nodes, PortraitLayout.PlaceholderAspect, "s1/");
        Assert.True(SetDepthStageReduction.TryReduce(state, "s1", "", "mid", "bust", depth.Level, null, out StageNodeClaim y, out _, out _));
        Assert.Equal(y.Value.Y, tree.GetState("s1/CharSlot_DepthY").AnchoredPosition.Y, 3);

        // 커브가 없으면 깊이 축은 덤프 그대로다.
        RectNodeTree plain = ScenePreview.Build(RigFile, scene);
        Assert.Equal(1f, plain.GetState("s1/CharSlot_DepthScale").LocalScale.X);
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

    [Fact]
    public void 오브젝트는_부모_노드_밑에_붙고_자식은_부모를_따라_움직인다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddSceneObject(null, "bg", "background", SceneLayers.Far);
        editor.AddSceneObject(null, "prop", "character", SceneLayers.Close, parent: "s1", parentNode: "CharacterPortrait_Track_Move");
        Assert.Throws<InvalidOperationException>(() => editor.AddSceneObject(null, "pivot", "", SceneLayers.Mid)); // 리그 없는 오브젝트는 없다
        SceneAsset scene = editor.Document.ActiveSceneAsset!;

        SceneObject prop = scene.Find("prop")!;
        Assert.Equal("s1", prop.Parent);
        Assert.Equal("CharacterPortrait_Track_Move", prop.ParentNode);
        Assert.Equal(SceneLayers.Mid, prop.Layer); // 자식은 부모의 레이어
        Assert.Equal(new[] { "bg", "s1", "prop" }, scene.DrawOrder().Select(o => o.Id)); // far 먼저, 자식은 부모 바로 뒤

        var fallen = new List<SceneObject>();
        RectNodeTree tree = ScenePreview.Build(RigFile, scene, null, null, null, fallen);
        Assert.Empty(fallen);
        Assert.Equal("s1/CharacterPortrait_Track_Move", tree.GetParentKey("prop/__root"));

        // 부모를 옮기면 자식의 월드 위치가 같이 간다 — 코어 체인.
        Vec2 before = tree.TransformPoint("prop/CharacterPortraitSprite_Image", Vec3.Zero).XY;
        editor.SetObjectPlacement(null, "s1", 200f, 0f, 0f, 1f, 1f);
        RectNodeTree moved = ScenePreview.Build(RigFile, scene);
        Vec2 after = moved.TransformPoint("prop/CharacterPortraitSprite_Image", Vec3.Zero).XY;
        Assert.Equal(200f, after.X - before.X, 1);

        // 부모 노드가 그 리그에 없으면 부모 루트로 떨어지고 알린다.
        editor.SetObjectParent(null, "prop", "bg", "CharSlot_Track");
        RectNodeTree fallback = ScenePreview.Build(RigFile, scene, null, null, null, fallen);
        Assert.Equal("prop", Assert.Single(fallen).Id);
        Assert.Equal("bg/__root", fallback.GetParentKey("prop/__root"));

        // 배치 축이 없는 리그(배경)는 루트에 배치가 들어간다.
        editor.SetObjectPlacement(null, "bg", 0f, 0f, 0f, 2f, 2f);
        RectNodeTree placed = ScenePreview.Build(RigFile, scene);
        Assert.Equal(2f, placed.GetState("bg/__root").LocalScale.X);

        // 저장 왕복.
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Contains("\"parentNode\"", json, StringComparison.Ordinal);
        SceneAsset back = MotionDocumentJson.Read(json).ActiveSceneAsset!;
        Assert.Equal("bg", back.Find("prop")!.Parent);
    }

    [Fact]
    public void 부모_설정은_순환을_막고_삭제_복제_이름바꾸기는_자손까지_간다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddSceneObject(null, "s2", "character", SceneLayers.Front, parent: "s1");
        editor.AddSceneObject(null, "s3", "character", SceneLayers.Front, parent: "s2", parentNode: "CharSlot_Scale");
        SceneAsset scene = editor.Document.ActiveSceneAsset!;

        Assert.Throws<InvalidOperationException>(() => editor.SetObjectParent(null, "s1", "s3")); // 자손 밑으로
        Assert.Throws<InvalidOperationException>(() => editor.SetObjectParent(null, "s1", "s1")); // 자기 자신
        Assert.Throws<KeyNotFoundException>(() => editor.SetObjectParent(null, "s1", "ghost"));

        // 레이어는 최상위가 정하고 자손이 따라간다.
        editor.SetObjectLayer(null, "s1", SceneLayers.Close);
        Assert.All(scene.Objects, o => Assert.Equal(SceneLayers.Close, o.Layer));

        // 이름 바꾸기 — 자식의 parent 참조가 따라간다.
        editor.RenameSceneObject(null, "s2", "arm");
        Assert.Equal("arm", scene.Find("s3")!.Parent);
        Assert.Throws<InvalidOperationException>(() => editor.RenameSceneObject(null, "arm", "s1")); // 겹침
        Assert.Throws<InvalidOperationException>(() => editor.RenameSceneObject(null, "arm", "Arm")); // 규칙 밖

        // 복제 — 자손까지, 새 id는 숫자 올림, 원본 바로 뒤.
        long before = editor.Revision;
        SceneObject copy = editor.DuplicateSceneObject(null, "s1");
        Assert.Equal(before + 1, editor.Revision);
        Assert.Equal("s2", copy.Id); // s1 → s2 (비어 있다)
        Assert.Equal(new[] { "s1", "s2", "arm_2", "s4", "arm", "s3" }, scene.Objects.Select(o => o.Id));
        Assert.Equal("s2", scene.Find("arm_2")!.Parent);
        Assert.Equal("arm_2", scene.Find("s4")!.Parent);
        Assert.Equal("CharSlot_Scale", scene.Find("s4")!.ParentNode);
        editor.Undo();
        scene = editor.Document.ActiveSceneAsset!; // 되돌리기는 스냅샷 복원 — 참조를 다시 읽는다
        Assert.Equal(3, scene.Objects.Count);

        // 떼기 — 최상위로, 레이어를 주면 그 레이어로.
        editor.SetObjectParent(null, "arm", null, null, SceneLayers.Back);
        Assert.Null(scene.Find("arm")!.Parent);
        Assert.Equal(SceneLayers.Back, scene.Find("arm")!.Layer);
        Assert.Equal(SceneLayers.Back, scene.Find("s3")!.Layer);

        // 삭제 — 자손까지 한 번에, 되돌리기 한 번.
        before = editor.Revision;
        editor.RemoveSceneObject(null, "arm");
        Assert.Equal(before + 1, editor.Revision);
        Assert.Equal(new[] { "s1" }, scene.Objects.Select(o => o.Id));
        editor.Undo();
        Assert.Equal(3, editor.Document.ActiveSceneAsset!.Objects.Count);

        Assert.Equal("s5", MotionEditor.NextObjectId("s1", new[] { "s2", "s3", "s4" }));
        Assert.Equal("bg_2", MotionEditor.NextObjectId("bg", Array.Empty<string>()));
        Assert.Equal("11_2", MotionEditor.NextObjectId("11", Array.Empty<string>())); // 전부 숫자면 줄기가 없다
    }

    [Fact]
    public void 부모를_바꿀_때_월드_위치를_지키는_배치_값을_센다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.SetObjectPlacement(null, "s1", 200f, -40f, 0f, 1f, 1f);
        editor.AddSceneObject(null, "s2", "character", SceneLayers.Front);
        editor.SetObjectPlacement(null, "s2", -100f, 60f, 0f, 1f, 1f);
        SceneAsset scene = editor.Document.ActiveSceneAsset!;

        Vec2 before = ScenePreview.Build(RigFile, scene).TransformPoint("s2/CharSlot_Track", Vec3.Zero).XY;
        (float x, float y) = ScenePreview.PlacementKeepingWorld(RigFile, scene, "s2", "s1", "CharacterPortrait_Track_Move");
        Assert.NotEqual(-100f, x); // 부모 공간으로 옮겨졌다

        editor.SetObjectParent(null, "s2", "s1", "CharacterPortrait_Track_Move", null, (x, y));
        Vec2 after = ScenePreview.Build(RigFile, scene).TransformPoint("s2/CharSlot_Track", Vec3.Zero).XY;
        Assert.Equal(before.X, after.X, 1);
        Assert.Equal(before.Y, after.Y, 1);

        // 떼어도 같은 자리.
        (float bx, float by) = ScenePreview.PlacementKeepingWorld(RigFile, scene, "s2", null, null);
        editor.SetObjectParent(null, "s2", null, null, SceneLayers.Mid, (bx, by));
        Vec2 back = ScenePreview.Build(RigFile, scene).TransformPoint("s2/CharSlot_Track", Vec3.Zero).XY;
        Assert.Equal(before.X, back.X, 1);
        Assert.Equal(before.Y, back.Y, 1);
        Assert.Equal(-100f, scene.Find("s2")!.PositionX, 1);
    }

    [Fact]
    public void 배역이_있으면_focus_표의_오프셋이_깊이에_들어간다()
    {
        string focusPath = FocusTuningFile.TryFindBeside(Fixtures.PathOf("rig-schemas.json")) ?? throw new InvalidOperationException("presets/focus.json이 없다");
        var focus = FocusTuningFile.Load(focusPath);
        Assert.Equal(new[] { "bandi" }, focus.CharacterKeys);
        var depth = DepthTuningFile.Load(DepthTuningFile.TryFindBeside(Fixtures.PathOf("rig-schemas.json"))!);

        var editor = new MotionEditor(new MotionDocument());
        editor.SetObjectCharacter(null, "s1", "bandi");
        editor.SetObjectCharacter(null, "s1", " bandi "); // 같은 값(정리됨)은 조작이 아니다
        SceneAsset scene = editor.Document.ActiveSceneAsset!;
        Assert.Equal("bandi", scene.Find("s1")!.Character);

        float plain = ScenePreview.Build(RigFile, scene, null, null, depth).GetState("s1/CharSlot_DepthY").AnchoredPosition.Y;
        float withFocus = ScenePreview.Build(RigFile, scene, null, null, depth, null, focus).GetState("s1/CharSlot_DepthY").AnchoredPosition.Y;
        Assert.NotEqual(plain, withFocus); // focus 점(bust 820 + 배역 오프셋)이 배율 중심과 떨어져 있으면 보존이 DepthY를 움직인다

        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Contains("\"character\": \"bandi\"", json, StringComparison.Ordinal);
        Assert.Equal("bandi", editor.DuplicateSceneObject(null, "s1").Character);
    }

    [Fact]
    public void 씬은_여럿일_수_있고_활성_씬이_문서에_남는다()
    {
        var editor = new MotionEditor(new MotionDocument());
        Assert.Throws<InvalidOperationException>(() => editor.RemoveScene("scene")); // 마지막 씬
        Assert.Throws<InvalidOperationException>(() => editor.AddScene("Scene B")); // 규칙 밖

        editor.AddScene("scene_b");
        Assert.Equal("scene_b", editor.Document.ActiveScene);
        Assert.Equal("s1", Assert.Single(editor.Document.ActiveSceneAsset!.Objects).Id);
        editor.AddSceneObject(null, "s2", "character", SceneLayers.Front);

        editor.DuplicateScene("scene_b", "scene_c");
        Assert.Equal("scene_c", editor.Document.ActiveScene);
        Assert.Equal(2, editor.Document.ActiveSceneAsset!.Objects.Count);
        Assert.Equal(new[] { "scene", "scene_b", "scene_c" }, editor.Document.Scenes.Select(x => x.Name));

        editor.SetActiveScene("scene");
        Assert.Single(editor.Document.ActiveSceneAsset!.Objects);
        editor.RenameScene("scene", "main");
        Assert.Equal("main", editor.Document.ActiveScene);
        Assert.Throws<InvalidOperationException>(() => editor.RenameScene("scene_b", "main"));

        editor.RemoveScene("main"); // 활성 씬을 지우면 이웃이 활성
        Assert.Equal("scene_b", editor.Document.ActiveScene);
        editor.Undo();
        Assert.Equal("main", editor.Document.ActiveScene);

        string json = MotionDocumentJson.Write(editor.Document);
        MotionDocument read = MotionDocumentJson.Read(json);
        Assert.Equal("main", read.ActiveScene);
        Assert.Equal(3, read.Scenes.Count);
    }

    [Fact]
    public void 내장_기본값은_런타임_ExportedTuning_그대로이고_뼈대는_항등_85노드다()
    {
        Assert.Equal(RigFile.Sha256, BuiltinTuning.RigSchemas.Sha256); // 픽스처 = 익스포터 산출물 = 내장본
        Assert.Contains("character", BuiltinTuning.RigSchemas.RigKinds);
        Assert.Equal(1920f, BuiltinTuning.BaseResolution.X);
        Assert.True(BuiltinTuning.Depth.TryResolveLayer("mid", out _, out _));
        Assert.NotEmpty(BuiltinTuning.Focus.CharacterKeys); // 실제 표의 배역들(픽스처의 bandi와 다르다)
        RigSchemaRigDto stage = BuiltinTuning.StageRig;
        Assert.Equal(85, stage.nodes.Count);
        Assert.All(stage.nodes, n => Assert.Equal(1f, n.localScale.x));
        Assert.Contains(stage.nodes, n => n.id == "Stage00Depth_Mid_Content" && n.parent == "Stage00Depth_Mid_FramingScale");
        Assert.True(StageNodes.TryParseContent("Stage02Depth_Close_Content", out string st, out string layer));
        Assert.Equal(("stage02", "close"), (st, layer));
        Assert.False(StageNodes.TryParseContent("Stage00Mask_Root", out _, out _));
    }

    [Fact]
    public void 뼈대_위에_세워도_좌표는_같고_오브젝트는_그_무대_층의_Content_밑에_선다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddSceneObject(null, "s2", "character", SceneLayers.Far, stage: "stage01");
        editor.SetObjectPlacement(null, "s1", 120f, -30f, 5f, 1.1f, 1f);
        SceneAsset scene = editor.Document.ActiveSceneAsset!;
        Assert.Throws<InvalidOperationException>(() => editor.AddSceneObject(null, "s3", "character", SceneLayers.Mid, stage: "stage07"));

        RectNodeTree plain = ScenePreview.Build(RigFile, scene, null, null, BuiltinTuning.Depth, null, BuiltinTuning.Focus);
        RectNodeTree staged = ScenePreview.Build(RigFile, scene, null, null, BuiltinTuning.Depth, null, BuiltinTuning.Focus, BuiltinTuning.StageRig);

        Assert.Equal("stage/Stage00Depth_Mid_Content", staged.GetParentKey("s1/__root"));
        Assert.Equal("stage/Stage01Depth_Far_Content", staged.GetParentKey("s2/__root"));
        Assert.Null(plain.GetParentKey("s1/__root"));

        foreach (string key in new[] { "s1/CharacterPortraitSprite_Image", "s2/CharacterPortraitSprite_Image", "s1/CharSlot_Track" })
        {
            Vec2 a = plain.TransformPoint(key, Vec3.Zero).XY;
            Vec2 b = staged.TransformPoint(key, Vec3.Zero).XY;
            Assert.Equal(a.X, b.X, 3);
            Assert.Equal(a.Y, b.Y, 3);
        }

        Assert.Equal(1920f, staged.GetRectSize("stage/Stage00Depth_Mid_Content").X, 3);
    }

    [Fact]
    public void 카메라는_런타임과_같은_노드에_들어가고_층마다_패럴랙스가_붙는다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddSceneObject(null, "far", "character", SceneLayers.Far);
        editor.SetCamera(null, 20f, 100f, 0f); // 배율 2, pan +100
        SceneAsset scene = editor.Document.ActiveSceneAsset!;
        RectNodeTree tree = ScenePreview.Build(RigFile, scene, null, null, null, null, null, BuiltinTuning.StageRig);

        Assert.Equal(2f, tree.GetState("stage/StageZoom_Root").LocalScale.X, 4);
        Assert.Equal(100f, tree.GetState("stage/StagePan_Root").AnchoredPosition.X, 4);
        // Far: pan × −0.04, scale ^ −0.15 · Mid: 항등 · Close: pan × 0.2, scale ^ 0.16 (PresentationResponseProfile)
        Assert.Equal(-4f, tree.GetState("stage/Stage00Depth_Far_FramingTransform").AnchoredPosition.X, 4);
        Assert.Equal(MathF.Pow(2f, -0.15f), tree.GetState("stage/Stage00Depth_Far_FramingScale").LocalScale.X, 4);
        Assert.Equal(0f, tree.GetState("stage/Stage00Depth_Mid_FramingTransform").AnchoredPosition.X, 4);
        Assert.Equal(1f, tree.GetState("stage/Stage00Depth_Mid_FramingScale").LocalScale.X, 4);
        Assert.Equal(20f, tree.GetState("stage/Stage02Depth_Close_FramingTransform").AnchoredPosition.X, 4);
        Assert.Equal(MathF.Pow(2f, 0.16f), tree.GetState("stage/Stage02Depth_Close_FramingScale").LocalScale.X, 4);

        // 월드: mid 오브젝트는 pan + 2·0 = 100, far 오브젝트는 pan + 2·(−4) = 92.
        Assert.Equal(100f, tree.TransformPoint("s1/CharSlot_Track", Vec3.Zero).X, 2);
        Assert.Equal(92f, tree.TransformPoint("far/CharSlot_Track", Vec3.Zero).X, 2);
    }

    [Fact]
    public void 내장_깊이와_focus_표로_세우면_실제_게임_프리팹의_DepthY_DepthScale과_같다()
    {
        // 게임 진행 중 PresentataionUIRoot 프리팹(Stage00 Mid, 배역 오프셋 없음): CharSlot_DepthY (0, −580.56) · DepthScale 1.708.
        var editor = new MotionEditor(new MotionDocument());
        SceneAsset scene = editor.Document.ActiveSceneAsset!;
        RectNodeTree tree = ScenePreview.Build(RigFile, scene, null, null, BuiltinTuning.Depth, null, BuiltinTuning.Focus, BuiltinTuning.StageRig);

        Assert.Equal(1.708f, tree.GetState("s1/CharSlot_DepthScale").LocalScale.X, 3);
        Assert.Equal(-580.56f, tree.GetState("s1/CharSlot_DepthY").AnchoredPosition.Y, 1);
        Assert.Equal(0f, tree.GetState("s1/CharSlot_Track_Focus").AnchoredPosition.X, 3); // place 없음

        // place(left, bust): focus 점이 화면 왼쪽 지점(−0.24·1920)에 온다 — 코어 place 폴드.
        editor.SetObjectPlace(null, "s1", "left");
        Assert.Throws<InvalidOperationException>(() => editor.SetObjectPlace(null, "s1", "middle"));
        Assert.Throws<InvalidOperationException>(() => editor.SetObjectPlace(null, "s1", "left", "nose"));
        RectNodeTree placed = ScenePreview.Build(RigFile, scene, null, null, BuiltinTuning.Depth, null, BuiltinTuning.Focus, BuiltinTuning.StageRig);
        Assert.Equal(-0.24f * 1920f, placed.GetState("s1/CharSlot_Track_Focus").AnchoredPosition.X, 1);
        Assert.Equal(-580.56f, placed.GetState("s1/CharSlot_DepthY").AnchoredPosition.Y, 1); // 깊이 → place 순서라 DepthY는 그대로(게임과 같다)
        Assert.NotEqual(0f, placed.GetState("s1/CharSlot_Track_Focus").AnchoredPosition.Y);

        editor.SetObjectPlace(null, "s1", "left", "face");
        Assert.Equal("face", scene.Find("s1")!.PlaceFocus);
        editor.SetObjectPlace(null, "s1", null);
        Assert.Null(scene.Find("s1")!.Place);
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.DoesNotContain("\"place\"", json, StringComparison.Ordinal);
    }
}
