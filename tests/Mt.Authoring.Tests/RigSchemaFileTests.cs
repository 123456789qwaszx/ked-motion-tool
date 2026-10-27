using System.Text;
using System.Text.Json;
using Ked.Presentation.Core;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Tests;

public class RigSchemaFileTests
{
    [Fact]
    public void 덤프에서_리그와_노드_목록이_나온다_하드코딩된_이름은_없다()
    {
        RigSchemaFile rig = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));

        Assert.Contains("character", rig.RigKinds);
        Assert.Contains("background", rig.RigKinds);
        Assert.Contains("CharacterPortrait_Track_Move_Y", rig.NodeIds["character"]);
        Assert.Contains("CharacterPortrait_SwayPivot", rig.NodeIds["character"]);
        Assert.DoesNotContain("CharacterPortrait_SwayPivot", rig.NodeIds["background"]);
        Assert.True(rig.TryGetRig("character", out RigSchemaRigDto character));
        Assert.Equal(26, character.nodes.Count);
    }

    [Fact]
    public void 해시는_파일_바이트의_SHA256_소문자다()
    {
        byte[] bytes = Fixtures.BytesOf("rig-schemas.json");
        RigSchemaFile rig = RigSchemaFile.Parse(bytes);

        Assert.Equal(64, rig.Sha256.Length);
        Assert.Equal(rig.Sha256, rig.Sha256.ToLowerInvariant());
        Assert.Equal(RigSchemaFile.ComputeSha256(bytes), rig.Sha256);
        Assert.NotEqual(rig.Sha256, RigSchemaFile.ComputeSha256(Encoding.UTF8.GetBytes("{}")));
    }

    [Fact]
    public void 코어_트리로_세워지고_시작값은_덤프의_것이다()
    {
        RigSchemaFile rig = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));

        RectNodeTree tree = rig.BuildTree("character", "c1/");
        RectNodeState sway = tree.GetState("c1/CharacterPortrait_SwayPivot");

        Assert.Equal(1f, sway.LocalScale.X);
        Assert.Throws<KeyNotFoundException>(() => tree.GetState("CharacterPortrait_SwayPivot"));
    }

    [Fact]
    public void 손상된_덤프는_예외다_0으로_떨어뜨리지_않는다()
    {
        Assert.Throws<JsonException>(() => RigSchemaFile.Parse(Encoding.UTF8.GetBytes("""{ "rigs": [] }""")));
        Assert.ThrowsAny<Exception>(() => RigSchemaFile.Parse(Encoding.UTF8.GetBytes(
            """{ "capturedUnderParentSize": { "x": 1920, "y": 1080 }, "rigs": [ { "rigKind": "character", "nodes": [ { "id": "__root", "parent": "" } ] } ] }""")));
    }
}
