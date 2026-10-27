using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Preview;

namespace Mt.App.Tests;

public class RangeSliderViewTests
{
    private const double Frame = 1.0 / 24.0;

    private static (Window Window, RangeSliderView Slider, ViewportView Viewport) Show()
    {
        MotionEditor editor = TestDocuments.Nod();
        var viewport = new ViewportView { Height = 200 };
        var slider = new RangeSliderView();
        var window = new Window { Width = 900, Height = 400, Content = new StackPanel { Children = { viewport, slider } } };
        window.Show();
        viewport.Bind(editor, "nod");
        slider.Viewport = viewport;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, slider, viewport);
    }

    private static Point At(Window window, RangeSliderView slider, double x)
        => slider.TranslatePoint(new Point(x, slider.Track.Center.Y), window) ?? throw new InvalidOperationException("슬라이더가 창에 없다");

    [Fact]
    public void 끝_손잡이를_끌면_범위_끝이_프레임으로_바뀌고_왕복은_그_안에서_돈다() => HeadlessUi.Run(() =>
    {
        (Window window, RangeSliderView slider, ViewportView viewport) = Show();

        try
        {
            int changes = 0;
            viewport.RangeChanged += (_, _) => changes++;
            Point handle = At(window, slider, slider.XAtTime(viewport.Clock.Duration) + 3);
            Point target = At(window, slider, slider.XAtTime(0.3));
            window.MouseDown(handle, MouseButton.Left);
            window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            window.MouseUp(target, MouseButton.Left);

            Assert.Equal(7 * Frame, viewport.Clock.RangeEnd, 3);
            Assert.True(changes > 0);

            viewport.Mode = PlaybackMode.Oscillate;
            viewport.Play();
            viewport.Advance(0.25);
            viewport.Advance(0.1);
            Assert.True(viewport.IsPlaying);
            Assert.InRange(viewport.Time, 0, viewport.Clock.RangeEnd);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 바를_끌면_범위가_통째로_옮겨지고_더블클릭은_전체와_직전_범위를_오간다() => HeadlessUi.Run(() =>
    {
        (Window window, RangeSliderView slider, ViewportView viewport) = Show();

        try
        {
            viewport.SetRange(0, 0.2);
            Point grab = At(window, slider, slider.XAtTime(0.1));
            Point drop = At(window, slider, slider.XAtTime(0.3));
            window.MouseDown(grab, MouseButton.Left);
            window.MouseMove(drop, RawInputModifiers.LeftMouseButton);
            window.MouseUp(drop, MouseButton.Left);

            Assert.Equal(5 * Frame, viewport.Clock.RangeStart, 3);
            Assert.Equal(5 * Frame + 0.2, viewport.Clock.RangeEnd, 3);

            slider.ToggleFullRange();
            Assert.Equal(0, viewport.Clock.RangeStart, 6);
            Assert.Equal(viewport.Clock.Duration, viewport.Clock.RangeEnd, 6);

            slider.ToggleFullRange();
            Assert.Equal(5 * Frame, viewport.Clock.RangeStart, 3);
        }
        finally
        {
            window.Close();
        }
    });
}
