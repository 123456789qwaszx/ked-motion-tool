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
    private const double PadLeft = 52;
    private const double PadRight = 10;
    private const double PadTop = 10;
    private const double PadBottom = 24;
    private const double DragThreshold = 3;

    private static readonly IBrush Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 28));
    private static readonly IBrush PlotBackground = new SolidColorBrush(Color.FromArgb(255, 26, 26, 36));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(40, 148, 163, 184)), 1);
    private static readonly Pen AxisPen = new(new SolidColorBrush(Color.FromArgb(130, 148, 163, 184)), 1);
    private static readonly Pen DurationPen = new(new SolidColorBrush(Color.FromArgb(180, 251, 146, 60)), 1);
    private static readonly Pen CurrentTimePen = new(new SolidColorBrush(Color.FromArgb(200, 34, 211, 238)), 1);
    private static readonly Pen BoxPen = new(new SolidColorBrush(Color.FromArgb(200, 125, 211, 252)), 1, DashStyle.Dash);
    private static readonly IBrush BoxFill = new SolidColorBrush(Color.FromArgb(30, 125, 211, 252));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromArgb(170, 203, 213, 225));
    private static readonly IBrush SelectedKeyBrush = new SolidColorBrush(Color.FromArgb(255, 250, 204, 21));
    private static readonly Pen HandlePen = new(new SolidColorBrush(Color.FromArgb(210, 125, 211, 252)), 1);
    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.FromArgb(235, 125, 211, 252));
    private static readonly IBrush ValueLabelBrush = new SolidColorBrush(Color.FromArgb(230, 250, 204, 21));
    private static readonly IBrush PivotBrush = new SolidColorBrush(Color.FromArgb(230, 251, 146, 60));

    private readonly List<CurveSlot> _curves = new();
    private KeySelection _selection = new();

    private MotionEditor? _editor;
    private string? _motionName;
    private GraphCamera _camera = new(-0.1, -1, 400, 100);
    private bool _cameraFitted;
    private double _currentTime;

    private enum Drag { None, Keys, InHandle, OutHandle, Box, Pan, Scrub, Zoom, Scale, Lasso }

    /// <summary>마야 그래프 에디터의 툴: 선택(Q)·이동(W)·스케일(R). MMB 드래그는 툴과 무관하게 이동이다.</summary>
    public GraphTool Tool { get; set; } = GraphTool.Select;

    private readonly List<Point> _lasso = new();
    private GraphPoint _scalePivot;

    private Drag _drag = Drag.None;
    private Point _dragStart;
    private Point _dragNow;
    private bool _dragMoved;
    private KeyRef _handleKey;
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
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
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
        _cameraFitted = false;
        ReloadCurves();
        FitAll();
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

    private void PruneSelection() => _selection.Prune(Motion);

    /// <summary>축이 색을 정한다(x 빨강·y 초록·z 파랑). 채널 순서가 색조를 조금씩 돌려 같은 축 둘을 가른다.</summary>
    public static Color ColorFor(Axis axis, int channelIndex)
    {
        double baseHue = axis switch { Axis.X => 4, Axis.Y => 130, _ => 212 };
        double hue = (baseHue + channelIndex * 22) % 360;
        return new HsvColor(1.0, hue, 0.72, 0.95).ToRgb();
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
        return asset is null ? null : ToScreen(new GraphPoint(asset.T, asset.V));
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
                    keys.AddRange(curve.Keys);
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
                keys.Add(k);
            }
        }

        FitTo(keys, null, null);
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
        IEnumerable<CurveRef> targets = _selection.Count > 0
            ? _selection.Select(k => k.Curve).Distinct()
            : _curves.Where(s => s.Visible).Select(s => s.Curve);

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

                List<GraphPoint> points = CurveSampler.Sample(keys, left.T, right.T, samples);
                var pen = new Pen(new SolidColorBrush(slot.Color), 2);
                var polyline = new PolylineGeometry(points.Select(ToScreen), false);
                context.DrawGeometry(null, pen, polyline);
            }

            foreach (CurveSlot slot in _curves)
            {
                if (!slot.Visible)
                {
                    continue;
                }

                List<KeyAsset> keys = KeysForRender(slot.Curve, motion);
                var keyBrush = new SolidColorBrush(slot.Color);

                for (int i = 0; i < keys.Count; i++)
                {
                    var keyRef = new KeyRef(slot.Curve, i);
                    Point center = ToScreen(new GraphPoint(keys[i].T, keys[i].V));
                    bool selected = _selection.Contains(keyRef);

                    if (selected)
                    {
                        DrawHandles(context, keys, i, center);
                        DrawLabel(context, $"{keys[i].T:0.###}s  {keys[i].V:0.###}", new Point(center.X + 7, center.Y - 16), ValueLabelBrush, 10);
                    }

                    context.FillRectangle(selected ? SelectedKeyBrush : keyBrush, new Rect(center.X - 4, center.Y - 4, 8, 8));
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

    private void DrawHandles(DrawingContext context, List<KeyAsset> keys, int index, Point center)
    {
        KeyAsset key = keys[index];
        Pixel origin = ToPixel(center);

        if (index > 0)
        {
            Point inEnd = ToPoint(HandleMath.HandleEnd(origin, key.InTangent, inward: true, _camera));
            context.DrawLine(HandlePen, center, inEnd);
            context.DrawEllipse(HandleBrush, null, inEnd, 3.5, 3.5);
        }

        if (index < keys.Count - 1)
        {
            Point outEnd = ToPoint(HandleMath.HandleEnd(origin, key.OutTangent, inward: false, _camera));
            context.DrawLine(HandlePen, center, outEnd);
            context.DrawEllipse(HandleBrush, null, outEnd, 3.5, 3.5);
        }
    }

    private void DrawGrid(DrawingContext context, Rect plot, MotionAsset? motion)
    {
        GraphPoint bottomLeft = ToGraph(new Point(plot.X, plot.Bottom));
        GraphPoint topRight = ToGraph(new Point(plot.Right, plot.Y));

        // 시간 눈금 — 프레임 친화 단계. 라벨은 초와 24fps 프레임.
        double tStep = TimeSnap.TickStep(_camera.PixelsPerSecond);
        double tStart = Math.Floor(bottomLeft.T / tStep) * tStep;

        for (double t = tStart; t <= topRight.T; t += tStep)
        {
            Point x = ToScreen(new GraphPoint(t, 0));
            context.DrawLine(GridPen, new Point(x.X, plot.Y), new Point(x.X, plot.Bottom));
            string label = Math.Abs(t) < 1e-9 ? "0" : $"{t:0.###}s · {TimeSnap.ToFrames(t):0.#}f";
            DrawLabel(context, label, new Point(x.X + 3, plot.Bottom + 4), LabelBrush, 10);
        }

        double vStep = TimeSnap.ValueTickStep(_camera.PixelsPerValue);
        double vStart = Math.Floor(bottomLeft.V / vStep) * vStep;

        for (double v = vStart; v <= topRight.V; v += vStep)
        {
            Point y = ToScreen(new GraphPoint(0, v));
            context.DrawLine(GridPen, new Point(plot.X, y.Y), new Point(plot.Right, y.Y));
            DrawLabel(context, $"{v:0.###}", new Point(4, y.Y - 7), LabelBrush, 10);
        }

        // 기준선: 값 0 · 시각 0 · 길이 · 현재 시각.
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
            if (!slot.Visible || slot.Curve.Resolve(motion) is not { } curve)
            {
                continue;
            }

            for (int i = 0; i < curve.Keys.Count; i++)
            {
                candidates.Add((new KeyRef(slot.Curve, i), ToPixel(ToScreen(new GraphPoint(curve.Keys[i].T, curve.Keys[i].V)))));
            }
        }

        return candidates;
    }

    private bool TryHitHandle(MotionAsset motion, Pixel at, out KeyRef key, out bool inward)
    {
        var candidates = new List<((KeyRef Key, bool Inward) Item, Pixel At)>();

        foreach (KeyRef keyRef in _selection)
        {
            if (keyRef.Curve.Resolve(motion) is not { } curve || keyRef.Index < 0 || keyRef.Index >= curve.Keys.Count)
            {
                continue;
            }

            KeyAsset k = curve.Keys[keyRef.Index];
            Pixel origin = ToPixel(ToScreen(new GraphPoint(k.T, k.V)));

            if (keyRef.Index > 0)
            {
                candidates.Add(((keyRef, true), HandleMath.HandleEnd(origin, k.InTangent, inward: true, _camera)));
            }

            if (keyRef.Index < curve.Keys.Count - 1)
            {
                candidates.Add(((keyRef, false), HandleMath.HandleEnd(origin, k.OutTangent, inward: false, _camera)));
            }
        }

        if (GraphHitTest.TryNearest(candidates, at, GraphHitTest.HandleRadius, out (KeyRef Key, bool Inward) hit))
        {
            key = hit.Key;
            inward = hit.Inward;
            return true;
        }

        key = default;
        inward = false;
        return false;
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

        // MMB 드래그 = 선택 키 이동(마야). 툴과 무관하다.
        if (point.Properties.IsMiddleButtonPressed)
        {
            if (pressedMotion is not null && _selection.Count > 0)
            {
                BeginPreview(pressedMotion, _selection.Select(k => k.Curve).Distinct());
                StartDrag(Drag.Keys, position, e);
            }

            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // 아래 눈금자: 스크럽(마야 타임 슬라이더와 같다).
        if (position.Y >= Plot.Bottom)
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

        // 빈 자리: 박스 선택. Shift가 아니면 선택을 비운다.
        if (!shift)
        {
            _selection.Clear();
        }

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
            if (!slot.Visible || slot.Curve.Resolve(motion) is not { } curve)
            {
                continue;
            }

            List<Pixel> polyline = CurveSampler.Sample(curve.Keys, left.T, right.T, 256).Select(p => ToPixel(ToScreen(p))).ToList();
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

        if (!_dragMoved && Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold && _drag != Drag.Pan && _drag != Drag.Box && _drag != Drag.Scrub && _drag != Drag.Zoom)
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

            case Drag.Keys:
                if (motion is not null)
                {
                    MoveSelectionPreview(motion, dx, dy, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                }

                break;

            case Drag.InHandle:
            case Drag.OutHandle:
                if (motion is not null)
                {
                    DragHandlePreview(motion, position, _drag == Drag.InHandle);
                }

                break;

            case Drag.Box:
                break;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    private void MoveSelectionPreview(MotionAsset motion, double dx, double dy, bool axisLock)
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

            preview.T = (float)Math.Max(0, SnapTime(original.T + delta.T));
            preview.V = (float)(original.V + delta.V);
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
            preview.V = (float)(_scalePivot.V + (original.V - _scalePivot.V) * valueFactor);
        }
    }

    private void DragHandlePreview(MotionAsset motion, Point position, bool inward)
    {
        if (_handleKey.Resolve(motion) is not { } original || !_previewKeys.TryGetValue(_handleKey, out KeyAsset? preview))
        {
            return;
        }

        Pixel origin = ToPixel(ToScreen(new GraphPoint(original.T, original.V)));
        float slope = (float)HandleMath.SlopeFromHandle(origin, ToPixel(position), inward, _camera);

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
                case Drag.Keys:
                case Drag.Scale:
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

                case Drag.Box:
                    if (motion is not null)
                    {
                        List<KeyRef> inBox = GraphHitTest.InBox(KeyCandidates(motion), ToPixel(_dragStart), ToPixel(_dragNow));
                        _selection.AddRange(inBox);
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
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                _editor?.Redo();
                break;
            case Key.Z when ctrl:
                _editor?.Undo();
                break;
            case Key.D1:
                SetTangentMode(TangentMode.Auto);
                break;
            case Key.D2:
                SetTangentMode(TangentMode.Flat);
                break;
            case Key.D3:
                SetTangentMode(TangentMode.Linear);
                break;
            case Key.D4:
                SetTangentMode(TangentMode.Free);
                break;
            case Key.D5:
                SetTangentMode(TangentMode.Spline);
                break;
            case Key.D6:
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
