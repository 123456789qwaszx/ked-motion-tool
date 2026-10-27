using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;

namespace Mt.App.Views;

/// <summary>타임 슬라이더의 키 틱 표시(마야 Display Key Ticks).</summary>
public enum KeyTickMode
{
    All,
    Selected,
    None,
}

/// <summary>
/// 마야 타임 슬라이더 — 재생 범위를 프레임 눈금으로 펼치고, 키는 빨간 틱, 현재 프레임은 검은 상자다.
///
/// - LMB 클릭·드래그 = 스크럽(프레임 스냅). 빨간 범위 선택은 지운다.
/// - Shift+LMB 드래그 = 범위 선택(빨강). 한 프레임이면 그 프레임의 키가 선택된다.
/// - 범위 안 MMB 드래그 = 범위의 키 이동 · 범위 양끝 LMB 드래그 = 반대쪽 끝 기준 시간 스케일 (놓을 때 편집 하나)
/// - 범위 밖 MMB 드래그 = <b>포즈 들고 스크럽</b>(뷰포트가 포즈를 든 채 시각만 간다 — S로 그 포즈를 키)
/// - RMB = 메뉴(잘라내기·복사·붙여넣기·삭제·프레임에 스냅·범위를 선택으로·재생 모드/속도·키 틱)
///
/// 시계는 뷰포트의 것이다. 편집은 <see cref="MotionEditor"/> 명령뿐이다.
/// </summary>
public sealed class TimeSliderView : Control
{
    public const double SliderHeight = 34;
    private const double HandleWidth = 5;

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x3B, 0x3B));
    private static readonly Pen MinorTickPen = new(new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A)), 1);
    private static readonly Pen MajorTickPen = new(new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x6E)), 1);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0xB4, 0xB4));
    private static readonly IBrush KeyTickBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x3C, 0x3C));
    private static readonly IBrush SelectedKeyTickBrush = new SolidColorBrush(Color.FromRgb(0xF9, 0xD7, 0x1C));
    private static readonly IBrush GhostTickBrush = new SolidColorBrush(Color.FromArgb(140, 0xF9, 0xD7, 0x1C));
    private static readonly IBrush RangeFill = new SolidColorBrush(Color.FromArgb(96, 0xFF, 0x30, 0x30));
    private static readonly Pen RangePen = new(new SolidColorBrush(Color.FromArgb(220, 0xE0, 0x40, 0x40)), 1);
    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.FromArgb(230, 0xF0, 0x60, 0x60));
    private static readonly IBrush CurrentFrameBrush = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
    private static readonly IBrush CurrentFrameLabel = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
    private static readonly Pen BottomLinePen = new(new SolidColorBrush(Color.FromRgb(0x4C, 0x7F, 0xC0)), 2);
    private static readonly Pen HoldPen = new(new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x50)), 1, DashStyle.Dash);

    private ViewportView? _viewport;
    private MotionEditor? _editor;
    private string? _motionName;
    private KeySelection _selection = new();

    private enum Drag { None, Scrub, SelectRange, HoldScrub, MoveRange, ScaleStart, ScaleEnd }

    private Drag _drag = Drag.None;
    private double _anchorTime;
    private double _previewDelta;
    private double _previewFactor = 1;
    private double _previewPivot;
    private ContextMenu? _menu;

    public TimeSliderView()
    {
        Height = SliderHeight;
        MinWidth = 120;
        ClipToBounds = true;
        Focusable = true;
        _selection.Changed += OnSelectionChanged;
    }

    /// <summary>범위 선택(초, 양끝 프레임 포함). 없으면 null.</summary>
    public (double Start, double End)? SelectedRange { get; set; }

    public KeyClipboardSlot Clipboard { get; set; } = new();

    public bool SnapEnabled { get; set; } = true;

    public double SnapStep { get; set; } = TimeSnap.DefaultStep;

    public TimeDisplay TimeDisplay { get; set; } = TimeDisplay.Frames;

    public KeyTickMode KeyTicks { get; set; } = KeyTickMode.All;

    /// <summary>시계·포즈의 주인. 시각이 바뀌면 다시 그린다.</summary>
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
                _viewport.TimeChanged -= OnViewportChanged;
                _viewport.RangeChanged -= OnViewportChanged;
                _viewport.PlayStateChanged -= OnViewportChanged;
                _viewport.PendingChanged -= OnViewportChanged;
            }

            _viewport = value;

            if (_viewport is not null)
            {
                _viewport.TimeChanged += OnViewportChanged;
                _viewport.RangeChanged += OnViewportChanged;
                _viewport.PlayStateChanged += OnViewportChanged;
                _viewport.PendingChanged += OnViewportChanged;
            }

            InvalidateVisual();
        }
    }

    public KeySelection Selection
    {
        get => _selection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_selection, value))
            {
                return;
            }

            _selection.Changed -= OnSelectionChanged;
            _selection = value;
            _selection.Changed += OnSelectionChanged;
            InvalidateVisual();
        }
    }

    public MotionAsset? Motion => _editor is not null && _motionName is not null ? _editor.Document.Find(_motionName) : null;

    public void Bind(MotionEditor? editor, string? motionName)
    {
        if (_editor is not null)
        {
            _editor.Changed -= OnEditorChanged;
        }

        _editor = editor;
        _motionName = motionName;

        if (_editor is not null)
        {
            _editor.Changed += OnEditorChanged;
        }

        SelectedRange = null;
        InvalidateVisual();
    }

    private void OnEditorChanged(object? sender, MotionChangedEventArgs e) => InvalidateVisual();

    private void OnViewportChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnSelectionChanged(object? sender, EventArgs e) => InvalidateVisual();

    // ── 좌표 ──────────────────────────────────────────────────────────

    private PlaybackClock? Clock => _viewport?.Clock;

    private double SpanStart => Clock?.RangeStart ?? 0;

    private double SpanEnd => Math.Max(SpanStart + 1e-3, Clock?.RangeEnd ?? 1);

    public static double Frame => 1.0 / TimeSnap.FramesPerSecond;

    /// <summary>눈금이 펼쳐지는 영역 — 재생 범위가 꽉 찬다.</summary>
    public Rect Track => new(2, 0, Math.Max(1, Bounds.Width - 4), Bounds.Height);

    public double XAtTime(double seconds)
    {
        Rect track = Track;
        return track.X + (seconds - SpanStart) / (SpanEnd - SpanStart) * track.Width;
    }

    public double TimeAtX(double x)
    {
        Rect track = Track;
        double u = Math.Clamp((x - track.X) / Math.Max(1, track.Width), 0, 1);
        return SpanStart + u * (SpanEnd - SpanStart);
    }

    private double SnapTime(double seconds) => SnapEnabled ? TimeSnap.Snap(seconds, SnapStep) : seconds;

    /// <summary>프레임 눈금으로 — 범위 선택은 언제나 프레임 단위다(마야).</summary>
    private static double ToFrameTime(double seconds) => Math.Round(seconds * TimeSnap.FramesPerSecond) * Frame;

    private Rect StartHandleRect => SelectedRange is { } r ? new Rect(XAtTime(r.Start) - HandleWidth, 0, HandleWidth * 2, Bounds.Height) : default;

    private Rect EndHandleRect => SelectedRange is { } r ? new Rect(XAtTime(r.End + Frame) - HandleWidth, 0, HandleWidth * 2, Bounds.Height) : default;

    // ── 범위 명령 (RMB 메뉴 · 테스트) ────────────────────────────────

    /// <summary>범위의 키, 범위가 없으면 현재 프레임의 키.</summary>
    public List<KeyRef> KeysInScope()
    {
        if (Motion is not { } motion)
        {
            return new List<KeyRef>();
        }

        return SelectedRange is { } r
            ? TimeSliderOps.KeysBetween(motion, r.Start, r.End)
            : TimeSliderOps.KeysAt(motion, Clock?.Time ?? 0);
    }

    public bool CopyScopeKeys()
    {
        if (Motion is not { } motion || KeyClipboard.Copy(motion, KeysInScope()) is not { } clipboard || clipboard.Count == 0)
        {
            return false;
        }

        Clipboard.Current = clipboard;
        return true;
    }

    public bool CutScopeKeys()
    {
        if (!CopyScopeKeys() || _editor is null || _motionName is null)
        {
            return false;
        }

        _editor.RemoveKeys(_motionName, KeysInScope());
        return true;
    }

    public bool DeleteScopeKeys()
    {
        List<KeyRef> keys = KeysInScope();

        if (keys.Count == 0 || _editor is null || _motionName is null)
        {
            return false;
        }

        _editor.RemoveKeys(_motionName, keys);
        return true;
    }

    public bool PasteAtCurrentTime()
    {
        if (_editor is null || _motionName is null || Clipboard.Current is not { } clipboard || clipboard.Count == 0)
        {
            return false;
        }

        float at = (float)SnapTime(Clock?.Time ?? 0);
        List<KeyRef> pasted = _editor.PasteKeys(_motionName, clipboard, at, SnapEnabled ? t => (float)TimeSnap.Snap(t, SnapStep) : null);
        _selection.Set(pasted);
        return true;
    }

    /// <summary>마야 Snap — 범위(또는 현재 프레임)의 키 시각을 프레임에 맞춘다.</summary>
    public bool SnapScopeKeysToFrames()
    {
        if (_editor is null || _motionName is null || Motion is not { } motion)
        {
            return false;
        }

        List<KeyRef> keys = KeysInScope();
        var offGrid = keys.Where(k => k.Resolve(motion) is { } a && Math.Abs(a.T - ToFrameTime(a.T)) > MotionRules.Tolerance).ToList();

        if (offGrid.Count == 0)
        {
            return false;
        }

        _editor.SetKeysMany(_motionName, KeyOps.Move(motion, offGrid, 0f, t => (float)ToFrameTime(t)));
        return true;
    }

    /// <summary>마야 "Set Range to › Selection" — 재생 범위를 빨간 선택으로.</summary>
    public bool SetPlaybackRangeToSelection()
    {
        if (_viewport is null || SelectedRange is not { } r)
        {
            return false;
        }

        _viewport.SetRange(r.Start, Math.Max(r.End, r.Start + Frame));
        return true;
    }

    // ── 그리기 ────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));
        Rect track = Track;
        double pixelsPerFrame = track.Width / Math.Max(1e-6, (SpanEnd - SpanStart) * TimeSnap.FramesPerSecond);

        // 눈금: 프레임마다 짧은 선, 라벨 단계마다 긴 선과 숫자.
        if (TimeDisplay == TimeDisplay.Frames)
        {
            int step = TimeText.FrameTickStep(pixelsPerFrame, 26);
            long first = (long)Math.Floor(SpanStart * TimeSnap.FramesPerSecond);
            long last = (long)Math.Ceiling(SpanEnd * TimeSnap.FramesPerSecond);

            for (long f = first; f <= last; f++)
            {
                double x = XAtTime(f * Frame);

                if (x < track.X - 0.5 || x > track.Right + 0.5)
                {
                    continue;
                }

                bool major = f % step == 0;

                if (major)
                {
                    context.DrawLine(MajorTickPen, new Point(x, 12), new Point(x, track.Height - 3));
                    DrawLabel(context, f.ToString(CultureInfo.InvariantCulture), new Point(x + 2, 0), LabelBrush, 9);
                }
                else if (pixelsPerFrame >= 3)
                {
                    context.DrawLine(MinorTickPen, new Point(x, track.Height - 10), new Point(x, track.Height - 3));
                }
            }
        }
        else
        {
            double tStep = TimeSnap.TickStep(track.Width / (SpanEnd - SpanStart), 40);
            double tStart = Math.Floor(SpanStart / tStep) * tStep;

            for (double t = tStart; t <= SpanEnd + 1e-9; t += tStep)
            {
                double x = XAtTime(t);

                if (x < track.X - 0.5 || x > track.Right + 0.5)
                {
                    continue;
                }

                context.DrawLine(MajorTickPen, new Point(x, 12), new Point(x, track.Height - 3));
                DrawLabel(context, TimeText.Format(t, TimeDisplay.Seconds), new Point(x + 2, 0), LabelBrush, 9);
            }
        }

        // 범위 선택(빨강).
        if (SelectedRange is { } range)
        {
            (double rs, double re) = PreviewRange(range);
            double x0 = XAtTime(rs);
            double x1 = XAtTime(re + Frame);
            var rect = new Rect(Math.Min(x0, x1), 1, Math.Max(1, Math.Abs(x1 - x0)), track.Height - 4);
            context.FillRectangle(RangeFill, rect);
            context.DrawRectangle(RangePen, rect);
            context.FillRectangle(HandleBrush, new Rect(rect.X - 2, 1, 4, rect.Height));
            context.FillRectangle(HandleBrush, new Rect(rect.Right - 2, 1, 4, rect.Height));
        }

        // 키 틱.
        if (Motion is { } motion && KeyTicks != KeyTickMode.None)
        {
            List<float> selected = KeyTimes.OfSelection(motion, _selection);
            List<float> times = KeyTicks == KeyTickMode.Selected ? selected : KeyTimes.All(motion);
            bool previewing = _drag is Drag.MoveRange or Drag.ScaleStart or Drag.ScaleEnd;

            foreach (float t in times)
            {
                bool isSelected = selected.Any(st => Math.Abs(st - t) <= MotionRules.Tolerance);
                bool inRange = SelectedRange is { } r && t >= r.Start - MotionRules.Tolerance && t <= r.End + MotionRules.Tolerance;
                double tx = XAtTime(t);

                if (previewing && inRange)
                {
                    double gx = XAtTime(PreviewTime(t));
                    context.FillRectangle(GhostTickBrush, new Rect(gx - 1, 2, 2, track.Height - 6));
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb(70, 0xD3, 0x3C, 0x3C)), new Rect(tx - 1, 2, 2, track.Height - 6));
                    continue;
                }

                context.FillRectangle(isSelected ? SelectedKeyTickBrush : KeyTickBrush, new Rect(tx - 1, 2, 2, track.Height - 6));
            }
        }

        // 현재 프레임: 검은 상자 + 숫자(마야).
        if (Clock is { } clock)
        {
            double x = XAtTime(clock.Time);
            double width = Math.Max(16, pixelsPerFrame);
            var box = new Rect(x, 1, width, track.Height - 4);
            context.FillRectangle(CurrentFrameBrush, box);

            if (_viewport?.IsHoldingPose == true)
            {
                context.DrawRectangle(HoldPen, box);
            }

            DrawLabel(context, TimeText.Format(clock.Time, TimeDisplay), new Point(x + 3, 2), CurrentFrameLabel, 10);
        }

        context.DrawLine(BottomLinePen, new Point(track.X, track.Bottom - 1), new Point(track.Right, track.Bottom - 1));
    }

    private (double Start, double End) PreviewRange((double Start, double End) range)
        => _drag switch
        {
            Drag.MoveRange => (range.Start + _previewDelta, range.End + _previewDelta),
            Drag.ScaleStart or Drag.ScaleEnd => (Math.Min(PreviewTime(range.Start), PreviewTime(range.End)), Math.Max(PreviewTime(range.Start), PreviewTime(range.End))),
            _ => range,
        };

    private double PreviewTime(double t) => _drag switch
    {
        Drag.MoveRange => Math.Max(0, t + _previewDelta),
        Drag.ScaleStart or Drag.ScaleEnd => Math.Max(0, _previewPivot + (t - _previewPivot) * _previewFactor),
        _ => t,
    };

    private static void DrawLabel(DrawingContext context, string text, Point at, IBrush brush, double size)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
        context.DrawText(formatted, at);
    }

    // ── 입력 ──────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        PointerPoint point = e.GetCurrentPoint(this);
        Point position = point.Position;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (point.Properties.IsRightButtonPressed)
        {
            ShowMenu();
            e.Handled = true;
            return;
        }

        if (_viewport is null)
        {
            return;
        }

        double t = TimeAtX(position.X);

        if (point.Properties.IsMiddleButtonPressed)
        {
            if (SelectedRange is { } r && t >= r.Start - 1e-9 && t <= r.End + Frame + 1e-9)
            {
                _drag = Drag.MoveRange;
                _anchorTime = ToFrameTime(t);
                _previewDelta = 0;
            }
            else
            {
                _drag = Drag.HoldScrub;
                _viewport.HoldScrubTo(SnapTime(t));
            }

            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (SelectedRange is { } selected)
        {
            if (EndHandleRect.Contains(position))
            {
                _drag = Drag.ScaleEnd;
                _previewPivot = selected.Start;
                _previewFactor = 1;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (StartHandleRect.Contains(position))
            {
                _drag = Drag.ScaleStart;
                _previewPivot = selected.End + Frame;
                _previewFactor = 1;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        if (shift)
        {
            _drag = Drag.SelectRange;
            _anchorTime = ToFrameTime(t);
            SelectedRange = (_anchorTime, _anchorTime);
        }
        else
        {
            _drag = Drag.Scrub;
            SelectedRange = null;
            _viewport.Pause();
            _viewport.ScrubTo(SnapTime(t));
        }

        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_drag == Drag.None || _viewport is null)
        {
            return;
        }

        double t = TimeAtX(e.GetPosition(this).X);

        switch (_drag)
        {
            case Drag.Scrub:
                _viewport.ScrubTo(SnapTime(t));
                break;
            case Drag.HoldScrub:
                _viewport.HoldScrubTo(SnapTime(t));
                break;
            case Drag.SelectRange:
            {
                double frame = ToFrameTime(t);
                SelectedRange = (Math.Min(_anchorTime, frame), Math.Max(_anchorTime, frame));
                break;
            }

            case Drag.MoveRange:
                _previewDelta = ToFrameTime(t) - _anchorTime;
                break;
            case Drag.ScaleEnd:
            case Drag.ScaleStart:
                if (SelectedRange is { } r)
                {
                    double span = r.End + Frame - r.Start;
                    double edge = ToFrameTime(t);
                    double newSpan = _drag == Drag.ScaleEnd ? edge - r.Start : r.End + Frame - edge;
                    _previewFactor = Math.Max(Frame, newSpan) / Math.Max(Frame, span);
                }

                break;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_drag == Drag.None)
        {
            return;
        }

        Drag drag = _drag;
        _drag = Drag.None;
        e.Pointer.Capture(null);

        try
        {
            switch (drag)
            {
                case Drag.HoldScrub:
                    _viewport?.EndHoldScrub();
                    break;

                case Drag.SelectRange:
                    if (SelectedRange is { } picked && Motion is { } motion && Math.Abs(picked.End - picked.Start) < 1e-9)
                    {
                        _selection.Set(TimeSliderOps.KeysAt(motion, picked.Start));
                    }

                    break;

                case Drag.MoveRange:
                    if (SelectedRange is { } r && _editor is not null && _motionName is not null && Motion is { } m && Math.Abs(_previewDelta) > 1e-9)
                    {
                        List<KeyRef> keys = TimeSliderOps.KeysBetween(m, r.Start, r.End);
                        float delta = (float)_previewDelta;

                        if (keys.Count > 0)
                        {
                            _editor.MoveKeys(_motionName, keys, delta, SnapEnabled ? t => (float)TimeSnap.Snap(t, SnapStep) : null);
                        }

                        SelectedRange = (Math.Max(0, r.Start + delta), Math.Max(0, r.End + delta));
                    }

                    break;

                case Drag.ScaleStart:
                case Drag.ScaleEnd:
                    if (SelectedRange is { } sr && _editor is not null && _motionName is not null && Motion is { } sm && Math.Abs(_previewFactor - 1) > 1e-9)
                    {
                        List<KeyRef> keys = TimeSliderOps.KeysBetween(sm, sr.Start, sr.End);

                        if (keys.Count > 0)
                        {
                            _editor.ScaleKeys(_motionName, keys, (float)_previewPivot, (float)_previewFactor, SnapEnabled ? t => (float)TimeSnap.Snap(t, SnapStep) : null);
                        }

                        double a = PreviewTimeWith(sr.Start, _previewPivot, _previewFactor);
                        double b = PreviewTimeWith(sr.End, _previewPivot, _previewFactor);
                        SelectedRange = (ToFrameTime(Math.Min(a, b)), ToFrameTime(Math.Max(a, b)));
                    }

                    break;
            }
        }
        finally
        {
            _previewDelta = 0;
            _previewFactor = 1;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    private static double PreviewTimeWith(double t, double pivot, double factor) => Math.Max(0, pivot + (t - pivot) * factor);

    // ── 메뉴 ──────────────────────────────────────────────────────────

    private void ShowMenu()
    {
        _menu ??= BuildMenu();
        _menu.Open(this);
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header, FontSize = 11 };
            item.Click += (_, _) => action();
            return item;
        }

        menu.Items.Add(Item("키 잘라내기", () => CutScopeKeys()));
        menu.Items.Add(Item("키 복사", () => CopyScopeKeys()));
        menu.Items.Add(Item("키 붙여넣기 (현재 프레임)", () => PasteAtCurrentTime()));
        menu.Items.Add(Item("키 삭제", () => DeleteScopeKeys()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("키를 프레임에 스냅", () => SnapScopeKeysToFrames()));
        menu.Items.Add(Item("재생 범위를 선택으로", () => SetPlaybackRangeToSelection()));
        menu.Items.Add(Item("재생 범위 전체로", () => _viewport?.ResetRange()));
        menu.Items.Add(Item("범위 선택 지우기", () => { SelectedRange = null; InvalidateVisual(); }));
        menu.Items.Add(new Separator());

        var mode = new MenuItem { Header = "재생 모드", FontSize = 11 };
        mode.Items.Add(Item("한 번", () => SetMode(PlaybackMode.Once)));
        mode.Items.Add(Item("루프", () => SetMode(PlaybackMode.Loop)));
        mode.Items.Add(Item("왕복", () => SetMode(PlaybackMode.Oscillate)));
        menu.Items.Add(mode);

        var speed = new MenuItem { Header = "재생 속도", FontSize = 11 };

        foreach (double s in new[] { 0.25, 0.5, 1, 2 })
        {
            double captured = s;
            speed.Items.Add(Item($"×{captured:0.##}", () => { if (_viewport is not null) { _viewport.Speed = captured; } }));
        }

        menu.Items.Add(speed);

        var ticks = new MenuItem { Header = "키 틱 표시", FontSize = 11 };
        ticks.Items.Add(Item("전체", () => { KeyTicks = KeyTickMode.All; InvalidateVisual(); }));
        ticks.Items.Add(Item("선택 키만", () => { KeyTicks = KeyTickMode.Selected; InvalidateVisual(); }));
        ticks.Items.Add(Item("없음", () => { KeyTicks = KeyTickMode.None; InvalidateVisual(); }));
        menu.Items.Add(ticks);
        return menu;
    }

    private void SetMode(PlaybackMode mode)
    {
        if (_viewport is not null)
        {
            _viewport.Mode = mode;
        }
    }
}
