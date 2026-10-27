using Ked.Presentation.Core;
using Mt.Authoring.Editing;
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
