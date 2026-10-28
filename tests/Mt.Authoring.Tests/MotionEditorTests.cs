using Ked.Presentation.Core;
using Mt.Authoring.Editing;
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
}
