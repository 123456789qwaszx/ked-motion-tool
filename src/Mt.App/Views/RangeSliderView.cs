using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Mt.Authoring.Graph;
using Mt.Authoring.Preview;

namespace Mt.App.Views;

/// <summary>
/// 마야 레인지 슬라이더 — 바깥은 모션 전체(0~길이), 안쪽 바는 재생 범위.
/// 바를 끌면 범위가 통째로 옮겨지고, 양끝을 끌면 크기가 바뀌고, 더블클릭은 전체 ↔ 직전 범위다.
/// </summary>
public sealed class RangeSliderView : Control
{
    public const double SliderHeight = 16;
    private const double HandleWidth = 7;

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E));
    private static readonly IBrush GrooveBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C));
    private static readonly IBrush BarBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A));
    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x8E));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));

    private ViewportView? _viewport;
    private (double Start, double End)? _remembered;

    private enum Drag { None, Bar, Start, End }

    private Drag _drag = Drag.None;
    private double _grabOffset;

    public RangeSliderView()
    {
        Height = SliderHeight;
        MinWidth = 80;
        ClipToBounds = true;
    }

    public TimeDisplay TimeDisplay { get; set; } = TimeDisplay.Frames;

    public ViewportView? Viewport
    {
        get => _viewport;
        set
        {
            if (ReferenceEquals(_viewport, value))
            {
                return;
            }

            if (_viewport is not null)
            {
                _viewport.RangeChanged -= OnViewportChanged;
            }

            _viewport = value;

            if (_viewport is not null)
            {
                _viewport.RangeChanged += OnViewportChanged;
            }

            InvalidateVisual();
        }
    }

    private void OnViewportChanged(object? sender, EventArgs e) => InvalidateVisual();

    private PlaybackClock? Clock => _viewport?.Clock;

    private double Duration => Math.Max(1e-3, Clock?.Duration ?? 1);

    public Rect Track => new(HandleWidth, 2, Math.Max(1, Bounds.Width - HandleWidth * 2), Math.Max(1, Bounds.Height - 4));

    public double XAtTime(double seconds) => Track.X + Math.Clamp(seconds / Duration, 0, 1) * Track.Width;

    public double TimeAtX(double x) => Math.Clamp((x - Track.X) / Math.Max(1, Track.Width), 0, 1) * Duration;

    private static double Frame => 1.0 / TimeSnap.FramesPerSecond;

    private static double ToFrameTime(double seconds) => Math.Round(seconds * TimeSnap.FramesPerSecond) * Frame;

    private Rect BarRect
    {
        get
        {
            double x0 = XAtTime(Clock?.RangeStart ?? 0);
            double x1 = XAtTime(Clock?.RangeEnd ?? Duration);
            return new Rect(x0, Track.Y, Math.Max(2, x1 - x0), Track.Height);
        }
    }

    private Rect StartHandle => new(BarRect.X - HandleWidth, Track.Y, HandleWidth, Track.Height);

    private Rect EndHandle => new(BarRect.Right, Track.Y, HandleWidth, Track.Height);

    /// <summary>마야 더블클릭: 범위가 전체면 직전 범위로, 아니면 전체로.</summary>
    public void ToggleFullRange()
    {
        if (_viewport is null || Clock is not { } clock)
        {
            return;
        }

        bool full = clock.RangeStart <= 1e-9 && Math.Abs(clock.RangeEnd - clock.Duration) < 1e-9;

        if (full)
        {
            if (_remembered is { } r && r.End > r.Start && r.End <= clock.Duration + 1e-9)
            {
                _viewport.SetRange(r.Start, r.End);
            }

            return;
        }

        _remembered = (clock.RangeStart, clock.RangeEnd);
        _viewport.ResetRange();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));
        context.FillRectangle(GrooveBrush, Track);
        Rect bar = BarRect;
        context.FillRectangle(BarBrush, bar);
        context.FillRectangle(HandleBrush, StartHandle);
        context.FillRectangle(HandleBrush, EndHandle);

        if (Clock is { } clock)
        {
            string start = TimeText.Format(clock.RangeStart, TimeDisplay);
            string end = TimeText.Format(clock.RangeEnd, TimeDisplay);
            DrawLabel(context, start, new Point(bar.X + 4, bar.Y));
            var endText = new FormattedText(end, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 9, LabelBrush);
            context.DrawText(endText, new Point(bar.Right - endText.Width - 4, bar.Y));
        }
    }

    private static void DrawLabel(DrawingContext context, string text, Point at)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 9, LabelBrush);
        context.DrawText(formatted, at);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_viewport is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleFullRange();
            e.Handled = true;
            return;
        }

        Point position = e.GetPosition(this);

        if (EndHandle.Inflate(2).Contains(position))
        {
            _drag = Drag.End;
        }
        else if (StartHandle.Inflate(2).Contains(position))
        {
            _drag = Drag.Start;
        }
        else if (BarRect.Contains(position))
        {
            _drag = Drag.Bar;
            _grabOffset = TimeAtX(position.X) - (Clock?.RangeStart ?? 0);
        }
        else
        {
            return;
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_drag == Drag.None || _viewport is null || Clock is not { } clock)
        {
            return;
        }

        double t = ToFrameTime(TimeAtX(e.GetPosition(this).X));

        try
        {
            switch (_drag)
            {
                case Drag.End:
                    _viewport.SetRange(clock.RangeStart, Math.Max(t, clock.RangeStart + Frame));
                    break;
                case Drag.Start:
                    _viewport.SetRange(Math.Min(t, clock.RangeEnd - Frame), clock.RangeEnd);
                    break;
                case Drag.Bar:
                {
                    double span = clock.RangeEnd - clock.RangeStart;
                    double start = Math.Clamp(ToFrameTime(t - _grabOffset), 0, Math.Max(0, clock.Duration - span));
                    _viewport.SetRange(start, Math.Min(clock.Duration, start + span));
                    break;
                }
            }
        }
        catch (ArgumentException)
        {
            // 범위가 너무 좁다 — 그 프레임은 건너뛴다.
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_drag != Drag.None)
        {
            _drag = Drag.None;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
}
