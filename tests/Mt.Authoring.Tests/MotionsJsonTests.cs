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
    [InlineData(8f, "8")]
    [InlineData(-18f, "-18")]
    [InlineData(0.6f, "0.6")]
    [InlineData(0.3f, "0.3")]
    [InlineData(0.1f, "0.1")]
    [InlineData(1.5f, "1.5")]
    [InlineData(-120f, "-120")]
    public void 수는_invariant_R이고_정수는_소수점_없이다(float value, string expected)
    {
        Assert.Equal(expected, MotionsJson.F(value));
    }

    [Fact]
    public void 음수_0은_0이다()
    {
        // 컴파일 상수 -0f는 0f와 같은 값으로 접히므로 비트로 만든다 — 실제로 음수 0이 F에 들어가는지 본다.
        float negativeZero = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));

        Assert.True(float.IsNegative(negativeZero));
        Assert.Equal("0", MotionsJson.F(negativeZero));
    }

    [Fact]
    public void 읽기는_런타임_로더만큼_관용적이다_빈_성분은_없음이고_토큰은_대소문자를_가리지_않는다()
    {
        const string json = """
            {
              "schema": "motions/2",
              "motions": [
                { "name": "a", "rig": "character", "duration": 1, "end": "Restore",
                  "channels": [ { "node": "N", "kind": " POSITION ", "x": [], "y": [ { "t": 0, "v": 0 }, { "t": 1, "v": 0 } ] } ] }
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
    public void end는_필수이고_빠진_모션은_사유와_함께_건너뛴다_채널에_mode는_없다()
    {
        const string json = """
            {
              "schema": "motions/2",
              "motions": [
                { "name": "no_end", "rig": "character", "duration": 1, "channels": [] },
                { "name": "ok", "rig": "character", "duration": 1, "end": "commit",
                  "channels": [ { "node": "N", "kind": "euler", "z": [ { "t": 0, "v": 0 }, { "t": 1, "v": 1 } ] } ] }
              ]
            }
            """;

        Assert.True(MotionsJson.TryRead(json, out List<MotionClip> clips, out List<string> problems));

        Assert.Equal("ok", Assert.Single(clips).Name);
        string problem = Assert.Single(problems);
        Assert.Contains("no_end", problem, StringComparison.Ordinal);
        Assert.Contains("end는", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void 옛_motions_1은_읽히고_채널의_mode는_무시한다_그리고_알린다()
    {
        const string json = """
            {
              "schema": "motions/1",
              "motions": [
                { "name": "old", "rig": "character", "duration": 1, "end": "commit",
                  "channels": [ { "node": "N", "kind": "position", "mode": "absolute", "x": [ { "t": 0, "v": 140 }, { "t": 1, "v": 360 } ] } ] }
              ]
            }
            """;

        Assert.True(MotionsJson.TryRead(json, out List<MotionClip> clips, out List<string> problems));

        Assert.Equal(140f, Assert.Single(clips).Channels[0].X[0].Value);
        string note = Assert.Single(problems);
        Assert.Contains("motions/1", note, StringComparison.Ordinal);
        Assert.Contains("mode", note, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mode\"", MotionsJson.Write(clips), StringComparison.Ordinal);
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
