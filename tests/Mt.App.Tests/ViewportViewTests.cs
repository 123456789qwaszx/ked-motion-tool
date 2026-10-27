using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Ked.Presentation.Core;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;

namespace Mt.App.Tests;

public class ViewportViewTests
{
    private static (Window Window, ViewportView View, MotionEditor Editor) Show()
    {
        MotionEditor editor = TestDocuments.Nod();
        var view = new ViewportView();
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.SetRig(RigSchemaFile.Load(TestDocuments.FixturePath("rig-schemas.json")));
        view.Bind(editor, "nod");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, view, editor);
    }

    [Fact]
    public void 리그를_실으면_초상이_서고_포즈가_시각을_따른다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            Assert.True(view.HasRig);
            Quad rest = view.CurrentPortraitQuad() ?? throw new InvalidOperationException("초상이 없다");
            Assert.Equal(0f, rest.Center.Y, 2);

            view.ScrubTo(0.3);
            Quad dipped = view.CurrentPortraitQuad()!.Value;
            Assert.Equal(-18f, dipped.Center.Y, 2);

            // 길이에서는 restore라 시작 상태.
            view.ScrubTo(0.6);
            Assert.Equal(0f, view.CurrentPortraitQuad()!.Value.Center.Y, 2);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 재생은_시계를_밀고_루프를_끄면_끝에서_멈춘다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            int changes = 0;
            view.TimeChanged += (_, _) => changes++;

            view.Play();
            Assert.True(view.IsPlaying);
            view.Advance(0.3);
            Assert.Equal(0.3, view.Time, 9);
            Assert.Equal(1, changes);

            view.Loop = false;
            view.Advance(1);
            Assert.Equal(0.6f, view.Time, 6); // 길이는 float(0.6f)다
            Assert.False(view.IsPlaying);

            view.Stop();
            Assert.Equal(0, view.Time);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 타임라인_띠를_누르면_스크럽이다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            Rect timeline = view.TimelineRect;
            var local = new Point(timeline.X + timeline.Width * 0.5, timeline.Y + timeline.Height / 2);
            Point at = view.TranslatePoint(local, window) ?? throw new InvalidOperationException("뷰가 창에 없다");

            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);

            Assert.Equal(0.3, view.Time, 2);
            Assert.False(view.IsPlaying);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 키_틱은_키_시각에_있고_점은_키로_점프한다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            Rect timeline = view.TimelineRect;
            Assert.Equal(timeline.X, view.XAtTime(0), 3);
            Assert.Equal(timeline.Right, view.XAtTime(view.Clock.Duration), 3);
            Assert.Equal(timeline.X + timeline.Width / 2, view.XAtTime(0.3), 3);

            Assert.True(view.JumpToNextKey());
            Assert.Equal(0.3, view.Time, 6);
            Assert.True(view.JumpToNextKey());
            Assert.Equal(0.6f, view.Time, 6);
            Assert.False(view.JumpToNextKey());
            Assert.True(view.JumpToPreviousKey());
            Assert.Equal(0.3, view.Time, 6);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 범위_핸들을_끌면_재생_범위가_바뀌고_왕복은_그_안에서_돈다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            int changes = 0;
            view.RangeChanged += (_, _) => changes++;
            Rect timeline = view.TimelineRect;
            Point endHandle = view.TranslatePoint(new Point(view.XAtTime(view.Clock.Duration) - 2, timeline.Center.Y), window)!.Value;
            Point target = view.TranslatePoint(new Point(view.XAtTime(0.3), timeline.Center.Y), window)!.Value;

            window.MouseDown(endHandle, MouseButton.Left);
            window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            window.MouseUp(target, MouseButton.Left);

            Assert.Equal(0.3, view.Clock.RangeEnd, 2);
            Assert.True(changes > 0);

            view.Mode = PlaybackMode.Oscillate;
            view.Speed = 1;
            view.Play();
            view.Advance(0.25);
            Assert.Equal(0.25, view.Time, 6);
            view.Advance(0.1);
            Assert.Equal(0.25, view.Time, 6); // 0.35 → 되돌아 0.25
            Assert.True(view.IsPlaying);

            view.ResetRange();
            Assert.Equal(0.6f, view.Clock.RangeEnd, 6);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 이동_매니퓰레이터는_AutoKey면_놓을_때_바뀐_성분만_키로_놓는다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, MotionEditor editor) = Show();

        try
        {
            const string node = "CharacterPortrait_Track_Move_Y";
            view.SelectedNode = node;
            view.Manipulator = ManipulatorMode.Move;
            view.AutoKey = true;
            view.ScrubTo(0.3);
            long before = editor.Revision;

            Point centerLocal = view.GizmoCenter() ?? throw new InvalidOperationException("기즈모가 없다");
            Point center = view.TranslatePoint(centerLocal, window)!.Value;
            double scale = view.StageRect.Width / 1920.0;
            var target = new Point(center.X, center.Y + 20);

            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            Assert.Equal(before, editor.Revision); // 드래그 중간값은 렌더만
            window.MouseUp(target, MouseButton.Left);

            Assert.Equal(before + 1, editor.Revision);
            Assert.False(view.HasPending);
            ChannelAsset channel = editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Position)!;
            Assert.Null(channel.X); // 세로로만 끌었으니 x는 건드리지 않는다
            KeyAsset key = channel.Y!.Keys.Single(k => Math.Abs(k.T - 0.3f) < 1e-4);
            Assert.Equal(-18.0 - 20.0 / scale, key.V, 1);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void AutoKey가_꺼져_있으면_키_없는_포즈로_남고_S가_키를_놓고_시각을_옮기면_버린다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, MotionEditor editor) = Show();

        try
        {
            const string node = "CharacterPortrait_Track_Move_Y";
            view.SelectedNode = node;
            view.Manipulator = ManipulatorMode.Move;
            view.AutoKey = false;
            view.ScrubTo(0.15); // 키가 없는 시각
            long before = editor.Revision;
            int pendingChanges = 0;
            view.PendingChanged += (_, _) => pendingChanges++;

            Point center = view.TranslatePoint(view.GizmoCenter()!.Value, window)!.Value;
            var target = new Point(center.X, center.Y - 30);
            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            window.MouseUp(target, MouseButton.Left);

            Assert.Equal(before, editor.Revision);
            Assert.True(view.HasPending);
            Assert.True(pendingChanges > 0);
            Assert.Equal(3, editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Position)!.Y!.Keys.Count);

            view.SetKey();
            Assert.False(view.HasPending);
            Assert.Equal(before + 1, editor.Revision);
            List<KeyAsset> keys = editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Position)!.Y!.Keys;
            Assert.Equal(4, keys.Count);
            Assert.Contains(keys, k => Math.Abs(k.T - 0.15f) < 1e-4 && k.V > -9f);

            // 다시 끌고 시각을 옮기면 버린다.
            center = view.TranslatePoint(view.GizmoCenter()!.Value, window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(new Point(center.X, center.Y - 30), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(center.X, center.Y - 30), MouseButton.Left);
            Assert.False(view.HasPending); // 0.15에 이미 키가 있으니 바로 키가 된다(마야: 키 있는 시각은 덮어쓴다)
            Assert.Equal(before + 2, editor.Revision);

            view.ScrubTo(0.5);
            view.ScrubTo(0.2);
            center = view.TranslatePoint(view.GizmoCenter()!.Value, window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(new Point(center.X, center.Y - 30), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(center.X, center.Y - 30), MouseButton.Left);
            Assert.True(view.HasPending);
            view.ScrubTo(0.4);
            Assert.False(view.HasPending);
            Assert.Equal(before + 2, editor.Revision);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 회전_매니퓰레이터는_고리를_끌어_z_회전_키를_놓는다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, MotionEditor editor) = Show();

        try
        {
            const string node = "CharacterPortrait_SwayPivot";
            view.SelectedNode = node;
            view.Manipulator = ManipulatorMode.Rotate;
            view.AutoKey = true;
            view.ScrubTo(0);
            long before = editor.Revision;

            Point centerLocal = view.GizmoCenter()!.Value;
            Point onRing = view.TranslatePoint(new Point(centerLocal.X + 60, centerLocal.Y), window)!.Value;
            Point rotated = view.TranslatePoint(new Point(centerLocal.X, centerLocal.Y - 60), window)!.Value; // 화면 위 = 루트 y 위 = 반시계 90도

            window.MouseDown(onRing, MouseButton.Left);
            window.MouseMove(rotated, RawInputModifiers.LeftMouseButton);
            window.MouseUp(rotated, MouseButton.Left);

            Assert.Equal(before + 1, editor.Revision);
            ChannelAsset channel = editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Euler)!;
            Assert.Null(channel.X);
            Assert.Null(channel.Y);
            Assert.Equal(90f, channel.Z!.Keys[0].V, 1);
            Assert.Equal(MotionChannelMode.Relative, channel.Mode);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 초상_비트맵이_종횡비를_정하고_없으면_자리표시다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            Assert.Equal(PortraitLayout.PlaceholderAspect, view.Aspect);

            using var bitmap = new WriteableBitmap(new PixelSize(80, 100), new Vector(96, 96));
            view.SetPortrait(bitmap);

            Assert.Equal(0.8f, view.Aspect, 4);
            Quad quad = view.CurrentPortraitQuad()!.Value;
            Assert.Equal(864f, (quad.BottomRight - quad.BottomLeft).X, 1);
            Assert.Equal(1080f, (quad.TopLeft - quad.BottomLeft).Y, 1);

            view.SetPortrait(null);
            Assert.Equal(PortraitLayout.PlaceholderAspect, view.Aspect);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 리그가_없거나_rigKind가_다르면_초상이_없다_그래도_그려진다() => HeadlessUi.Run(() =>
    {
        MotionEditor editor = TestDocuments.Nod();
        editor.AddMotion("robot_wave", "robot", 1f);
        var view = new ViewportView();
        var window = new Window { Width = 600, Height = 400, Content = view };
        window.Show();

        try
        {
            view.Bind(editor, "nod");
            Assert.False(view.HasRig);
            Assert.Null(view.CurrentPortraitQuad());

            view.SetRig(RigSchemaFile.Load(TestDocuments.FixturePath("rig-schemas.json")));
            Assert.True(view.HasRig);

            view.Bind(editor, "robot_wave");
            Assert.False(view.HasRig);
            view.InvalidateVisual();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            window.Close();
        }
    });
}
