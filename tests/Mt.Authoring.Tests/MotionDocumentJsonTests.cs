using System.Text.Json;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Serialization;

namespace Mt.Authoring.Tests;

public class MotionDocumentJsonTests
{
    [Fact]
    public void 저작_원본은_쓰고_읽으면_같은_문서다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        var editor = new MotionEditor(document);
        editor.SetRigSchemas("../ExportedTuning/rig-schemas.json", "abc123");
        editor.SetPreviewPortrait("assets/harin_a_01.png");
        editor.SetNotes("nod", "끄덕임 — 한글 메모");

        string first = MotionDocumentJson.Write(editor.Document);
        MotionDocument read = MotionDocumentJson.Read(first);
        string second = MotionDocumentJson.Write(read);

        Assert.Equal(first, second);
        Assert.Equal(AuthoringFormat.AuthoringSchema, read.Schema);
        Assert.Equal("abc123", read.RigSchemas!.Sha256);
        Assert.Equal("assets/harin_a_01.png", read.Preview!.Portrait);
        Assert.Equal("끄덕임 — 한글 메모", read.Find("nod")!.Notes);
        Assert.Contains("끄덕임", first, StringComparison.Ordinal);
    }

    [Fact]
    public void 제작_정보_탄젠트_모드와_끊김은_저작_원본에_남고_열거형은_소문자다()
    {
        var document = new MotionDocument();
        var editor = new MotionEditor(document);
        editor.AddMotion("a", "character", 1f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("a", "N", MotionChannelKind.Euler);
        editor.SetKeys("a", "N", MotionChannelKind.Euler, Axis.Z, new[]
        {
            new KeyAsset { T = 0f, V = 0f, Tangent = TangentMode.Auto },
            new KeyAsset { T = 1f, V = 10f, InTangent = 3f, OutTangent = -3f, Tangent = TangentMode.Free, Broken = true },
        });

        string json = MotionDocumentJson.Write(editor.Document);

        Assert.Contains("\"end\": \"commit\"", json, StringComparison.Ordinal);
        Assert.Contains("\"defaultMode\": \"absolute\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"euler\"", json, StringComparison.Ordinal);
        Assert.Contains("\"tangent\": \"auto\"", json, StringComparison.Ordinal);
        Assert.Contains("\"broken\": true", json, StringComparison.Ordinal);
        // 없는 성분은 쓰지 않는다.
        Assert.DoesNotContain("\"x\"", json, StringComparison.Ordinal);

        MotionDocument read = MotionDocumentJson.Read(json);
        ChannelAsset channel = read.Find("a")!.Channels[0];
        Assert.Null(channel.X);
        Assert.Equal(TangentMode.Free, channel.Z!.Keys[1].Tangent);
        Assert.True(channel.Z.Keys[1].Broken);
    }

    [Fact]
    public void 다른_스키마는_거부한다()
    {
        Assert.Throws<JsonException>(() => MotionDocumentJson.Read("""{ "schema": "motion-authoring/0", "motions": [] }"""));
    }

    [Fact]
    public void 빈_성분_목록은_없음으로_읽는다()
    {
        string json = """
            { "schema": "motion-authoring/1",
              "motions": [ { "name": "a", "rig": "character", "duration": 1, "end": "restore",
                             "channels": [ { "node": "N", "kind": "position", "mode": "relative", "x": { "keys": [] } } ] } ] }
            """;

        MotionDocument read = MotionDocumentJson.Read(json);
        Assert.Null(read.Find("a")!.Channels[0].X);
    }
}
