using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;

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
}
