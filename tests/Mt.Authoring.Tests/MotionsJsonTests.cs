using System.Text;
using Ked.Presentation.Core;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>
/// 심판: Unity 내보내기가 만든 견본과 **바이트까지** 같아야 한다 — 파일에 "무엇으로 만들었는지"가 남지 않는다.
/// </summary>
public class MotionsJsonTests
{
    [Fact]
    public void nod_bow를_편집기로_지어_내보내면_Unity_산출물과_바이트까지_같다()
    {
        byte[] expected = Fixtures.BytesOf("motions.nod-bow.json");
        byte[] actual = MotionsJson.WriteBytes(MotionExport.ToClips(Fixtures.NodBowDocument()));

        Assert.Equal(Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 견본을_읽어_다시_쓰면_같은_바이트다()
    {
        string text = Fixtures.TextOf("motions.nod-bow.json");

        Assert.True(MotionsJson.TryRead(text, out List<MotionClip> clips, out List<string> problems), string.Join("\n", problems));
        Assert.Empty(problems);
        Assert.Equal(new[] { "nod", "bow" }, clips.Select(c => c.Name));

        Assert.Equal(text, MotionsJson.Write(clips));
    }

    [Fact]
    public void 산출물은_BOM이_없고_LF이고_끝에_개행_하나다()
    {
        byte[] bytes = MotionsJson.WriteBytes(MotionExport.ToClips(Fixtures.NodBowDocument()));

        Assert.Equal((byte)'{', bytes[0]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.NotEqual((byte)'\n', bytes[^2]);
    }

    [Theory]
    [InlineData(0f, "0")]
    [InlineData(-0f, "0")]
    [InlineData(8f, "8")]
    [InlineData(-18f, "-18")]
    [InlineData(0.6f, "0.6")]
    [InlineData(0.3f, "0.3")]
    [InlineData(0.1f, "0.1")]
    [InlineData(1.5f, "1.5")]
    [InlineData(-120f, "-120")]
    public void 수는_invariant_R이고_정수는_소수점_없이_음수_0은_0이다(float value, string expected)
    {
        Assert.Equal(expected, MotionsJson.F(value));
    }

    [Fact]
    public void 읽기는_런타임_로더만큼_관용적이다_빈_성분은_없음이고_토큰은_대소문자를_가리지_않는다()
    {
        const string json = """
            {
              "schema": "motions/1",
              "motions": [
                { "name": "a", "rig": "character", "duration": 1, "end": "Restore",
                  "channels": [ { "node": "N", "kind": " POSITION ", "mode": "relative", "x": [], "y": [ { "t": 0, "v": 0 }, { "t": 1, "v": 0 } ] } ] }
              ]
            }
            """;

        Assert.True(MotionsJson.TryRead(json, out List<MotionClip> clips, out List<string> problems));
        Assert.Empty(problems);

        MotionClip a = Assert.Single(clips);
        Assert.Equal(MotionEnd.Restore, a.End);
        MotionChannel ch = Assert.Single(a.Channels);
        Assert.Equal(MotionChannelKind.Position, ch.Kind);
        Assert.Null(ch.X);
        Assert.NotNull(ch.Y);
        Assert.Equal(2, ch.Y.Length);
        Assert.Equal(0f, ch.Y[1].InTangent);
    }

    [Fact]
    public void end와_mode는_필수다_빠진_모션은_사유와_함께_건너뛴다()
    {
        const string json = """
            {
              "schema": "motions/1",
              "motions": [
                { "name": "no_end", "rig": "character", "duration": 1, "channels": [] },
                { "name": "no_mode", "rig": "character", "duration": 1, "end": "commit",
                  "channels": [ { "node": "N", "kind": "euler", "z": [ { "t": 0, "v": 0 }, { "t": 1, "v": 1 } ] } ] },
                { "name": "ok", "rig": "character", "duration": 1, "end": "commit",
                  "channels": [ { "node": "N", "kind": "euler", "mode": "absolute", "z": [ { "t": 0, "v": 0 }, { "t": 1, "v": 1 } ] } ] }
              ]
            }
            """;

        Assert.True(MotionsJson.TryRead(json, out List<MotionClip> clips, out List<string> problems));

        Assert.Equal("ok", Assert.Single(clips).Name);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("no_end", StringComparison.Ordinal) && p.Contains("end는", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("no_mode", StringComparison.Ordinal) && p.Contains("mode는", StringComparison.Ordinal));
    }

    [Fact]
    public void 깨진_JSON은_false이고_사유가_하나다()
    {
        Assert.False(MotionsJson.TryRead("{ not json", out List<MotionClip> clips, out List<string> problems));
        Assert.Empty(clips);
        Assert.Single(problems);
    }

    [Fact]
    public void 다른_스키마는_경고하되_읽기는_시도한다()
    {
        const string json = """{ "schema": "motions/0", "motions": [] }""";

        Assert.True(MotionsJson.TryRead(json, out List<MotionClip> clips, out List<string> problems));
        Assert.Empty(clips);
        Assert.Single(problems);
        Assert.Contains("motions/0", problems[0], StringComparison.Ordinal);
    }
}
