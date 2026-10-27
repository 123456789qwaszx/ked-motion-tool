using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;

namespace Mt.App.Views;

/// <summary>
/// 뷰포트 — 리그 덤프로 세운 트리 위에 초상 한 장을 올리고 모션을 재생한다.
///
/// 포즈는 <see cref="MotionPreview"/>(코어 수학), 자리는 <see cref="PortraitLayout"/>(런타임 사이징)이다 —
/// 여기에는 그리기·시계·입력만 있다. 무대는 기준 해상도 크기이고 컨트롤 안에 레터박스로 맞춘다.
/// 깊이는 mid(DepthScale 1) 고정이고 카메라는 없다 — v1 범위.
///
/// 아래 띠가 타임라인이다: 클릭·드래그로 스크럽. Space 재생/일시정지, Home/End 처음/끝.
/// </summary>
public sealed class ViewportView : Control
{
    private const double Pad = 8;
    private const double TimelineHeight = 22;

    private static readonly IBrush Background = new SolidColorBrush(Color.FromArgb(255, 16, 16, 22));
    private static readonly IBrush StageBrush = new SolidColorBrush(Color.FromArgb(255, 44, 44, 58));
    private static readonly Pen StagePen = new(new SolidColorBrush(Color.FromArgb(120, 148, 163, 184)), 1);
    private static readonly Pen PlaceholderPen = new(new SolidColorBrush(Color.FromArgb(200, 226, 232, 240)), 1.5, DashStyle.Dash);
    private static readonly IBrush PlaceholderFill = new SolidColorBrush(Color.FromArgb(40, 226, 232, 240));
    private static readonly Pen XAxisPen = new(new SolidColorBrush(Color.FromArgb(230, 248, 113, 113)), 2);
    private static readonly Pen YAxisPen = new(new SolidColorBrush(Color.FromArgb(230, 74, 222, 128)), 2);
    private static readonly IBrush PivotBrush = new SolidColorBrush(Color.FromArgb(240, 250, 204, 21));
    private static readonly IBrush TimelineBrush = new SolidColorBrush(Color.FromArgb(255, 30, 30, 40));
    private static readonly IBrush TimelineFill = new SolidColorBrush(Color.FromArgb(120, 34, 211, 238));
    private static readonly Pen TimelinePen = new(new SolidColorBrush(Color.FromArgb(255, 34, 211, 238)), 2);
    private static readonly Pen DurationPen = new(new SolidColorBrush(Color.FromArgb(180, 251, 146, 60)), 1);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromArgb(200, 203, 213, 225));

    private readonly PlaybackClock _clock = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = new();

    private MotionEditor? _editor;
    private string? _motionName;
    private RigSchemaFile? _rig;
    private RectNodeTree? _template;
    private string? _templateRigKind;
    private Bitmap? _portrait;
    private float _aspect = PortraitLayout.PlaceholderAspect;
    private Vec2 _stageSize = BaseResolutionFile.Default;
    private bool _scrubbing;

    public ViewportView()
    {
        MinHeight = 120;
        ClipToBounds = true;
        Focusable = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => OnTick();
    }

    public event EventHandler? TimeChanged;

    public event EventHandler? PlayStateChanged;

    public PlaybackClock Clock => _clock;

    public double Time => _clock.Time;

    public bool IsPlaying => _clock.IsPlaying;

    public bool Loop
    {
        get => _clock.Loop;
        set => _clock.Loop = value;
    }

    public bool OnionSkinEnabled { get; set; }

    public int OnionSkinFrames { get; set; } = 2;

    /// <summary>축을 그릴 노드. null이면 안 그린다.</summary>
    public string? SelectedNode { get; set; }

    public Vec2 StageSize
    {
        get => _stageSize;
        set
        {
            _stageSize = value.X > 0 && value.Y > 0 ? value : BaseResolutionFile.Default;
            InvalidateVisual();
        }
    }

    public string? MotionName => _motionName;

    public MotionAsset? Motion => _editor is not null && _motionName is not null ? _editor.Document.Find(_motionName) : null;

    public bool HasRig => _template is not null;

    public float Aspect => _aspect;

    // ── 바인딩 ────────────────────────────────────────────────────────

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

        _clock.Stop();
        _timer.Stop();
        SyncDuration();
        RebuildTemplate();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
        TimeChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void SetRig(RigSchemaFile? rig)
    {
        _rig = rig;
        RebuildTemplate();
        InvalidateVisual();
    }

    /// <summary>초상 비트맵. null이면 자리표시 사각형. 종횡비는 비트맵에서 온다.</summary>
    public void SetPortrait(Bitmap? portrait)
    {
        _portrait = portrait;
        _aspect = portrait is { PixelSize: { Width: > 0, Height: > 0 } }
            ? portrait.PixelSize.Width / (float)portrait.PixelSize.Height
            : PortraitLayout.PlaceholderAspect;
        RebuildTemplate();
        InvalidateVisual();
    }

    private void OnEditorChanged(object? sender, MotionChangedEventArgs e)
    {
        SyncDuration();

        if (Motion is { } motion && motion.Rig != _templateRigKind)
        {
            RebuildTemplate();
        }

        InvalidateVisual();
    }

    private void SyncDuration()
    {
        if (Motion is { } motion)
        {
            _clock.SetDuration(motion.Duration);
        }
    }

    private void RebuildTemplate()
    {
        _template = null;
        _templateRigKind = null;
        MotionAsset? motion = Motion;

        if (_rig is null || motion is null || !_rig.TryGetRig(motion.Rig, out _))
        {
            return;
        }

        RectNodeTree tree = RigSchemaLoader.BuildTree(_rig.Dto, motion.Rig);
        PortraitLayout.ApplySizing(tree, _aspect);
        _template = tree;
        _templateRigKind = motion.Rig;
    }

    // ── 시계 ──────────────────────────────────────────────────────────

    public void Play()
    {
        _clock.Play();
        _stopwatch.Restart();
        _timer.Start();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        _clock.Pause();
        _timer.Stop();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlay()
    {
        if (_clock.IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Stop()
    {
        _clock.Stop();
        _timer.Stop();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
        TimeChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void ScrubTo(double seconds)
    {
        double before = _clock.Time;
        _clock.ScrubTo(seconds);

        if (Math.Abs(before - _clock.Time) > 1e-12)
        {
            TimeChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }

    /// <summary>시계를 손으로 민다 — 타이머 대신 테스트가 부른다.</summary>
    public void Advance(double seconds)
    {
        if (_clock.Advance(seconds))
        {
            TimeChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        if (!_clock.IsPlaying)
        {
            _timer.Stop();
            PlayStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnTick()
    {
        double elapsed = _stopwatch.Elapsed.TotalSeconds;
        _stopwatch.Restart();
        Advance(elapsed);
    }

    // ── 포즈 조회 (테스트·인스펙터) ───────────────────────────────────

    public RectNodeTree? PoseAt(double seconds)
    {
        MotionAsset? motion = Motion;
        return _template is null || motion is null ? null : MotionPreview.Pose(_template, motion, seconds);
    }

    /// <summary>지금 시각의 초상 사각형(루트 공간). 리그가 없으면 null.</summary>
    public Quad? CurrentPortraitQuad()
    {
        RectNodeTree? pose = PoseAt(_clock.Time);
        return pose is null || !pose.Contains(PortraitLayout.ImageNodeId)
            ? null
            : PortraitLayout.VisualQuad(pose, PortraitLayout.ImageNodeId, _aspect);
    }

    // ── 좌표 ──────────────────────────────────────────────────────────

    /// <summary>무대가 놓이는 컨트롤 영역(레터박스).</summary>
    public Rect StageRect
    {
        get
        {
            double availableWidth = Math.Max(1, Bounds.Width - Pad * 2);
            double availableHeight = Math.Max(1, Bounds.Height - Pad * 2 - TimelineHeight);
            double scale = Math.Min(availableWidth / _stageSize.X, availableHeight / _stageSize.Y);
            double width = _stageSize.X * scale;
            double height = _stageSize.Y * scale;
            return new Rect(Pad + (availableWidth - width) / 2, Pad + (availableHeight - height) / 2, width, height);
        }
    }

    public Rect TimelineRect => new(Pad, Bounds.Height - TimelineHeight - 2, Math.Max(1, Bounds.Width - Pad * 2), TimelineHeight);

    private Point ToControl(Vec2 root)
    {
        Rect stage = StageRect;
        Vec2 canvas = PortraitLayout.ToCanvas(root, _stageSize);
        double scale = stage.Width / _stageSize.X;
        return new Point(stage.X + canvas.X * scale, stage.Y + canvas.Y * scale);
    }

    private double TimeAtX(double x)
    {
        Rect timeline = TimelineRect;
        double u = Math.Clamp((x - timeline.X) / Math.Max(1, timeline.Width), 0, 1);
        return u * _clock.Duration;
    }

    // ── 그리기 ────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));
        Rect stage = StageRect;
        context.FillRectangle(StageBrush, stage);
        context.DrawRectangle(StagePen, stage);

        MotionAsset? motion = Motion;

        if (motion is null)
        {
            DrawLabel(context, "모션을 고르면 여기에 무대가 보입니다.", new Point(stage.X + 10, stage.Y + 10));
            DrawTimeline(context);
            return;
        }

        if (_template is null)
        {
            DrawLabel(context, _rig is null ? "리그 덤프(rig-schemas.json)를 실으면 초상이 선다." : $"덤프에 rigKind '{motion.Rig}'가 없다.", new Point(stage.X + 10, stage.Y + 10));
            DrawTimeline(context);
            return;
        }

        using (context.PushClip(stage))
        {
            if (OnionSkinEnabled)
            {
                foreach ((double t, double opacity) in OnionSkin.Frames(_clock.Time, _clock.Duration, OnionSkinFrames))
                {
                    using (context.PushOpacity(opacity))
                    {
                        DrawPortrait(context, MotionPreview.Pose(_template, motion, t));
                    }
                }
            }

            RectNodeTree pose = MotionPreview.Pose(_template, motion, _clock.Time);
            DrawPortrait(context, pose);
            DrawAxes(context, pose);
        }

        string endRule = motion.End == MotionEnd.Restore ? "restore — 끝에서 시작 상태로" : "commit — 마지막 키가 남는다";
        DrawLabel(context, $"{motion.Name} · {endRule}", new Point(stage.X + 8, stage.Y + 6));
        DrawTimeline(context);
    }

    private void DrawPortrait(DrawingContext context, RectNodeTree pose)
    {
        if (!pose.Contains(PortraitLayout.ImageNodeId))
        {
            return;
        }

        Quad quad = PortraitLayout.VisualQuad(pose, PortraitLayout.ImageNodeId, _aspect);
        Point topLeft = ToControl(quad.TopLeft);
        Point topRight = ToControl(quad.TopRight);
        Point bottomLeft = ToControl(quad.BottomLeft);
        Point bottomRight = ToControl(quad.BottomRight);

        if (_portrait is null)
        {
            var outline = new PolylineGeometry(new[] { topLeft, topRight, bottomRight, bottomLeft, topLeft }, true);
            context.DrawGeometry(PlaceholderFill, PlaceholderPen, outline);
            context.DrawLine(PlaceholderPen, topLeft, bottomRight);
            context.DrawLine(PlaceholderPen, topRight, bottomLeft);
            return;
        }

        // 이미지 로컬 (0,0)-(w,h) → 화면 사각형 네 점. 직교 투영 아래서 평면 사각형의 변환은 아핀이라 세 점이면 정확하다.
        double w = _portrait.PixelSize.Width;
        double h = _portrait.PixelSize.Height;
        var matrix = new Matrix(
            (topRight.X - topLeft.X) / w, (topRight.Y - topLeft.Y) / w,
            (bottomLeft.X - topLeft.X) / h, (bottomLeft.Y - topLeft.Y) / h,
            topLeft.X, topLeft.Y);

        using (context.PushTransform(matrix))
        {
            context.DrawImage(_portrait, new Rect(0, 0, w, h));
        }
    }

    private void DrawAxes(DrawingContext context, RectNodeTree pose)
    {
        if (SelectedNode is null || !pose.Contains(SelectedNode))
        {
            return;
        }

        (Vec2 origin, Vec2 xEnd, Vec2 yEnd) = PortraitLayout.Axes(pose, SelectedNode, 80f);
        Point o = ToControl(origin);
        context.DrawLine(XAxisPen, o, ToControl(xEnd));
        context.DrawLine(YAxisPen, o, ToControl(yEnd));
        context.DrawEllipse(PivotBrush, null, o, 4, 4);
        DrawLabel(context, SelectedNode, new Point(o.X + 6, o.Y + 6));
    }

    private void DrawTimeline(DrawingContext context)
    {
        Rect timeline = TimelineRect;
        context.FillRectangle(TimelineBrush, timeline);

        double u = _clock.Duration <= 0 ? 0 : _clock.Time / _clock.Duration;
        double x = timeline.X + u * timeline.Width;
        context.FillRectangle(TimelineFill, new Rect(timeline.X, timeline.Y, Math.Max(0, x - timeline.X), timeline.Height));
        context.DrawLine(TimelinePen, new Point(x, timeline.Y), new Point(x, timeline.Bottom));
        context.DrawLine(DurationPen, new Point(timeline.Right - 1, timeline.Y), new Point(timeline.Right - 1, timeline.Bottom));

        string label = string.Format(
            CultureInfo.InvariantCulture, "{0:0.000}s · {1:0.#}f / {2:0.###}s{3}",
            _clock.Time, TimeSnap.ToFrames(_clock.Time), _clock.Duration, _clock.IsPlaying ? " ▶" : "");
        DrawLabel(context, label, new Point(timeline.X + 6, timeline.Y + 4));
    }

    private static void DrawLabel(DrawingContext context, string text, Point at)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);
        context.DrawText(formatted, at);
    }

    // ── 입력 ──────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        Point position = e.GetPosition(this);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (TimelineRect.Contains(position))
        {
            _scrubbing = true;
            Pause();
            ScrubTo(TimeAtX(position.X));
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_scrubbing)
        {
            ScrubTo(TimeAtX(e.GetPosition(this).X));
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_scrubbing)
        {
            _scrubbing = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Space:
                TogglePlay();
                break;
            case Key.Home:
                Pause();
                ScrubTo(0);
                break;
            case Key.End:
                Pause();
                ScrubTo(_clock.Duration);
                break;
            case Key.Left:
                Pause();
                ScrubTo(_clock.Time - 1.0 / TimeSnap.FramesPerSecond);
                break;
            case Key.Right:
                Pause();
                ScrubTo(_clock.Time + 1.0 / TimeSnap.FramesPerSecond);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
