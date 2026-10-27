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
    private static readonly IBrush KeyTickBrush = new SolidColorBrush(Color.FromArgb(220, 248, 113, 113));
    private static readonly IBrush SelectedTickBrush = new SolidColorBrush(Color.FromArgb(255, 250, 204, 21));
    private static readonly IBrush RangeOutsideBrush = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
    private static readonly IBrush RangeHandleBrush = new SolidColorBrush(Color.FromArgb(240, 125, 211, 252));
    private static readonly IBrush GizmoCenterBrush = new SolidColorBrush(Color.FromArgb(230, 250, 204, 21));
    private static readonly IBrush GizmoXBrush = new SolidColorBrush(Color.FromArgb(240, 248, 113, 113));
    private static readonly IBrush GizmoYBrush = new SolidColorBrush(Color.FromArgb(240, 74, 222, 128));
    private static readonly Pen GizmoRingPen = new(new SolidColorBrush(Color.FromArgb(230, 96, 165, 250)), 2);
    private static readonly Pen PendingPen = new(new SolidColorBrush(Color.FromArgb(220, 251, 146, 60)), 1.5, DashStyle.Dash);

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
    private KeySelection _selection = new();

    private enum Drag { None, Scrub, RangeStart, RangeEnd, MoveFree, MoveX, MoveY, Rotate, ScaleFree, ScaleX, ScaleY }

    private Drag _drag = Drag.None;

    private const double GizmoArm = 60;
    private const double GizmoHit = 9;

    // 키로 아직 안 들어간 포즈(마야: 키 없이 바꾼 값). 시각이 바뀌면 버린다.
    private readonly Dictionary<string, RectNodeState> _overrides = new();
    private readonly Dictionary<(string Node, MotionChannelKind Kind), HashSet<Axis>> _pending = new();
    private Vec2 _dragStartRoot;
    private Vec2 _dragPivotRoot;
    private RectNodeState _dragStartState;

    public ViewportView()
    {
        MinHeight = 120;
        ClipToBounds = true;
        Focusable = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => OnTick();
        _selection.Changed += OnSelectionChanged;
    }

    public event EventHandler? TimeChanged;

    public event EventHandler? PlayStateChanged;

    /// <summary>재생 범위·모드·속도가 바뀌었다.</summary>
    public event EventHandler? RangeChanged;

    /// <summary>키 없이 바꾼 포즈가 생기거나 사라졌다(S로 키를 놓거나 시각이 바뀌면 사라진다).</summary>
    public event EventHandler? PendingChanged;

    /// <summary>매니퓰레이터·S·Auto Key로 키가 들어갔다.</summary>
    public event EventHandler? PoseKeyed;

    public ManipulatorMode Manipulator { get; set; } = ManipulatorMode.Move;

    /// <summary>켜져 있으면 매니퓰레이터를 놓는 순간 키가 들어간다. 꺼져 있으면 S를 누를 때까지 "키 없는 포즈"다.</summary>
    public bool AutoKey { get; set; }

    /// <summary>새 키의 탄젠트 모드(설정).</summary>
    public TangentMode DefaultTangent { get; set; } = TangentMode.Auto;

    public bool SnapEnabled { get; set; } = true;

    public double SnapStep { get; set; } = TimeSnap.DefaultStep;

    public bool HasPending => _pending.Count > 0;

    /// <summary>매니퓰레이터를 둘 수 있는 노드들 — 덤프의 노드 중 __root·키잉 금지 노드를 뺀 것.</summary>
    public IReadOnlyList<string> NodeChoices => _template is null
        ? Array.Empty<string>()
        : _template.Keys.Where(k => k != RigSchemaLoader.RootKey && !MotionRules.ForbiddenNodes.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

    private double SnapTime(double seconds) => SnapEnabled ? TimeSnap.Snap(seconds, SnapStep) : seconds;

    /// <summary>지금 보이는 포즈 — 시각의 포즈에 키 없는 포즈를 얹은 것.</summary>
    public RectNodeTree? CurrentPose()
    {
        RectNodeTree? pose = PoseAt(_clock.Time);

        if (pose is null)
        {
            return null;
        }

        foreach ((string node, RectNodeState state) in _overrides)
        {
            if (pose.Contains(node))
            {
                pose.SetState(node, state);
            }
        }

        return pose;
    }

    /// <summary>선택 노드의 피벗(컨트롤 좌표) — 매니퓰레이터 중심. 테스트가 쓴다.</summary>
    public Point? GizmoCenter()
    {
        RectNodeTree? pose = CurrentPose();

        if (pose is null || SelectedNode is null || !pose.Contains(SelectedNode))
        {
            return null;
        }

        return ToControl(pose.TransformPoint(SelectedNode, Vec3.Zero).XY);
    }

    /// <summary>키 없는 포즈를 버린다(시각이 바뀌거나 Esc).</summary>
    public void DiscardPending()
    {
        if (_overrides.Count == 0 && _pending.Count == 0)
        {
            return;
        }

        _overrides.Clear();
        _pending.Clear();
        PendingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// Set Key(S). 키 없는 포즈가 있으면 그것을 키로. 없으면 선택 노드의 있는 채널·성분을 지금 포즈 값으로 키(홀드 키).
    /// </summary>
    public void SetKey()
    {
        MotionAsset? motion = Motion;

        if (_editor is null || motion is null)
        {
            return;
        }

        if (_pending.Count > 0)
        {
            foreach (((string node, MotionChannelKind kind), HashSet<Axis> axes) in _pending.ToList())
            {
                KeyNow(node, kind, axes.ToList());
            }

            DiscardPending();
            return;
        }

        if (SelectedNode is null)
        {
            return;
        }

        foreach (ChannelAsset channel in motion.Channels.Where(c => c.Node == SelectedNode).ToList())
        {
            var axes = new List<Axis>();

            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is not null)
                {
                    axes.Add(axis);
                }
            }

            if (axes.Count > 0)
            {
                KeyNow(channel.Node, channel.Kind, axes);
            }
        }
    }

    /// <summary>지금 포즈의 값으로 키를 놓는다 — 시작값은 덤프(템플릿), 키 값은 채널 mode를 따른다.</summary>
    private void KeyNow(string node, MotionChannelKind kind, IReadOnlyList<Axis> axes)
    {
        MotionAsset? motion = Motion;
        RectNodeTree? pose = CurrentPose();

        if (_editor is null || motion is null || _template is null || pose is null || !pose.Contains(node) || axes.Count == 0)
        {
            return;
        }

        MotionChannelMode mode = motion.FindChannel(node, kind)?.Mode ?? motion.DefaultMode;
        RectNodeState start = _template.GetState(node);
        RectNodeState posed = pose.GetState(node);
        var values = new List<(Axis, float)>();

        foreach (Axis axis in axes)
        {
            values.Add((axis, PoseEdit.KeyValueFor(kind, mode, PoseEdit.Component(kind, start, axis), PoseEdit.Component(kind, posed, axis))));
        }

        _overrides.Remove(node);
        _pending.Remove((node, kind));
        _editor.KeyComponents(motion.Name, node, kind, (float)Math.Max(0, SnapTime(_clock.Time)), values, DefaultTangent);
        PendingChanged?.Invoke(this, EventArgs.Empty);
        PoseKeyed?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>선택 키를 띠에 노랗게 보이려고 든다. 그래프·도프시트와 같은 객체를 꽂는다.</summary>
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

    private void OnSelectionChanged(object? sender, EventArgs e) => InvalidateVisual();

    public PlaybackMode Mode
    {
        get => _clock.Mode;
        set
        {
            _clock.Mode = value;
            RangeChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public double Speed
    {
        get => _clock.Speed;
        set
        {
            _clock.Speed = Math.Clamp(value, 0.05, 8);
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetRange(double start, double end)
    {
        _clock.SetRange(start, end);
        RangeChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void ResetRange()
    {
        _clock.ResetRange();
        RangeChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>다음 키 시각으로(`.`). 없으면 그대로.</summary>
    public bool JumpToNextKey()
    {
        if (Motion is not { } motion)
        {
            return false;
        }

        float? next = KeyTimes.Next(KeyTimes.All(motion), _clock.Time);

        if (next is null)
        {
            return false;
        }

        Pause();
        ScrubTo(next.Value);
        return true;
    }

    /// <summary>이전 키 시각으로(`,`). 없으면 그대로.</summary>
    public bool JumpToPreviousKey()
    {
        if (Motion is not { } motion)
        {
            return false;
        }

        float? previous = KeyTimes.Previous(KeyTimes.All(motion), _clock.Time);

        if (previous is null)
        {
            return false;
        }

        Pause();
        ScrubTo(previous.Value);
        return true;
    }

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
        _overrides.Clear();
        _pending.Clear();
        SyncDuration();
        RebuildTemplate();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
        TimeChanged?.Invoke(this, EventArgs.Empty);
        PendingChanged?.Invoke(this, EventArgs.Empty);
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
            RangeChanged?.Invoke(this, EventArgs.Empty);
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
            DiscardPending();
            TimeChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }

    /// <summary>시계를 손으로 민다 — 타이머 대신 테스트가 부른다.</summary>
    public void Advance(double seconds)
    {
        if (_clock.Advance(seconds))
        {
            DiscardPending();
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

    /// <summary>컨트롤 좌표 → 루트 공간(가운데 원점·y 위).</summary>
    public Vec2 ToRoot(Point control)
    {
        Rect stage = StageRect;
        double scale = stage.Width / _stageSize.X;
        double canvasX = (control.X - stage.X) / scale;
        double canvasY = (control.Y - stage.Y) / scale;
        return new Vec2((float)(canvasX - _stageSize.X / 2), (float)(_stageSize.Y / 2 - canvasY));
    }

    private double TimeAtX(double x)
    {
        Rect timeline = TimelineRect;
        double u = Math.Clamp((x - timeline.X) / Math.Max(1, timeline.Width), 0, 1);
        return u * _clock.Duration;
    }

    public double XAtTime(double seconds)
    {
        Rect timeline = TimelineRect;
        return timeline.X + (_clock.Duration <= 0 ? 0 : seconds / _clock.Duration) * timeline.Width;
    }

    private static Rect HandleRect(double x, Rect timeline) => new(x - 4, timeline.Y, 8, timeline.Height);

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

            RectNodeTree pose = CurrentPose() ?? MotionPreview.Pose(_template, motion, _clock.Time);
            DrawPortrait(context, pose);
            DrawAxes(context, pose);
            DrawGizmo(context, pose);
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

    private void DrawGizmo(DrawingContext context, RectNodeTree pose)
    {
        if (SelectedNode is null || !pose.Contains(SelectedNode) || Manipulator == ManipulatorMode.Select)
        {
            return;
        }

        (Point center, Point xEnd, Point yEnd) = GizmoPoints(pose);

        if (_pending.Count > 0)
        {
            context.DrawEllipse(null, PendingPen, center, GizmoArm + 8, GizmoArm + 8);
        }

        switch (Manipulator)
        {
            case ManipulatorMode.Move:
            case ManipulatorMode.Scale:
                context.DrawLine(XAxisPen, center, xEnd);
                context.DrawLine(YAxisPen, center, yEnd);
                context.FillRectangle(GizmoXBrush, new Rect(xEnd.X - 5, xEnd.Y - 5, 10, 10));
                context.FillRectangle(GizmoYBrush, new Rect(yEnd.X - 5, yEnd.Y - 5, 10, 10));
                context.FillRectangle(GizmoCenterBrush, new Rect(center.X - 6, center.Y - 6, 12, 12));
                break;
            case ManipulatorMode.Rotate:
                context.DrawEllipse(null, GizmoRingPen, center, GizmoArm, GizmoArm);
                context.FillRectangle(GizmoCenterBrush, new Rect(center.X - 4, center.Y - 4, 8, 8));
                break;
        }
    }

    /// <summary>매니퓰레이터의 중심과 x·y 팔 끝(컨트롤 좌표, 팔 길이는 화면 고정).</summary>
    private (Point Center, Point XEnd, Point YEnd) GizmoPoints(RectNodeTree pose)
    {
        (Vec2 origin, Vec2 xAxis, Vec2 yAxis) = PortraitLayout.Axes(pose, SelectedNode!, 100f);
        Point center = ToControl(origin);
        return (center, Along(center, ToControl(xAxis)), Along(center, ToControl(yAxis)));
    }

    private static Point Along(Point from, Point towards)
    {
        double dx = towards.X - from.X;
        double dy = towards.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 1e-6)
        {
            return new Point(from.X + GizmoArm, from.Y);
        }

        return new Point(from.X + dx / length * GizmoArm, from.Y + dy / length * GizmoArm);
    }

    private void DrawTimeline(DrawingContext context)
    {
        Rect timeline = TimelineRect;
        context.FillRectangle(TimelineBrush, timeline);

        // 재생 범위 밖은 어둡게.
        double startX = XAtTime(_clock.RangeStart);
        double endX = XAtTime(_clock.RangeEnd);
        context.FillRectangle(RangeOutsideBrush, new Rect(timeline.X, timeline.Y, Math.Max(0, startX - timeline.X), timeline.Height));
        context.FillRectangle(RangeOutsideBrush, new Rect(endX, timeline.Y, Math.Max(0, timeline.Right - endX), timeline.Height));

        double x = XAtTime(_clock.Time);
        context.FillRectangle(TimelineFill, new Rect(timeline.X, timeline.Y, Math.Max(0, x - timeline.X), timeline.Height));

        // 키 틱 — 모든 키는 빨강, 선택 키는 노랑(마야 타임 슬라이더).
        if (Motion is { } motion)
        {
            List<float> selected = KeyTimes.OfSelection(motion, _selection);

            foreach (float t in KeyTimes.All(motion))
            {
                bool isSelected = selected.Any(st => Math.Abs(st - t) <= MotionRules.Tolerance);
                double tx = XAtTime(t);
                context.FillRectangle(isSelected ? SelectedTickBrush : KeyTickBrush, new Rect(tx - 1.5, timeline.Y + 2, 3, timeline.Height - 4));
            }
        }

        context.DrawLine(TimelinePen, new Point(x, timeline.Y), new Point(x, timeline.Bottom));
        context.DrawLine(DurationPen, new Point(timeline.Right - 1, timeline.Y), new Point(timeline.Right - 1, timeline.Bottom));
        context.FillRectangle(RangeHandleBrush, HandleRect(startX, timeline));
        context.FillRectangle(RangeHandleBrush, HandleRect(endX, timeline));

        string mode = _clock.Mode switch { PlaybackMode.Once => "한 번", PlaybackMode.Oscillate => "왕복", _ => "루프" };
        string label = string.Format(
            CultureInfo.InvariantCulture, "{0:0.000}s · {1:0.#}f / {2:0.###}s · {3}{4}{5}",
            _clock.Time, TimeSnap.ToFrames(_clock.Time), _clock.Duration, mode,
            Math.Abs(_clock.Speed - 1) > 1e-9 ? $" ×{_clock.Speed:0.##}" : "", _clock.IsPlaying ? " ▶" : "");
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

        if (TryStartManipulation(position))
        {
            Pause();
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        Rect timeline = TimelineRect;

        if (!timeline.Contains(position))
        {
            return;
        }

        if (HandleRect(XAtTime(_clock.RangeEnd), timeline).Inflate(3).Contains(position))
        {
            _drag = Drag.RangeEnd;
        }
        else if (HandleRect(XAtTime(_clock.RangeStart), timeline).Inflate(3).Contains(position))
        {
            _drag = Drag.RangeStart;
        }
        else
        {
            _drag = Drag.Scrub;
            Pause();
            ScrubTo(TimeAtX(position.X));
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private bool TryStartManipulation(Point position)
    {
        RectNodeTree? pose = CurrentPose();

        if (Manipulator == ManipulatorMode.Select || SelectedNode is null || pose is null || !pose.Contains(SelectedNode) || !StageRect.Contains(position))
        {
            return false;
        }

        (Point center, Point xEnd, Point yEnd) = GizmoPoints(pose);
        double toCenter = Distance(position, center);
        Drag drag = Drag.None;

        switch (Manipulator)
        {
            case ManipulatorMode.Move:
                drag = Distance(position, xEnd) <= GizmoHit ? Drag.MoveX
                    : Distance(position, yEnd) <= GizmoHit ? Drag.MoveY
                    : toCenter <= GizmoHit + 2 ? Drag.MoveFree : Drag.None;
                break;
            case ManipulatorMode.Scale:
                drag = Distance(position, xEnd) <= GizmoHit ? Drag.ScaleX
                    : Distance(position, yEnd) <= GizmoHit ? Drag.ScaleY
                    : toCenter <= GizmoHit + 2 ? Drag.ScaleFree : Drag.None;
                break;
            case ManipulatorMode.Rotate:
                drag = Math.Abs(toCenter - GizmoArm) <= GizmoHit ? Drag.Rotate : Drag.None;
                break;
        }

        if (drag == Drag.None)
        {
            return false;
        }

        _drag = drag;
        _dragStartRoot = ToRoot(position);
        _dragPivotRoot = pose.TransformPoint(SelectedNode, Vec3.Zero).XY;
        _dragStartState = pose.GetState(SelectedNode);
        return true;
    }

    private void DragManipulation(Point position)
    {
        RectNodeTree? pose = CurrentPose();

        if (SelectedNode is null || pose is null || !pose.Contains(SelectedNode))
        {
            return;
        }

        Vec2 now = ToRoot(position);
        RectNodeState start = _dragStartState;
        RectNodeState next;

        switch (_drag)
        {
            case Drag.MoveFree:
            case Drag.MoveX:
            case Drag.MoveY:
            {
                Vec2 delta = PoseEdit.RootDeltaToLocal(pose, SelectedNode, _dragStartRoot, now);
                float dx = _drag == Drag.MoveY ? 0f : delta.X;
                float dy = _drag == Drag.MoveX ? 0f : delta.Y;
                next = start.WithAnchoredPosition(new Vec2(start.AnchoredPosition.X + dx, start.AnchoredPosition.Y + dy));
                break;
            }

            case Drag.Rotate:
            {
                float angle = PoseEdit.AngleDelta(_dragPivotRoot, _dragStartRoot, now);
                Vec3 euler = start.LocalEulerAngles;
                next = start.WithLocalEuler(new Vec3(euler.X, euler.Y, euler.Z + angle));
                break;
            }

            case Drag.ScaleFree:
            case Drag.ScaleX:
            case Drag.ScaleY:
            {
                float factor = PoseEdit.ScaleFactor(_dragPivotRoot, _dragStartRoot, now);
                float sx = _drag == Drag.ScaleY ? start.LocalScale.X : start.LocalScale.X * factor;
                float sy = _drag == Drag.ScaleX ? start.LocalScale.Y : start.LocalScale.Y * factor;
                next = start.WithLocalScale(new Vec3(sx, sy, start.LocalScale.Z));
                break;
            }

            default:
                return;
        }

        _overrides[SelectedNode] = next;
        InvalidateVisual();
    }

    /// <summary>놓는 순간: Auto Key거나 그 시각에 이미 키가 있으면 키로, 아니면 키 없는 포즈로 남긴다.</summary>
    private void EndManipulation()
    {
        MotionAsset? motion = Motion;
        string? node = SelectedNode;

        if (motion is null || node is null || !_overrides.TryGetValue(node, out RectNodeState posed))
        {
            return;
        }

        (MotionChannelKind kind, List<Axis> axes) = ChangedAxes(_dragStartState, posed);

        if (axes.Count == 0)
        {
            _overrides.Remove(node);
            InvalidateVisual();
            return;
        }

        ChannelAsset? channel = motion.FindChannel(node, kind);
        float time = (float)SnapTime(_clock.Time);
        bool keyExists = channel is not null && axes.Any(a => channel.Get(a)?.Keys.Any(k => Math.Abs(k.T - time) <= MotionRules.Tolerance) == true);

        if (AutoKey || keyExists)
        {
            KeyNow(node, kind, axes);
            return;
        }

        if (!_pending.TryGetValue((node, kind), out HashSet<Axis>? pendingAxes))
        {
            pendingAxes = new HashSet<Axis>();
            _pending[(node, kind)] = pendingAxes;
        }

        pendingAxes.UnionWith(axes);
        PendingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>드래그 종류의 채널 종류와, 실제로 값이 바뀐 성분만(마야 Auto Key의 "바뀐 속성만").</summary>
    private (MotionChannelKind Kind, List<Axis> Axes) ChangedAxes(in RectNodeState start, in RectNodeState posed)
    {
        const float epsilon = 1e-4f;
        var axes = new List<Axis>();

        switch (_drag)
        {
            case Drag.MoveFree:
            case Drag.MoveX:
            case Drag.MoveY:
                if (Math.Abs(posed.AnchoredPosition.X - start.AnchoredPosition.X) > epsilon) axes.Add(Axis.X);
                if (Math.Abs(posed.AnchoredPosition.Y - start.AnchoredPosition.Y) > epsilon) axes.Add(Axis.Y);
                return (MotionChannelKind.Position, axes);
            case Drag.Rotate:
                if (Math.Abs(posed.LocalEulerAngles.Z - start.LocalEulerAngles.Z) > epsilon) axes.Add(Axis.Z);
                return (MotionChannelKind.Euler, axes);
            default:
                if (Math.Abs(posed.LocalScale.X - start.LocalScale.X) > epsilon) axes.Add(Axis.X);
                if (Math.Abs(posed.LocalScale.Y - start.LocalScale.Y) > epsilon) axes.Add(Axis.Y);
                return (MotionChannelKind.Scale, axes);
        }
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private void DragRange(double x)
    {
        double t = TimeAtX(x);

        try
        {
            if (_drag == Drag.RangeEnd)
            {
                SetRange(_clock.RangeStart, Math.Max(t, _clock.RangeStart + 1.0 / TimeSnap.FramesPerSecond));
            }
            else
            {
                SetRange(Math.Min(t, _clock.RangeEnd - 1.0 / TimeSnap.FramesPerSecond), _clock.RangeEnd);
            }
        }
        catch (ArgumentException)
        {
            // 범위가 너무 좁다 — 그 프레임은 건너뛴다.
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        switch (_drag)
        {
            case Drag.Scrub:
                ScrubTo(TimeAtX(e.GetPosition(this).X));
                e.Handled = true;
                break;
            case Drag.RangeStart:
            case Drag.RangeEnd:
                DragRange(e.GetPosition(this).X);
                e.Handled = true;
                break;
            case Drag.MoveFree:
            case Drag.MoveX:
            case Drag.MoveY:
            case Drag.Rotate:
            case Drag.ScaleFree:
            case Drag.ScaleX:
            case Drag.ScaleY:
                DragManipulation(e.GetPosition(this));
                e.Handled = true;
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_drag != Drag.None)
        {
            Drag drag = _drag;

            if (drag is Drag.MoveFree or Drag.MoveX or Drag.MoveY or Drag.Rotate or Drag.ScaleFree or Drag.ScaleX or Drag.ScaleY)
            {
                EndManipulation();
            }

            _drag = Drag.None;
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
            case Key.OemPeriod:
                JumpToNextKey();
                break;
            case Key.OemComma:
                JumpToPreviousKey();
                break;
            case Key.Q:
                Manipulator = ManipulatorMode.Select;
                InvalidateVisual();
                break;
            case Key.W:
                Manipulator = ManipulatorMode.Move;
                InvalidateVisual();
                break;
            case Key.E:
                Manipulator = ManipulatorMode.Rotate;
                InvalidateVisual();
                break;
            case Key.R:
                Manipulator = ManipulatorMode.Scale;
                InvalidateVisual();
                break;
            case Key.S:
                SetKey();
                break;
            case Key.Escape:
                DiscardPending();
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
