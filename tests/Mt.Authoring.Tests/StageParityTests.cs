using System.Text;
using System.Text.Json;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>
/// 무대 런타임 등가(M6) — 툴이 내보낸 파일을 런타임이 읽으면 툴 미리보기와 같은 포즈가 나오는가.
///
/// 견본 둘(<c>Fixtures/stage-parity/</c>)이 두 저장소를 잇는다. <c>motions.json</c>은 이 툴의 내보내기 산출물,
/// <c>poses.json</c>은 0이 아닌 시작값 위에 그 모션을 relative·absolute로 얹은 포즈 표다(길이를 지난 뒤 = 끝 상태 포함).
/// 무대 저장소(<c>ked-stage-runtime</c>)의 <c>UnityParity/ToolExportMotionParityTests</c>가 같은 두 파일을
/// 런타임 로더(<c>MotionLibrary</c>, Unity JsonUtility)와 리듀서로 읽어 같은 표를 확인한다 — 견본을 바꾸면 그쪽 사본도 같이.
///
/// 모션 셋은 규약의 갈래를 하나씩 덮는다: sway(restore · 회전 z만 · 탄젠트 있음), step(commit · 위치 x만 · 첫 키가
/// 중립값이 아니라 relative면 튄다), grow(commit · 배율 x·y · relative는 곱셈).
/// </summary>
public class StageParityTests
{
    private static readonly RigSchemaFile RigFile = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));
    private const string Slot = "c1";
    private const float Eps = 1e-3f;

    private sealed record Sample(string Motion, MotionChannelMode Mode, float T, string Node, MotionChannelKind Kind, Vec3 Value);

    private sealed record Start(string Node, MotionChannelKind Kind, Vec3 Value);

    [Fact]
    public void 편집기로_지어_내보내면_견본_motions_json과_바이트까지_같다()
    {
        Assert.True(MotionExport.TryExport(ParityDocument(), RigFile.NodeIds, out string json, out IReadOnlyList<MotionProblem> problems),
            string.Join("\n", problems.Select(p => p.Message)));

        byte[] expected = Fixtures.BytesOf("stage-parity/motions.json");
        byte[] actual = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);

        Assert.Equal(Encoding.UTF8.GetString(expected), json);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 미리보기_포즈가_포즈_표와_같다()
    {
        var editor = new MotionEditor(ParityDocument());
        (List<Start> starts, List<Sample> samples) = ReadPoses();

        Assert.Equal(34, samples.Count);

        foreach (Sample sample in samples)
        {
            editor.SetPreviewMode(sample.Motion, sample.Mode);

            RectNodeTree template = RigFile.BuildTree("character");

            foreach (Start start in starts)
            {
                template.SetState(start.Node, MotionPreview.Apply(start.Kind, template.GetState(start.Node), start.Value));
            }

            RectNodeTree pose = MotionPreview.Pose(template, editor.Document.Find(sample.Motion)!, sample.T);

            AssertNear(sample, ValueOf(sample.Kind, pose.GetState(sample.Node)));
        }
    }

    [Fact]
    public void 내보낸_파일을_리듀서로_접은_끝_상태가_포즈_표의_끝과_같다()
    {
        string json = Fixtures.TextOf("stage-parity/motions.json");
        Assert.True(MotionsJson.TryRead(json, out List<MotionClip> clips, out List<string> problems), string.Join("\n", problems));
        Assert.Empty(problems);

        foreach (MotionClip clip in clips)
        {
            Assert.True(MotionRules.TryValidate(clip, RigFile.NodeIds["character"], out string reason), reason);
        }

        var tuning = new StageReducerTuning
        {
            RigSchemas = RigFile.Dto,
            ReferenceStageWidth = 1920f,
            BaseResolution = new Vec2(1920f, 1080f),
            RoleAnchors = new RoleAnchorTuningBodyDto(),
            Motions = clips.ToDictionary(c => c.Name, c => c),
        };

        StageState spawned = StageReducer.Apply(StageReducer.CreateInitialState(tuning), Cmd("slot", Slot), tuning);
        Assert.Empty(spawned.Unhandled);

        (List<Start> starts, List<Sample> samples) = ReadPoses();

        foreach (Start start in starts)
        {
            string key = StageState.NodeKeyOf(Slot, start.Node);
            spawned.Nodes.SetState(key, MotionPreview.Apply(start.Kind, spawned.Nodes.GetState(key), start.Value));
        }

        // 길이를 지난 표본이 곧 정지 프레임이다 — restore는 시작값, commit은 마지막 키를 mode로 합성한 값.
        List<Sample> ends = samples.Where(s => s.T > tuning.Motions[s.Motion].Duration).ToList();
        Assert.Equal(6, ends.Count);

        foreach (Sample sample in ends)
        {
            StageState folded = StageReducer.Apply(spawned, Cmd("motion", Slot, sample.Motion, MotionModeToken.Of(sample.Mode)), tuning);

            Assert.Empty(folded.Unhandled);
            AssertNear(sample, ValueOf(sample.Kind, folded.Nodes.GetState(StageState.NodeKeyOf(Slot, sample.Node))));
        }
    }

    /// <summary>견본 motions.json의 세 모션을 편집기 명령으로 짓는다. 탄젠트는 손으로 둔 값(free) 그대로 나간다.</summary>
    private static MotionDocument ParityDocument()
    {
        var editor = new MotionEditor(new MotionDocument());

        editor.AddMotion("sway", "character", 0.5f, MotionEnd.Restore);
        editor.AddChannel("sway", "CharacterPortrait_SwayPivot", MotionChannelKind.Euler);
        editor.SetKeys("sway", "CharacterPortrait_SwayPivot", MotionChannelKind.Euler, Axis.Z, new[]
        {
            Free(0f, 0f, 0f, 40f), Free(0.25f, 6f, 0f, 0f), Free(0.5f, 0f, -40f, 0f),
        });

        editor.AddMotion("step", "character", 0.4f, MotionEnd.Commit);
        editor.AddChannel("step", "CharSlot_Track_X", MotionChannelKind.Position);
        editor.SetKeys("step", "CharSlot_Track_X", MotionChannelKind.Position, Axis.X, new[]
        {
            Free(0f, 10f, 0f, 150f), Free(0.4f, 80f, 50f, 0f),
        });

        editor.AddMotion("grow", "character", 0.5f, MotionEnd.Commit);
        editor.AddChannel("grow", "CharSlot_Scale", MotionChannelKind.Scale);
        editor.SetKeys("grow", "CharSlot_Scale", MotionChannelKind.Scale, Axis.X, new[]
        {
            Fixtures.Key(0f, 1f), Fixtures.Key(0.5f, 1.5f),
        });
        editor.SetKeys("grow", "CharSlot_Scale", MotionChannelKind.Scale, Axis.Y, new[]
        {
            Fixtures.Key(0f, 1f), Fixtures.Key(0.25f, 0.9f), Fixtures.Key(0.5f, 1.2f),
        });

        return editor.Document;
    }

    private static KeyAsset Free(float t, float v, float inTangent, float outTangent) => new()
    {
        T = t,
        V = v,
        InTangent = inTangent,
        OutTangent = outTangent,
        Tangent = TangentMode.Free,
        Broken = inTangent != outTangent,
    };

    private static StageCommand Cmd(string name, params string[] args) => new(name, args, "stage-parity:1");

    private static Vec3 ValueOf(MotionChannelKind kind, in RectNodeState state) => kind switch
    {
        MotionChannelKind.Position => new Vec3(state.AnchoredPosition, 0f),
        MotionChannelKind.Scale => state.LocalScale,
        _ => state.LocalEulerAngles,
    };

    private static void AssertNear(Sample sample, Vec3 actual)
    {
        string where = $"{sample.Motion} {MotionModeToken.Of(sample.Mode)} t={sample.T} {sample.Node}";

        Assert.True(Math.Abs(sample.Value.X - actual.X) < Eps, $"{where} x: 표 {sample.Value.X}, 실제 {actual.X}");
        Assert.True(Math.Abs(sample.Value.Y - actual.Y) < Eps, $"{where} y: 표 {sample.Value.Y}, 실제 {actual.Y}");
        Assert.True(Math.Abs(sample.Value.Z - actual.Z) < Eps, $"{where} z: 표 {sample.Value.Z}, 실제 {actual.Z}");
    }

    private static (List<Start> Starts, List<Sample> Samples) ReadPoses()
    {
        using JsonDocument doc = JsonDocument.Parse(Fixtures.TextOf("stage-parity/poses.json"));
        JsonElement root = doc.RootElement;

        Assert.Equal("motion-poses/1", root.GetProperty("schema").GetString());

        List<Start> starts = root.GetProperty("start").EnumerateArray()
            .Select(e => new Start(Text(e, "node"), KindOf(Text(e, "kind")), VecOf(e)))
            .ToList();

        List<Sample> samples = root.GetProperty("samples").EnumerateArray()
            .Select(e =>
            {
                Assert.True(MotionModeToken.TryParse(Text(e, "mode"), out MotionChannelMode mode));
                return new Sample(Text(e, "motion"), mode, e.GetProperty("t").GetSingle(), Text(e, "node"), KindOf(Text(e, "kind")), VecOf(e));
            })
            .ToList();

        return (starts, samples);
    }

    private static string Text(JsonElement e, string name) => e.GetProperty(name).GetString() ?? "";

    private static Vec3 VecOf(JsonElement e)
        => new(e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(), e.GetProperty("z").GetSingle());

    private static MotionChannelKind KindOf(string token) => token switch
    {
        "position" => MotionChannelKind.Position,
        "scale" => MotionChannelKind.Scale,
        "euler" => MotionChannelKind.Euler,
        _ => throw new InvalidDataException($"poses.json: 모르는 kind '{token}'"),
    };
}
