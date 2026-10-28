using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.App.Views;

/// <summary>
/// 도프시트 — 왼쪽 트리(노드 › 채널 › 성분), 오른쪽 시간 레인에 키 블록.
///
/// 선택은 그래프와 **같은 모델**(<see cref="KeySelection"/>)이다. 블록 클릭·Shift·박스 선택, 블록 드래그 = 시간 이동,
/// Alt+드래그 = 복제, 눈금자 아래 범위 띠의 양끝 핸들 드래그 = 시간 스케일(반대쪽 끝이 기준점),
/// 성분 행 더블클릭 = 그 시각에 키, Delete, 눈금자 클릭·드래그 = 스크럽. 휠 = 시간 줌, 가운데 버튼/Alt+드래그 = 팬.
/// 노드 행 라벨 클릭 = 접기/펼치기. 요약 행의 블록 하나는 그 밑의 키 전부를 덮는다.
///
/// 드래그 중간값은 렌더만이고 놓는 순간이 편집 하나다. 산수는 <see cref="KeyOps"/>·<see cref="DopeSheetRows"/>다.
/// </summary>
public sealed class DopeSheetView : Control
{
    public const double LabelWidth = 220;
    public const double RulerHeight = 22;
    public const double RangeHeight = 12;
    public const double RowHeight = 22;
    private const double HitRadius = 7;
    private const double DragThreshold = 3;

    private static readonly IBrush Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 28));
    private static readonly IBrush LabelBackground = new SolidColorBrush(Color.FromArgb(255, 26, 26, 36));
    private static readonly IBrush RowStripe = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(40, 148, 163, 184)), 1);
    private static readonly Pen RulerPen = new(new SolidColorBrush(Color.FromArgb(130, 148, 163, 184)), 1);
    private static readonly Pen DurationPen = new(new SolidColorBrush(Color.FromArgb(180, 251, 146, 60)), 1);
    private static readonly Pen CurrentTimePen = new(new SolidColorBrush(Color.FromArgb(220, 34, 211, 238)), 1.5);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromArgb(200, 203, 213, 225));
    private static readonly IBrush DimLabelBrush = new SolidColorBrush(Color.FromArgb(120, 203, 213, 225));
    private static readonly IBrush BlockBrush = new SolidColorBrush(Color.FromArgb(230, 226, 232, 240));
    private static readonly IBrush SummaryBlockBrush = new SolidColorBrush(Color.FromArgb(170, 148, 163, 184));
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.FromArgb(255, 250, 204, 21));
    private static readonly IBrush GhostBrush = new SolidColorBrush(Color.FromArgb(110, 250, 204, 21));
    private static readonly IBrush RangeBrush = new SolidColorBrush(Color.FromArgb(70, 125, 211, 252));
    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.FromArgb(240, 125, 211, 252));
    private static readonly Pen BoxPen = new(new SolidColorBrush(Color.FromArgb(200, 125, 211, 252)), 1, DashStyle.Dash);
    private static readonly IBrush BoxFill = new SolidColorBrush(Color.FromArgb(30, 125, 211, 252));

    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly List<DopeRow> _rows = new();

    private KeySelection _selection = new();
    private CurveFlags _flags = new();
    private MotionEditor? _editor;

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

    private bool IsLocked(DopeBlock block) => block.Keys.Count > 0 && block.Keys.All(k => _flags.IsLocked(k.Curve));
    private string? _motionName;
    private double _timeOrigin = -0.05;
    private double _pixelsPerSecond = 400;
    private bool _fitted;
    private double _currentTime;

    private enum Drag { None, Blocks, Duplicate, Box, Scrub, ScaleLeft, ScaleRight, Pan }

    private Drag _drag = Drag.None;
    private Point _dragStart;
    private Point _dragNow;
    private bool _dragMoved;
    private double _panStartOrigin;
    private double _previewDelta;
    private double _previewPivot;
    private double _previewFactor = 1;

    public DopeSheetView()
    {
        MinHeight = 100;
        ClipToBounds = true;
        Focusable = true;
        _selection.Changed += OnSelectionChanged;
        _flags.Changed += OnFlagsChanged;
    }

    public event EventHandler? SelectionChanged;

    public event EventHandler? CurrentTimeChanged;

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

    public bool SnapEnabled { get; set; } = true;

    /// <summary>눈금 단위(마야처럼 프레임이 기본).</summary>
    public TimeDisplay TimeDisplay { get; set; } = TimeDisplay.Frames;

    public double SnapStep { get; set; } = TimeSnap.DefaultStep;

    /// <summary>복사/붙여넣기 자리 — 그래프와 같은 객체를 꽂는다.</summary>
    public KeyClipboardSlot Clipboard { get; set; } = new();

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

    public void Paste()
    {
        if (_editor is null || _motionName is null || Clipboard.Current is not { } clipboard || clipboard.Count == 0)
        {
            return;
        }

        float at = (float)SnapTime(CurrentTime);
        _selection.Set(_editor.PasteKeys(_motionName, clipboard, at, Snapper()));
    }

    public string? MotionName => _motionName;

    private ControllerRig? _controllers;

    /// <summary>노드 행 라벨용 컨트롤러 표(모션의 리그) — 없으면 노드 이름.</summary>
    public ControllerRig? Controllers
    {
        get => _controllers;
        set
        {
            if (ReferenceEquals(_controllers, value))
            {
                return;
            }

            _controllers = value;
            RebuildRows();
            InvalidateVisual();
        }
    }

    public MotionAsset? Motion => _editor is not null && _motionName is not null ? _editor.Document.Find(_motionName) : null;

    public IReadOnlyList<DopeRow> Rows => _rows;

    public double TimeOrigin => _timeOrigin;

    public double PixelsPerSecond => _pixelsPerSecond;

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

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

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

        _fitted = false;
        RebuildRows();
        FitAll();
    }

    private void OnEditorChanged(object? sender, MotionChangedEventArgs e)
    {
        RebuildRows();
        _selection.Prune(Motion);
        InvalidateVisual();
    }

    private void RebuildRows()
    {
        _rows.Clear();

        if (Motion is { } motion)
        {
            _rows.AddRange(DopeSheetRows.Build(motion, node => _collapsed.Contains(node), _controllers));
        }
    }

    public bool IsCollapsed(string node) => _collapsed.Contains(node);

    public void ToggleCollapsed(string node)
    {
        if (!_collapsed.Remove(node))
        {
            _collapsed.Add(node);
        }

        RebuildRows();
        InvalidateVisual();
    }

    // ── 좌표 ──────────────────────────────────────────────────────────

    public Rect RulerRect => new(LabelWidth, 0, Math.Max(1, Bounds.Width - LabelWidth), RulerHeight);

    public Rect RangeRect => new(LabelWidth, RulerHeight, Math.Max(1, Bounds.Width - LabelWidth), RangeHeight);

    public Rect LaneRect => new(LabelWidth, RulerHeight + RangeHeight, Math.Max(1, Bounds.Width - LabelWidth), Math.Max(1, Bounds.Height - RulerHeight - RangeHeight));

    public Rect RowRect(int index) => new(0, RulerHeight + RangeHeight + index * RowHeight, Math.Max(1, Bounds.Width), RowHeight);

    public double XAtTime(double seconds) => LabelWidth + (seconds - _timeOrigin) * _pixelsPerSecond;

    public double TimeAtX(double x) => _timeOrigin + (x - LabelWidth) / _pixelsPerSecond;

    /// <summary>블록의 중심(컨트롤 좌표). 행이 안 보이면 null.</summary>
    public Point? BlockPosition(DopeRow row, DopeBlock block)
    {
        int index = _rows.IndexOf(row);
        return index < 0 ? null : new Point(XAtTime(block.Time), RowRect(index).Center.Y);
    }

    public int RowIndexAt(double y)
    {
        double top = RulerHeight + RangeHeight;
        int index = (int)Math.Floor((y - top) / RowHeight);
        return y >= top && index >= 0 && index < _rows.Count ? index : -1;
    }

    private double SnapTime(double seconds) => SnapEnabled ? TimeSnap.Snap(seconds, SnapStep) : seconds;

    public void FitAll()
    {
        MotionAsset? motion = Motion;
        double end = Math.Max(motion?.Duration ?? 1f, 0.1);

        foreach (DopeRow row in _rows)
        {
            foreach (DopeBlock block in row.Blocks)
            {
                end = Math.Max(end, block.Time);
            }
        }

        double width = Math.Max(1, Bounds.Width - LabelWidth);
        const double padding = 28;
        _pixelsPerSecond = Math.Clamp((width - padding * 2) / end, GraphCamera.MinPixelsPerSecond, GraphCamera.MaxPixelsPerSecond);
        _timeOrigin = -padding / _pixelsPerSecond;
        _fitted = Bounds.Width > 0;
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (!_fitted && e.NewSize.Width > 0)
        {
            FitAll();
        }
    }

    // ── 편집 명령 ─────────────────────────────────────────────────────

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

    /// <summary>성분 행의 그 시각에 키(값은 곡선 위). 요약 행이면 아무것도 안 한다.</summary>
    public void InsertKey(DopeRow row, double seconds)
    {
        MotionAsset? motion = Motion;

        if (_editor is null || _motionName is null || motion is null || row.Curve is not { } curveRef || curveRef.Resolve(motion) is not { } curve)
        {
            return;
        }

        float t = (float)Math.Max(0, SnapTime(seconds));
        _editor.SetKeysMany(_motionName, new[] { (curveRef, (IEnumerable<KeyAsset>)KeyOps.WithKeyOnCurve(curve.Keys, t)) });
        SelectByTime(new[] { (curveRef, t) });
    }

    public void MoveSelection(double deltaSeconds)
    {
        if (_editor is null || _motionName is null || _selection.Count == 0)
        {
            return;
        }

        SelectByTime(_editor.MoveKeys(_motionName, _selection.ToList(), (float)deltaSeconds, Snapper()));
    }

    public void DuplicateSelection(double offsetSeconds)
    {
        if (_editor is null || _motionName is null || _selection.Count == 0)
        {
            return;
        }

        SelectByTime(_editor.DuplicateKeys(_motionName, _selection.ToList(), (float)offsetSeconds, Snapper()));
    }

    public void ScaleSelection(double pivotSeconds, double factor)
    {
        if (_editor is null || _motionName is null || _selection.Count == 0 || !(factor > 0))
        {
            return;
        }

        SelectByTime(_editor.ScaleKeys(_motionName, _selection.ToList(), (float)pivotSeconds, (float)factor, Snapper()));
    }

    private Func<float, float>? Snapper() => SnapEnabled ? t => (float)TimeSnap.Snap(t, SnapStep) : null;

    /// <summary>선택 키들의 시각 범위. 둘 이상 다른 시각일 때만 있다 — 스케일 핸들의 조건.</summary>
    public (double Min, double Max)? SelectionRange()
    {
        MotionAsset? motion = Motion;

        if (motion is null)
        {
            return null;
        }

        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;

        foreach (KeyRef key in _selection)
        {
            if (key.Resolve(motion) is { } k)
            {
                min = Math.Min(min, k.T);
                max = Math.Max(max, k.T);
            }
        }

        return max - min > MotionRules.Tolerance ? (min, max) : null;
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

    // ── 그리기 ────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));
        context.FillRectangle(LabelBackground, new Rect(0, 0, LabelWidth, Bounds.Height));

        DrawRuler(context);
        DrawRange(context);

        MotionAsset? motion = Motion;

        if (motion is null)
        {
            DrawLabel(context, "모션을 고르면 여기에 키가 보입니다.", new Point(8, RulerHeight + RangeHeight + 6), LabelBrush);
            return;
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            DopeRow row = _rows[i];
            Rect rect = RowRect(i);

            if (rect.Y > Bounds.Height)
            {
                break;
            }

            if (i % 2 == 1)
            {
                context.FillRectangle(RowStripe, rect);
            }

            string prefix = row.Level == DopeRowLevel.Node ? (_collapsed.Contains(row.Node) ? "▸ " : "▾ ") : "";
            DrawLabel(context, prefix + row.Label, new Point(8 + row.Depth * 14, rect.Y + 4), row.Level == DopeRowLevel.Component ? DimLabelBrush : LabelBrush);
        }

        using (context.PushClip(LaneRect))
        {
            DrawCurrentTime(context);

            for (int i = 0; i < _rows.Count; i++)
            {
                DopeRow row = _rows[i];
                Rect rect = RowRect(i);

                foreach (DopeBlock block in row.Blocks)
                {
                    bool selected = block.Keys.Any(_selection.Contains);
                    double time = selected ? PreviewTime(block.Time) : block.Time;
                    var center = new Point(XAtTime(time), rect.Center.Y);

                    if (selected && _drag == Drag.Duplicate && _dragMoved)
                    {
                        DrawBlock(context, row.Level, new Point(XAtTime(block.Time), rect.Center.Y), SelectedBrush);
                        DrawBlock(context, row.Level, center, GhostBrush);
                        continue;
                    }

                    DrawBlock(context, row.Level, center, selected ? SelectedBrush : IsLocked(block) ? DimLabelBrush : row.Level == DopeRowLevel.Component ? BlockBrush : SummaryBlockBrush);
                }
            }

            if (_drag == Drag.Box)
            {
                var box = new Rect(_dragStart, _dragNow);
                context.FillRectangle(BoxFill, box);
                context.DrawRectangle(BoxPen, box);
            }
        }
    }

    private double PreviewTime(double time)
    {
        if (!_dragMoved)
        {
            return time;
        }

        return _drag switch
        {
            Drag.Blocks or Drag.Duplicate => Math.Max(0, SnapTime(time + _previewDelta)),
            Drag.ScaleLeft or Drag.ScaleRight => Math.Max(0, SnapTime(_previewPivot + (time - _previewPivot) * _previewFactor)),
            _ => time,
        };
    }

    private static void DrawBlock(DrawingContext context, DopeRowLevel level, Point center, IBrush brush)
    {
        switch (level)
        {
            case DopeRowLevel.Component:
                var diamond = new PolylineGeometry(new[]
                {
                    new Point(center.X, center.Y - 6), new Point(center.X + 6, center.Y),
                    new Point(center.X, center.Y + 6), new Point(center.X - 6, center.Y), new Point(center.X, center.Y - 6),
                }, true);
                context.DrawGeometry(brush, null, diamond);
                break;
            case DopeRowLevel.Channel:
                context.FillRectangle(brush, new Rect(center.X - 4, center.Y - 6, 8, 12));
                break;
            default:
                context.FillRectangle(brush, new Rect(center.X - 5, center.Y - 7, 10, 14));
                break;
        }
    }

    private void DrawRuler(DrawingContext context)
    {
        Rect ruler = RulerRect;
        context.DrawLine(RulerPen, new Point(ruler.X, ruler.Bottom), new Point(ruler.Right, ruler.Bottom));

        double step = TimeSnap.TickStep(_pixelsPerSecond);
        double start = Math.Floor(TimeAtX(ruler.X) / step) * step;
        double end = TimeAtX(ruler.Right);

        for (double t = start; t <= end; t += step)
        {
            double x = XAtTime(t);
            context.DrawLine(GridPen, new Point(x, ruler.Y + 14), new Point(x, Bounds.Height));
            DrawLabel(context, TimeText.Format(t, TimeDisplay), new Point(x + 3, ruler.Y + 2), DimLabelBrush);
        }

        if (Motion is { } motion)
        {
            double x = XAtTime(motion.Duration);
            context.DrawLine(DurationPen, new Point(x, ruler.Y), new Point(x, Bounds.Height));
        }
    }

    private void DrawRange(DrawingContext context)
    {
        Rect range = RangeRect;

        if (SelectionRange() is not { } span)
        {
            return;
        }

        double min = PreviewTime(span.Min);
        double max = PreviewTime(span.Max);
        double left = XAtTime(Math.Min(min, max));
        double right = XAtTime(Math.Max(min, max));
        context.FillRectangle(RangeBrush, new Rect(left, range.Y + 2, Math.Max(1, right - left), range.Height - 4));
        context.FillRectangle(HandleBrush, HandleRect(left, range));
        context.FillRectangle(HandleBrush, HandleRect(right, range));
    }

    private static Rect HandleRect(double x, Rect range) => new(x - 4, range.Y + 1, 8, range.Height - 2);

    private void DrawCurrentTime(DrawingContext context)
    {
        double x = XAtTime(_currentTime);
        context.DrawLine(CurrentTimePen, new Point(x, RulerHeight), new Point(x, Bounds.Height));
    }

    private static void DrawLabel(DrawingContext context, string text, Point at, IBrush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, brush)
        {
            MaxTextWidth = LabelWidth,
        };
        context.DrawText(formatted, at);
    }

    // ── 입력 ──────────────────────────────────────────────────────────

    private bool TryHitBlock(Point at, out DopeRow row, out DopeBlock block)
    {
        int index = RowIndexAt(at.Y);

        if (index >= 0 && at.X >= LabelWidth)
        {
            DopeRow candidate = _rows[index];

            foreach (DopeBlock b in candidate.Blocks)
            {
                if (!IsLocked(b) && Math.Abs(XAtTime(b.Time) - at.X) <= HitRadius)
                {
                    row = candidate;
                    block = b;
                    return true;
                }
            }
        }

        row = null!;
        block = null!;
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
        _dragStart = position;
        _dragNow = position;
        _dragMoved = false;

        if (point.Properties.IsMiddleButtonPressed || (point.Properties.IsLeftButtonPressed && alt && position.X < LabelWidth))
        {
            StartDrag(Drag.Pan, e);
            _panStartOrigin = _timeOrigin;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // 눈금자: 스크럽.
        if (RulerRect.Contains(position))
        {
            CurrentTime = Math.Max(0, SnapTime(TimeAtX(position.X)));
            StartDrag(Drag.Scrub, e);
            return;
        }

        // 범위 띠: 스케일 핸들.
        if (RangeRect.Contains(position) && SelectionRange() is { } span)
        {
            Rect range = RangeRect;

            if (HandleRect(XAtTime(span.Max), range).Inflate(3).Contains(position))
            {
                _previewPivot = span.Min;
                StartDrag(Drag.ScaleRight, e);
                return;
            }

            if (HandleRect(XAtTime(span.Min), range).Inflate(3).Contains(position))
            {
                _previewPivot = span.Max;
                StartDrag(Drag.ScaleLeft, e);
                return;
            }
        }

        // 라벨 열: 노드 행은 접기/펼치기.
        if (position.X < LabelWidth)
        {
            int index = RowIndexAt(position.Y);

            if (index >= 0 && _rows[index].Level == DopeRowLevel.Node)
            {
                ToggleCollapsed(_rows[index].Node);
                e.Handled = true;
            }

            return;
        }

        if (Motion is null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            int index = RowIndexAt(position.Y);

            if (index >= 0)
            {
                InsertKey(_rows[index], TimeAtX(position.X));
            }

            e.Handled = true;
            return;
        }

        if (TryHitBlock(position, out DopeRow _, out DopeBlock block))
        {
            if (shift)
            {
                bool allSelected = block.Keys.All(_selection.Contains);

                if (allSelected)
                {
                    _selection.RemoveWhere(k => block.Keys.Contains(k));
                }
                else
                {
                    _selection.AddRange(block.Keys);
                }
            }
            else if (!block.Keys.All(_selection.Contains))
            {
                _selection.Set(block.Keys);
            }

            if (block.Keys.Any(_selection.Contains))
            {
                _previewDelta = 0;
                StartDrag(alt ? Drag.Duplicate : Drag.Blocks, e);
            }

            e.Handled = true;
            return;
        }

        if (!shift)
        {
            _selection.Clear();
        }

        StartDrag(Drag.Box, e);
    }

    private void StartDrag(Drag drag, PointerPressedEventArgs e)
    {
        _drag = drag;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
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

        if (!_dragMoved && Math.Abs(dx) < DragThreshold && Math.Abs(position.Y - _dragStart.Y) < DragThreshold && _drag != Drag.Scrub && _drag != Drag.Pan)
        {
            return;
        }

        _dragMoved = true;

        switch (_drag)
        {
            case Drag.Scrub:
                CurrentTime = Math.Max(0, SnapTime(TimeAtX(position.X)));
                break;
            case Drag.Pan:
                _timeOrigin = _panStartOrigin - dx / _pixelsPerSecond;
                break;
            case Drag.Blocks:
            case Drag.Duplicate:
                _previewDelta = dx / _pixelsPerSecond;
                break;
            case Drag.ScaleLeft:
            case Drag.ScaleRight:
                if (SelectionRange() is { } span)
                {
                    double movingEnd = _drag == Drag.ScaleRight ? span.Max : span.Min;
                    double target = TimeAtX(position.X);
                    double denominator = movingEnd - _previewPivot;
                    _previewFactor = Math.Abs(denominator) < 1e-9 ? 1 : Math.Max(0.01, (target - _previewPivot) / denominator);
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
        MotionAsset? motion = Motion;

        try
        {
            switch (drag)
            {
                case Drag.Blocks when _dragMoved:
                    MoveSelection(_previewDelta);
                    break;
                case Drag.Duplicate when _dragMoved:
                    DuplicateSelection(_previewDelta);
                    break;
                case Drag.ScaleLeft when _dragMoved:
                case Drag.ScaleRight when _dragMoved:
                    ScaleSelection(_previewPivot, _previewFactor);
                    break;
                case Drag.Box when motion is not null:
                    SelectInBox(new Rect(_dragStart, _dragNow));
                    break;
            }
        }
        finally
        {
            _dragMoved = false;
            _previewDelta = 0;
            _previewFactor = 1;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    private void SelectInBox(Rect box)
    {
        var hits = new List<KeyRef>();

        for (int i = 0; i < _rows.Count; i++)
        {
            Rect rect = RowRect(i);

            if (rect.Bottom < box.Y || rect.Y > box.Bottom)
            {
                continue;
            }

            foreach (DopeBlock block in _rows[i].Blocks)
            {
                double x = XAtTime(block.Time);

                if (x >= box.X && x <= box.Right)
                {
                    hits.AddRange(block.Keys.Where(k => !_flags.IsLocked(k.Curve)));
                }
            }
        }

        _selection.AddRange(hits);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        double anchorX = e.GetPosition(this).X;
        double anchorTime = TimeAtX(anchorX);
        _pixelsPerSecond = Math.Clamp(_pixelsPerSecond * factor, GraphCamera.MinPixelsPerSecond, GraphCamera.MaxPixelsPerSecond);
        _timeOrigin = anchorTime - (anchorX - LabelWidth) / _pixelsPerSecond;
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
            case Key.Delete:
            case Key.Back:
                DeleteSelection();
                break;
            case Key.A when ctrl:
                _selection.Set(_rows.Where(r => r.Level == DopeRowLevel.Component).SelectMany(r => r.Blocks).SelectMany(b => b.Keys));
                break;
            case Key.A:
                FitAll();
                break;
            case Key.Escape:
                _selection.Clear();
                break;
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                _editor?.Redo();
                break;
            case Key.Z when ctrl:
                _editor?.Undo();
                break;
            case Key.D when ctrl:
                DuplicateSelection(1.0 / TimeSnap.FramesPerSecond);
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
            case Key.OemPeriod:
            case Key.OemComma:
                if (Motion is { } motion)
                {
                    List<float> times = KeyTimes.All(motion);
                    float? target = e.Key == Key.OemPeriod ? KeyTimes.Next(times, CurrentTime) : KeyTimes.Previous(times, CurrentTime);

                    if (target is not null)
                    {
                        CurrentTime = target.Value;
                    }
                }

                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
