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
/// 타임 슬라이더는 따로(<see cref="TimeSliderView"/>)다. 재생 토글은 전역 Alt+`(셸), Home/End 처음/끝.
/// <b>포즈 들고 스크럽</b>(마야 MMB 드래그): <see cref="HoldScrubTo"/>로 시각을 옮겨도 보이는 포즈는 그대로라 S로 그 포즈를 다른 프레임에 키한다.
/// </summary>
public sealed class ViewportView : Control
{
    private const double Pad = 8;

    private static readonly IBrush Background = new SolidColorBrush(Color.FromArgb(255, 16, 16, 22));
    private static readonly IBrush StageBrush = new SolidColorBrush(Color.FromArgb(255, 44, 44, 58));
    private static readonly Pen StagePen = new(new SolidColorBrush(Color.FromArgb(120, 148, 163, 184)), 1);
    private static readonly Pen PlaceholderPen = new(new SolidColorBrush(Color.FromArgb(200, 226, 232, 240)), 1.5, DashStyle.Dash);
    private static readonly IBrush PlaceholderFill = new SolidColorBrush(Color.FromArgb(40, 226, 232, 240));
    private static readonly Pen XAxisPen = new(new SolidColorBrush(Color.FromArgb(230, 248, 113, 113)), 2);
    private static readonly Pen YAxisPen = new(new SolidColorBrush(Color.FromArgb(230, 74, 222, 128)), 2);
    private static readonly IBrush PivotBrush = new SolidColorBrush(Color.FromArgb(240, 250, 204, 21));
    private static readonly Pen GridMinorPen = new(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
    private static readonly Pen GridMajorPen = new(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1);
    private static readonly Pen WorldXPen = new(new SolidColorBrush(Color.FromArgb(200, 0xE2, 0x4C, 0x3F)), 1.5);
    private static readonly Pen WorldYPen = new(new SolidColorBrush(Color.FromArgb(200, 0x5E, 0xC6, 0x5A)), 1.5);
    private static readonly Pen ObjectOutlinePen = new(new SolidColorBrush(Color.FromArgb(180, 0x9E, 0xB4, 0xFF)), 1, DashStyle.Dash);
    private static readonly Pen SelectedObjectPen = new(new SolidColorBrush(Color.FromArgb(230, 0xF9, 0xD7, 0x1C)), 1.5);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromArgb(200, 203, 213, 225));
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
    private DepthTuningFile? _depth;
    private FocusTuningFile? _focus;
    // 무대 뼈대(PresentataionUIRoot). 리그가 있으면 내장 뼈대를 쓴다 — 카메라·패럴랙스가 이 트리에 들어간다.
    private RigSchemaRigDto? _stage;
    // 씬 전체의 시작 상태(오브젝트마다 슬롯 접두사). 모션은 선택 오브젝트에만 얹는다.
    private RectNodeTree? _template;
    private readonly Dictionary<string, Bitmap?> _portraits = new();
    private string? _selectedObjectId;
    public bool ShowGrid { get; set; } = true;
    private Vec2 _stageSize = BaseResolutionFile.Default;
    private KeySelection _selection = new();
    private CurveFlags _flags = new();

    /// <summary>뮤트된 성분은 재생에서 뺀다(시작값 그대로). 그래프·도프시트와 같은 객체.</summary>
    public CurveFlags Flags
    {
        get => _flags;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_flags, value))
            {
                return;
            }

            _flags.Changed -= OnFlagsChanged;
            _flags = value;
            _flags.Changed += OnFlagsChanged;
            InvalidateVisual();
        }
    }

    private void OnFlagsChanged(object? sender, EventArgs e) => InvalidateVisual();

    private enum Drag { None, MoveFree, MoveX, MoveY, Rotate, ScaleFree, ScaleX, ScaleY }

    private Drag _drag = Drag.None;

    private const double GizmoArm = 60;
    private const double GizmoHit = 9;

    // 키로 아직 안 들어간 포즈(마야: 키 없이 바꾼 값). 시각이 바뀌면 버린다.
    private readonly Dictionary<string, RectNodeState> _overrides = new();
    private readonly Dictionary<(string Node, MotionChannelKind Kind), HashSet<Axis>> _pending = new();

    // 들고 있는 포즈(마야 MMB 스크럽). 시각이 바뀌어도 이것을 보여 주고, S가 이것을 키한다.
    private RectNodeTree? _held;
    private bool _holdScrub;
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
        _flags.Changed += OnFlagsChanged;
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

    // 보기 변환(네비게이션): 무대를 컨트롤에 맞춘 배율 × ViewZoom, 가운데에서 ViewPan만큼. 씬 데이터가 아니라 화면뿐이다.
    private double _viewZoom = 1;
    private Vector _viewPan;
    private bool _panning;
    private Point _panPointerStart;
    private Vector _panStart;

    public const double MinViewZoom = 0.2;
    public const double MaxViewZoom = 10;

    /// <summary>보기 배율 — 1 = 무대가 컨트롤에 맞는다(레터박스). 휠이 커서를 축으로 바꾼다.</summary>
    public double ViewZoom => _viewZoom;

    /// <summary>보기 이동(컨트롤 픽셀) — MMB 드래그.</summary>
    public Vector ViewPan => _viewPan;

    /// <summary>보기 맞춤(F) — 배율 1, 이동 0.</summary>
    public void ResetView()
    {
        _viewZoom = 1;
        _viewPan = default;
        InvalidateVisual();
    }

    /// <summary>커서 아래의 루트 점이 제자리에 있도록 배율을 바꾼다(그래프 편집기 휠과 같은 관습).</summary>
    public void ZoomViewAt(Point anchor, double factor)
    {
        double next = Math.Clamp(_viewZoom * factor, MinViewZoom, MaxViewZoom);

        if (Math.Abs(next - _viewZoom) < 1e-9)
        {
            return;
        }

        Vec2 under = ToRoot(anchor);
        _viewZoom = next;
        Point moved = ToControl(under);
        _viewPan = new Vector(_viewPan.X + anchor.X - moved.X, _viewPan.Y + anchor.Y - moved.Y);
        InvalidateVisual();
    }

    /// <summary>켜져 있으면 매니퓰레이터를 놓는 순간 키가 들어간다. 꺼져 있으면 S를 누를 때까지 "키 없는 포즈"다.</summary>
    public bool AutoKey { get; set; }

    /// <summary>새 키의 탄젠트 모드(설정).</summary>
    public TangentMode DefaultTangent { get; set; } = TangentMode.Auto;

    public bool SnapEnabled { get; set; } = true;

    public double SnapStep { get; set; } = TimeSnap.DefaultStep;

    public bool HasPending => _pending.Count > 0 || _held is not null;

    /// <summary>포즈를 들고 있는가(MMB 스크럽 뒤). S가 모든 채널에 그 포즈를 키한다.</summary>
    public bool IsHoldingPose => _held is not null;

    /// <summary>매니퓰레이터를 둘 수 있는 노드들 — 덤프의 노드 중 __root·키잉 금지 노드를 뺀 것.</summary>
    public IReadOnlyList<string> NodeChoices
    {
        get
        {
            if (_rig is null || SelectedObject is not { } o || !_rig.TryGetRig(o.Rig, out RigSchemaRigDto dto))
            {
                return Array.Empty<string>();
            }

            return dto.nodes.Select(n => n.id).Where(k => k != RigSchemaLoader.RootKey && !MotionRules.ForbiddenNodes.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
    }

    // ── 씬·오브젝트 ───────────────────────────────────────────────────

    /// <summary>뷰포트가 보여 주는 씬(문서의 활성 씬).</summary>
    public SceneAsset? Scene => _editor?.Document.ActiveSceneAsset;

    public IReadOnlyList<SceneObject> SceneObjects => Scene?.Objects ?? (IReadOnlyList<SceneObject>)Array.Empty<SceneObject>();

    /// <summary>선택 오브젝트 — 매니퓰레이터·모션 미리보기·초상 바꾸기의 대상.</summary>
    public string? SelectedObjectId
    {
        get => _selectedObjectId;
        set
        {
            if (_selectedObjectId == value)
            {
                return;
            }

            _selectedObjectId = value;
            DiscardPending();
            ObjectSelected?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public SceneObject? SelectedObject => _selectedObjectId is null ? null : Scene?.Find(_selectedObjectId);

    /// <summary>선택 오브젝트의 노드 키 접두사("c1/"). 오브젝트가 없으면 빈 문자열.</summary>
    private string Prefix => SelectedObject?.Prefix ?? "";

    /// <summary>선택 노드의 트리 키(접두사 포함).</summary>
    private string? SelectedKey => SelectedNode is null ? null : Prefix + SelectedNode;

    /// <summary>오브젝트를 골랐다.</summary>
    public event EventHandler? ObjectSelected;

    /// <summary>이 오브젝트에 지금 모션을 입혀 볼 수 있나(rigKind가 같거나 채널 노드가 전부 있으면).</summary>
    public bool CanWear(SceneObject o) => _rig is not null && Motion is { } motion && ScenePreview.CanWear(_rig, o, motion);

    /// <summary>오브젝트의 그림(초상) 사각형 — 루트 공간. 그림 노드가 없는 리그면 루트 rect. 트리가 없으면 null.</summary>
    public Quad? ObjectQuad(string objectId)
    {
        RectNodeTree? pose = PoseAt(_clock.Time);
        SceneObject? o = Scene?.Find(objectId);

        if (pose is null || o is null)
        {
            return null;
        }

        string? image = ScenePreview.ImageKeyOf(pose, o);
        return image is not null
            ? PortraitLayout.VisualQuad(pose, image, AspectOf(o))
            : pose.Contains(ScenePreview.RootKeyOf(o)) ? PortraitLayout.RectQuad(pose, ScenePreview.RootKeyOf(o)) : null;
    }

    private float AspectOf(SceneObject o)
        => _portraits.TryGetValue(o.Id, out Bitmap? bitmap) && bitmap is { PixelSize: { Width: > 0, Height: > 0 } }
            ? bitmap.PixelSize.Width / (float)bitmap.PixelSize.Height
            : PortraitLayout.PlaceholderAspect;

    private double SnapTime(double seconds) => SnapEnabled ? TimeSnap.Snap(seconds, SnapStep) : seconds;

    /// <summary>지금 보이는 포즈 — 시각의 포즈에 키 없는 포즈를 얹은 것.</summary>
    public RectNodeTree? CurrentPose()
    {
        if (_held is not null)
        {
            return _held;
        }

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

        if (pose is null || SelectedKey is not { } key || !pose.Contains(key))
        {
            return null;
        }

        return ToControl(pose.TransformPoint(key, Vec3.Zero).XY);
    }

    /// <summary>키 없는 포즈를 버린다(시각이 바뀌거나 Esc).</summary>
    public void DiscardPending()
    {
        if (_overrides.Count == 0 && _pending.Count == 0 && _held is null)
        {
            return;
        }

        _overrides.Clear();
        _pending.Clear();
        _held = null;
        _holdScrub = false;
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

        if (_held is not null)
        {
            KeyHeldPose(motion);
            return;
        }

        if (_pending.Count > 0)
        {
            // 키 없는 포즈 전부가 조작 하나 — 되돌리기 한 칸(성분·노드가 여럿이어도).
            _editor.Batch(MotionChangeKind.Keys, () =>
            {
                foreach (((string node, MotionChannelKind kind), HashSet<Axis> axes) in _pending.ToList())
                {
                    KeyNow(node, kind, axes.ToList());
                }
            });

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

    /// <summary>들고 있는 포즈를 모든 채널의 있는 성분에 한 조작으로 키(마야: MMB로 옮긴 뒤 S).</summary>
    private void KeyHeldPose(MotionAsset motion)
    {
        RectNodeTree? pose = _held;

        if (_editor is null || _template is null || pose is null)
        {
            DiscardPending();
            return;
        }

        var items = new List<(string, MotionChannelKind, IReadOnlyList<(Axis, float)>)>();

        foreach (ChannelAsset channel in motion.Channels)
        {
            string key = Prefix + channel.Node;

            if (!pose.Contains(key))
            {
                continue;
            }

            RectNodeState start = _template.GetState(key);
            RectNodeState posed = pose.GetState(key);
            var values = new List<(Axis, float)>();

            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is not null)
                {
                    values.Add((axis, PoseEdit.KeyValueFor(channel.Kind, motion.PreviewMode, PoseEdit.Component(channel.Kind, start, axis), PoseEdit.Component(channel.Kind, posed, axis))));
                }
            }

            items.Add((channel.Node, channel.Kind, values));
        }

        float time = (float)Math.Max(0, SnapTime(_clock.Time));
        DiscardPending();
        _editor.KeyPose(motion.Name, time, items, DefaultTangent);
        PoseKeyed?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// 마야 MMB 스크럽 — 지금 보이는 포즈를 든 채 시각만 옮긴다. 처음 부르면 포즈를 들고, 놓을 때 <see cref="EndHoldScrub"/>.
    /// 든 포즈는 S로 키하거나 Esc·보통 스크럽으로 버린다.
    /// </summary>
    public void HoldScrubTo(double seconds)
    {
        if (_held is null)
        {
            _held = CurrentPose();
            _holdScrub = _held is not null;
            PendingChanged?.Invoke(this, EventArgs.Empty);
        }

        if (_held is null)
        {
            ScrubTo(seconds);
            return;
        }

        Pause();
        double before = _clock.Time;
        _clock.ScrubTo(seconds);

        if (Math.Abs(before - _clock.Time) > 1e-12)
        {
            TimeChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }

    public void EndHoldScrub() => _holdScrub = false;

    /// <summary>지금 포즈의 값으로 키를 놓는다 — 시작값은 덤프(템플릿), 키 값은 모션의 재생 기준(relative/absolute)를 따른다.</summary>
    private void KeyNow(string node, MotionChannelKind kind, IReadOnlyList<Axis> axes)
    {
        MotionAsset? motion = Motion;
        RectNodeTree? pose = CurrentPose();

        string key = Prefix + node;

        if (_editor is null || motion is null || _template is null || pose is null || !pose.Contains(key) || axes.Count == 0)
        {
            return;
        }

        MotionChannelMode mode = motion.PreviewMode;
        RectNodeState start = _template.GetState(key);
        RectNodeState posed = pose.GetState(key);
        var values = new List<(Axis, float)>();

        foreach (Axis axis in axes)
        {
            values.Add((axis, PoseEdit.KeyValueFor(kind, mode, PoseEdit.Component(kind, start, axis), PoseEdit.Component(kind, posed, axis))));
        }

        _overrides.Remove(key);
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

    /// <summary>1프레임 단위 재생 — 보이는 시각이 정수 프레임이다. 꺼지면 연속.</summary>
    public bool PlayWholeFrames
    {
        get => _clock.WholeFrames;
        set
        {
            _clock.WholeFrames = value;
            RangeChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>선택 오브젝트의 초상 종횡비(없으면 자리표시).</summary>
    public float Aspect => SelectedObject is { } o ? AspectOf(o) : PortraitLayout.PlaceholderAspect;

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
        _held = null;
        _holdScrub = false;
        SyncDuration();
        PickSelectedObject();
        RebuildTemplate();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
        TimeChanged?.Invoke(this, EventArgs.Empty);
        PendingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void SetRig(RigSchemaFile? rig)
    {
        _rig = rig;
        _stage = rig is null ? null : BuiltinTuning.StageRig;
        PickSelectedObject();
        RebuildTemplate();
        InvalidateVisual();
    }

    /// <summary>카메라가 트리(뼈대의 Zoom/Pan 노드)에 들어가 있나. 뼈대가 없으면 보기 변환으로 입힌다(같은 식).</summary>
    public bool CameraInTree => _stage is not null;

    /// <summary>presets/depth.json — 레이어를 깊이(DepthY·DepthScale)로. null이면 레이어는 그리는 순서만.</summary>
    public void SetDepth(DepthTuningFile? depth)
    {
        _depth = depth;
        RebuildTemplate();
        InvalidateVisual();
    }

    /// <summary>presets/focus.json — focus 오프셋(base + bust)이 깊이·place 폴드에 들어간다.</summary>
    public void SetFocus(FocusTuningFile? focus)
    {
        _focus = focus;
        RebuildTemplate();
        InvalidateVisual();
    }

    /// <summary>씬 카메라(보기 전용) 배율 — 코어 규약 zoom 1당 +0.05.</summary>
    public float CameraScale => ShotIntentMath.EvaluateCameraScale(Scene?.Camera.Zoom ?? 0f);

    private Vec2 CameraPan => Scene is { } scene ? new Vec2(scene.Camera.PanX, scene.Camera.PanY) : Vec2.Zero;

    /// <summary>루트 공간 → 컨트롤 좌표(카메라 적용). 테스트가 쓴다.</summary>
    public Point ToScreen(Vec2 root) => ToControl(root);

    /// <summary>선택 오브젝트의 초상 비트맵(호환용). null이면 자리표시 사각형.</summary>
    public void SetPortrait(Bitmap? portrait)
    {
        if (SelectedObject is { } o)
        {
            SetPortrait(o.Id, portrait);
        }
    }

    /// <summary>오브젝트의 초상 비트맵. 종횡비는 비트맵에서 온다.</summary>
    public void SetPortrait(string objectId, Bitmap? portrait)
    {
        ArgumentNullException.ThrowIfNull(objectId);
        _portraits[objectId] = portrait;
        RebuildTemplate();
        InvalidateVisual();
    }

    public void ClearPortraits()
    {
        _portraits.Clear();
        RebuildTemplate();
        InvalidateVisual();
    }

    private void OnEditorChanged(object? sender, MotionChangedEventArgs e)
    {
        SyncDuration();

        if (e.Kind != MotionChangeKind.Keys)
        {
            PickSelectedObject();
            RebuildTemplate();
        }

        InvalidateVisual();
    }

    /// <summary>선택 오브젝트가 없거나 씬에서 사라졌으면 — 모션을 입을 수 있는 첫 오브젝트, 없으면 첫 오브젝트.</summary>
    private void PickSelectedObject()
    {
        SceneAsset? scene = Scene;

        if (scene is null || scene.Objects.Count == 0)
        {
            SelectedObjectId = null;
            return;
        }

        if (_selectedObjectId is not null && scene.Find(_selectedObjectId) is not null)
        {
            return;
        }

        SceneObject pick = scene.Objects.FirstOrDefault(CanWear) ?? scene.Objects[0];
        SelectedObjectId = pick.Id;
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
        SceneAsset? scene = Scene;

        if (_rig is null || scene is null)
        {
            return;
        }

        _template = ScenePreview.Build(_rig, scene, AspectOf, null, _depth, _focus, _stage);
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

    /// <summary>거꾸로 재생(마야 ◀).</summary>
    public void PlayBackward()
    {
        _clock.PlayBackward();
        _stopwatch.Restart();
        _timer.Start();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>처음부터 재생(Alt+`) — 범위 시작으로 간 뒤 재생. 이미 재생 중이면 처음으로 되감는다.</summary>
    public void PlayFromStart()
    {
        Stop();
        Play();
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
            // 포즈 들고 스크럽 중(MMB)이면 든 포즈를 지키고, 보통 스크럽은 키 없는 포즈를 버린다(마야).
            if (!_holdScrub)
            {
                DiscardPending();
            }

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
        if (_template is null)
        {
            return null;
        }

        return motion is not null && SelectedObject is { } o && CanWear(o)
            ? ScenePreview.Pose(_template, o, motion, seconds, _flags.IsMuted)
            : _template.Clone();
    }

    /// <summary>지금 시각의 초상 사각형(루트 공간). 리그가 없으면 null.</summary>
    public Quad? CurrentPortraitQuad()
    {
        return SelectedObject is { } o ? ObjectQuad(o.Id) : null;
    }

    // ── 좌표 ──────────────────────────────────────────────────────────

    /// <summary>무대가 놓이는 컨트롤 영역(레터박스).</summary>
    public Rect StageRect
    {
        get
        {
            double availableWidth = Math.Max(1, Bounds.Width - Pad * 2);
            double availableHeight = Math.Max(1, Bounds.Height - Pad * 2);
            double scale = Math.Min(availableWidth / _stageSize.X, availableHeight / _stageSize.Y) * _viewZoom;
            double width = _stageSize.X * scale;
            double height = _stageSize.Y * scale;
            double centerX = Bounds.Width / 2 + _viewPan.X;
            double centerY = Bounds.Height / 2 + _viewPan.Y;
            return new Rect(centerX - width / 2, centerY - height / 2, width, height);
        }
    }

    // 카메라(샷): 보이는 좌표 = 루트 × 배율 + pan (코어 ShotIntentMath와 같은 규약). 보기만 바뀐다.
    private Point ToControl(Vec2 root)
    {
        Rect stage = StageRect;
        Vec2 shot = CameraInTree ? root : root * CameraScale + CameraPan;
        Vec2 canvas = PortraitLayout.ToCanvas(shot, _stageSize);
        double scale = stage.Width / _stageSize.X;
        return new Point(stage.X + canvas.X * scale, stage.Y + canvas.Y * scale);
    }

    /// <summary>컨트롤 좌표 → 루트 공간(가운데 원점·y 위). 카메라를 벗긴다.</summary>
    public Vec2 ToRoot(Point control)
    {
        Rect stage = StageRect;
        double scale = stage.Width / _stageSize.X;
        double canvasX = (control.X - stage.X) / scale;
        double canvasY = (control.Y - stage.Y) / scale;
        var shot = new Vec2((float)(canvasX - _stageSize.X / 2), (float)(_stageSize.Y / 2 - canvasY));
        return CameraInTree ? shot : (shot - CameraPan) * (1f / CameraScale);
    }

    // ── 그리기 ────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));
        Rect stage = StageRect;
        context.FillRectangle(StageBrush, stage);
        context.DrawRectangle(StagePen, stage);

        MotionAsset? motion = Motion;

        using (context.PushClip(stage))
        {
            if (ShowGrid)
            {
                DrawWorld(context, stage);
            }

            if (_template is null)
            {
                DrawLabel(context, _rig is null ? "리그 덤프(rig-schemas.json)를 실으면 오브젝트가 선다." : "씬에 오브젝트가 없다 — 왼쪽 '씬'에서 추가한다.", new Point(stage.X + 10, stage.Y + 10));
                return;
            }

            RectNodeTree pose = CurrentPose() ?? _template;
            DrawObjects(context, pose);
            DrawAxes(context, pose);
            DrawGizmo(context, pose);
        }

        string objectLabel = SelectedObject is { } so ? $"{so.Id} ({so.Rig} · {so.Layer})" : "오브젝트 없음";
        string motionLabel = motion is null
            ? "모션 없음"
            : $"{motion.Name} · {(motion.End == MotionEnd.Restore ? "restore — 끝에서 시작 상태로" : "commit — 마지막 키가 남는다")}"
              + (SelectedObject is { } wearer && !CanWear(wearer) ? " · 이 오브젝트의 리그에는 안 맞는다" : "");
        DrawLabel(context, $"{objectLabel} · {motionLabel}", new Point(stage.X + 8, stage.Y + 6));
    }

    /// <summary>월드 축·격자·무대 프레임 — 루트 공간(가운데 원점·y 위, px). 가는 선 1u(기준 폭/48), 굵은 선 5u.</summary>
    private void DrawWorld(DrawingContext context, Rect stage)
    {
        double unit = _stageSize.X / 48.0;
        double scale = stage.Width / _stageSize.X;
        double unitPx = unit * scale;

        if (unitPx >= 4)
        {
            Point origin = ToControl(Vec2.Zero);
            int countX = (int)Math.Ceiling(_stageSize.X / 2 / unit) + 1;
            int countY = (int)Math.Ceiling(_stageSize.Y / 2 / unit) + 1;

            for (int i = -countX; i <= countX; i++)
            {
                double x = origin.X + i * unitPx;
                context.DrawLine(i % 5 == 0 ? GridMajorPen : GridMinorPen, new Point(x, stage.Y), new Point(x, stage.Bottom));
            }

            for (int j = -countY; j <= countY; j++)
            {
                double y = origin.Y - j * unitPx;
                context.DrawLine(j % 5 == 0 ? GridMajorPen : GridMinorPen, new Point(stage.X, y), new Point(stage.Right, y));
            }

            context.DrawLine(WorldXPen, new Point(stage.X, origin.Y), new Point(stage.Right, origin.Y));
            context.DrawLine(WorldYPen, new Point(origin.X, stage.Y), new Point(origin.X, stage.Bottom));
            context.DrawEllipse(null, new Pen(PivotBrush, 1), origin, 5, 5);
            DrawLabel(context, $"0,0 · 1u = {unit:0}px", new Point(origin.X + 6, origin.Y + 4));
        }

        context.DrawRectangle(StagePen, stage);
    }

    private void DrawObjects(DrawingContext context, RectNodeTree pose)
    {
        SceneAsset? scene = Scene;

        if (scene is null)
        {
            return;
        }

        foreach (SceneObject o in scene.DrawOrder())
        {
            if (!o.Visible)
            {
                continue;
            }

            DrawObject(context, pose, o, selected: o.Id == _selectedObjectId);
        }
    }

    private void DrawObject(DrawingContext context, RectNodeTree pose, SceneObject o, bool selected)
    {
        string? imageKey = ScenePreview.ImageKeyOf(pose, o);

        if (imageKey is null)
        {
            // 그림 노드가 없는 리그(배경 등): 루트 rect 윤곽과 이름.
            string rootKey = ScenePreview.RootKeyOf(o);

            if (!pose.Contains(rootKey))
            {
                return;
            }

            Quad root = PortraitLayout.RectQuad(pose, rootKey);
            Point a = ToControl(root.BottomLeft);
            Point b = ToControl(root.BottomRight);
            Point c = ToControl(root.TopRight);
            Point d = ToControl(root.TopLeft);
            context.DrawGeometry(null, selected ? SelectedObjectPen : ObjectOutlinePen, new PolylineGeometry(new[] { a, b, c, d, a }, true));
            DrawLabel(context, $"{o.Id} ({o.Rig})", new Point(Math.Min(a.X, d.X) + 4, Math.Min(c.Y, d.Y) + 4));
            return;
        }

        Quad quad = PortraitLayout.VisualQuad(pose, imageKey, AspectOf(o));
        Point topLeft = ToControl(quad.TopLeft);
        Point topRight = ToControl(quad.TopRight);
        Point bottomLeft = ToControl(quad.BottomLeft);
        Point bottomRight = ToControl(quad.BottomRight);
        _portraits.TryGetValue(o.Id, out Bitmap? portrait);

        if (portrait is null)
        {
            var outline = new PolylineGeometry(new[] { topLeft, topRight, bottomRight, bottomLeft, topLeft }, true);
            context.DrawGeometry(PlaceholderFill, PlaceholderPen, outline);
            context.DrawLine(PlaceholderPen, topLeft, bottomRight);
            context.DrawLine(PlaceholderPen, topRight, bottomLeft);
        }
        else
        {
            // 이미지 로컬 (0,0)-(w,h) → 화면 사각형 네 점. 직교 투영 아래서 평면 사각형의 변환은 아핀이라 세 점이면 정확하다.
            double w = portrait.PixelSize.Width;
            double h = portrait.PixelSize.Height;
            var matrix = new Matrix(
                (topRight.X - topLeft.X) / w, (topRight.Y - topLeft.Y) / w,
                (bottomLeft.X - topLeft.X) / h, (bottomLeft.Y - topLeft.Y) / h,
                topLeft.X, topLeft.Y);

            using (context.PushTransform(matrix))
            {
                context.DrawImage(portrait, new Rect(0, 0, w, h));
            }
        }

        if (selected)
        {
            context.DrawGeometry(null, SelectedObjectPen, new PolylineGeometry(new[] { topLeft, topRight, bottomRight, bottomLeft, topLeft }, true));
        }

        DrawLabel(context, o.Id, new Point(Math.Min(topLeft.X, bottomLeft.X) + 4, Math.Min(topLeft.Y, topRight.Y) + 4));
    }

    private void DrawAxes(DrawingContext context, RectNodeTree pose)
    {
        if (SelectedKey is not { } key || !pose.Contains(key))
        {
            return;
        }

        (Vec2 origin, Vec2 xEnd, Vec2 yEnd) = PortraitLayout.Axes(pose, key, 80f);
        Point o = ToControl(origin);
        context.DrawLine(XAxisPen, o, ToControl(xEnd));
        context.DrawLine(YAxisPen, o, ToControl(yEnd));
        context.DrawEllipse(PivotBrush, null, o, 4, 4);
        DrawLabel(context, key, new Point(o.X + 6, o.Y + 6));
    }

    private void DrawGizmo(DrawingContext context, RectNodeTree pose)
    {
        if (SelectedKey is not { } gizmoKey || !pose.Contains(gizmoKey) || Manipulator == ManipulatorMode.Select)
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
        (Vec2 origin, Vec2 xAxis, Vec2 yAxis) = PortraitLayout.Axes(pose, SelectedKey!, 100f);
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

        // MMB·RMB 드래그 = 보기 이동(팬). 씬·키는 안 건드린다. (소유자: 그래프와 같이 둘 다)
        PointerPointProperties pressed = e.GetCurrentPoint(this).Properties;

        if (pressed.IsMiddleButtonPressed || pressed.IsRightButtonPressed)
        {
            _panning = true;
            _panPointerStart = position;
            _panStart = _viewPan;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!pressed.IsLeftButtonPressed)
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

        if (TryPickObject(position, out string picked))
        {
            SelectedObjectId = picked;
            e.Handled = true;
        }
    }

    /// <summary>클릭 자리의 오브젝트(앞 레이어 우선). 초상 사각형 안이면 맞은 것.</summary>
    private bool TryPickObject(Point position, out string objectId)
    {
        objectId = "";
        RectNodeTree? pose = CurrentPose();
        SceneAsset? scene = Scene;

        if (pose is null || scene is null)
        {
            return false;
        }

        var at = new Pixel(position.X, position.Y);

        foreach (SceneObject o in scene.DrawOrder().Reverse())
        {
            if (!o.Visible)
            {
                continue;
            }

            if (ObjectQuad(o.Id) is not { } quad)
            {
                continue;
            }

            var polygon = new List<Pixel>();

            foreach (Vec2 corner in new[] { quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft })
            {
                Point p = ToControl(corner);
                polygon.Add(new Pixel(p.X, p.Y));
            }

            if (GraphHitTest.Contains(polygon, at))
            {
                objectId = o.Id;
                return true;
            }
        }

        return false;
    }

    private bool TryStartManipulation(Point position)
    {
        RectNodeTree? pose = CurrentPose();

        if (Manipulator == ManipulatorMode.Select || SelectedKey is not { } key || pose is null || !pose.Contains(key) || !StageRect.Contains(position))
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
        _dragPivotRoot = pose.TransformPoint(key, Vec3.Zero).XY;
        _dragStartState = pose.GetState(key);
        return true;
    }

    private void DragManipulation(Point position)
    {
        RectNodeTree? pose = CurrentPose();

        if (SelectedKey is not { } key || pose is null || !pose.Contains(key))
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
                Vec2 delta = PoseEdit.RootDeltaToLocal(pose, key, _dragStartRoot, now);
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

        _overrides[key] = next;
        InvalidateVisual();
    }

    /// <summary>놓는 순간: Auto Key거나 그 시각에 이미 키가 있으면 키로, 아니면 키 없는 포즈로 남긴다.</summary>
    private void EndManipulation()
    {
        MotionAsset? motion = Motion;
        string? node = SelectedNode;

        if (motion is null || node is null || !_overrides.TryGetValue(Prefix + node, out RectNodeState posed))
        {
            return;
        }

        (MotionChannelKind kind, List<Axis> axes) = ChangedAxes(_dragStartState, posed);

        if (axes.Count == 0)
        {
            _overrides.Remove(Prefix + node);
            InvalidateVisual();
            return;
        }

        CommitPose(motion, node, kind, axes);
    }

    /// <summary>바뀐 포즈의 처리: Auto Key거나 그 시각에 이미 키가 있으면 키로, 아니면 키 없는 포즈로 남긴다.</summary>
    private void CommitPose(MotionAsset motion, string node, MotionChannelKind kind, IReadOnlyList<Axis> axes)
    {
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

    // ── 채널 박스(마야) ───────────────────────────────────────────────

    /// <summary>선택 오브젝트의 노드 성분의 지금 포즈 값(키 없는 포즈 포함). 노드·리그가 없으면 null.</summary>
    public float? ComponentValue(string node, MotionChannelKind kind, Axis axis)
    {
        RectNodeTree? pose = CurrentPose();
        string key = Prefix + node;

        if (pose is null || !pose.Contains(key))
        {
            return null;
        }

        return PoseEdit.Component(kind, pose.GetState(key), axis);
    }

    /// <summary>이 성분이 키 없는 포즈로 남아 있는가(채널 박스가 주황으로 보인다).</summary>
    public bool IsPending(string node, MotionChannelKind kind, Axis axis)
        => _pending.TryGetValue((node, kind), out HashSet<Axis>? axes) && axes.Contains(axis);

    /// <summary>
    /// 채널 박스 입력 — 노드 성분 하나를 이 값으로. 매니퓰레이터를 놓은 것과 같다:
    /// Auto Key거나 그 시각에 키가 있으면 키로, 아니면 키 없는 포즈(S로 키·Esc로 버림). 값이 같으면 아무것도 안 한다.
    /// </summary>
    public void SetComponent(string node, MotionChannelKind kind, Axis axis, float value)
        => SetComponents(node, kind, new[] { (axis, value) });

    /// <summary>여러 성분을 한 번에(깊이 컨트롤러의 X·Y 배율처럼). 바뀐 성분만 키·키 없는 포즈가 된다.</summary>
    public void SetComponents(string node, MotionChannelKind kind, IReadOnlyList<(Axis Axis, float Value)> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        MotionAsset? motion = Motion;
        RectNodeTree? pose = CurrentPose();
        string key = Prefix + node;

        if (motion is null || pose is null || !pose.Contains(key) || MotionRules.ForbiddenNodes.Contains(node))
        {
            return;
        }

        RectNodeState state = pose.GetState(key);
        var changed = new List<Axis>();

        foreach ((Axis axis, float value) in values)
        {
            if (Math.Abs(PoseEdit.Component(kind, state, axis) - value) < 1e-6f)
            {
                continue;
            }

            state = PoseEdit.WithComponent(kind, state, axis, value);
            changed.Add(axis);
        }

        if (changed.Count == 0)
        {
            return;
        }

        _overrides[key] = state;
        InvalidateVisual();
        CommitPose(motion, node, kind, changed);
    }

    /// <summary>채널 박스의 ● — 성분 하나를 지금 포즈 값으로 지금 키(마야 Key Selected). 없는 채널·성분은 생긴다.</summary>
    public void KeyComponent(string node, MotionChannelKind kind, Axis axis) => KeyComponents(node, kind, new[] { axis });

    public void KeyComponents(string node, MotionChannelKind kind, IReadOnlyList<Axis> axes)
    {
        if (MotionRules.ForbiddenNodes.Contains(node) || axes.Count == 0)
        {
            return;
        }

        KeyNow(node, kind, axes);
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

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_panning)
        {
            Point now = e.GetPosition(this);
            _viewPan = new Vector(_panStart.X + now.X - _panPointerStart.X, _panStart.Y + now.Y - _panPointerStart.Y);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        switch (_drag)
        {
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

        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

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

    /// <summary>휠 = 커서를 축으로 확대·축소(한 칸 = ×1.15).</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomViewAt(e.GetPosition(this), Math.Pow(1.15, e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.F when e.KeyModifiers == KeyModifiers.None:
                ResetView();
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
