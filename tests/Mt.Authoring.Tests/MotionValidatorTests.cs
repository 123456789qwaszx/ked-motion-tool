using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

public class MotionValidatorTests
{
    private static readonly RigSchemaFile RigFile = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));

    [Fact]
    public void nod_bow는_리그_덤프로_검증해도_문제가_0건이다()
    {
        Assert.Empty(MotionValidator.Validate(Fixtures.NodBowDocument(), RigFile.NodeIds));
    }

    [Fact]
    public void 문구는_코어_MotionRules의_것이다_Track_Focus는_키잉할_수_없다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("a", "character", 1f);
        editor.AddChannel("a", "CharSlot_Track_Focus", MotionChannelKind.Position);
        editor.SetKeys("a", "CharSlot_Track_Focus", MotionChannelKind.Position, Axis.X, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(1f, 0f) });

        MotionProblem problem = Assert.Single(MotionValidator.Validate(editor.Document, RigFile.NodeIds));
        Assert.Equal("a", problem.MotionName);
        Assert.Contains("키잉할 수 없다", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 리그_덤프에_없는_노드는_거부하고_덤프가_없으면_노드_검사는_생략한다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("a", "character", 1f);
        editor.AddChannel("a", "NoSuchNode", MotionChannelKind.Position);
        // relative 전 구간 중립값이면 성분이 내보내기에서 빠지므로 가운데 키를 둔다 — 보려는 것은 노드 검사뿐이다.
        editor.SetKeys("a", "NoSuchNode", MotionChannelKind.Position, Axis.X, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.5f, 5f), Fixtures.Key(1f, 0f) });

        Assert.Contains("리그 스키마에 없는 노드", Assert.Single(MotionValidator.Validate(editor.Document, RigFile.NodeIds)).Message, StringComparison.Ordinal);
        Assert.Empty(MotionValidator.Validate(editor.Document));
    }

    [Fact]
    public void 모르는_rigKind는_문서_수준_문제다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("a", "robot", 1f);

        MotionProblem problem = Assert.Single(MotionValidator.Validate(editor.Document, RigFile.NodeIds));
        Assert.Contains("rigKind 'robot'", problem.Message, StringComparison.Ordinal);
        Assert.Contains("character", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 같은_이름이_둘이면_문제다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        document.Motions.Add(MotionAssetCodec.FromClip(MotionAssetCodec.ToClip(document.Motions[0])));

        Assert.Contains(MotionValidator.Validate(document), p => p.Message == "같은 이름이 둘이다");
    }

    [Fact]
    public void 편집_중간_상태는_허용되고_내보내기만_막힌다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("a", "character", 1f, MotionEnd.Restore);
        editor.AddChannel("a", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position);
        // 마지막 키가 길이(1)에 있지 않다 — 편집 중엔 자연스러운 상태.
        editor.SetKeys("a", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.25f, -10f), Fixtures.Key(0.5f, 0f) });

        Assert.False(MotionExport.TryExport(editor.Document, RigFile.NodeIds, out string json, out IReadOnlyList<MotionProblem> problems));
        Assert.Equal("", json);
        Assert.Contains("마지막 키 시각이 길이와 다르다", Assert.Single(problems).Message, StringComparison.Ordinal);

        editor.SetDuration("a", 0.5f);

        Assert.True(MotionExport.TryExport(editor.Document, RigFile.NodeIds, out json, out problems));
        Assert.Empty(problems);
        Assert.Contains("\"duration\": 0.5", json, StringComparison.Ordinal);
    }
}

public class MotionFixesTests
{
    private static readonly CurveRef Y = new("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);

    [Fact]
    public void 문구에서_돕는_손을_고른다()
    {
        Assert.Equal(new[] { MotionFix.EndKeysToDuration, MotionFix.DurationToLastKey }, MotionFixes.Suggest(new MotionProblem("a", "채널 0: Y: 마지막 키 시각이 길이와 다르다 (0.5 ≠ 1)")));
        Assert.Equal(new[] { MotionFix.FirstKeysToNeutral }, MotionFixes.Suggest(new MotionProblem("a", "채널 0: X: relative 채널의 첫 키 값이 중립값 0이 아니다 (3)")));
        Assert.Equal(new[] { MotionFix.LastKeysToNeutral, MotionFix.EndToCommit }, MotionFixes.Suggest(new MotionProblem("a", "채널 0: Y: restore 모션의 마지막 키 값이 중립값 0이 아니다 (5) — 남기려면 commit")));
        Assert.Empty(MotionFixes.Suggest(new MotionProblem("a", "채널이 하나 이상이어야 한다")));
    }

    [Fact]
    public void 끝_키를_길이로_옮기면_문제가_사라지고_되돌리기_한_번이다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        editor.SetDuration("nod", 0.5f); // 마지막 키 0.6 ≠ 0.5
        Assert.NotEmpty(MotionValidator.Validate(editor.Document));
        long before = editor.Revision;

        Assert.True(MotionFixes.Apply(editor, "nod", MotionFix.EndKeysToDuration));

        Assert.Equal(before + 1, editor.Revision);
        Assert.Empty(MotionValidator.Validate(editor.Document));
        Assert.Equal(0.5f, Y.Resolve(editor.Document.Find("nod")!)!.Keys[^1].T, 4);
        Assert.False(MotionFixes.Apply(editor, "nod", MotionFix.EndKeysToDuration)); // 바꿀 것이 없다
    }

    [Fact]
    public void 길이를_마지막_키에_맞추기_첫_키_중립_마지막_키_중립_commit()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        editor.SetDuration("nod", 0.5f);
        Assert.True(MotionFixes.Apply(editor, "nod", MotionFix.DurationToLastKey));
        Assert.Equal(0.6f, editor.Document.Find("nod")!.Duration, 4);

        editor.UpdateKey("nod", Y.Node, Y.Kind, Y.Axis, 0, new KeyAsset { T = 0f, V = 3f });
        editor.UpdateKey("nod", Y.Node, Y.Kind, Y.Axis, 2, new KeyAsset { T = 0.6f, V = 5f });
        Assert.NotEmpty(MotionValidator.Validate(editor.Document));

        Assert.True(MotionFixes.Apply(editor, "nod", MotionFix.FirstKeysToNeutral));
        Assert.True(MotionFixes.Apply(editor, "nod", MotionFix.LastKeysToNeutral));
        Assert.Empty(MotionValidator.Validate(editor.Document));

        Assert.True(MotionFixes.Apply(editor, "nod", MotionFix.EndToCommit));
        Assert.Equal(MotionEnd.Commit, editor.Document.Find("nod")!.End);
        Assert.False(MotionFixes.Apply(editor, "nod", MotionFix.EndToCommit));

        // bow(absolute)를 restore로 바꾸면 relative가 필요하다 — 채널을 relative로.
        editor.SetEnd("bow", MotionEnd.Restore);
        Assert.True(MotionFixes.Apply(editor, "bow", MotionFix.ChannelsToRelative));
        Assert.All(editor.Document.Find("bow")!.Channels, c => Assert.Equal(MotionChannelMode.Relative, c.Mode));
    }

    [Fact]
    public void 첫_키를_0초로_옮기면_겹치는_키는_사라진다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        editor.SetKeys("nod", Y.Node, Y.Kind, Y.Axis, new[] { Fixtures.Key(0.1f, 0f), Fixtures.Key(0.3f, -18f), Fixtures.Key(0.6f, 0f) });

        Assert.True(MotionFixes.Apply(editor, "nod", MotionFix.FirstKeysToZero));

        Assert.Equal(new[] { 0f, 0.3f, 0.6f }, Y.Resolve(editor.Document.Find("nod")!)!.Keys.Select(k => (float)Math.Round(k.T, 4)));
    }
}

public class ToolSettingsTests
{
    [Fact]
    public void 설정은_왕복하고_없으면_기본값이고_깨지면_기본값에_사유를_남긴다()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, Mt.Authoring.Settings.ToolSettings.FileName);

        try
        {
            var settings = Mt.Authoring.Settings.ToolSettings.Load(path);
            Assert.True(settings.SnapEnabled);
            Assert.Equal(TangentMode.Auto, settings.DefaultTangent);

            settings.SnapEnabled = false;
            settings.DefaultTangent = TangentMode.Plateau;
            settings.OnionSkinFrames = 4;
            settings.AutoSaveMinutes = 1;
            settings.Touch("/a/one.motion-authoring.json");
            settings.Touch("/a/two.motion-authoring.json");
            settings.Touch("/a/one.motion-authoring.json");
            settings.Save(path);

            var back = Mt.Authoring.Settings.ToolSettings.Load(path);
            Assert.False(back.SnapEnabled);
            Assert.Equal(TangentMode.Plateau, back.DefaultTangent);
            Assert.Equal(4, back.OnionSkinFrames);
            Assert.Equal(1, back.AutoSaveMinutes);
            Assert.Equal(2, back.RecentFiles.Count);
            Assert.EndsWith("one.motion-authoring.json", back.RecentFiles[0]);
            Assert.Contains("\"plateau\"", File.ReadAllText(path), StringComparison.Ordinal);

            File.WriteAllText(path, "{ broken");
            var broken = Mt.Authoring.Settings.ToolSettings.Load(path);
            Assert.True(broken.SnapEnabled);
            Assert.NotNull(broken.LoadProblem);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void 최근_파일은_여덟_개까지다()
    {
        var settings = new Mt.Authoring.Settings.ToolSettings();

        for (int i = 0; i < 12; i++)
        {
            settings.Touch($"/x/{i}.json");
        }

        Assert.Equal(Mt.Authoring.Settings.ToolSettings.MaxRecentFiles, settings.RecentFiles.Count);
        Assert.EndsWith("11.json", settings.RecentFiles[0]);
    }
}
