using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Serialization;

namespace Mt.Authoring.Tests;

public class MotionEditorTests
{
    [Fact]
    public void 되돌리기_한_번은_조작_하나다()
    {
        var editor = new MotionEditor(new MotionDocument());
        var kinds = new List<MotionChangeKind>();
        editor.Changed += (_, e) => kinds.Add(e.Kind);

        editor.AddMotion("a", "character", 1f);
        editor.AddChannel("a", "N", MotionChannelKind.Position);
        editor.AddKey("a", "N", MotionChannelKind.Position, Axis.X, Fixtures.Key(0f, 0f));
        editor.AddKey("a", "N", MotionChannelKind.Position, Axis.X, Fixtures.Key(1f, 5f));

        Assert.Equal(4, editor.Revision);
        Assert.Equal(new[] { MotionChangeKind.Structure, MotionChangeKind.Structure, MotionChangeKind.Keys, MotionChangeKind.Keys }, kinds);
        Assert.Equal(2, editor.Document.Find("a")!.Channels[0].X!.Keys.Count);

        editor.Undo();
        Assert.Single(editor.Document.Find("a")!.Channels[0].X!.Keys);

        editor.Undo();
        Assert.Null(editor.Document.Find("a")!.Channels[0].X);

        editor.Undo();
        Assert.Empty(editor.Document.Find("a")!.Channels);
        Assert.True(editor.CanRedo);

        editor.Redo();
        editor.Redo();
        editor.Redo();
        Assert.False(editor.CanRedo);
        Assert.Equal(2, editor.Document.Find("a")!.Channels[0].X!.Keys.Count);
        Assert.Equal(5f, editor.Document.Find("a")!.Channels[0].X!.Keys[1].V);
    }

    [Fact]
    public void 새_조작은_다시하기_기록을_지운다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("a", "character", 1f);
        editor.Undo();
        Assert.True(editor.CanRedo);

        editor.AddMotion("b", "character", 1f);
        Assert.False(editor.CanRedo);
        Assert.Null(editor.Document.Find("a"));
        Assert.NotNull(editor.Document.Find("b"));
    }

    [Fact]
    public void 같은_시각의_키는_하나다_더하면_바뀌고_시각_순서를_지킨다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("a", "character", 1f);
        editor.AddChannel("a", "N", MotionChannelKind.Position);
        editor.AddKey("a", "N", MotionChannelKind.Position, Axis.X, Fixtures.Key(1f, 1f));
        editor.AddKey("a", "N", MotionChannelKind.Position, Axis.X, Fixtures.Key(0f, 0f));
        editor.AddKey("a", "N", MotionChannelKind.Position, Axis.X, Fixtures.Key(1f, 9f));

        List<KeyAsset> keys = editor.Document.Find("a")!.Channels[0].X!.Keys;
        Assert.Equal(new[] { 0f, 1f }, keys.Select(k => k.T));
        Assert.Equal(9f, keys[1].V);

        editor.UpdateKey("a", "N", MotionChannelKind.Position, Axis.X, 1, Fixtures.Key(-0.5f, 9f));
        Assert.Equal(new[] { -0.5f, 0f }, editor.Document.Find("a")!.Channels[0].X!.Keys.Select(k => k.T));

        editor.RemoveKey("a", "N", MotionChannelKind.Position, Axis.X, 0);
        editor.RemoveKey("a", "N", MotionChannelKind.Position, Axis.X, 0);
        Assert.Null(editor.Document.Find("a")!.Channels[0].X);
    }

    [Fact]
    public void 호출자의_키_객체는_문서에_꽂히지_않는다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("a", "character", 1f);
        editor.AddChannel("a", "N", MotionChannelKind.Position);
        KeyAsset mine = Fixtures.Key(0f, 0f);
        KeyAsset stored = editor.AddKey("a", "N", MotionChannelKind.Position, Axis.X, mine);

        mine.V = 99f;

        Assert.NotSame(mine, stored);
        Assert.Equal(0f, editor.Document.Find("a")!.Channels[0].X!.Keys[0].V);
    }

    [Fact]
    public void 중복_이름과_중복_채널은_거부한다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("a", "character", 1f);
        editor.AddChannel("a", "N", MotionChannelKind.Position);

        Assert.Throws<InvalidOperationException>(() => editor.AddMotion("a", "character", 2f));
        Assert.Throws<InvalidOperationException>(() => editor.AddChannel("a", "N", MotionChannelKind.Position));
        Assert.Throws<KeyNotFoundException>(() => editor.SetDuration("zzz", 1f));
        Assert.Throws<KeyNotFoundException>(() => editor.RemoveChannel("a", "M", MotionChannelKind.Position));

        editor.AddMotion("b", "character", 1f);
        Assert.Throws<InvalidOperationException>(() => editor.RenameMotion("b", "a"));
        editor.RenameMotion("b", "c");
        Assert.NotNull(editor.Document.Find("c"));
    }

    [Fact]
    public void 미리보기_mode는_모션의_것이고_채널에는_mode가_없다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("a", "character", 1f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("a", "N", MotionChannelKind.Position);

        Assert.Equal(MotionChannelMode.Absolute, editor.Document.Find("a")!.PreviewMode);
        Assert.Equal(MotionChannelMode.Relative, editor.AddMotion("b", "character", 1f).PreviewMode);
    }

    [Fact]
    public void 가져오기는_이름이_겹치면_replace일_때만_바꾼다_그리고_한_번에_되돌린다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        editor.SetDuration("nod", 9f);

        Assert.True(MotionsJson.TryRead(Fixtures.TextOf("motions.nod-bow.json"), out List<MotionClip> clips, out _));

        long before = editor.Revision;
        Assert.Empty(editor.ImportClips(clips, replace: false));
        Assert.Equal(9f, editor.Document.Find("nod")!.Duration);
        Assert.Equal(before, editor.Revision);

        Assert.Equal(new[] { "nod", "bow" }, editor.ImportClips(clips, replace: true));
        Assert.Equal(before + 1, editor.Revision);
        Assert.Equal(0.6f, editor.Document.Find("nod")!.Duration);

        editor.Undo();
        Assert.Equal(9f, editor.Document.Find("nod")!.Duration);
    }

    [Fact]
    public void Replace는_기록을_버린다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("a", "character", 1f);

        editor.Replace(Fixtures.NodBowDocument());

        Assert.False(editor.CanUndo);
        Assert.Null(editor.Document.Find("a"));
        Assert.NotNull(editor.Document.Find("nod"));
    }

    [Fact]
    public void SetPreviewMode는_렌즈만_바꾸고_키_값은_그대로이며_같으면_아무것도_안_한다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("nod", "character", 1f);
        editor.AddChannel("nod", "A", MotionChannelKind.Position);
        editor.SetKeys("nod", "A", MotionChannelKind.Position, Axis.X, new[] { Fixtures.Key(0f, 140f), Fixtures.Key(1f, 360f) });
        long revision = editor.Revision;

        editor.SetPreviewMode("nod", MotionChannelMode.Absolute);
        MotionAsset nod = editor.Document.Find("nod")!;
        Assert.Equal(MotionChannelMode.Absolute, nod.PreviewMode);
        Assert.Equal(140f, nod.Channels[0].X!.Keys[0].V);
        Assert.Equal(revision + 1, editor.Revision);

        editor.SetPreviewMode("nod", MotionChannelMode.Absolute); // 같으면 아무것도 안 한다
        Assert.Equal(revision + 1, editor.Revision);

        editor.Undo();
        Assert.Equal(MotionChannelMode.Relative, editor.Document.Find("nod")!.PreviewMode);
    }

    private static readonly CurveRef NodY = new("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);

    [Fact]
    public void 바뀐_것이_없는_명령은_기록도_다시하기_삭제도_Revision도_없다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        long revision = editor.Revision;

        editor.SetKeyValues("nod", new[] { new KeyRef(NodY, 1) }, k => k.V); // 같은 값
        editor.SetDuration("nod", 0.6f);                                      // 같은 길이
        Assert.False(editor.CanUndo);
        Assert.Equal(revision, editor.Revision);

        editor.SetKeyValues("nod", new[] { new KeyRef(NodY, 1) }, k => k.V + 1f);
        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.True(editor.CanRedo);

        editor.SetKeyValues("nod", new[] { new KeyRef(NodY, 1) }, k => k.V); // 되돌린 뒤 같은 값 확정 — 다시하기가 남는다
        Assert.True(editor.CanRedo);

        editor.SetKeyValues("nod", new[] { new KeyRef(NodY, 1) }, k => k.V - 1f); // 실제 새 조작 — 다시하기가 사라진다
        Assert.False(editor.CanRedo);
    }

    [Fact]
    public void 던지는_명령은_문서도_기록도_남기지_않는다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        long revision = editor.Revision;

        Assert.Throws<InvalidOperationException>(() => editor.Batch(MotionChangeKind.Keys, () =>
        {
            editor.SetDuration("nod", 9f);
            throw new InvalidOperationException("중간에");
        }));

        Assert.Equal(0.6f, editor.Document.Find("nod")!.Duration);
        Assert.False(editor.CanUndo);
        Assert.Equal(revision, editor.Revision);

        // 아무 명령도 안 지난 묶음·같은 값만 넣은 묶음은 기록이 없다.
        editor.Batch(MotionChangeKind.Keys, () => { });
        editor.Batch(MotionChangeKind.Keys, () => editor.SetDuration("nod", 0.6f));
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void 문서_밖_조작은_문서_조작과_한_줄에서_실제_순서대로_되돌아간다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        var log = new List<string>();

        editor.SetDuration("nod", 2f);
        editor.RecordExternal(() => log.Add("u1"), () => log.Add("r1"));
        long revision = editor.Revision;
        Assert.True(editor.CanUndo);
        editor.SetDuration("nod", 3f);

        editor.Undo();
        Assert.Equal(2f, editor.Document.Find("nod")!.Duration);
        Assert.Empty(log);
        editor.Undo();
        Assert.Equal(new[] { "u1" }, log);
        Assert.Equal(revision + 1, editor.Revision); // 손만 있는 칸은 Revision을 안 올린다(문서 복원 한 번만)
        editor.Undo();
        Assert.Equal(0.6f, editor.Document.Find("nod")!.Duration);

        editor.Redo();
        editor.Redo();
        Assert.Equal(new[] { "u1", "r1" }, log);
        editor.Redo();
        Assert.Equal(3f, editor.Document.Find("nod")!.Duration);
        Assert.False(editor.CanRedo);

        // 손만 올려도 새 조작이라 다시하기는 사라진다.
        editor.Undo();
        Assert.True(editor.CanRedo);
        editor.RecordExternal(() => log.Add("u2"), () => log.Add("r2"));
        Assert.False(editor.CanRedo);
    }

    [Fact]
    public void 묶음_안의_문서_밖_조작은_같은_칸에_붙어_함께_되돌아간다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        var log = new List<string>();

        editor.Batch(MotionChangeKind.Keys, () =>
        {
            editor.RecordExternal(() => log.Add("u-a"), () => log.Add("r-a"));
            editor.SetDuration("nod", 5f);
            editor.RecordExternal(() => log.Add("u-b"), () => log.Add("r-b"));
        });

        editor.Undo();
        Assert.Equal(0.6f, editor.Document.Find("nod")!.Duration);
        Assert.Equal(new[] { "u-b", "u-a" }, log); // 문서 복원 뒤 손은 거꾸로
        Assert.False(editor.CanUndo);

        editor.Redo();
        Assert.Equal(5f, editor.Document.Find("nod")!.Duration);
        Assert.Equal(new[] { "u-b", "u-a", "r-a", "r-b" }, log);

        // 손만 있는 묶음도 칸 하나다.
        long revision = editor.Revision;
        editor.Batch(MotionChangeKind.Keys, () => editor.RecordExternal(() => log.Add("u-c"), () => log.Add("r-c")));
        Assert.Equal(revision, editor.Revision);
        editor.Undo();
        Assert.Equal("u-c", log[^1]);
    }
}
