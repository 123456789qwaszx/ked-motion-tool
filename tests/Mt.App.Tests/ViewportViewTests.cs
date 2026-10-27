using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Ked.Presentation.Core;
using Mt.App.Views;
using Mt.Authoring.Editing;
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
            Assert.Equal(0.6, view.Time, 9);
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
