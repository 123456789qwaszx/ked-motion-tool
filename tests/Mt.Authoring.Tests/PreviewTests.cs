using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Tests;

public class MotionPreviewTests
{
    private static readonly RigSchemaFile RigFile = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));
    private const string MoveY = "CharacterPortrait_Track_Move_Y";
    private const string Sway = "CharacterPortrait_SwayPivot";

    [Fact]
    public void nod는_가운데서_18_내려가고_길이를_지나면_restore라_시작_상태다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        RectNodeTree template = RigFile.BuildTree("character");
        MotionAsset nod = document.Find("nod")!;

        Assert.Equal(0f, MotionPreview.Pose(template, nod, 0).GetState(MoveY).AnchoredPosition.Y, 4);
        Assert.Equal(-18f, MotionPreview.Pose(template, nod, 0.3).GetState(MoveY).AnchoredPosition.Y, 4);
        Assert.Equal(0f, MotionPreview.Pose(template, nod, 0.6).GetState(MoveY).AnchoredPosition.Y, 4);
        Assert.Equal(0f, MotionPreview.Pose(template, nod, 5).GetState(MoveY).AnchoredPosition.Y, 4);

        // 포즈는 사본이다 — 원본 트리는 그대로.
        Assert.Equal(0f, template.GetState(MoveY).AnchoredPosition.Y);
    }

    [Fact]
    public void bow는_commit이라_길이를_지나도_마지막_키가_남고_중간은_에르미트다()
    {
        MotionAsset bow = Fixtures.NodBowDocument().Find("bow")!;
        RectNodeTree template = RigFile.BuildTree("character");

        Assert.Equal(4f, MotionPreview.Pose(template, bow, 0.2).GetState(Sway).LocalEulerAngles.Z, 3);
        Assert.Equal(8f, MotionPreview.Pose(template, bow, 0.4).GetState(Sway).LocalEulerAngles.Z, 4);
        Assert.Equal(8f, MotionPreview.Pose(template, bow, 9).GetState(Sway).LocalEulerAngles.Z, 4);
    }

    [Fact]
    public void 뮤트된_성분은_재생에서_빠진다()
    {
        MotionAsset nod = Fixtures.NodBowDocument().Find("nod")!;
        RectNodeTree template = RigFile.BuildTree("character");
        var y = new CurveRef(MoveY, MotionChannelKind.Position, Axis.Y);

        Assert.Equal(-18f, MotionPreview.Pose(template, nod, 0.3, c => c != y).GetState(MoveY).AnchoredPosition.Y, 4);
        Assert.Equal(0f, MotionPreview.Pose(template, nod, 0.3, c => c == y).GetState(MoveY).AnchoredPosition.Y, 4);
    }

    [Fact]
    public void relative_배율은_곱이고_absolute_위치는_그_값이며_없는_노드는_건너뛴다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("a", "character", 1f, MotionEnd.Commit);
        editor.AddChannel("a", "CharSlot_Scale", MotionChannelKind.Scale, MotionChannelMode.Relative);
        editor.SetKeys("a", "CharSlot_Scale", MotionChannelKind.Scale, Axis.X, new[] { Fixtures.Key(0f, 1f), Fixtures.Key(1f, 2f) });
        editor.AddChannel("a", "CharSlot_Track_X", MotionChannelKind.Position, MotionChannelMode.Absolute);
        editor.SetKeys("a", "CharSlot_Track_X", MotionChannelKind.Position, Axis.X, new[] { Fixtures.Key(0f, 100f), Fixtures.Key(1f, 300f) });
        editor.AddChannel("a", "NoSuchNode", MotionChannelKind.Position, MotionChannelMode.Absolute);
        editor.SetKeys("a", "NoSuchNode", MotionChannelKind.Position, Axis.X, new[] { Fixtures.Key(0f, 1f), Fixtures.Key(1f, 2f) });

        RectNodeTree template = RigFile.BuildTree("character");
        RectNodeTree pose = MotionPreview.Pose(template, editor.Document.Find("a")!, 1);

        Assert.Equal(2f, pose.GetState("CharSlot_Scale").LocalScale.X, 4);
        Assert.Equal(1f, pose.GetState("CharSlot_Scale").LocalScale.Y, 4); // 없는 성분은 그대로
        Assert.Equal(300f, pose.GetState("CharSlot_Track_X").AnchoredPosition.X, 4);
        Assert.False(pose.Contains("NoSuchNode"));
    }
}

public class PortraitLayoutTests
{
    private static readonly RigSchemaFile RigFile = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));

    [Fact]
    public void 사이징은_런타임과_같다_sizeDelta는_부모_높이_곱_종횡비()
    {
        RectNodeTree tree = RigFile.BuildTree("character");
        PortraitLayout.ApplySizing(tree, 0.8f);

        Vec2 sizeDelta = tree.GetState(PortraitLayout.ImageNodeId).SizeDelta;
        Assert.Equal(1080f * 0.8f, sizeDelta.X, 3);
        Assert.Equal(0f, sizeDelta.Y);
    }

    [Fact]
    public void 보이는_사각형은_높이_맞춤이고_무대_가운데_선다()
    {
        RectNodeTree tree = RigFile.BuildTree("character");
        PortraitLayout.ApplySizing(tree, 0.8f);

        Quad quad = PortraitLayout.VisualQuad(tree, PortraitLayout.ImageNodeId, 0.8f);

        Assert.Equal(-432f, quad.BottomLeft.X, 2);
        Assert.Equal(-540f, quad.BottomLeft.Y, 2);
        Assert.Equal(432f, quad.TopRight.X, 2);
        Assert.Equal(540f, quad.TopRight.Y, 2);
        Assert.Equal(0f, quad.Center.X, 2);
        Assert.Equal(0f, quad.Center.Y, 2);
    }

    [Fact]
    public void 포즈가_바뀌면_사각형이_따라온다_nod_가운데는_18_아래다()
    {
        MotionAsset nod = Fixtures.NodBowDocument().Find("nod")!;
        RectNodeTree template = RigFile.BuildTree("character");
        PortraitLayout.ApplySizing(template, 0.8f);

        Quad rest = PortraitLayout.VisualQuad(MotionPreview.Pose(template, nod, 0), PortraitLayout.ImageNodeId, 0.8f);
        Quad dipped = PortraitLayout.VisualQuad(MotionPreview.Pose(template, nod, 0.3), PortraitLayout.ImageNodeId, 0.8f);

        Assert.Equal(rest.Center.Y - 18f, dipped.Center.Y, 2);
        Assert.Equal(rest.Center.X, dipped.Center.X, 2);
    }

    [Fact]
    public void 회전하면_사각형이_기운다_축_정렬_경계가_아니다()
    {
        MotionAsset bow = Fixtures.NodBowDocument().Find("bow")!;
        RectNodeTree template = RigFile.BuildTree("character");
        PortraitLayout.ApplySizing(template, 0.8f);

        Quad tilted = PortraitLayout.VisualQuad(MotionPreview.Pose(template, bow, 0.4), PortraitLayout.ImageNodeId, 0.8f);

        Assert.NotEqual(tilted.BottomLeft.Y, tilted.BottomRight.Y);
        float width = (tilted.BottomRight - tilted.BottomLeft).X;
        float height = (tilted.TopLeft - tilted.BottomLeft).Y;
        Assert.True(Math.Abs(width - 864f) < 864f * 0.02f, "너비가 거의 보존돼야 한다(8도 회전)");
        Assert.True(height < 1080f && height > 1000f);
    }

    [Fact]
    public void 캔버스_변환과_축()
    {
        Vec2 canvas = PortraitLayout.ToCanvas(new Vec2(10f, 20f), new Vec2(1920f, 1080f));
        Assert.Equal(970f, canvas.X);
        Assert.Equal(520f, canvas.Y);

        RectNodeTree tree = RigFile.BuildTree("character");
        (Vec2 origin, Vec2 xEnd, Vec2 yEnd) = PortraitLayout.Axes(tree, "CharacterPortrait_SwayPivot", 50f);
        Assert.Equal(origin.X + 50f, xEnd.X, 3);
        Assert.Equal(origin.Y + 50f, yEnd.Y, 3);
    }

    [Fact]
    public void 종횡비_0은_거부한다()
    {
        RectNodeTree tree = RigFile.BuildTree("character");
        Assert.Throws<ArgumentOutOfRangeException>(() => PortraitLayout.VisualQuad(tree, PortraitLayout.ImageNodeId, 0f));
    }
}

public class PlaybackClockTests
{
    [Fact]
    public void 루프는_감고_한_번은_끝에서_멈춘다()
    {
        var clock = new PlaybackClock();
        clock.SetDuration(0.6);
        clock.Play();

        Assert.True(clock.Advance(0.5));
        Assert.Equal(0.5, clock.Time, 9);
        Assert.True(clock.Advance(0.2));
        Assert.Equal(0.1, clock.Time, 9);
        Assert.True(clock.IsPlaying);

        clock.Mode = PlaybackMode.Once;
        clock.Advance(1);
        Assert.Equal(0.6, clock.Time, 9);
        Assert.False(clock.IsPlaying);

        // 끝에 선 채 다시 재생하면 범위 처음부터.
        clock.Play();
        Assert.Equal(0, clock.Time, 9);
        Assert.False(clock.Loop);
        clock.Loop = true;
        Assert.Equal(PlaybackMode.Loop, clock.Mode);
    }

    [Fact]
    public void 왕복은_범위_끝에서_되돌아오고_속도는_배율이다()
    {
        var clock = new PlaybackClock { Mode = PlaybackMode.Oscillate, Speed = 2 };
        clock.SetDuration(1);
        clock.SetRange(0.2, 0.6);
        clock.Play();
        Assert.Equal(0.2, clock.Time, 9);

        clock.Advance(0.1); // ×2 → 0.4
        Assert.Equal(0.4, clock.Time, 9);
        clock.Advance(0.15); // 0.7 → 되돌아 0.5
        Assert.Equal(0.5, clock.Time, 9);
        clock.Advance(0.2); // 0.1 → 되돌아 0.3
        Assert.Equal(0.3, clock.Time, 9);
        Assert.True(clock.IsPlaying);
    }

    [Fact]
    public void 재생_범위는_길이_안이고_끝에_붙은_범위는_길이를_따라간다()
    {
        var clock = new PlaybackClock();
        clock.SetDuration(1);
        Assert.Equal(1, clock.RangeEnd);

        clock.SetDuration(2);
        Assert.Equal(2, clock.RangeEnd);

        clock.SetRange(0.5, 1.5);
        clock.SetDuration(3);
        Assert.Equal(1.5, clock.RangeEnd);
        clock.SetDuration(1);
        Assert.Equal(1, clock.RangeEnd);
        Assert.True(clock.RangeStart < clock.RangeEnd);

        Assert.Throws<ArgumentException>(() => clock.SetRange(0.5, 0.5));
        clock.ResetRange();
        Assert.Equal(0, clock.RangeStart);
        Assert.Equal(1, clock.RangeEnd);

        // 범위 밖에서 재생하면 범위 처음으로.
        clock.SetRange(0.4, 0.8);
        clock.ScrubTo(0.1);
        clock.Play();
        Assert.Equal(0.4, clock.Time, 9);
        clock.Stop();
        Assert.Equal(0.4, clock.Time, 9);
    }

    [Fact]
    public void 일시정지면_흐르지_않고_스크럽은_길이_안으로_죈다()
    {
        var clock = new PlaybackClock();
        clock.SetDuration(1);
        Assert.False(clock.Advance(0.5));
        Assert.Equal(0, clock.Time);

        clock.ScrubTo(5);
        Assert.Equal(1, clock.Time);
        clock.ScrubTo(-1);
        Assert.Equal(0, clock.Time);

        clock.Play();
        clock.Toggle();
        Assert.False(clock.IsPlaying);
    }
}

public class PlaybackBackwardTests
{
    [Fact]
    public void 거꾸로_재생은_한_번이면_범위_시작에서_멈추고_루프면_끝으로_감는다()
    {
        var clock = new PlaybackClock();
        clock.SetDuration(1);
        clock.Mode = PlaybackMode.Once;
        clock.ScrubTo(0.6);
        clock.PlayBackward();
        Assert.True(clock.IsReversed);
        clock.Advance(0.2);
        Assert.Equal(0.4, clock.Time, 9);
        clock.Advance(1);
        Assert.Equal(0, clock.Time, 9);
        Assert.False(clock.IsPlaying);

        clock.Mode = PlaybackMode.Loop;
        clock.ScrubTo(0.1);
        clock.PlayBackward();
        clock.Advance(0.3);
        Assert.Equal(0.8, clock.Time, 9);
        Assert.True(clock.IsPlaying);

        clock.Play();
        Assert.False(clock.IsReversed);
    }

    [Fact]
    public void 시작에_서_있으면_거꾸로는_끝부터다()
    {
        var clock = new PlaybackClock();
        clock.SetDuration(1);
        clock.Mode = PlaybackMode.Once;
        clock.PlayBackward();
        Assert.Equal(1, clock.Time, 9);
    }
}

public class WholeFramePlaybackTests
{
    [Fact]
    public void 프레임_단위_재생은_실제_시간을_따르되_보이는_시각은_정수_프레임이다()
    {
        var clock = new PlaybackClock { WholeFrames = true };
        clock.SetDuration(1);
        clock.Play();

        Assert.False(clock.Advance(0.02)); // 0.48프레임 → 0프레임, 안 바뀜
        Assert.Equal(0, clock.Time, 9);
        Assert.True(clock.Advance(0.02)); // 0.96프레임 → 1프레임
        Assert.Equal(1.0 / 24.0, clock.Time, 9);
        clock.Advance(0.5); // 실제 0.54초 = 12.96프레임 → 13프레임 (0.04초가 사라지지 않는다)
        Assert.Equal(13.0 / 24.0, clock.Time, 9);

        clock.WholeFrames = false;
        clock.Advance(0.01);
        Assert.Equal(13.0 / 24.0 + 0.01, clock.Time, 9); // 연속으로 돌아가면 보이던 시각에서 흐른 시간 그대로

        clock.ScrubTo(0.3);
        Assert.Equal(0.3, clock.Time, 9); // 스크럽은 프레임에 안 맞춘다(슬라이더가 스냅한다)
    }

    [Fact]
    public void 프레임_단위_재생도_한_번이면_범위_끝에서_정확히_멈춘다()
    {
        var clock = new PlaybackClock { WholeFrames = true, Mode = PlaybackMode.Once };
        clock.SetDuration(0.55);
        clock.Play();
        clock.Advance(1);
        Assert.Equal(0.55, clock.Time, 6);
        Assert.False(clock.IsPlaying);
    }
}

public class KeyTimesTests
{
    [Fact]
    public void 모든_키_시각은_합쳐_오름차순이고_다음_이전을_찾는다()
    {
        MotionAsset bow = Fixtures.NodBowDocument().Find("bow")!;
        List<float> times = KeyTimes.All(bow);

        Assert.Equal(new[] { 0f, 0.4f }, times);
        Assert.Equal(0.4f, KeyTimes.Next(times, 0));
        Assert.Null(KeyTimes.Next(times, 0.4));
        Assert.Equal(0f, KeyTimes.Previous(times, 0.4));
        Assert.Null(KeyTimes.Previous(times, 0));
        Assert.Equal(0.4f, KeyTimes.Next(times, 0.2));
        Assert.Equal(0f, KeyTimes.Previous(times, 0.2));
    }

    [Fact]
    public void 선택_키_시각()
    {
        MotionAsset nod = Fixtures.NodBowDocument().Find("nod")!;
        var y = new CurveRef("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);

        List<float> selected = KeyTimes.OfSelection(nod, new[] { new KeyRef(y, 2), new KeyRef(y, 1), new KeyRef(y, 9) });
        Assert.Equal(new[] { 0.3f, 0.6f }, selected);
    }
}

public class OnionSkinTests
{
    [Fact]
    public void 앞뒤_프레임이고_멀수록_옅고_범위_밖은_없다()
    {
        List<(double Time, double Opacity)> frames = OnionSkin.Frames(0.1, 0.6, 2);

        Assert.Equal(4, frames.Count);
        Assert.Contains(frames, f => Math.Abs(f.Time - (0.1 - 1 / 24.0)) < 1e-9);
        double near = frames.First(f => Math.Abs(f.Time - (0.1 + 1 / 24.0)) < 1e-9).Opacity;
        double far = frames.First(f => Math.Abs(f.Time - (0.1 + 2 / 24.0)) < 1e-9).Opacity;
        Assert.True(near > far);

        Assert.Equal(2, OnionSkin.Frames(0, 0.6, 2).Count);
        Assert.Empty(OnionSkin.Frames(0.3, 0.6, 0));
    }
}

public class BaseResolutionFileTests
{
    [Fact]
    public void 기준_해상도를_읽고_없거나_0이면_거부한다()
    {
        Vec2 size = BaseResolutionFile.Parse("""{ "canvasName": "[UIManager]", "referenceResolution": { "x": 1920.0, "y": 1080.0 } }""");
        Assert.Equal(new Vec2(1920f, 1080f), size);

        Assert.Throws<System.Text.Json.JsonException>(() => BaseResolutionFile.Parse("""{ "referenceResolution": { "x": 0, "y": 1080 } }"""));
        Assert.Throws<System.Text.Json.JsonException>(() => BaseResolutionFile.Parse("""{ }"""));
        Assert.Null(BaseResolutionFile.TryFindBeside(Fixtures.PathOf("rig-schemas.json")));
    }
}
