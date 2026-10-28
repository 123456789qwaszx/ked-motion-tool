using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;

namespace Mt.App.Views;

public enum GraphTool
{
    Select,
    Move,
    Scale,

    /// <summary>마야 Region Tool — 상자로 고르고, 상자 안을 끌면 이동, 모서리를 끌면 그 축만 반대쪽 기준 스케일.</summary>
    Region,
}

/// <summary>그래프에 올라가는 성분 커브 하나의 표시 상태. 색은 축(x·y·z)이 정하고 채널 순서가 색조를 조금 돌린다.</summary>
public sealed class CurveSlot
{
    public CurveSlot(CurveRef curve, Color color)
    {
        Curve = curve;
        Color = color;
    }

    public CurveRef Curve { get; }

    public Color Color { get; }

    public bool Visible { get; set; } = true;
}

/// <summary>
/// 그래프 에디터 — 마야 그래프 에디터의 감각으로 모션의 키를 만진다 (VnTool <c>EaseCurveEditor</c>에서 자람).
///
/// - 좌표는 초 × 값. 팬(가운데 버튼·Alt+드래그) · 줌(휠, Ctrl=시간만·Shift=값만) · F(선택 맞춤) · A(전체 맞춤)
/// - 키: 클릭 선택 · Shift 추가 · 박스 선택 · 드래그(Shift=축 잠금) · 더블클릭=그 커브 위에 키 · I=현재 시각에 키 · Delete
/// - 탄젠트: 선택 키의 핸들 드래그(끊긴 키는 한쪽만) · 모드는 인스펙터/단축(1 auto · 2 flat · 3 linear · 4 free)
/// - 시간 스냅 1/60초(끌 수 있다). 값 스냅 없음.
///
/// <b>드래그 중간값은 렌더만이고, 놓는 순간이 편집 하나다</b>(되돌리기 한 번 = 조작 하나). 문서에 닿는 길은
/// <see cref="MotionEditor"/>뿐이다. 좌표 변환·히트 테스트·탄젠트 산수는 전부 <c>Mt.Authoring.Graph</c>의 순수 함수다 —
/// 여기에는 그리기와 입력만 있다.
/// </summary>
public sealed class GraphEditorView : Control
{
    /// <summary>값 라벨 칸(왼쪽). 마야처럼 값은 왼쪽, 프레임은 위쪽 눈금자다.</summary>
    public const double PadLeft = 46;
    private const double PadRight = 6;
    /// <summary>위 눈금자(프레임 숫자 + 현재 시각 마커). 여기를 누르면 스크럽이다.</summary>
    public const double PadTop = 18;
    private const double PadBottom = 4;
    private const double DragThreshold = 3;

    // 마야 그래프 에디터 팔레트: 회색 패널, 재생 범위 안은 조금 밝게, 격자는 어둡게, 현재 시각은 노랑, 키는 주황·선택은 흰색.
    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
    private static readonly IBrush PlotBackground = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D));
    private static readonly IBrush RangeBackground = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A));
    private static readonly IBrush RulerBackground = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromRgb(0x2F, 0x2F, 0x2F)), 1);
    private static readonly Pen MinorGridPen = new(new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)), 1);
    private static readonly Pen AxisPen = new(new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)), 1);
    private static readonly Pen DurationPen = new(new SolidColorBrush(Color.FromArgb(150, 0xF0, 0xA0, 0x50)), 1, DashStyle.Dash);
    private static readonly Pen CurrentTimePen = new(new SolidColorBrush(Color.FromRgb(0xF9, 0xD7, 0x1C)), 1);
    private static readonly IBrush CurrentTimeBrush = new SolidColorBrush(Color.FromRgb(0xF9, 0xD7, 0x1C));
    private static readonly Pen BoxPen = new(new SolidColorBrush(Color.FromArgb(220, 0xE8, 0xE8, 0xE8)), 1, DashStyle.Dash);
    private static readonly IBrush BoxFill = new SolidColorBrush(Color.FromArgb(24, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly IBrush KeyBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xA3, 0x3A));
    private static readonly IBrush SelectedKeyBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Pen HandlePen = new(new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC)), 1);
    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
    private static readonly IBrush ValueLabelBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
    private static readonly Pen BufferPen = new(new SolidColorBrush(Color.FromArgb(150, 0x9A, 0x9A, 0x9A)), 1.5, DashStyle.Dash);
    private static readonly IBrush PivotBrush = new SolidColorBrush(Color.FromRgb(0xF9, 0xD7, 0x1C));
    private static readonly IBrush SelectedCurveBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));

    private readonly List<CurveSlot> _curves = new();
    private KeySelection _selection = new();

    private MotionEditor? _editor;
    private string? _motionName;
    private GraphCamera _camera = new(-0.1, -1, 400, 100);
    private bool _cameraFitted;
    private double _currentTime;

    private enum Drag { None, Keys, KeysAxis, CurveInsert, InHandle, OutHandle, Handles, Box, Pan, Scrub, Zoom, Scale, Lasso, RegionLeft, RegionRight, RegionTop, RegionBottom }

    /// <summary>선택한 커브(마야: 커브 선을 클릭). MMB 클릭이 여기에 키를 끼운다.</summary>
    private readonly HashSet<CurveRef> _selectedCurves = new();
    private bool _axisLockDecided;
    private bool _axisLockHorizontal;

    /// <summary>리전 툴의 상자 — 선택 키의 시간·보기값 범위.</summary>
    private (double T0, double T1, double V0, double V1)? _region;
    private double _regionEdge;

    /// <summary>마야 그래프 에디터의 툴: 선택(Q)·이동(W)·스케일(R). MMB 드래그는 툴과 무관하게 이동이다.</summary>
    public GraphTool Tool { get; set; } = GraphTool.Select;

    private readonly List<Point> _lasso = new();
    private GraphPoint _scalePivot;

    private readonly Dictionary<CurveRef, List<KeyAsset>> _buffer = new();

    /// <summary>복사/붙여넣기 자리 — 도프시트와 같은 객체를 꽂는다.</summary>
    public KeyClipboardSlot Clipboard { get; set; } = new();

    /// <summary>길이로 감아 반복한 곡선을 양옆에 흐리게(마야 cycle infinity). 보기만이다. 켜면 앞뒤 무한이 cycle이다.</summary>
    public bool ShowCycle { get; set; }

    /// <summary>마야 Pre Infinity(첫 키 앞) — 보기만.</summary>
    public InfinityMode PreInfinity { get; set; } = InfinityMode.Constant;

    /// <summary>마야 Post Infinity(마지막 키 뒤) — 보기만.</summary>
    public InfinityMode PostInfinity { get; set; } = InfinityMode.Constant;

    /// <summary>마야 값 스냅 — 끌 때 키 값을 정수로.</summary>
    public bool ValueSnapEnabled { get; set; }

    /// <summary>선택한 커브들.</summary>
    public IReadOnlyCollection<CurveRef> SelectedCurves => _selectedCurves;

    /// <summary>고른 탄젠트 핸들(마야: 키를 고른 채 핸들 끝을 박스로 고른다) — MMB 드래그가 이 핸들들을 돌린다. 키 선택이 바뀌면 비운다.</summary>
    public IReadOnlyCollection<(KeyRef Key, bool Inward)> SelectedHandles => _selectedHandles;

    /// <summary>커브 선을 고른다(마야). additive가 아니면 다른 커브 선택은 풀린다.</summary>
    public void SelectCurve(CurveRef curve, bool additive)
    {
        if (!additive)
        {
            _selectedCurves.Clear();
        }

        if (!_selectedCurves.Add(curve) && additive)
        {
            _selectedCurves.Remove(curve);
        }

        InvalidateVisual();
    }

    public void ClearCurveSelection()
    {
        _selectedCurves.Clear();
        InvalidateVisual();
    }

    /// <summary>선택한 커브마다 그 시각(스냅)에 곡선 위 값으로 키 하나(한 조작). 마야 Insert Key(MMB 클릭).</summary>
    public List<KeyRef> InsertKeysOnSelectedCurves(double seconds)
    {
        MotionAsset? motion = Motion;
        var inserted = new List<KeyRef>();

        if (motion is null || _editor is null || _motionName is null || _selectedCurves.Count == 0)
        {
            return inserted;
        }

        float t = (float)Math.Max(0, SnapTime(seconds));
        var edits = new List<(CurveRef, IEnumerable<KeyAsset>)>();
        var targets = new List<(CurveRef, float)>();

        foreach (CurveRef curve in _selectedCurves)
        {
            if (_flags.IsLocked(curve) || curve.Resolve(motion) is not { } component)
            {
                continue;
            }

            edits.Add((curve, KeyOps.WithKeyOnCurve(component.Keys, t)));
            targets.Add((curve, t));
        }

        if (edits.Count == 0)
        {
            return inserted;
        }

        _editor.SetKeysMany(_motionName, edits);
        SelectByTime(targets);
        return _selection.ToList();
    }

    /// <summary>리전 툴의 상자(시간·보기값). 선택이 없으면 null.</summary>
    public (double T0, double T1, double V0, double V1)? Region => _region;

    public bool HasBuffer => _buffer.Count > 0;

    /// <summary>버퍼 커브 — 보이는 커브의 지금 키를 찍어 둔다(마야 Buffer Curve Snapshot). 흐린 점선으로 남는다.</summary>
    public void SnapshotBuffer()
    {
        _buffer.Clear();

        if (Motion is { } motion)
        {
            foreach (CurveSlot slot in _curves)
            {
                if (slot.Visible && slot.Curve.Resolve(motion) is { } curve)
                {
                    _buffer[slot.Curve] = curve.Keys.Select(k => new KeyAsset
                    {
                        T = k.T, V = k.V, InTangent = k.InTangent, OutTangent = k.OutTangent, Tangent = k.Tangent, Broken = k.Broken,
                    }).ToList();
                }
            }
        }

        InvalidateVisual();
    }

    /// <summary>버퍼와 지금 커브를 바꾼다(마야 Swap Buffer Curve). 한 번의 조작.</summary>
    public void SwapBuffer()
    {
        MotionAsset? motion = Motion;

        if (_editor is null || _motionName is null || motion is null || _buffer.Count == 0)
        {
            return;
        }

        var edits = new List<(CurveRef, IEnumerable<KeyAsset>)>();
        var next = new Dictionary<CurveRef, List<KeyAsset>>();

        foreach ((CurveRef curveRef, List<KeyAsset> buffered) in _buffer)
        {
            if (curveRef.Resolve(motion) is not { } current)
            {
                continue;
            }

            next[curveRef] = current.Keys.Select(k => new KeyAsset
            {
                T = k.T, V = k.V, InTangent = k.InTangent, OutTangent = k.OutTangent, Tangent = k.Tangent, Broken = k.Broken,
            }).ToList();
            edits.Add((curveRef, buffered));
        }

        if (edits.Count == 0)
        {
            return;
        }

        _editor.SetKeysMany(_motionName, edits);
        _buffer.Clear();

        foreach ((CurveRef curveRef, List<KeyAsset> keys) in next)
        {
            _buffer[curveRef] = keys;
        }

        InvalidateVisual();
    }

    public void ClearBuffer()
    {
        _buffer.Clear();
        InvalidateVisual();
    }

    public void CopySelection()
    {
        if (Motion is { } motion && _selection.Count > 0)
        {
            Clipboard.Current = KeyClipboard.Copy(motion, _selection) ?? Clipboard.Current;
        }
    }

    public void CutSelection()
    {
        if (Motion is { } motion && _selection.Count > 0 && KeyClipboard.Copy(motion, _selection) is { } copied)
        {
            Clipboard.Current = copied;
            DeleteSelection();
        }
    }

    /// <summary>
    /// 현재 시각에 붙인다(merge). 붙은 키가 선택된다.
    /// 붙일 커브(마야 paste to selected channels): 고른 커브 선이 있으면 그것, 없고 복사한 커브가 하나도 안 보이면(아웃라이너에서 다른 커브만
    /// 띄운 상태) 보이는 커브들, 그 밖에는 복사한 커브 그대로. 그래서 scale X의 키를 복사해 scale Y만 띄우고 붙이면 Y에 들어간다.
    /// </summary>
    public void Paste()
    {
        if (_editor is null || _motionName is null || Clipboard.Current is not { } clipboard || clipboard.Count == 0)
        {
            return;
        }

        float at = (float)SnapTime(CurrentTime);
        List<KeyRef> pasted = _editor.PasteKeys(_motionName, clipboard, at, SnapEnabled ? t => (float)TimeSnap.Snap(t, SnapStep) : null, PasteTargets(clipboard));
        _selection.Set(pasted);
    }

    private List<CurveRef>? PasteTargets(KeyClipboard clipboard)
    {
        if (_selectedCurves.Count > 0)
        {
            return _curves.Where(s => _selectedCurves.Contains(s.Curve)).Select(s => s.Curve).ToList();
        }

        List<CurveRef> visible = _curves.Where(s => s.Visible).Select(s => s.Curve).ToList();
        bool sourceVisible = clipboard.Items.Any(i => visible.Contains(i.Curve));
        return visible.Count > 0 && !sourceVisible ? visible : null;
    }
    private CurveFlags _flags = new();

    /// <summary>잠금·뮤트. 도프시트·뷰포트와 같은 객체를 꽂는다.</summary>
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

    private void OnFlagsChanged(object? sender, EventArgs e)
    {
        _selection.RemoveWhere(k => _flags.IsLocked(k.Curve));
        InvalidateVisual();
    }

    private Point ScreenOf(CurveRef curve, double t, double v)
        => ToScreen(new GraphPoint(t, v));

    private Drag _drag = Drag.None;
    private Point _dragStart;
    private Point _dragNow;
    private bool _dragMoved;
    private KeyRef _handleKey;
    private bool _boxShift;
    private readonly HashSet<(KeyRef Key, bool Inward)> _selectedHandles = new();
    private readonly List<((KeyRef Key, bool Inward) Handle, Pixel End)> _handleDrag = new();
    private GraphCamera _panStart;
    private readonly Dictionary<CurveRef, List<KeyAsset>> _preview = new();
    private readonly Dictionary<KeyRef, KeyAsset> _previewKeys = new();

    public GraphEditorView()
    {
        MinWidth = 200;
        MinHeight = 120;
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        _selection.Changed += OnSelectionChanged;
        _flags.Changed += OnFlagsChanged;
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        _selectedHandles.Clear();
        UpdateRegion();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>리전 상자 = 선택 키의 (시간, 보기값) 범위. 점이면 조금 벌린다.</summary>
    private void UpdateRegion()
    {
        MotionAsset? motion = Motion;

        if (motion is null || _selection.Count == 0)
        {
            _region = null;
            return;
        }

        double t0 = double.PositiveInfinity, t1 = double.NegativeInfinity, v0 = double.PositiveInfinity, v1 = double.NegativeInfinity;

        foreach (KeyRef key in _selection)
        {
            if (key.Resolve(motion) is not { } k)
            {
                continue;
            }

            double v = k.V;
            t0 = Math.Min(t0, k.T);
            t1 = Math.Max(t1, k.T);
            v0 = Math.Min(v0, v);
            v1 = Math.Max(v1, v);
        }

        if (double.IsInfinity(t0))
        {
            _region = null;
            return;
        }

        _region = (t0, t1, v0, v1);
    }

    private Rect RegionScreenRect((double T0, double T1, double V0, double V1) region)
    {
        Point a = ToScreen(new GraphPoint(region.T0, region.V1));
        Point b = ToScreen(new GraphPoint(region.T1, region.V0));
        const double pad = 8;
        return new Rect(a.X - pad, a.Y - pad, Math.Max(1, b.X - a.X) + pad * 2, Math.Max(1, b.Y - a.Y) + pad * 2);
    }

    public event EventHandler? SelectionChanged;

    public event EventHandler? CurrentTimeChanged;

    public event EventHandler? CurvesChanged;

    public MotionEditor? Editor => _editor;

    public string? MotionName => _motionName;

    public MotionAsset? Motion => _editor is not null && _motionName is not null ? _editor.Document.Find(_motionName) : null;

    public IReadOnlyList<CurveSlot> Curves => _curves;

    /// <summary>선택 모델. 도프시트와 같은 객체를 꽂으면 두 뷰의 선택이 하나다.</summary>
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

    public GraphCamera Camera => _camera;

    public bool SnapEnabled { get; set; } = true;

    public double SnapStep { get; set; } = TimeSnap.DefaultStep;

    /// <summary>눈금·라벨 단위. 마야처럼 프레임이 기본이다.</summary>
    public TimeDisplay TimeDisplay { get; set; } = TimeDisplay.Frames;

    /// <summary>재생 범위(초) — 안쪽을 밝게 칠한다(마야). 끝 ≤ 시작이면 안 칠한다. 셸이 뷰포트 시계에서 넣는다.</summary>
    public double RangeStart { get; set; }

    public double RangeEnd { get; set; } = -1;

    /// <summary>현재 시각(초). I가 여기에 키를 끼운다. 뷰포트의 스크럽도 여기로 온다(M3).</summary>
    public double CurrentTime
    {
        get => _currentTime;
        set
        {
            if (Math.Abs(_currentTime - value) < 1e-12)
            {
                return;
            }

            _currentTime = Math.Max(0, value);
            CurrentTimeChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    // ── 바인딩 ────────────────────────────────────────────────────────

    /// <summary>편집기와 모션을 싣는다. 모션이 null이면 빈 그래프다. 커브 목록·선택이 새로 선다.</summary>
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

        _selection.Clear();
        _curves.Clear();
        _buffer.Clear();
        _cameraFitted = false;
        ReloadCurves();
        FitAll();
    }

    /// <summary>보이는 커브를 한 번에 정한다(아웃라이너 행 클릭 = 그 커브만). 이벤트는 한 번.</summary>
    public void SetVisibleAll(Func<CurveRef, bool> visible)
    {
        ArgumentNullException.ThrowIfNull(visible);
        bool changed = false;

        foreach (CurveSlot slot in _curves)
        {
            bool next = visible(slot.Curve);

            if (slot.Visible != next)
            {
                slot.Visible = next;
                changed = true;
            }
        }

        if (changed)
        {
            _selection.RemoveWhere(k => _curves.Any(c => c.Curve == k.Curve && !c.Visible));
            CurvesChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public void SetVisible(CurveRef curve, bool visible)
    {
        foreach (CurveSlot slot in _curves)
        {
            if (slot.Curve == curve)
            {
                slot.Visible = visible;
            }
        }

        if (!visible)
        {
            _selection.RemoveWhere(k => k.Curve == curve);
        }

        InvalidateVisual();
    }

    private void OnEditorChanged(object? sender, MotionChangedEventArgs e)
    {
        ReloadCurves();
        PruneSelection();
        UpdateRegion();
        InvalidateVisual();
    }

    /// <summary>모션의 성분마다 슬롯 하나. 있던 슬롯의 보이기 상태는 지킨다.</summary>
    private void ReloadCurves()
    {
        var visible = new Dictionary<CurveRef, bool>();

        foreach (CurveSlot slot in _curves)
        {
            visible[slot.Curve] = slot.Visible;
        }

        _curves.Clear();
        MotionAsset? motion = Motion;

        if (motion is null)
        {
            CurvesChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        for (int c = 0; c < motion.Channels.Count; c++)
        {
            ChannelAsset channel = motion.Channels[c];

            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is null)
                {
                    continue;
                }

                var curve = new CurveRef(channel.Node, channel.Kind, axis);
                var slot = new CurveSlot(curve, ColorFor(axis, c));

                if (visible.TryGetValue(curve, out bool wasVisible))
                {
                    slot.Visible = wasVisible;
                }

                _curves.Add(slot);
            }
        }

        CurvesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PruneSelection()
    {
        _selection.Prune(Motion);
        _selectedCurves.RemoveWhere(c => !_curves.Any(s => s.Curve == c));
    }

    /// <summary>보이는 커브 선 중 가장 가까운 것(6px 안). 잠긴 커브는 뺀다.</summary>
    private bool TryHitCurve(MotionAsset motion, Pixel at, out CurveRef hit)
    {
        hit = default;
        double best = 6;
        GraphPoint left = ToGraph(new Point(Plot.X, Plot.Y));
        GraphPoint right = ToGraph(new Point(Plot.Right, Plot.Y));

        foreach (CurveSlot slot in _curves)
        {
            if (!slot.Visible || _flags.IsLocked(slot.Curve) || slot.Curve.Resolve(motion) is not { } curve || curve.Keys.Count == 0)
            {
                continue;
            }

            List<Pixel> polyline = CurveSampler.Sample(curve.Keys, left.T, right.T, 256).Select(p => ToPixel(ToScreen(new GraphPoint(p.T, p.V)))).ToList();
            double d = GraphHitTest.DistanceToPolyline(polyline, at);

            if (d < best)
            {
                best = d;
                hit = slot.Curve;
            }
        }

        return best < 6;
    }

    /// <summary>축이 색을 정한다(x 빨강·y 초록·z 파랑). 채널 순서가 색조를 조금씩 돌려 같은 축 둘을 가른다.</summary>
    public static Color ColorFor(Axis axis, int channelIndex)
    {
        double baseHue = axis switch { Axis.X => 3, Axis.Y => 118, _ => 218 };
        double hue = (baseHue + channelIndex * 22) % 360;
        return new HsvColor(1.0, hue, axis == Axis.Y ? 0.55 : 0.72, axis == Axis.Z ? 1.0 : 0.9).ToRgb();
    }

    // ── 좌표 ──────────────────────────────────────────────────────────

    private Rect Plot => new(
        PadLeft, PadTop,
        Math.Max(1, Bounds.Width - PadLeft - PadRight),
        Math.Max(1, Bounds.Height - PadTop - PadBottom));

    private Point ToScreen(GraphPoint point)
    {
        Rect plot = Plot;
        Pixel px = _camera.ToPixel(point, plot.Height);
        return new Point(plot.X + px.X, plot.Y + px.Y);
    }

    private Pixel ToPlotPixel(Point screen)
    {
        Rect plot = Plot;
        return new Pixel(screen.X - plot.X, screen.Y - plot.Y);
    }

    private GraphPoint ToGraph(Point screen) => _camera.ToGraph(ToPlotPixel(screen), Plot.Height);

    private static Point ToPoint(Pixel px) => new(px.X, px.Y);

    /// <summary>키의 화면 좌표(이 컨트롤 기준). 테스트와 인스펙터가 쓴다.</summary>
    public Point? ScreenPositionOf(KeyRef key)
    {
        MotionAsset? motion = Motion;
        KeyAsset? asset = motion is null ? null : key.Resolve(motion);
        return asset is null ? null : ScreenOf(key.Curve, asset.T, asset.V);
    }

    private static Pixel ToPixel(Point p) => new(p.X, p.Y);

    private double SnapTime(double seconds) => SnapEnabled ? TimeSnap.Snap(seconds, SnapStep) : seconds;

    // ── 카메라 명령 ───────────────────────────────────────────────────

    public void FitAll()
    {
        MotionAsset? motion = Motion;
        var keys = new List<KeyAsset>();

        if (motion is not null)
        {
            foreach (CurveSlot slot in _curves)
            {
                if (slot.Visible && slot.Curve.Resolve(motion) is { } curve)
                {
                    keys.AddRange(curve.Keys.Select(k => new KeyAsset { T = k.T, V = k.V }));
                }
            }
        }

        double duration = motion?.Duration ?? 1f;
        FitTo(keys, 0, Math.Max(duration, 0.1));
    }

    public void FitSelection()
    {
        MotionAsset? motion = Motion;

        if (motion is null || _selection.Count == 0)
        {
            FitAll();
            return;
        }

        var keys = new List<KeyAsset>();

        foreach (KeyRef key in _selection)
        {
            if (key.Resolve(motion) is { } k)
            {
                keys.Add(new KeyAsset { T = k.T, V = k.V });
            }
        }

        FitTo(keys, null, null);
    }

    /// <summary>마야 "Frame Playback Range" — 시간은 재생 범위, 값은 그 안의 키.</summary>
    public void FitRange()
    {
        MotionAsset? motion = Motion;
        double start = RangeStart;
        double end = RangeEnd > RangeStart ? RangeEnd : (motion?.Duration ?? 1f);
        var keys = new List<KeyAsset>();

        if (motion is not null)
        {
            foreach (CurveSlot slot in _curves)
            {
                if (slot.Visible && slot.Curve.Resolve(motion) is { } curve)
                {
                    keys.AddRange(curve.Keys.Where(k => k.T >= start - 1e-6 && k.T <= end + 1e-6).Select(k => new KeyAsset { T = k.T, V = k.V }));
                }
            }
        }

        FitTo(keys, start, Math.Max(end, start + 0.1));
    }

    /// <summary>마야 "Center Current Time" — 배율은 두고 현재 시각이 플롯 가운데 오게 민다.</summary>
    public void CenterCurrentTime()
    {
        double half = Plot.Width / 2 / _camera.PixelsPerSecond;
        _camera = _camera with { TimeOrigin = _currentTime - half };
        InvalidateVisual();
    }

    private void FitTo(List<KeyAsset> keys, double? tMinAtLeast, double? tMaxAtLeast)
    {
        Rect plot = Plot;
        double tMin = tMinAtLeast ?? double.PositiveInfinity;
        double tMax = tMaxAtLeast ?? double.NegativeInfinity;
        double vMin = double.PositiveInfinity;
        double vMax = double.NegativeInfinity;

        if (CurveSampler.TryBounds(keys, out double kt0, out double kt1, out double kv0, out double kv1))
        {
            tMin = Math.Min(tMin, kt0);
            tMax = Math.Max(tMax, kt1);
            vMin = kv0;
            vMax = kv1;
        }

        if (double.IsInfinity(tMin))
        {
            tMin = 0;
            tMax = 1;
        }

        if (double.IsInfinity(vMin))
        {
            vMin = -1;
            vMax = 1;
        }

        _camera = GraphCamera.Fit(tMin, tMax, vMin, vMax, plot.Width, plot.Height, 28);
        // 아직 배치 전(크기 0)이면 첫 배치에서 다시 맞춘다.
        _cameraFitted = Bounds.Width > 0 && Bounds.Height > 0;
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (!_cameraFitted && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            FitAll();
        }
    }

    // ── 편집 명령 ─────────────────────────────────────────────────────

    public void SelectAll()
    {
        MotionAsset? motion = Motion;
        var all = new List<KeyRef>();

        if (motion is not null)
        {
            foreach (CurveSlot slot in _curves)
            {
                if (slot.Visible && slot.Curve.Resolve(motion) is { } curve)
                {
                    for (int i = 0; i < curve.Keys.Count; i++)
                    {
                        all.Add(new KeyRef(slot.Curve, i));
                    }
                }
            }
        }

        _selection.Set(all);
    }

    public void ClearSelection() => _selection.Clear();

    public void Select(IEnumerable<KeyRef> keys, bool additive)
    {
        if (additive)
        {
            _selection.AddRange(keys);
        }
        else
        {
            _selection.Set(keys);
        }
    }

    public void DeleteSelection()
    {
        if (_editor is null || _motionName is null || _selection.Count == 0)
        {
            return;
        }

        List<KeyRef> doomed = _selection.ToList();
        _selection.Clear();
        _editor.RemoveKeys(_motionName, doomed);
    }

    /// <summary>보이는 커브 전부에 현재 시각의 키를 끼운다(값은 곡선 위). 선택이 있으면 선택된 키의 커브에만.</summary>
    public void InsertKeyAtCurrentTime()
    {
        MotionAsset? motion = Motion;

        if (_editor is null || _motionName is null || motion is null)
        {
            return;
        }

        float t = (float)SnapTime(CurrentTime);
        IEnumerable<CurveRef> targets = (_selection.Count > 0
            ? _selection.Select(k => k.Curve).Distinct()
            : _curves.Where(s => s.Visible).Select(s => s.Curve)).Where(c => !_flags.IsLocked(c));

        var edits = new List<(CurveRef, IEnumerable<KeyAsset>)>();
        var inserted = new List<(CurveRef Curve, float T)>();

        foreach (CurveRef curveRef in targets)
        {
            if (curveRef.Resolve(motion) is not { } curve)
            {
                continue;
            }

            edits.Add((curveRef, KeyOps.WithKeyOnCurve(curve.Keys, t)));
            inserted.Add((curveRef, t));
        }

        if (edits.Count == 0)
        {
            return;
        }

        _editor.SetKeysMany(_motionName, edits);
        SelectByTime(inserted);
    }

    public void SetTangentMode(TangentMode mode)
    {
        if (_editor is not null && _motionName is not null && _selection.Count > 0)
        {
            _editor.SetTangentMode(_motionName, _selection.ToList(), mode);
        }
    }

    public void SetBroken(bool broken)
    {
        if (_editor is not null && _motionName is not null && _selection.Count > 0)
        {
            _editor.SetBroken(_motionName, _selection.ToList(), broken);
        }
    }

    private void SelectByTime(IEnumerable<(CurveRef Curve, float T)> targets)
    {
        MotionAsset? motion = Motion;
        var next = new List<KeyRef>();

        if (motion is not null)
        {
            foreach ((CurveRef curveRef, float t) in targets)
            {
                if (curveRef.Resolve(motion) is not { } curve)
                {
                    continue;
                }

                int index = curve.Keys.FindIndex(k => Math.Abs(k.T - t) <= MotionRules.Tolerance);

                if (index >= 0)
                {
                    next.Add(new KeyRef(curveRef, index));
                }
            }
        }

        _selection.Set(next);
        InvalidateVisual();
    }

    /// <summary>다음/이전 키 시각으로 현재 시각을 옮긴다(`.` `,`).</summary>
    public bool JumpToKey(bool forward)
    {
        if (Motion is not { } motion)
        {
            return false;
        }

        List<float> times = KeyTimes.All(motion);
        float? target = forward ? KeyTimes.Next(times, CurrentTime) : KeyTimes.Previous(times, CurrentTime);

        if (target is null)
        {
            return false;
        }

        CurrentTime = target.Value;
        return true;
    }

    // ── 그리기 ────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));
        Rect plot = Plot;
        context.FillRectangle(PlotBackground, plot);

        MotionAsset? motion = Motion;
        DrawGrid(context, plot, motion);

        if (motion is null)
        {
            DrawLabel(context, "모션을 고르면 여기에 커브가 보입니다.", new Point(plot.X + 12, plot.Y + 12), LabelBrush, 12);
            return;
        }

        using (context.PushClip(plot))
        {
            GraphPoint left = ToGraph(new Point(plot.X, plot.Y));
            GraphPoint right = ToGraph(new Point(plot.Right, plot.Y));
            int samples = Math.Max(16, (int)(plot.Width / 2));

            foreach ((CurveRef curveRef, List<KeyAsset> buffered) in _buffer)
            {
                List<GraphPoint> bufferPoints = CurveSampler.Sample(buffered, left.T, right.T, samples);
                context.DrawGeometry(null, BufferPen, new PolylineGeometry(bufferPoints.Select(p => ToScreen(new GraphPoint(p.T, p.V))), false));
            }

            foreach (CurveSlot slot in _curves)
            {
                if (!slot.Visible)
                {
                    continue;
                }

                List<KeyAsset> keys = KeysForRender(slot.Curve, motion);

                if (keys.Count == 0)
                {
                    continue;
                }

                InfinityMode pre = ShowCycle ? InfinityMode.Cycle : PreInfinity;
                InfinityMode post = ShowCycle ? InfinityMode.Cycle : PostInfinity;

                if ((pre != InfinityMode.Constant || post != InfinityMode.Constant) && keys.Count >= 2)
                {
                    var infinityPen = new Pen(new SolidColorBrush(Color.FromArgb(90, slot.Color.R, slot.Color.G, slot.Color.B)), 1, DashStyle.Dash);
                    List<GraphPoint> extended = CurveSampler.SampleInfinity(keys, pre, post, left.T, right.T, samples);
                    context.DrawGeometry(null, infinityPen, new PolylineGeometry(extended.Select(p => ToScreen(new GraphPoint(p.T, p.V))), false));
                }

                List<GraphPoint> points = CurveSampler.Sample(keys, left.T, right.T, samples);
                bool locked = _flags.IsLocked(slot.Curve);
                bool muted = _flags.IsMuted(slot.Curve);
                Color color = locked || muted ? Color.FromArgb(110, slot.Color.R, slot.Color.G, slot.Color.B) : slot.Color;
                bool curveSelected = _selectedCurves.Contains(slot.Curve);
                var pen = curveSelected
                    ? new Pen(SelectedCurveBrush, 3, muted ? DashStyle.Dash : null)
                    : new Pen(new SolidColorBrush(color), locked ? 1 : 2, muted ? DashStyle.Dash : null);
                var polyline = new PolylineGeometry(points.Select(p => ToScreen(new GraphPoint(p.T, p.V))), false);
                context.DrawGeometry(null, pen, polyline);
            }

            foreach (CurveSlot slot in _curves)
            {
                if (!slot.Visible)
                {
                    continue;
                }

                List<KeyAsset> keys = KeysForRender(slot.Curve, motion);
                bool locked = _flags.IsLocked(slot.Curve);
                IBrush keyBrush = locked ? new SolidColorBrush(Color.FromArgb(110, 0xF2, 0xA3, 0x3A)) : KeyBrush;

                for (int i = 0; i < keys.Count; i++)
                {
                    var keyRef = new KeyRef(slot.Curve, i);
                    Point center = ScreenOf(slot.Curve, keys[i].T, keys[i].V);
                    bool selected = _selection.Contains(keyRef);

                    if (selected)
                    {
                        DrawHandles(context, keys, i, center, keyRef);
                        DrawLabel(context, $"{TimeText.Format(keys[i].T, TimeDisplay)}  {keys[i].V:0.###}", new Point(center.X + 7, center.Y - 16), ValueLabelBrush, 10);
                    }

                    // 마야: 키는 작은 주황 점, 선택 키는 흰색으로 조금 크게.
                    context.FillRectangle(selected ? SelectedKeyBrush : keyBrush, selected ? new Rect(center.X - 3.5, center.Y - 3.5, 7, 7) : new Rect(center.X - 2.5, center.Y - 2.5, 5, 5));
                }
            }

            if (Tool == GraphTool.Region && _region is { } region && _selection.Count > 0)
            {
                Rect rr = RegionScreenRect(region);
                context.DrawRectangle(BoxPen, rr);

                foreach (Point handle in new[] { new Point(rr.X, rr.Center.Y), new Point(rr.Right, rr.Center.Y), new Point(rr.Center.X, rr.Y), new Point(rr.Center.X, rr.Bottom) })
                {
                    context.FillRectangle(HandleBrush, new Rect(handle.X - 3, handle.Y - 3, 6, 6));
                }
            }

            if (_drag == Drag.Box)
            {
                var box = new Rect(_dragStart, _dragNow);
                context.FillRectangle(BoxFill, box);
                context.DrawRectangle(BoxPen, box);
            }

            if (_drag == Drag.Lasso && _lasso.Count > 1)
            {
                context.DrawGeometry(BoxFill, BoxPen, new PolylineGeometry(_lasso, true));
            }

            if (_drag == Drag.Scale && _dragMoved)
            {
                Point pivot = ToScreen(_scalePivot);
                context.DrawEllipse(PivotBrush, null, pivot, 4, 4);
            }
        }
    }

    private List<KeyAsset> KeysForRender(CurveRef curveRef, MotionAsset motion)
    {
        if (_preview.TryGetValue(curveRef, out List<KeyAsset>? preview))
        {
            return preview;
        }

        return curveRef.Resolve(motion)?.Keys ?? new List<KeyAsset>();
    }

    private void DrawHandles(DrawingContext context, List<KeyAsset> keys, int index, Point center, KeyRef keyRef)
    {
        KeyAsset key = keys[index];
        Pixel origin = ToPixel(center);

        if (index > 0)
        {
            Point inEnd = ToPoint(HandleMath.HandleEnd(origin, key.InTangent, inward: true, _camera));
            context.DrawLine(HandlePen, center, inEnd);
            DrawHandleEnd(context, inEnd, _selectedHandles.Contains((keyRef, true)));
        }

        if (index < keys.Count - 1)
        {
            Point outEnd = ToPoint(HandleMath.HandleEnd(origin, key.OutTangent, inward: false, _camera));
            context.DrawLine(HandlePen, center, outEnd);
            DrawHandleEnd(context, outEnd, _selectedHandles.Contains((keyRef, false)));
        }
    }

    /// <summary>핸들 끝 네모 — 고른 핸들은 선택 키 색으로 조금 크게.</summary>
    private static void DrawHandleEnd(DrawingContext context, Point end, bool selected)
        => context.FillRectangle(selected ? SelectedKeyBrush : HandleBrush, selected ? new Rect(end.X - 3.5, end.Y - 3.5, 7, 7) : new Rect(end.X - 2.5, end.Y - 2.5, 5, 5));

    private void DrawGrid(DrawingContext context, Rect plot, MotionAsset? motion)
    {
        GraphPoint bottomLeft = ToGraph(new Point(plot.X, plot.Bottom));
        GraphPoint topRight = ToGraph(new Point(plot.Right, plot.Y));

        // 재생 범위 안은 밝게(마야).
        double rangeEnd = RangeEnd > RangeStart ? RangeEnd : (motion?.Duration ?? -1);

        if (rangeEnd > RangeStart)
        {
            double x0 = Math.Max(plot.X, ToScreen(new GraphPoint(RangeStart, 0)).X);
            double x1 = Math.Min(plot.Right, ToScreen(new GraphPoint(rangeEnd, 0)).X);

            if (x1 > x0)
            {
                context.FillRectangle(RangeBackground, new Rect(x0, plot.Y, x1 - x0, plot.Height));
            }
        }

        // 위 눈금자.
        var ruler = new Rect(plot.X, 0, plot.Width, PadTop);
        context.FillRectangle(RulerBackground, ruler);

        if (TimeDisplay == TimeDisplay.Frames)
        {
            double pixelsPerFrame = _camera.PixelsPerSecond / TimeSnap.FramesPerSecond;
            int step = TimeText.FrameTickStep(pixelsPerFrame);
            long firstFrame = (long)Math.Floor(TimeSnap.ToFrames(bottomLeft.T));
            long lastFrame = (long)Math.Ceiling(TimeSnap.ToFrames(topRight.T));

            if (pixelsPerFrame >= 6)
            {
                for (long f = firstFrame; f <= lastFrame; f++)
                {
                    if (f % step != 0)
                    {
                        double mx = ToScreen(new GraphPoint(f / TimeSnap.FramesPerSecond, 0)).X;
                        context.DrawLine(MinorGridPen, new Point(mx, plot.Y), new Point(mx, plot.Bottom));
                    }
                }
            }

            long firstMajor = (long)Math.Floor(firstFrame / (double)step) * step;

            for (long f = firstMajor; f <= lastFrame; f += step)
            {
                double x = ToScreen(new GraphPoint(f / TimeSnap.FramesPerSecond, 0)).X;
                context.DrawLine(GridPen, new Point(x, plot.Y), new Point(x, plot.Bottom));
                context.DrawLine(GridPen, new Point(x, ruler.Bottom - 4), new Point(x, ruler.Bottom));
                DrawLabel(context, f.ToString(CultureInfo.InvariantCulture), new Point(x + 3, 2), LabelBrush, 10);
            }
        }
        else
        {
            double tStep = TimeSnap.TickStep(_camera.PixelsPerSecond);
            double tStart = Math.Floor(bottomLeft.T / tStep) * tStep;

            for (double t = tStart; t <= topRight.T; t += tStep)
            {
                double x = ToScreen(new GraphPoint(t, 0)).X;
                context.DrawLine(GridPen, new Point(x, plot.Y), new Point(x, plot.Bottom));
                DrawLabel(context, Math.Abs(t) < 1e-9 ? "0" : $"{t:0.###}s", new Point(x + 3, 2), LabelBrush, 10);
            }
        }

        double vStep = TimeSnap.ValueTickStep(_camera.PixelsPerValue);
        double vStart = Math.Floor(bottomLeft.V / vStep) * vStep;

        for (double v = vStart; v <= topRight.V; v += vStep)
        {
            Point y = ToScreen(new GraphPoint(0, v));
            context.DrawLine(GridPen, new Point(plot.X, y.Y), new Point(plot.Right, y.Y));
            DrawLabel(context, $"{v:0.###}", new Point(4, y.Y - 7), LabelBrush, 10);
        }

        // 기준선: 값 0 · 시각 0 · 길이 · 현재 시각(노랑, 위 눈금자에 마커).
        Point zero = ToScreen(new GraphPoint(0, 0));
        context.DrawLine(AxisPen, new Point(plot.X, zero.Y), new Point(plot.Right, zero.Y));
        context.DrawLine(AxisPen, new Point(zero.X, plot.Y), new Point(zero.X, plot.Bottom));

        if (motion is not null)
        {
            Point end = ToScreen(new GraphPoint(motion.Duration, 0));
            context.DrawLine(DurationPen, new Point(end.X, plot.Y), new Point(end.X, plot.Bottom));
        }

        Point now = ToScreen(new GraphPoint(_currentTime, 0));
        context.DrawLine(CurrentTimePen, new Point(now.X, plot.Y), new Point(now.X, plot.Bottom));
        var marker = new PolylineGeometry(new[]
        {
            new Point(now.X - 5, 1), new Point(now.X + 5, 1), new Point(now.X + 5, PadTop - 6), new Point(now.X, PadTop - 1), new Point(now.X - 5, PadTop - 6),
        }, true);
        context.DrawGeometry(CurrentTimeBrush, null, marker);
    }

    private static void DrawLabel(DrawingContext context, string text, Point at, IBrush brush, double size)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
        context.DrawText(formatted, at);
    }

    // ── 입력 ──────────────────────────────────────────────────────────

    private List<(KeyRef Item, Pixel At)> KeyCandidates(MotionAsset motion)
    {
        var candidates = new List<(KeyRef, Pixel)>();

        foreach (CurveSlot slot in _curves)
        {
            if (!slot.Visible || _flags.IsLocked(slot.Curve) || slot.Curve.Resolve(motion) is not { } curve)
            {
                continue;
            }

            for (int i = 0; i < curve.Keys.Count; i++)
            {
                candidates.Add((new KeyRef(slot.Curve, i), ToPixel(ScreenOf(slot.Curve, curve.Keys[i].T, curve.Keys[i].V))));
            }
        }

        return candidates;
    }

    private bool TryHitHandle(MotionAsset motion, Pixel at, out KeyRef key, out bool inward)
    {
        if (GraphHitTest.TryNearest(HandleCandidates(motion), at, GraphHitTest.HandleRadius, out (KeyRef Key, bool Inward) hit))
        {
            key = hit.Key;
            inward = hit.Inward;
            return true;
        }

        key = default;
        inward = false;
        return false;
    }

    /// <summary>고른 키들의 핸들 끝(in·out) 픽셀 — 핸들 클릭·박스 선택의 후보.</summary>
    private List<((KeyRef Key, bool Inward) Item, Pixel At)> HandleCandidates(MotionAsset motion)
    {
        var candidates = new List<((KeyRef Key, bool Inward) Item, Pixel At)>();

        foreach (KeyRef keyRef in _selection)
        {
            if (keyRef.Curve.Resolve(motion) is not { } curve || keyRef.Index < 0 || keyRef.Index >= curve.Keys.Count)
            {
                continue;
            }

            KeyAsset k = curve.Keys[keyRef.Index];
            Pixel origin = ToPixel(ScreenOf(keyRef.Curve, k.T, k.V));

            if (keyRef.Index > 0)
            {
                candidates.Add(((keyRef, true), HandleMath.HandleEnd(origin, k.InTangent, inward: true, _camera)));
            }

            if (keyRef.Index < curve.Keys.Count - 1)
            {
                candidates.Add(((keyRef, false), HandleMath.HandleEnd(origin, k.OutTangent, inward: false, _camera)));
            }
        }

        return candidates;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        PointerPoint point = e.GetCurrentPoint(this);
        Point position = point.Position;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        MotionAsset? pressedMotion = Motion;

        // 마야 내비게이션: Alt+MMB 팬 · Alt+RMB 줌 드래그 · Alt+LMB 팬(호환).
        if (alt && (point.Properties.IsMiddleButtonPressed || point.Properties.IsLeftButtonPressed))
        {
            StartDrag(Drag.Pan, position, e);
            _panStart = _camera;
            return;
        }

        if (alt && point.Properties.IsRightButtonPressed)
        {
            StartDrag(Drag.Zoom, position, e);
            _panStart = _camera;
            return;
        }

        // MMB: 고른 핸들이 있으면 돌리기, 선택 키 둘레(12px 안)에서 누르면 선택 키 이동(마야), 그 밖은 화면 이동(팬 — RMB와 같다, 소유자).
        // MMB 클릭(안 움직임) = 선택한 커브에 키 끼우기.
        if (point.Properties.IsMiddleButtonPressed)
        {
            if (pressedMotion is not null && _selectedHandles.Count > 0 && BeginHandleDrag(pressedMotion))
            {
                StartDrag(Drag.Handles, position, e);
            }
            else if (pressedMotion is not null && _selection.Count > 0 && SelectionScreenBounds(pressedMotion) is { } keyBounds && keyBounds.Inflate(12).Contains(position))
            {
                BeginPreview(pressedMotion, _selection.Select(k => k.Curve).Distinct());
                StartDrag(Drag.Keys, position, e);
            }
            else
            {
                StartDrag(Drag.CurveInsert, position, e);
                _panStart = _camera;
            }

            return;
        }

        // RMB 드래그: 선택 키 둘레(12px 안)에서 누르면 선택 키를 한 축으로만 이동(처음 움직인 쪽으로 잠긴다, Shift = 정수 프레임·정수 값),
        // 빈 자리에서 누르면 화면 이동(팬).
        if (point.Properties.IsRightButtonPressed)
        {
            if (pressedMotion is not null && _selection.Count > 0 && SelectionScreenBounds(pressedMotion) is { } bounds && bounds.Inflate(12).Contains(position))
            {
                BeginPreview(pressedMotion, _selection.Select(k => k.Curve).Distinct());
                _axisLockDecided = false;
                StartDrag(Drag.KeysAxis, position, e);
                return;
            }

            StartDrag(Drag.Pan, position, e);
            _panStart = _camera;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // 위 눈금자: 스크럽(마야 — 프레임 눈금자를 누르면 현재 시각이 간다).
        if (position.Y < Plot.Y)
        {
            _drag = Drag.Scrub;
            CurrentTime = Math.Max(0, SnapTime(ToGraph(position).T));
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        MotionAsset? motion = Motion;

        if (motion is null || _editor is null || _motionName is null)
        {
            return;
        }

        Pixel at = ToPixel(position);

        if (e.ClickCount == 2)
        {
            InsertKeyAt(motion, position);
            e.Handled = true;
            return;
        }

        if (TryHitHandle(motion, at, out KeyRef handleKey, out bool inward))
        {
            _drag = inward ? Drag.InHandle : Drag.OutHandle;
            _handleKey = handleKey;
            BeginPreview(motion, new[] { handleKey.Curve });
            _dragStart = position;
            _dragMoved = false;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // 리전 툴: 상자 모서리 = 그 축만 반대쪽 기준 스케일, 상자 안 = 이동, 밖 = 새 상자 선택.
        if (Tool == GraphTool.Region && !ctrl && _region is { } region && _selection.Count > 0)
        {
            Rect rr = RegionScreenRect(region);
            const double edge = 7;

            if (rr.Inflate(edge).Contains(position))
            {
                Drag regionDrag = Drag.None;

                if (Math.Abs(position.X - rr.X) <= edge)
                {
                    regionDrag = Drag.RegionLeft;
                    _scalePivot = new GraphPoint(region.T1, region.V0);
                    _regionEdge = region.T0;
                }
                else if (Math.Abs(position.X - rr.Right) <= edge)
                {
                    regionDrag = Drag.RegionRight;
                    _scalePivot = new GraphPoint(region.T0, region.V0);
                    _regionEdge = region.T1;
                }
                else if (Math.Abs(position.Y - rr.Y) <= edge)
                {
                    regionDrag = Drag.RegionTop;
                    _scalePivot = new GraphPoint(region.T0, region.V0);
                    _regionEdge = region.V1;
                }
                else if (Math.Abs(position.Y - rr.Bottom) <= edge)
                {
                    regionDrag = Drag.RegionBottom;
                    _scalePivot = new GraphPoint(region.T0, region.V1);
                    _regionEdge = region.V0;
                }

                BeginPreview(motion, _selection.Select(k => k.Curve).Distinct());
                StartDrag(regionDrag == Drag.None ? Drag.Keys : regionDrag, position, e);
                return;
            }
        }

        // 스케일 툴(R): LMB 드래그 = 선택 키를 선택 중심 기준으로 값·시간 스케일.
        if (Tool == GraphTool.Scale && _selection.Count > 0 && !ctrl)
        {
            _scalePivot = SelectionCenter(motion);
            BeginPreview(motion, _selection.Select(k => k.Curve).Distinct());
            StartDrag(Drag.Scale, position, e);
            return;
        }

        // 라소(Ctrl+LMB).
        if (ctrl)
        {
            if (!shift)
            {
                _selection.Clear();
            }

            _lasso.Clear();
            _lasso.Add(position);
            StartDrag(Drag.Lasso, position, e);
            return;
        }

        if (GraphHitTest.TryNearest(KeyCandidates(motion), at, GraphHitTest.KeyRadius, out KeyRef hit))
        {
            if (shift)
            {
                _selection.Toggle(hit);
            }
            else if (!_selection.Contains(hit))
            {
                _selection.Set(new[] { hit });
            }

            // 이동 툴(W)에서만 LMB 드래그가 이동이다. 선택 툴(Q)은 고르기만 — 마야처럼 MMB로 옮긴다.
            if (Tool == GraphTool.Move && _selection.Contains(hit))
            {
                BeginPreview(motion, _selection.Select(k => k.Curve).Distinct());
                StartDrag(Drag.Keys, position, e);
                return;
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // 커브 선 클릭 = 그 커브 선택(마야). Shift = 더하기/빼기.
        if (TryHitCurve(motion, at, out CurveRef curveHit))
        {
            if (!shift)
            {
                _selection.Clear();
            }

            SelectCurve(curveHit, additive: shift);
            e.Handled = true;
            return;
        }

        // 빈 자리: 박스 선택. 선택은 놓을 때 정한다 — 키를 고른 채 핸들 끝을 박스로 고를 수 있어야 한다(마야).
        _boxShift = shift;
        StartDrag(Drag.Box, position, e);
    }

    private void StartDrag(Drag drag, Point position, PointerPressedEventArgs e)
    {
        _drag = drag;
        _dragStart = position;
        _dragNow = position;
        _dragMoved = false;
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>선택 키들의 가운데(시각·값) — 스케일 툴의 피벗.</summary>
    /// <summary>선택 키들의 화면 둘레. 없으면 null.</summary>
    private Rect? SelectionScreenBounds(MotionAsset motion)
    {
        double left = double.PositiveInfinity, top = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.NegativeInfinity;

        foreach (KeyRef keyRef in _selection)
        {
            if (keyRef.Resolve(motion) is not { } k)
            {
                continue;
            }

            Point p = ScreenOf(keyRef.Curve, k.T, k.V);
            left = Math.Min(left, p.X);
            right = Math.Max(right, p.X);
            top = Math.Min(top, p.Y);
            bottom = Math.Max(bottom, p.Y);
        }

        return double.IsInfinity(left) ? null : new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>박스가 지나는 보이는 커브들 — 박스 드래그로 커브를 고를 때.</summary>
    private List<CurveRef> CurvesInBox(MotionAsset motion, Pixel a, Pixel b)
    {
        var hits = new List<CurveRef>();
        GraphPoint left = ToGraph(new Point(Plot.X, Plot.Y));
        GraphPoint right = ToGraph(new Point(Plot.Right, Plot.Y));

        foreach (CurveSlot slot in _curves)
        {
            if (!slot.Visible || _flags.IsLocked(slot.Curve) || slot.Curve.Resolve(motion) is not { } curve || curve.Keys.Count == 0)
            {
                continue;
            }

            List<Pixel> polyline = CurveSampler.Sample(curve.Keys, left.T, right.T, 256).Select(p => ToPixel(ToScreen(new GraphPoint(p.T, p.V)))).ToList();

            if (GraphHitTest.PolylineIntersectsBox(polyline, a, b))
            {
                hits.Add(slot.Curve);
            }
        }

        return hits;
    }

    private GraphPoint SelectionCenter(MotionAsset motion)
    {
        var keys = new List<KeyAsset>();

        foreach (KeyRef keyRef in _selection)
        {
            if (keyRef.Resolve(motion) is { } k)
            {
                keys.Add(k);
            }
        }

        return CurveSampler.TryBounds(keys, out double t0, out double t1, out double v0, out double v1)
            ? new GraphPoint((t0 + t1) / 2, (v0 + v1) / 2)
            : new GraphPoint(CurrentTime, 0);
    }

    /// <summary>더블클릭 — 가장 가까운 보이는 커브 위, 그 시각(스냅)에 키.</summary>
    private void InsertKeyAt(MotionAsset motion, Point position)
    {
        Pixel at = ToPixel(position);
        GraphPoint left = ToGraph(new Point(Plot.X, Plot.Y));
        GraphPoint right = ToGraph(new Point(Plot.Right, Plot.Y));
        CurveRef? best = null;
        double bestDistance = 14;

        foreach (CurveSlot slot in _curves)
        {
            if (!slot.Visible || _flags.IsLocked(slot.Curve) || slot.Curve.Resolve(motion) is not { } curve)
            {
                continue;
            }

            List<Pixel> polyline = CurveSampler.Sample(curve.Keys, left.T, right.T, 256).Select(p => ToPixel(ToScreen(new GraphPoint(p.T, p.V)))).ToList();
            double d = GraphHitTest.DistanceToPolyline(polyline, at);

            if (d < bestDistance)
            {
                bestDistance = d;
                best = slot.Curve;
            }
        }

        if (best is not { } target || _editor is null || _motionName is null || target.Resolve(motion) is not { } targetCurve)
        {
            return;
        }

        float t = (float)SnapTime(ToGraph(position).T);
        _editor.SetKeysMany(_motionName, new[] { (target, (IEnumerable<KeyAsset>)KeyOps.WithKeyOnCurve(targetCurve.Keys, t)) });
        SelectByTime(new[] { (target, t) });
    }

    private void BeginPreview(MotionAsset motion, IEnumerable<CurveRef> curves)
    {
        _preview.Clear();
        _previewKeys.Clear();

        foreach (CurveRef curveRef in curves)
        {
            if (curveRef.Resolve(motion) is not { } curve)
            {
                continue;
            }

            var copy = new List<KeyAsset>();

            for (int i = 0; i < curve.Keys.Count; i++)
            {
                KeyAsset k = curve.Keys[i];
                var clone = new KeyAsset
                {
                    T = k.T, V = k.V, InTangent = k.InTangent, OutTangent = k.OutTangent, Tangent = k.Tangent, Broken = k.Broken,
                };
                copy.Add(clone);
                _previewKeys[new KeyRef(curveRef, i)] = clone;
            }

            _preview[curveRef] = copy;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_drag == Drag.None)
        {
            return;
        }

        Point position = e.GetPosition(this);
        _dragNow = position;
        double dx = position.X - _dragStart.X;
        double dy = position.Y - _dragStart.Y;

        if (_drag == Drag.Lasso)
        {
            _lasso.Add(position);
            _dragMoved = true;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (!_dragMoved && Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold && _drag != Drag.Pan && _drag != Drag.Box && _drag != Drag.Scrub && _drag != Drag.Zoom && _drag != Drag.CurveInsert)
        {
            return;
        }

        _dragMoved = true;
        MotionAsset? motion = Motion;

        switch (_drag)
        {
            case Drag.Pan:
                _camera = _panStart.Pan(dx, dy);
                break;

            case Drag.Scrub:
                CurrentTime = Math.Max(0, SnapTime(ToGraph(position).T));
                break;

            case Drag.Zoom:
            {
                // 마야 Alt+RMB: 오른쪽으로 끌면 시간 확대, 위로 끌면 값 확대. 누른 자리를 고정한다.
                double timeFactor = Math.Pow(1.01, dx);
                double valueFactor = Math.Pow(1.01, -dy);
                _camera = _panStart.ZoomAt(ToPlotPixel(_dragStart), Plot.Height, timeFactor, valueFactor);
                break;
            }

            case Drag.Scale:
                if (motion is not null)
                {
                    ScaleSelectionPreview(motion, dx, dy, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                }

                break;

            case Drag.RegionLeft:
            case Drag.RegionRight:
            case Drag.RegionTop:
            case Drag.RegionBottom:
                if (motion is not null)
                {
                    RegionScalePreview(motion, position);
                }

                break;

            case Drag.Keys:
                if (motion is not null)
                {
                    MoveSelectionPreview(motion, dx, dy, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                }

                break;

            case Drag.KeysAxis:
                if (motion is not null)
                {
                    if (!_axisLockDecided)
                    {
                        _axisLockHorizontal = Math.Abs(dx) >= Math.Abs(dy);
                        _axisLockDecided = true;
                    }

                    MoveSelectionPreview(motion, _axisLockHorizontal ? dx : 0, _axisLockHorizontal ? 0 : dy, axisLock: false, wholeSnap: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                }

                break;

            case Drag.CurveInsert:
                _camera = _panStart.Pan(dx, dy); // 움직이면 팬, 안 움직이고 놓으면 키 끼우기
                break;

            case Drag.InHandle:
            case Drag.OutHandle:
                if (motion is not null)
                {
                    DragHandlePreview(motion, position, _drag == Drag.InHandle);
                }

                break;

            case Drag.Handles:
                if (motion is not null)
                {
                    Pixel delta = new(ToPixel(position).X - ToPixel(_dragStart).X, ToPixel(position).Y - ToPixel(_dragStart).Y);

                    foreach (((KeyRef key, bool inward), Pixel end) in _handleDrag)
                    {
                        ApplyHandleSlope(motion, key, inward, new Pixel(end.X + delta.X, end.Y + delta.Y));
                    }
                }

                break;

            case Drag.Box:
                break;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    private void MoveSelectionPreview(MotionAsset motion, double dx, double dy, bool axisLock, bool wholeSnap = false)
    {
        if (axisLock)
        {
            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                dy = 0;
            }
            else
            {
                dx = 0;
            }
        }

        GraphPoint delta = _camera.DeltaToGraph(dx, dy);

        foreach (KeyRef keyRef in _selection)
        {
            if (keyRef.Resolve(motion) is not { } original || !_previewKeys.TryGetValue(keyRef, out KeyAsset? preview))
            {
                continue;
            }

            double t = original.T + delta.T;
            double v = original.V + delta.V;

            if (wholeSnap)
            {
                // Shift: 정확히 1fr·2fr… 정수 프레임, 값은 정수.
                t = Math.Round(t * TimeSnap.FramesPerSecond) / TimeSnap.FramesPerSecond;
                v = Math.Round(v);
            }
            else
            {
                t = SnapTime(t);
                v = SnapValue((float)v);
            }

            preview.T = (float)Math.Max(0, t);
            preview.V = (float)v;
        }
    }

    private float SnapValue(float value) => ValueSnapEnabled ? MathF.Round(value) : value;

    /// <summary>리전 모서리 드래그 — 한 축만, 반대쪽 모서리(<see cref="_scalePivot"/>, 보기값) 기준. 시간 축은 값을 안 건드린다.</summary>
    private void RegionScalePreview(MotionAsset motion, Point position)
    {
        GraphPoint at = ToGraph(position);
        double timeFactor = 1;
        double valueFactor = 1;

        if (_drag is Drag.RegionLeft or Drag.RegionRight)
        {
            double span = _regionEdge - _scalePivot.T;
            timeFactor = Math.Abs(span) < 1e-9 ? 1 : Math.Max(0.01, (at.T - _scalePivot.T) / span);
        }
        else
        {
            double span = _regionEdge - _scalePivot.V;
            valueFactor = Math.Abs(span) < 1e-9 ? 1 : (at.V - _scalePivot.V) / span;
        }

        foreach (KeyRef keyRef in _selection)
        {
            if (keyRef.Resolve(motion) is not { } original || !_previewKeys.TryGetValue(keyRef, out KeyAsset? preview))
            {
                continue;
            }

            preview.T = (float)Math.Max(0, SnapTime(_scalePivot.T + (original.T - _scalePivot.T) * timeFactor));
            preview.V = SnapValue((float)(_scalePivot.V + (original.V - _scalePivot.V) * valueFactor));
        }
    }

    /// <summary>스케일 툴 — 오른쪽으로 끌면 시간이, 위로 끌면 값이 피벗 기준으로 늘어난다(1px = 1%). Shift = 한 축만.</summary>
    private void ScaleSelectionPreview(MotionAsset motion, double dx, double dy, bool axisLock)
    {
        if (axisLock)
        {
            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                dy = 0;
            }
            else
            {
                dx = 0;
            }
        }

        double timeFactor = Math.Max(0.01, 1 + dx / 100.0);
        double valueFactor = 1 - dy / 100.0;

        foreach (KeyRef keyRef in _selection)
        {
            if (keyRef.Resolve(motion) is not { } original || !_previewKeys.TryGetValue(keyRef, out KeyAsset? preview))
            {
                continue;
            }

            preview.T = (float)Math.Max(0, SnapTime(_scalePivot.T + (original.T - _scalePivot.T) * timeFactor));
            preview.V = SnapValue((float)(_scalePivot.V + (original.V - _scalePivot.V) * valueFactor));
        }
    }

    private void DragHandlePreview(MotionAsset motion, Point position, bool inward)
        => ApplyHandleSlope(motion, _handleKey, inward, ToPixel(position));

    /// <summary>MMB 핸들 드래그 시작 — 고른 핸들 끝의 지금 픽셀을 기억하고 그 커브들을 미리보기로 든다.</summary>
    private bool BeginHandleDrag(MotionAsset motion)
    {
        _handleDrag.Clear();

        foreach ((KeyRef key, bool inward) in _selectedHandles)
        {
            if (key.Resolve(motion) is not { } k)
            {
                continue;
            }

            Pixel origin = ToPixel(ScreenOf(key.Curve, k.T, k.V));
            _handleDrag.Add(((key, inward), HandleMath.HandleEnd(origin, (inward ? k.InTangent : k.OutTangent), inward, _camera)));
        }

        if (_handleDrag.Count == 0)
        {
            return false;
        }

        BeginPreview(motion, _handleDrag.Select(h => h.Handle.Key.Curve).Distinct());
        return true;
    }

    /// <summary>핸들 끝 픽셀 → 기울기 미리보기. 끊긴 키는 그쪽만, 아니면 양쪽 같이.</summary>
    private void ApplyHandleSlope(MotionAsset motion, KeyRef keyRef, bool inward, Pixel handleEnd)
    {
        if (keyRef.Resolve(motion) is not { } original || !_previewKeys.TryGetValue(keyRef, out KeyAsset? preview))
        {
            return;
        }

        Pixel origin = ToPixel(ScreenOf(keyRef.Curve, original.T, original.V));
        float slope = (float)HandleMath.SlopeFromHandle(origin, handleEnd, inward, _camera);

        if (original.Broken)
        {
            if (inward)
            {
                preview.InTangent = slope;
            }
            else
            {
                preview.OutTangent = slope;
            }
        }
        else
        {
            preview.InTangent = slope;
            preview.OutTangent = slope;
        }
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
        MotionAsset? motion = Motion;

        try
        {
            switch (drag)
            {
                case Drag.CurveInsert:
                    if (_selectedCurves.Count > 0 && Math.Abs(_dragNow.X - _dragStart.X) < DragThreshold && Math.Abs(_dragNow.Y - _dragStart.Y) < DragThreshold)
                    {
                        InsertKeysOnSelectedCurves(ToGraph(_dragStart).T);
                    }

                    break;

                case Drag.Keys:
                    if (!_dragMoved && motion is not null && _selectedCurves.Count > 0)
                    {
                        InsertKeysOnSelectedCurves(ToGraph(_dragStart).T);
                    }
                    else if (_dragMoved && motion is not null)
                    {
                        CommitMove(motion);
                    }

                    break;

                case Drag.KeysAxis:
                case Drag.Scale:
                case Drag.RegionLeft:
                case Drag.RegionRight:
                case Drag.RegionTop:
                case Drag.RegionBottom:
                    if (_dragMoved && motion is not null)
                    {
                        CommitMove(motion);
                    }

                    break;

                case Drag.Lasso:
                    if (motion is not null)
                    {
                        _selection.AddRange(GraphHitTest.InPolygon(KeyCandidates(motion), _lasso.Select(ToPixel).ToList()));
                    }

                    _lasso.Clear();
                    break;

                case Drag.InHandle:
                case Drag.OutHandle:
                    if (_dragMoved && motion is not null && _editor is not null && _motionName is not null
                        && _previewKeys.TryGetValue(_handleKey, out KeyAsset? preview))
                    {
                        _editor.SetTangents(_motionName, _handleKey, preview.InTangent, preview.OutTangent);
                    }

                    break;

                case Drag.Handles:
                    if (_dragMoved && motion is not null && _editor is not null && _motionName is not null)
                    {
                        string name = _motionName;
                        var keep = _selectedHandles.ToList();
                        List<(KeyRef Key, KeyAsset Preview)> edits = _handleDrag.Select(h => h.Handle.Key).Distinct()
                            .Where(k => _previewKeys.ContainsKey(k)).Select(k => (k, _previewKeys[k])).ToList();
                        _editor.Batch(MotionChangeKind.Keys, () =>
                        {
                            foreach ((KeyRef key, KeyAsset p) in edits)
                            {
                                _editor.SetTangents(name, key, p.InTangent, p.OutTangent);
                            }
                        });
                        // 편집 뒤에도 핸들은 고른 채로(마야) — 선택 갱신이 비운 것을 되돌린다.
                        _selectedHandles.UnionWith(keep);
                    }

                    _handleDrag.Clear();
                    break;

                case Drag.Box:
                    if (motion is not null && _dragMoved)
                    {
                        Pixel a = ToPixel(_dragStart);
                        Pixel b = ToPixel(_dragNow);
                        List<KeyRef> inBox = GraphHitTest.InBox(KeyCandidates(motion), a, b);
                        List<(KeyRef Key, bool Inward)> handlesInBox = GraphHitTest.InBox(HandleCandidates(motion), a, b);

                        if (inBox.Count == 0 && handlesInBox.Count > 0)
                        {
                            // 키를 고른 채 핸들 끝을 박스로 — 핸들 선택(키 선택은 그대로). MMB 드래그가 돌린다.
                            if (!_boxShift)
                            {
                                _selectedHandles.Clear();
                            }

                            _selectedHandles.UnionWith(handlesInBox);
                        }
                        else
                        {
                            if (!_boxShift)
                            {
                                _selection.Clear();
                                _selectedCurves.Clear();
                                _selectedHandles.Clear();
                            }

                            if (inBox.Count > 0)
                            {
                                // 박스에 키가 들면 키만 — 커브는 하얘지지 않는다(마야).
                                _selection.AddRange(inBox);
                            }
                            else
                            {
                                // 키 없이 커브만 지나면 커브 선택(정확히 선을 안 눌러도 고를 수 있다).
                                foreach (CurveRef curve in CurvesInBox(motion, a, b))
                                {
                                    _selectedCurves.Add(curve);
                                }
                            }
                        }
                    }
                    else if (!_dragMoved && !_boxShift)
                    {
                        // 빈 자리 클릭 = 선택 비우기(키·커브·핸들).
                        _selection.Clear();
                        _selectedCurves.Clear();
                        _selectedHandles.Clear();
                    }

                    break;
            }
        }
        finally
        {
            _preview.Clear();
            _previewKeys.Clear();
            _dragMoved = false;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    /// <summary>드래그 확정 — 건드린 커브마다 키 목록을 통째로, 한 번의 조작으로. 선택은 새 시각으로 다시 푼다.</summary>
    private void CommitMove(MotionAsset motion)
    {
        if (_editor is null || _motionName is null)
        {
            return;
        }

        var moved = new List<(CurveRef Curve, float T)>();

        foreach (KeyRef keyRef in _selection)
        {
            if (_previewKeys.TryGetValue(keyRef, out KeyAsset? preview))
            {
                moved.Add((keyRef.Curve, preview.T));
            }
        }

        var edits = _preview.Select(pair => (pair.Key, (IEnumerable<KeyAsset>)pair.Value)).ToList();
        _editor.SetKeysMany(_motionName, edits);
        SelectByTime(moved);
        UpdateRegion();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        double factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        double timeFactor = shift ? 1 : factor;
        double valueFactor = ctrl ? 1 : factor;

        _camera = _camera.ZoomAt(ToPlotPixel(e.GetPosition(this)), Plot.Height, timeFactor, valueFactor);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            case Key.F:
                FitSelection();
                break;
            case Key.A when ctrl:
                SelectAll();
                break;
            case Key.A:
                FitAll();
                break;
            case Key.C when ctrl:
                CopySelection();
                break;
            case Key.X when ctrl:
                CutSelection();
                break;
            case Key.V when ctrl:
                Paste();
                break;
            case Key.B when ctrl && shift:
                SwapBuffer();
                break;
            case Key.B when ctrl:
                SnapshotBuffer();
                break;
            case Key.I:
                InsertKeyAtCurrentTime();
                break;
            case Key.Delete:
            case Key.Back:
                DeleteSelection();
                break;
            case Key.Escape:
                ClearSelection();
                break;
            case Key.D1 when ctrl:
                SetTangentMode(TangentMode.Auto);
                break;
            case Key.D2 when ctrl:
                SetTangentMode(TangentMode.Flat);
                break;
            case Key.D3 when ctrl:
                SetTangentMode(TangentMode.Linear);
                break;
            case Key.D4 when ctrl:
                SetTangentMode(TangentMode.Free);
                break;
            case Key.D5 when ctrl:
                SetTangentMode(TangentMode.Spline);
                break;
            case Key.D6 when ctrl:
                SetTangentMode(TangentMode.Plateau);
                break;
            case Key.Q:
                Tool = GraphTool.Select;
                break;
            case Key.W:
                Tool = GraphTool.Move;
                break;
            case Key.R:
                Tool = GraphTool.Scale;
                break;
            case Key.OemPeriod:
                JumpToKey(forward: true);
                break;
            case Key.OemComma:
                JumpToKey(forward: false);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
