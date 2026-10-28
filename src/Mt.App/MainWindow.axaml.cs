using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Ked.Presentation.Core;
using Mt.App.Services;
using Mt.App.Views;
using Mt.Authoring;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Settings;
using Mt.Authoring.Validation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Mt.App;

/// <summary>
/// 셸 — 왼쪽 모션·채널 목록, 가운데 그래프, 오른쪽 인스펙터, 아래 상태·문제.
/// 모든 편집은 <see cref="MotionEditor"/> 명령이고, 화면은 <see cref="MotionEditor.Changed"/>를 듣고 다시 읽는다.
/// 파일은 <see cref="DocumentSession"/>을 지난다.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string[] EndTokens = { "restore", "commit" };
    private static readonly string[] ModeTokens = { "relative", "absolute" };
    private static readonly string[] KindTokens = ChannelKindLabel.All;

    private readonly DocumentSession _session = new();
    private readonly GraphEditorView _graph = new();
    private readonly DopeSheetView _dope = new();
    private readonly ViewportView _viewport = new();

    /// <summary>그래프와 도프시트가 같은 선택을 든다 — 한 모델, 두 뷰.</summary>
    private readonly KeySelection _selection = new();

    /// <summary>잠금·뮤트 — 그래프·도프시트·뷰포트가 같은 객체를 든다.</summary>
    private readonly CurveFlags _flags = new();
    private readonly ComboBox _viewMode = new() { ItemsSource = new[] { "절대", "정규화", "쌓기" }, SelectedIndex = 0, FontSize = 11 };
    private readonly KeyClipboardSlot _clipboard = new();
    private readonly ToolSettings _settings;
    private readonly string _settingsPath;
    private readonly DispatcherTimer _autoSaveTimer = new();
    private readonly Button _recentButton = new() { Content = "최근…", FontSize = 11, Padding = new Avalonia.Thickness(8, 3) };
    private readonly ComboBox _onionFrames = new() { ItemsSource = new[] { "0", "1", "2", "3", "4", "6" }, SelectedIndex = 2, FontSize = 11 };
    private readonly ComboBox _snapStepCombo = new() { ItemsSource = new[] { "1/60초", "1/24초(프레임)", "1/120초" }, SelectedIndex = 0, FontSize = 11 };
    private readonly ComboBox _autoSaveCombo = new() { ItemsSource = new[] { "끔", "1분", "5분", "10분" }, SelectedIndex = 2, FontSize = 11 };
    private static readonly double[] SnapSteps = { 1.0 / 60.0, 1.0 / 24.0, 1.0 / 120.0 };
    private static readonly int[] AutoSaveMinutes = { 0, 1, 5, 10 };

    // 트랜스포트
    private readonly Button _playButton = new() { Content = "▶", FontSize = 11, Padding = new Avalonia.Thickness(8, 3) };
    private readonly ComboBox _playMode = new() { ItemsSource = new[] { "한 번", "루프", "왕복" }, SelectedIndex = 1, FontSize = 11 };
    private readonly ComboBox _playSpeed = new() { ItemsSource = new[] { "×0.25", "×0.5", "×1", "×2" }, SelectedIndex = 2, FontSize = 11 };
    private readonly ComboBox _playStep = new() { ItemsSource = new[] { "연속 재생", "1프레임 재생" }, SelectedIndex = 0, FontSize = 11 };
    private readonly TextBox _rangeStart = new() { Text = "0", Width = 56, FontSize = 11 };
    private readonly TextBox _rangeEnd = new() { Text = "1", Width = 56, FontSize = 11 };
    private readonly TextBox _animEnd = new() { Width = 56, FontSize = 11 };
    private readonly TimeSliderView _timeSlider = new();
    private readonly RangeSliderView _rangeSlider = new();
    private readonly ToggleButton _autoKeyToggle = new() { Content = "● Auto Key", FontSize = 10, Padding = new Avalonia.Thickness(6, 2) };
    private static readonly IBrush AutoKeyOnBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x3C, 0x3C));
    private readonly CheckBox _onion = new() { Content = "어니언 스킨", FontSize = 11 };
    private readonly TextBlock _stageInfo = new() { FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
    private bool _syncingTime;

    // 뷰포트 조작
    private readonly ComboBox _nodeCombo = new() { FontSize = 11, MinWidth = 200 };
    private readonly CheckBox _followSelection = new() { Content = "선택 키의 노드 따라가기", IsChecked = true, FontSize = 11 };
    private readonly CheckBox _autoKey = new() { Content = "Auto Key", FontSize = 11 };
    private readonly TextBlock _pendingText = new() { FontSize = 11, Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap };
    private readonly Button[] _manipulatorButtons = new Button[4];
    private readonly Button[] _toolButtons = new Button[4];
    private readonly CheckBox _valueSnap = new() { Content = "값 스냅", FontSize = 11 };
    private static readonly string[] InfinityTokens = { "상수", "사이클", "사이클+오프셋", "왕복", "직선" };
    private readonly ComboBox _preInfinity = new() { ItemsSource = InfinityTokens, SelectedIndex = 0, FontSize = 11 };
    private readonly ComboBox _postInfinity = new() { ItemsSource = InfinityTokens, SelectedIndex = 0, FontSize = 11 };
    private static readonly TangentMode[] NewKeyTangentChoices = { TangentMode.Auto, TangentMode.Spline, TangentMode.Plateau, TangentMode.Flat, TangentMode.Linear };
    private readonly ComboBox _newKeyTangent = new() { ItemsSource = new[] { "auto", "spline", "plateau", "flat", "linear" }, SelectedIndex = 0, FontSize = 11 };

    /// <summary>새 키(뷰포트 키·성분 토글)의 탄젠트 — 설정. 곡선 위에 끼우는 I·더블클릭은 모양을 지키는 free다.</summary>
    private TangentMode DefaultTangent => NewKeyTangentChoices[Math.Max(0, _newKeyTangent.SelectedIndex)];

    // 왼쪽 — 씬
    // 높이 제한 없음 — 왼쪽 패널이 스크롤한다. 오브젝트가 없는 가지는 기본 접힘이라 길지 않다.
    private readonly TreeView _hierarchy = new() { MinHeight = 160, FontSize = 11 };

    /// <summary>펼침 상태 기억 — 기본이 펼침인 항목(폴더·노드)의 접힘, 기본이 접힘인 항목(오브젝트)의 펼침.</summary>
    private readonly HashSet<string> _collapsedHierarchy = new();
    private readonly HashSet<string> _expandedHierarchy = new();
    private readonly Dictionary<TreeViewItem, TreeViewItem?> _hierarchyParents = new();
    private readonly ContextMenu _hierarchyMenu = new();
    private object? _menuTarget;
    private string? _dragObjectId;
    private Avalonia.Point _dragStart;
    private bool _dragging;
    private TreeViewItem? _dropItem;
    private object? _dropHeader;
    private readonly ComboBox _objCharacter = new() { FontSize = 11, MinWidth = 90 };
    private const string NoCharacterToken = "(배역 없음)";
    private static readonly IBrush ActingNodeBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xE0, 0xFF));
    private static readonly IBrush PlacementNodeBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
    private static readonly IBrush ForbiddenNodeBrush = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
    private readonly ComboBox _exportScope = new() { ItemsSource = new[] { "전체", "이 rigKind만", "이 오브젝트가 입는 모션만" }, SelectedIndex = 0, FontSize = 11 };
    private readonly TextBox _camZoom = new() { FontSize = 11, Width = 56 };
    private readonly TextBox _camPanX = new() { FontSize = 11, Width = 56 };
    private readonly TextBox _camPanY = new() { FontSize = 11, Width = 56 };

    // 오른쪽 — 오브젝트(채널 박스)
    private readonly TextBox _objX = new() { FontSize = 11, Width = 64 };
    private readonly TextBox _objY = new() { FontSize = 11, Width = 64 };
    private readonly TextBox _objRot = new() { FontSize = 11, Width = 64 };
    private readonly TextBox _objSx = new() { FontSize = 11, Width = 64 };
    private readonly TextBox _objSy = new() { FontSize = 11, Width = 64 };
    private readonly ComboBox _objLayer = new() { ItemsSource = SceneLayers.Order, FontSize = 11 };
    private readonly ComboBox _objStage = new() { ItemsSource = StageNodes.Stages, FontSize = 11 };
    private readonly ComboBox _objPlace = new() { FontSize = 11, MinWidth = 90 };
    private readonly ComboBox _objPlaceFocus = new() { ItemsSource = ScenePreview.FocusPresets, FontSize = 11, MinWidth = 90 };
    private const string NoPlaceToken = "(place 없음)";

    private readonly CheckBox _objVisible = new() { Content = "보이기", FontSize = 11 };
    private readonly TextBox _objId = new() { FontSize = 11, Width = 90 };
    private readonly ComboBox _objParent = new() { FontSize = 11, MinWidth = 90 };
    private readonly ComboBox _objParentNode = new() { FontSize = 11, MinWidth = 150 };
    private const string NoParentToken = "(없음 — 최상위)";
    private const string RootNodeToken = "(루트)";
    private readonly TextBlock _objSummary = new() { FontSize = 11, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };

    // 왼쪽
    private readonly ListBox _motionList = new() { MinHeight = 120 };
    private readonly StackPanel _channelRows = new() { Spacing = 0 };
    private readonly TextBox _outlinerSearch = new() { PlaceholderText = "검색…", FontSize = 11, Margin = new Avalonia.Thickness(2) };
    private static readonly IBrush OutlinerBackground = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
    private static readonly IBrush OutlinerRowSelected = new SolidColorBrush(Color.FromArgb(170, 0x4B, 0x6E, 0xAF));
    private readonly ComboBox _timeDisplayCombo = new() { ItemsSource = new[] { "프레임 (24fps)", "초" }, SelectedIndex = 0, FontSize = 11 };

    /// <summary>시간 글자 단위(설정) — 마야처럼 프레임이 기본.</summary>
    private TimeDisplay Display => _settings.TimeDisplay;
    private readonly ComboBox _newChannelNode = new() { FontSize = 11, MinWidth = 180 };
    private readonly ComboBox _newChannelKind = new() { ItemsSource = KindTokens, SelectedIndex = 0, FontSize = 11 };
    private readonly ComboBox _newChannelMode = new() { ItemsSource = ModeTokens, SelectedIndex = 0, FontSize = 11 };
    private string? _channelModeInitializedFor;

    // 오른쪽 — 모션
    private readonly TextBox _motionName = new() { FontSize = 11 };
    private readonly TextBox _motionDuration = new() { FontSize = 11 };
    private readonly ComboBox _motionEnd = new() { ItemsSource = EndTokens, FontSize = 11 };
    private readonly ComboBox _motionDefaultMode = new() { ItemsSource = ModeTokens, FontSize = 11 };
    private readonly TextBox _motionNotes = new() { FontSize = 11, AcceptsReturn = true, MinHeight = 48, PlaceholderText = "메모" };

    // 오른쪽 — 키
    private readonly TextBlock _selectionSummary = new() { FontSize = 11, Opacity = 0.8 };
    private readonly TextBox _keyTime = new() { FontSize = 11 };
    private readonly TextBox _keyValue = new() { FontSize = 11 };
    private readonly TextBox _keyIn = new() { FontSize = 11 };
    private readonly TextBox _keyOut = new() { FontSize = 11 };
    private readonly CheckBox _keyBroken = new() { Content = "끊긴 탄젠트", FontSize = 11 };

    // 오른쪽 — 보기
    private readonly CheckBox _snap = new() { Content = "시간 스냅 1/60초", IsChecked = true, FontSize = 11 };
    private readonly TextBox _currentTime = new() { Text = "0", FontSize = 11, Width = 72 };

    private int _refreshDepth;

    private bool _refreshing => _refreshDepth > 0;

    public MainWindow() : this(ToolSettings.DefaultPath())
    {
    }

    /// <param name="settingsPath">설정 파일 자리 — 테스트가 임시 경로를 준다.</param>
    internal MainWindow(string settingsPath)
    {
        _settingsPath = settingsPath;
        _settings = ToolSettings.Load(settingsPath);
        InitializeComponent();
        BuildToolbar();
        BuildLeft();
        BuildRight();
        _graph.Selection = _selection;
        _dope.Selection = _selection;
        _viewport.Selection = _selection;
        _graph.Flags = _flags;
        _dope.Flags = _flags;
        _viewport.Flags = _flags;
        _graph.Clipboard = _clipboard;
        _dope.Clipboard = _clipboard;
        _timeSlider.Viewport = _viewport;
        _timeSlider.Selection = _selection;
        _timeSlider.Clipboard = _clipboard;
        _rangeSlider.Viewport = _viewport;
        _flags.Changed += (_, _) => RefreshChannels();
        GraphHost.Child = BuildCenter();

        _session.DocumentReplaced += (_, _) => OnDocumentReplaced();
        _session.StatusChanged += (_, _) => StatusText.Text = _session.Status;
        _session.AssetsChanged += (_, _) => RefreshAssets();
        _selection.Changed += (_, _) =>
        {
            RefreshKeyInspector();
            RefreshSelectedNode();
        };
        _graph.CurrentTimeChanged += (_, _) =>
        {
            _currentTime.Text = TimeText.Format(_graph.CurrentTime, Display);
            SyncTime(() =>
            {
                _viewport.ScrubTo(_graph.CurrentTime);
                _dope.CurrentTime = _graph.CurrentTime;
            });
        };
        _dope.CurrentTimeChanged += (_, _) => SyncTime(() =>
        {
            _graph.CurrentTime = _dope.CurrentTime;
            _viewport.ScrubTo(_dope.CurrentTime);
        });
        _graph.CurvesChanged += (_, _) =>
        {
            RefreshChannels();
            RefreshSelectedNode();
        };
        _viewport.TimeChanged += (_, _) => SyncTime(() =>
        {
            _graph.CurrentTime = _viewport.Time;
            _dope.CurrentTime = _viewport.Time;
        });
        _viewport.PlayStateChanged += (_, _) => _playButton.Content = _viewport.IsPlaying ? "❚❚" : "▶";

        OnDocumentReplaced();
        ApplySettings();

        if (_settings.LoadProblem is { } problem)
        {
            _session.SetStatus(problem);
        }

        _autoSaveTimer.Tick += (_, _) => Guard("자동 저장", () => _session.AutoSave());
        Closing += (_, _) => SaveSettings();
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
    }

    // ── 전역 단축키 (마야 타임 슬라이더 관습) ───────────────────────────
    //
    // 1 / 2 이전·다음 키 · Alt+1 / Alt+2 이전·다음 프레임 · ` 첫 프레임 · Alt+` / Alt+Q 재생 토글
    // Shift+W / E / F 현재 시각의 키 복사·붙여넣기·지우기 · Shift+1 / 2 인비트윈 빼기·넣기
    // 창 어디에 포커스가 있든 듣는다(터널). 글 입력 칸에서는 안 듣는다.

    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || e.Handled)
        {
            return;
        }

        KeyModifiers mods = e.KeyModifiers;
        bool none = mods == KeyModifiers.None;
        bool alt = mods == KeyModifiers.Alt;
        bool shift = mods == KeyModifiers.Shift;

        switch (e.PhysicalKey)
        {
            case PhysicalKey.Digit1 when none:
                PreviousKey();
                break;
            case PhysicalKey.Digit2 when none:
                NextKey();
                break;
            case PhysicalKey.Digit1 when alt:
                StepFrame(-1);
                break;
            case PhysicalKey.Digit2 when alt:
                StepFrame(+1);
                break;
            case PhysicalKey.Q when alt:
            case PhysicalKey.Backquote when alt:
                _viewport.TogglePlay();
                break;
            case PhysicalKey.Backquote when none:
                _viewport.Stop();
                break;
            case PhysicalKey.W when shift:
                Guard("키 복사", CopyKeysAtCurrentTime);
                break;
            case PhysicalKey.E when shift:
                Guard("키 붙여넣기", PasteKeysAtCurrentTime);
                break;
            case PhysicalKey.F when shift:
                Guard("키 지우기", ClearKeysAtCurrentTime);
                break;
            case PhysicalKey.Digit1 when shift:
                Guard("인비트윈 빼기", RemoveInbetween);
                break;
            case PhysicalKey.Digit2 when shift:
                Guard("인비트윈 넣기", AddInbetween);
                break;
            // 매니퓰레이터(마야): Q 선택 · W 이동 · E 회전 · R 배율 — 창 어디에 포커스가 있든. 그래프 안에서는 그래프 도구(Q/W/R)가 먼저다.
            case PhysicalKey.Q when none && !InsideGraph(e.Source):
                SetManipulator(ManipulatorMode.Select);
                break;
            case PhysicalKey.W when none && !InsideGraph(e.Source):
                SetManipulator(ManipulatorMode.Move);
                break;
            case PhysicalKey.E when none:
                SetManipulator(ManipulatorMode.Rotate);
                break;
            case PhysicalKey.R when none && !InsideGraph(e.Source):
                SetManipulator(ManipulatorMode.Scale);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private bool InsideGraph(object? source)
        => source is Visual v && (ReferenceEquals(v, _graph) || v.FindAncestorOfType<GraphEditorView>() is not null);

    private void SetManipulator(ManipulatorMode mode)
    {
        _viewport.Manipulator = mode;
        _viewport.InvalidateVisual();
        RefreshManipulatorButtons();
    }

    /// <summary>테스트 손잡이.</summary>
    internal ManipulatorMode Manipulator => _viewport.Manipulator;

    /// <summary>한 프레임(24fps) — 뷰포트 ←/→와 같은 규약.</summary>
    internal static float FrameSeconds => (float)(1.0 / TimeSnap.FramesPerSecond);

    internal void PreviousKey() => _viewport.JumpToPreviousKey();

    internal void NextKey() => _viewport.JumpToNextKey();

    internal void StepFrame(int frames)
    {
        _viewport.Pause();
        _viewport.ScrubTo(_viewport.Time + frames * (double)FrameSeconds);
    }

    /// <summary>마야 timeSliderCopyKey — 현재 시각의 키 전부(모든 커브)를 클립보드에.</summary>
    internal void CopyKeysAtCurrentTime()
    {
        if (_graph.Motion is not { } motion)
        {
            return;
        }

        List<KeyRef> keys = TimeSliderOps.KeysAt(motion, _viewport.Time);
        KeyClipboard? clipboard = KeyClipboard.Copy(motion, keys);

        if (clipboard is null || clipboard.Count == 0)
        {
            _session.SetStatus("현재 시각에 키가 없다.");
            return;
        }

        _clipboard.Current = clipboard;
        _session.SetStatus($"키 {clipboard.Count}개 복사 ({_viewport.Time:0.###}s).");
    }

    /// <summary>마야 timeSliderPasteKey — 클립보드를 현재 시각에(같은 시각은 덮어씀, 없는 채널은 만든다).</summary>
    internal void PasteKeysAtCurrentTime()
    {
        if (_graph.MotionName is not { } name)
        {
            return;
        }

        if (_clipboard.Current is not { } clipboard || clipboard.Count == 0)
        {
            _session.SetStatus("클립보드에 키가 없다.");
            return;
        }

        float at = (float)_viewport.Time;
        Func<float, float>? snap = _settings.SnapEnabled ? t => (float)TimeSnap.Snap(t, _settings.SnapStep) : null;
        List<KeyRef> pasted = _session.Editor.PasteKeys(name, clipboard, snap?.Invoke(at) ?? at, snap);
        _selection.Set(pasted);
    }

    /// <summary>마야 timeSliderClearKey — 현재 시각의 키를 모든 커브에서 지운다.</summary>
    internal void ClearKeysAtCurrentTime()
    {
        if (_graph.MotionName is { } name && !_session.Editor.RemoveKeysAt(name, _viewport.Time))
        {
            _session.SetStatus("현재 시각에 키가 없다.");
        }
    }

    /// <summary>마야 removeInbetween — 현재 시각 뒤의 키를 한 프레임 당긴다.</summary>
    internal void RemoveInbetween()
    {
        if (_graph.MotionName is { } name && !_session.Editor.ShiftKeysAfter(name, (float)_viewport.Time, -FrameSeconds))
        {
            _session.SetStatus("현재 시각 뒤에 키가 없다.");
        }
    }

    /// <summary>마야 addInbetween — 현재 시각 뒤의 키를 한 프레임 민다.</summary>
    internal void AddInbetween()
    {
        if (_graph.MotionName is { } name && !_session.Editor.ShiftKeysAfter(name, (float)_viewport.Time, FrameSeconds))
        {
            _session.SetStatus("현재 시각 뒤에 키가 없다.");
        }
    }

    internal ToolSettings Settings => _settings;

    /// <summary>설정 → 화면·뷰. 설정 파일에서 읽은 값을 한 번에 입힌다.</summary>
    private void ApplySettings()
    {
        _refreshDepth++;

        try
        {
            _snap.IsChecked = _settings.SnapEnabled;
            int snapIndex = Array.FindIndex(SnapSteps, v => Math.Abs(v - _settings.SnapStep) < 1e-9);
            _snapStepCombo.SelectedIndex = snapIndex < 0 ? 0 : snapIndex;
            int tangentIndex = Array.IndexOf(NewKeyTangentChoices, _settings.DefaultTangent);
            _newKeyTangent.SelectedIndex = tangentIndex < 0 ? 0 : tangentIndex;
            int onionIndex = Array.IndexOf(new[] { 0, 1, 2, 3, 4, 6 }, _settings.OnionSkinFrames);
            _onionFrames.SelectedIndex = onionIndex < 0 ? 2 : onionIndex;
            _autoKey.IsChecked = _settings.AutoKey;
            int autoSaveIndex = Array.IndexOf(AutoSaveMinutes, _settings.AutoSaveMinutes);
            _autoSaveCombo.SelectedIndex = autoSaveIndex < 0 ? 2 : autoSaveIndex;
            _timeDisplayCombo.SelectedIndex = (int)_settings.TimeDisplay;
            _playStep.SelectedIndex = _settings.PlayWholeFrames ? 1 : 0;
        }
        finally
        {
            _refreshDepth--;
        }

        PushSettingsToViews();
        RefreshRecentMenu();
    }

    private void PushSettingsToViews()
    {
        _graph.SnapEnabled = _settings.SnapEnabled;
        _dope.SnapEnabled = _settings.SnapEnabled;
        _viewport.SnapEnabled = _settings.SnapEnabled;
        _graph.SnapStep = _settings.SnapStep;
        _dope.SnapStep = _settings.SnapStep;
        _viewport.SnapStep = _settings.SnapStep;
        _viewport.DefaultTangent = _settings.DefaultTangent;
        _viewport.OnionSkinFrames = _settings.OnionSkinFrames;
        _viewport.AutoKey = _settings.AutoKey;
        _viewport.PlayWholeFrames = _settings.PlayWholeFrames;
        _graph.TimeDisplay = _settings.TimeDisplay;
        _dope.TimeDisplay = _settings.TimeDisplay;
        _timeSlider.SnapEnabled = _settings.SnapEnabled;
        _timeSlider.SnapStep = _settings.SnapStep;
        _timeSlider.TimeDisplay = _settings.TimeDisplay;
        _rangeSlider.TimeDisplay = _settings.TimeDisplay;
        _timeSlider.InvalidateVisual();
        _rangeSlider.InvalidateVisual();
        _graph.InvalidateVisual();
        _dope.InvalidateVisual();
        RefreshTimeTexts();
        _autoSaveTimer.Stop();

        if (_settings.AutoSaveMinutes > 0)
        {
            _autoSaveTimer.Interval = TimeSpan.FromMinutes(_settings.AutoSaveMinutes);
            _autoSaveTimer.Start();
        }

        _viewport.InvalidateVisual();
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(_settingsPath);
        }
        catch (Exception exception)
        {
            _session.SetStatus($"설정을 저장하지 못했다: {exception.Message}");
        }
    }

    private void RefreshRecentMenu()
    {
        var flyout = new MenuFlyout();

        foreach (string path in _settings.RecentFiles)
        {
            var item = new MenuItem { Header = path, FontSize = 11 };
            string captured = path;
            item.Click += (_, _) => Guard("최근 파일", () => OpenDocument(captured));
            flyout.Items.Add(item);
        }

        if (_settings.RecentFiles.Count == 0)
        {
            flyout.Items.Add(new MenuItem { Header = "(없음)", IsEnabled = false, FontSize = 11 });
        }

        _recentButton.Flyout = flyout;
    }

    internal void OpenDocument(string path)
    {
        _session.Open(path);
        _settings.Touch(path);
        RefreshRecentMenu();
        SaveSettings();
    }

    internal DocumentSession Session => _session;

    internal GraphEditorView Graph => _graph;

    internal ViewportView Viewport => _viewport;

    internal TimeSliderView TimeSlider => _timeSlider;

    internal RangeSliderView RangeSlider => _rangeSlider;

    internal DopeSheetView Dope => _dope;

    internal KeySelection SharedSelection => _selection;

    internal CurveFlags SharedFlags => _flags;

    internal bool AutoKeyChecked
    {
        get => _autoKey.IsChecked == true;
        set => _autoKey.IsChecked = value;
    }

    internal string? SelectedMotion => _motionList.SelectedItem as string;

    /// <summary>헤드리스 테스트가 폼을 채우는 손잡이 — 화면과 같은 명령 경로를 지난다.</summary>
    internal void AddMotionFromForm(string name, string rig, string duration, MotionEnd end)
    {
        if (!TryFloat(duration, out float seconds))
        {
            throw new InvalidOperationException("길이는 초 단위 숫자다.");
        }

        AddMotion(name, rig, seconds, end);
    }

    /// <summary>테스트 손잡이 — 채널 추가 폼. mode를 안 주면 모션의 기본 mode.</summary>
    internal void AddChannelFromForm(string node, MotionChannelKind kind, MotionChannelMode? mode = null)
    {
        _newChannelNode.SelectedItem = node;
        _newChannelKind.SelectedIndex = (int)kind;
        _newChannelMode.SelectedIndex = (mode ?? _graph.Motion?.DefaultMode ?? MotionChannelMode.Relative) == MotionChannelMode.Absolute ? 1 : 0;
        AddChannel();
    }

    /// <summary>채널 행의 성분 토글과 같은 길 — 없는 성분을 현재 시각의 중립값 키 하나로 만든다.</summary>
    internal void EnsureComponent(string node, MotionChannelKind kind, Axis axis)
    {
        MotionAsset motion = _graph.Motion ?? throw new InvalidOperationException("먼저 모션을 고른다.");

        if (motion.FindChannel(node, kind)?.Get(axis) is not null)
        {
            return;
        }

        _session.Editor.AddKey(motion.Name, node, kind, axis, new KeyAsset
        {
            T = (float)_graph.CurrentTime,
            V = MotionMath.Neutral(kind),
            Tangent = DefaultTangent,
        });
    }

    /// <summary>인스펙터의 값 칸에 적고 Enter 한 것과 같다.</summary>
    internal void CommitKeyValueFromInspector(string text)
    {
        _keyValue.Text = text;
        WithSingleKey((key, k) => { if (TryFloat(text, out float v) && v != k.V) UpdateKey(key, k, value: v); });
    }

    internal IReadOnlyList<MotionProblem> ProblemsShown => _session.Problems;

    // ── 구성 ──────────────────────────────────────────────────────────

    /// <summary>가운데: 위 뷰포트, 트랜스포트 줄, 아래 그래프. 사이에 스플리터.</summary>
    private Control BuildCenter()
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("2*,Auto,Auto,4,3*") };

        Grid.SetRow(_viewport, 0);
        grid.Children.Add(_viewport);

        _playButton.Click += (_, _) => _viewport.TogglePlay();
        _playMode.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _playMode.SelectedIndex >= 0)
            {
                _viewport.Mode = (PlaybackMode)_playMode.SelectedIndex;
            }
        };
        _playStep.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _playStep.SelectedIndex >= 0)
            {
                _settings.PlayWholeFrames = _playStep.SelectedIndex == 1;
                PushSettingsToViews();
            }
        };
        ToolTip.SetTip(_playStep, "연속: 흐른 시간 그대로 · 1프레임: 보이는 시각을 정수 프레임(24fps)에 맞춘다(마야 Real-time)");
        _playSpeed.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _playSpeed.SelectedIndex >= 0)
            {
                _viewport.Speed = new[] { 0.25, 0.5, 1, 2 }[_playSpeed.SelectedIndex];
            }
        };
        OnCommit(_rangeStart, text => Guard("재생 범위", () => { if (TimeText.TryParse(text, Display, out double v)) _viewport.SetRange(v, _viewport.Clock.RangeEnd); }));
        OnCommit(_rangeEnd, text => Guard("재생 범위", () => { if (TimeText.TryParse(text, Display, out double v)) _viewport.SetRange(_viewport.Clock.RangeStart, v); }));
        _viewport.RangeChanged += (_, _) => RefreshTransport();
        _onion.IsCheckedChanged += (_, _) =>
        {
            _viewport.OnionSkinEnabled = _onion.IsChecked == true;
            _viewport.InvalidateVisual();
        };

        // 마야 트랜스포트: [현재 프레임] ⏮ |◀ ◁ ◀ ▶ ▷ ▶| ⏭  — 타임 슬라이더가 나머지를 채운다.
        _playButton.Content = "▶";
        _playButton.FontSize = 10;
        _playButton.Padding = new Avalonia.Thickness(6, 2);
        ToolTip.SetTip(_playButton, "재생 / 일시정지 (Alt+` · Alt+Q)");
        Button playBack = SmallButton("◀", (_, _) =>
        {
            if (_viewport.IsPlaying)
            {
                _viewport.Pause();
            }
            else
            {
                _viewport.PlayBackward();
            }
        });
        ToolTip.SetTip(playBack, "거꾸로 재생");
        OnCommit(_animEnd, text => WithMotion(m =>
        {
            if (TimeText.TryParse(text, Display, out double v) && Math.Abs(v - m.Duration) > 1e-6)
            {
                _session.Editor.SetDuration(m.Name, (float)v);
            }
        }));
        ToolTip.SetTip(_currentTime, "현재 프레임 — 숫자를 넣으면 그 시각으로(초는 0.5s처럼)");
        ToolTip.SetTip(_rangeStart, "재생 범위 시작");
        ToolTip.SetTip(_rangeEnd, "재생 범위 끝");
        ToolTip.SetTip(_animEnd, "모션 길이(애니메이션 끝) — 바꾸면 길이가 바뀐다");
        (string Label, string Tip, EventHandler<RoutedEventArgs> Click)[] transportSpec =
        {
            ("⏮", "범위 처음으로 (`)", (_, _) => _viewport.Stop()),
            ("|◀", "이전 키 (1 · ,)", (_, _) => _viewport.JumpToPreviousKey()),
            ("◁", "이전 프레임 (Alt+1 · ←)", (_, _) => StepFrame(-1)),
        };
        var transportButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Avalonia.Thickness(6, 0, 0, 0) };
        _currentTime.Width = 56;
        transportButtons.Children.Add(_currentTime);

        foreach ((string label, string tip, EventHandler<RoutedEventArgs> click) in transportSpec)
        {
            Button b = SmallButton(label, click);
            ToolTip.SetTip(b, tip);
            transportButtons.Children.Add(b);
        }

        transportButtons.Children.Add(playBack);
        transportButtons.Children.Add(_playButton);
        (string Label, string Tip, EventHandler<RoutedEventArgs> Click)[] transportTail =
        {
            ("▷", "다음 프레임 (Alt+2 · →)", (_, _) => StepFrame(+1)),
            ("▶|", "다음 키 (2 · .)", (_, _) => _viewport.JumpToNextKey()),
            ("⏭", "범위 끝으로", (_, _) => { _viewport.Pause(); _viewport.ScrubTo(_viewport.Clock.RangeEnd); }),
        };

        foreach ((string label, string tip, EventHandler<RoutedEventArgs> click) in transportTail)
        {
            Button b = SmallButton(label, click);
            ToolTip.SetTip(b, tip);
            transportButtons.Children.Add(b);
        }

        var transportRow = new DockPanel { Margin = new Avalonia.Thickness(4, 2, 4, 0) };
        DockPanel.SetDock(transportButtons, Dock.Right);
        transportRow.Children.Add(transportButtons);
        transportRow.Children.Add(_timeSlider);
        Grid.SetRow(transportRow, 1);
        grid.Children.Add(transportRow);

        // 레인지 슬라이더 줄: [0][범위 시작] ═바═ [범위 끝][길이] · 모드 · 속도 · fps · Auto Key · 어니언 · 초상.
        var rangeLeft = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Avalonia.Thickness(0, 0, 4, 0),
            Children = { new TextBlock { Text = "0", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, Width = 20, TextAlignment = TextAlignment.Center }, _rangeStart },
        };
        var rangeRight = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Avalonia.Thickness(4, 0, 0, 0),
            Children =
            {
                _rangeEnd,
                _animEnd,
                _playMode,
                _playSpeed,
                _playStep,
                new TextBlock { Text = "24 fps", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 },
                _autoKeyToggle,
                _onion,
                _stageInfo,
            },
        };
        var rangeRow = new DockPanel { Margin = new Avalonia.Thickness(4, 2, 4, 2) };
        DockPanel.SetDock(rangeLeft, Dock.Left);
        DockPanel.SetDock(rangeRight, Dock.Right);
        rangeRow.Children.Add(rangeLeft);
        rangeRow.Children.Add(rangeRight);
        rangeRow.Children.Add(_rangeSlider);
        Grid.SetRow(rangeRow, 2);
        grid.Children.Add(rangeRow);

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Background = Brushes.Transparent };
        Grid.SetRow(splitter, 3);
        grid.Children.Add(splitter);

        var tabs = new TabControl
        {
            Padding = new Avalonia.Thickness(0),
            ItemsSource = new[]
            {
                new TabItem { Header = "그래프", Content = BuildGraphTab(), FontSize = 12 },
                new TabItem { Header = "도프 시트", Content = _dope, FontSize = 12 },
            },
        };
        Grid.SetRow(tabs, 4);
        grid.Children.Add(tabs);
        return grid;
    }

    /// <summary>마야 그래프 에디터 배치: 왼쪽 아웃라이너(검색 · 행 클릭 = 그 커브만) · 위 툴바 · 플롯.</summary>
    private Control BuildGraphTab()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("210,4,*") };

        var outliner = new DockPanel { Background = OutlinerBackground };
        DockPanel.SetDock(_outlinerSearch, Dock.Top);
        outliner.Children.Add(_outlinerSearch);
        outliner.Children.Add(new ScrollViewer { Content = _channelRows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _outlinerSearch.TextChanged += (_, _) => RefreshChannels();
        Grid.SetColumn(outliner, 0);
        grid.Children.Add(outliner);

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        var right = new DockPanel();
        Control toolbar = BuildGraphToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        right.Children.Add(toolbar);
        right.Children.Add(_graph);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>마야 그래프 에디터 툴바: 툴 · 맞춤 · 탄젠트 · 버퍼 · 스냅/보기 · Stats.</summary>
    private Control BuildGraphToolbar()
    {
        _snap.Content = "시간 스냅";
        _valueSnap.IsCheckedChanged += (_, _) =>
        {
            _graph.ValueSnapEnabled = _valueSnap.IsChecked == true;
        };
        _preInfinity.SelectionChanged += (_, _) =>
        {
            if (_preInfinity.SelectedIndex >= 0)
            {
                _graph.PreInfinity = (InfinityMode)_preInfinity.SelectedIndex;
                _graph.InvalidateVisual();
            }
        };
        _postInfinity.SelectionChanged += (_, _) =>
        {
            if (_postInfinity.SelectedIndex >= 0)
            {
                _graph.PostInfinity = (InfinityMode)_postInfinity.SelectedIndex;
                _graph.InvalidateVisual();
            }
        };
        ToolTip.SetTip(_preInfinity, "Pre Infinity — 첫 키 앞을 어떻게 이어 보이나(보기만, 산출물의 끝은 restore/commit)");
        ToolTip.SetTip(_postInfinity, "Post Infinity — 마지막 키 뒤를 어떻게 이어 보이나(보기만)");
        ToolTip.SetTip(_valueSnap, "끌 때 키 값을 정수로(마야 Snap Values)");
        ToolTip.SetTip(_toolButtons[3], "리전 툴 — 상자로 고르고, 안을 끌면 이동, 모서리를 끌면 그 축만 반대쪽 기준 스케일");
        _keyTime.Width = 64;
        _keyValue.Width = 64;
        ToolTip.SetTip(_keyTime, "선택 키의 시각(표시 단위). 여러 키면 +=3 · -=1 식 상대 입력");
        ToolTip.SetTip(_keyValue, "선택 키의 값. 여러 키면 +=3 · *=2 식 상대 입력");

        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Avalonia.Thickness(2, 2, 2, 4) };

        void Add(params Control[] controls)
        {
            foreach (Control c in controls)
            {
                c.Margin = new Avalonia.Thickness(1, 1);
                c.VerticalAlignment = VerticalAlignment.Center;
                bar.Children.Add(c);
            }
        }

        Add(_toolButtons[0], _toolButtons[1], _toolButtons[2]);
        Add(SmallButton("끼우기 (I)", (_, _) => _graph.InsertKeyAtCurrentTime()),
            SmallButton("전체 (A)", (_, _) => _graph.FitAll()),
            SmallButton("선택 (F)", (_, _) => _graph.FitSelection()),
            SmallButton("재생 범위", (_, _) => _graph.FitRange()),
            SmallButton("현재 시각 가운데", (_, _) => _graph.CenterCurrentTime()),
            Separator());
        Add(SmallButton("auto", (_, _) => _graph.SetTangentMode(TangentMode.Auto)),
            SmallButton("spline", (_, _) => _graph.SetTangentMode(TangentMode.Spline)),
            SmallButton("linear", (_, _) => _graph.SetTangentMode(TangentMode.Linear)),
            SmallButton("flat", (_, _) => _graph.SetTangentMode(TangentMode.Flat)),
            SmallButton("plateau", (_, _) => _graph.SetTangentMode(TangentMode.Plateau)),
            SmallButton("free", (_, _) => _graph.SetTangentMode(TangentMode.Free)),
            SmallButton("끊기", (_, _) => _graph.SetBroken(true)),
            SmallButton("붙이기", (_, _) => _graph.SetBroken(false)),
            Separator());
        Add(SmallButton("버퍼 찍기", (_, _) => _graph.SnapshotBuffer()),
            SmallButton("버퍼 바꾸기", (_, _) => _graph.SwapBuffer()),
            SmallButton("버퍼 지우기", (_, _) => _graph.ClearBuffer()),
            Separator());
        Add(_toolButtons[3], Separator());
        Add(_snap, _valueSnap, _viewMode, Separator());
        Add(new TextBlock { Text = "무한", FontSize = 10, Opacity = 0.7 }, _preInfinity, _postInfinity, Separator());
        Add(new TextBlock { Text = "Stats", FontSize = 10, Opacity = 0.7 }, _keyTime, _keyValue);
        ToolTip.SetTip(bar, "탄젠트 Ctrl+1 auto · 2 flat · 3 linear · 4 free · 5 spline · 6 plateau · 버퍼 Ctrl+B / Ctrl+Shift+B");
        return bar;
    }

    private void RefreshTimeTexts()
    {
        _currentTime.Text = TimeText.Format(_graph.CurrentTime, Display);
        RefreshTransport();
        RefreshKeyInspector();
    }

    private void RefreshToolButtons()
    {
        for (int i = 0; i < _toolButtons.Length; i++)
        {
            bool active = (int)_graph.Tool == i;
            _toolButtons[i].Opacity = active ? 1 : 0.55;
            _toolButtons[i].FontWeight = active ? FontWeight.Bold : FontWeight.Normal;
        }
    }

    private void RefreshManipulatorButtons()
    {
        for (int i = 0; i < _manipulatorButtons.Length; i++)
        {
            _manipulatorButtons[i].Opacity = (int)_viewport.Manipulator == i ? 1 : 0.55;
            _manipulatorButtons[i].FontWeight = (int)_viewport.Manipulator == i ? FontWeight.Bold : FontWeight.Normal;
        }
    }

    private void RefreshPending()
    {
        _pendingText.Text = _viewport.IsHoldingPose
            ? "든 포즈(MMB 스크럽) — S로 이 프레임에 키를 놓거나 Esc·보통 스크럽으로 버린다."
            : _viewport.HasPending ? "키 없는 포즈 — S로 키를 놓거나 Esc로 버린다. 시각을 옮기면 사라진다." : "";
        _pendingText.IsVisible = _viewport.HasPending;
    }

    private void RefreshNodeChoices()
    {
        _refreshDepth++;

        try
        {
            List<string> nodes = _viewport.NodeChoices.ToList();
            string? current = _viewport.SelectedNode;
            _nodeCombo.ItemsSource = nodes;
            _nodeCombo.SelectedIndex = current is null ? -1 : nodes.IndexOf(current);
            _nodeCombo.IsEnabled = nodes.Count > 0;
        }
        finally
        {
            _refreshDepth--;
        }
    }

    private void RefreshTransport()
    {
        _refreshDepth++;

        try
        {
            _playMode.SelectedIndex = (int)_viewport.Mode;
            double[] speeds = { 0.25, 0.5, 1, 2 };
            int speedIndex = Array.FindIndex(speeds, v => Math.Abs(v - _viewport.Speed) < 1e-9);
            _playSpeed.SelectedIndex = speedIndex < 0 ? 2 : speedIndex;
            _rangeStart.Text = TimeText.Format(_viewport.Clock.RangeStart, Display);
            _rangeEnd.Text = TimeText.Format(_viewport.Clock.RangeEnd, Display);
            _animEnd.Text = TimeText.Format(_viewport.Clock.Duration, Display);
            _graph.RangeStart = _viewport.Clock.RangeStart;
            _graph.RangeEnd = _viewport.Clock.RangeEnd;
            _graph.InvalidateVisual();
        }
        finally
        {
            _refreshDepth--;
        }
    }

    /// <summary>그래프와 뷰포트의 현재 시각은 하나다 — 서로를 되부르지 않게 한쪽만 민다.</summary>
    private void SyncTime(Action push)
    {
        if (_syncingTime)
        {
            return;
        }

        _syncingTime = true;

        try
        {
            push();
        }
        finally
        {
            _syncingTime = false;
        }
    }

    private void BindMotion(string? name)
    {
        _graph.Bind(_session.Editor, name);
        _dope.Bind(_session.Editor, name);
        _viewport.Bind(_session.Editor, name);
        _timeSlider.Bind(_session.Editor, name);
        RefreshSelectedNode();
    }

    /// <summary>뷰포트가 축을 그릴 노드 — 선택한 키의 노드, 없으면 첫 채널의 노드.</summary>
    private void RefreshSelectedNode()
    {
        MotionAsset? motion = _graph.Motion;
        string? node = _graph.Selection.Count > 0
            ? _graph.Selection.First().Curve.Node
            : motion?.Channels.FirstOrDefault()?.Node;

        if (_followSelection.IsChecked == true || _viewport.SelectedNode is null)
        {
            _viewport.SelectedNode = node ?? _viewport.SelectedNode;
        }

        _viewport.InvalidateVisual();
        RefreshNodeChoices();
    }

    private void RefreshAssets()
    {
        _viewport.SetRig(_session.Rig);
        _viewport.SetDepth(_session.Depth);
        _viewport.SetFocus(_session.Focus);
        _viewport.StageSize = _session.StageSize;
        _viewport.ClearPortraits();
        int portraits = 0;

        foreach (SceneObject o in _viewport.SceneObjects)
        {
            if (_session.PortraitPathOf(o) is not { } portraitPath)
            {
                continue;
            }

            try
            {
                _viewport.SetPortrait(o.Id, new Bitmap(portraitPath));
                portraits++;
            }
            catch (Exception exception)
            {
                _session.SetStatus($"{o.Id}의 초상을 읽지 못했다: {exception.Message}");
            }
        }

        RefreshNodeChoices();
        RefreshObjects();
        _stageInfo.Text = $"무대 {_session.StageSize.X:0}×{_session.StageSize.Y:0}"
                          + (_session.IsBuiltinRig ? $" · 내장 리그({BuiltinTuning.Source})" : "")
                          + $" · 오브젝트 {_viewport.SceneObjects.Count} · 초상 {portraits}";
        RefreshNewForms();
        RefreshProblems();
    }

    private async void OnPickPortraitClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickOpenAsync("초상 PNG", new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } });

            if (path is not null)
            {
                string objectId = _viewport.SelectedObjectId ?? throw new InvalidOperationException("먼저 오브젝트를 고른다.");
                _session.SetPortrait(objectId, path);
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"초상 실패: {exception.Message}");
        }
    }

    private void BuildToolbar()
    {
        Toolbar.Children.Add(ToolButton("새로", (_, _) => Guard("새 문서", _session.New)));
        Toolbar.Children.Add(ToolButton("열기…", OnOpenClick));
        Toolbar.Children.Add(_recentButton);
        Toolbar.Children.Add(ToolButton("저장", OnSaveClick));
        Toolbar.Children.Add(ToolButton("다른 이름으로…", OnSaveAsClick));
        Toolbar.Children.Add(Separator());
        Toolbar.Children.Add(ToolButton("리그 덤프…", OnLoadRigClick));
        Toolbar.Children.Add(ToolButton("motions.json 가져오기…", OnImportClick));
        Toolbar.Children.Add(_exportScope);
        ToolTip.SetTip(_exportScope, "내보낼 범위 — 전체 / 선택 오브젝트의 rigKind 모션만 / 선택 오브젝트가 입을 수 있는 모션만. 모션의 주인은 rigKind다");
        Toolbar.Children.Add(ToolButton("motions.json 내보내기…", OnExportClick));
        Toolbar.Children.Add(Separator());
        Toolbar.Children.Add(ToolButton("되돌리기", (_, _) => _session.Editor.Undo()));
        Toolbar.Children.Add(ToolButton("다시하기", (_, _) => _session.Editor.Redo()));
    }

    private void BuildLeft()
    {
        LeftPanel.Children.Add(Header("씬 — 오브젝트"));
        _hierarchy.SelectionChanged += (_, _) => OnHierarchySelected();
        _hierarchy.DoubleTapped += (_, _) =>
        {
            if (_hierarchy.SelectedItem is TreeViewItem { Tag: HierarchyNode node })
            {
                SelectNodeInHierarchy(node);
                Guard("채널 추가", AddChannel);
            }
        };
        _hierarchy.ContextMenu = _hierarchyMenu;
        _hierarchyMenu.Opening += (_, _) => RebuildHierarchyMenu();
        _hierarchy.AddHandler(PointerPressedEvent, OnHierarchyPointerPressed, RoutingStrategies.Tunnel);
        _hierarchy.AddHandler(PointerMovedEvent, OnHierarchyPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        _hierarchy.AddHandler(PointerReleasedEvent, OnHierarchyPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        _hierarchy.KeyDown += OnHierarchyKeyDown;
        LeftPanel.Children.Add(_hierarchy);
        var plus = SmallButton("+ 캐릭터 리그", (_, _) => Guard("캐릭터 리그 만들기", () =>
            CreateObject(SceneObject.DefaultRig, _hierarchy.SelectedItem is TreeViewItem { Tag: { } tag } ? tag : null)));
        ToolTip.SetTip(plus, "바로 캐릭터 리그를 만든다 — 고른 층(Content)·Stage면 거기에, 오브젝트/노드를 고른 채면 그 자식으로, 아무것도 안 골랐으면 선택 오브젝트의 무대·층(없으면 Stage00 Mid)에. 우클릭 메뉴와 같다 (Ctrl+Shift+N)");
        LeftPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                plus,
                SmallButton("삭제", (_, _) => Guard("오브젝트 삭제", RemoveObject)),
                SmallButton("초상 PNG…", OnPickPortraitClick),
            },
        });
        ToolTip.SetTip(_hierarchy, "PresentataionUIRoot › … › Stage{NN}Depth_{Layer}_Content › 오브젝트 › 리그 노드(덤프의 부모–자식 그대로) › 자식 오브젝트. 클릭 = 선택, 노드 클릭 = 매니퓰레이터 노드·채널 추가 대상, 더블클릭 = 채널 추가. 우클릭 = 만들기/복제/삭제/레이어/부모 메뉴(오브젝트나 노드 위에서 만들면 그 자식). 끌어서 다른 오브젝트·노드에 놓으면 자식으로, 레이어에 놓으면 그 레이어로. Del · Ctrl+D · Ctrl+Shift+N · F2");

        LeftPanel.Children.Add(Header("모션"));
        _motionList.SelectionChanged += (_, _) => OnMotionSelected();
        LeftPanel.Children.Add(_motionList);

        var addMotionButton = SmallButton("모션 추가", (_, _) => Guard("모션 추가", () => AddMotion()));
        ToolTip.SetTip(addMotionButton, "이름(motion, motion_2…)·리그(선택 오브젝트의 rigKind)·길이 1초·끝 restore로 만든다 — 오른쪽 인스펙터에서 바꾼다");
        LeftPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { addMotionButton, SmallButton("모션 삭제", (_, _) => Guard("모션 삭제", RemoveMotion)) },
        });

        LeftPanel.Children.Add(Header("채널 추가"));
        var addChannel = new StackPanel { Spacing = 4 };
        addChannel.Children.Add(_newChannelNode);
        addChannel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { _newChannelKind, _newChannelMode, SmallButton("채널 추가", (_, _) => Guard("채널 추가", AddChannel)) },
        });
        ToolTip.SetTip(_newChannelKind, "채널 종류 — position · scale · rotate(파일에는 euler로 적힌다)");
        ToolTip.SetTip(_newChannelMode, "relative = 중립값에 더함 · absolute = 값 그대로. 처음엔 모션의 기본 mode");
        LeftPanel.Children.Add(addChannel);
    }

    private void BuildRight()
    {
        RightPanel.Children.Add(Header("오브젝트 (채널 박스 — 배치, 모션 키가 아니다)"));
        RightPanel.Children.Add(_objSummary);
        RightPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { Small("id"), _objId, Small("부모"), _objParent, Small("노드"), _objParentNode },
        });
        ToolTip.SetTip(_objId, "이름 바꾸기 — [a-z0-9_]+. 자식의 부모 참조도 따라간다");
        ToolTip.SetTip(_objParent, "부모 오브젝트 — 그 오브젝트의 노드 밑에 붙는다(유니티 자식). 자식은 부모의 레이어·깊이를 따른다");
        ToolTip.SetTip(_objParentNode, "부모 리그에서 붙는 노드. (루트)면 리그 루트");
        OnCommit(_objId, _ => RenameSelectedObject());
        _objParent.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _viewport.SelectedObject is { } o && _objParent.SelectedItem is string chosen)
            {
                string? parent = chosen == NoParentToken ? null : chosen;

                if (parent != o.Parent)
                {
                    Guard("부모", () => _session.Editor.SetObjectParent(null, o.Id, parent, null, null, _viewport.PlacementKeepingWorld(o.Id, parent, null)));
                }
            }
        };
        _objParentNode.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _viewport.SelectedObject is { Parent: { } parent } o && _objParentNode.SelectedItem is string chosen)
            {
                string? node = chosen == RootNodeToken ? null : chosen;

                if (node != o.ParentNode)
                {
                    Guard("부모 노드", () => _session.Editor.SetObjectParent(null, o.Id, parent, node, null, _viewport.PlacementKeepingWorld(o.Id, parent, node)));
                }
            }
        };
        RightPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { Small("자리"), _objPlace, Small("focus"), _objPlaceFocus },
        });
        ToolTip.SetTip(_objPlace, "place — focus 프리셋 점이 이 화면 지점에 오도록 CharSlot_Track_Focus를 푼다(코어 place 폴드, 런타임과 같은 함수). 없으면 Track_Focus는 0");
        ToolTip.SetTip(_objPlaceFocus, "place가 맞추는 focus 프리셋(bust 기본)");
        _objPlace.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _viewport.SelectedObject is { } o && _objPlace.SelectedItem is string chosen)
            {
                string? point = chosen == NoPlaceToken ? null : chosen;

                if (point != o.Place)
                {
                    Guard("자리", () => _session.Editor.SetObjectPlace(null, o.Id, point, o.PlaceFocus));
                }
            }
        };
        _objPlaceFocus.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _viewport.SelectedObject is { Place: not null } o && _objPlaceFocus.SelectedItem is string chosen && chosen != (o.PlaceFocus ?? SceneObject.DefaultPlaceFocus))
            {
                Guard("focus", () => _session.Editor.SetObjectPlace(null, o.Id, o.Place, chosen));
            }
        };
        RightPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { Small("오프셋 X"), _objX, Small("Y"), _objY, Small("Rot Z"), _objRot },
        });
        ToolTip.SetTip(_objX, "오프셋(move) — CharSlot_Track. 자리(place)와 별개로 더해진다");
        RightPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { Small("Scale X"), _objSx, Small("Y"), _objSy, Small("Stage"), _objStage, Small("Layer"), _objLayer, _objVisible },
        });
        _objStage.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _objStage.SelectedItem is string stage && _viewport.SelectedObject is { } o && o.Stage != stage)
            {
                Guard("무대", () => _session.Editor.SetObjectStage(null, o.Id, stage));
            }
        };
        RightPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { Small("배역"), _objCharacter },
        });
        ToolTip.SetTip(_objCharacter, "배역(characterKey) — presets/focus.json의 entries 키. 깊이 폴드의 focus 오프셋이 배역을 따른다(런타임 cast와 같은 뜻)");
        _objCharacter.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _viewport.SelectedObject is { } o && _objCharacter.SelectedItem is string chosen)
            {
                string? character = chosen == NoCharacterToken ? null : chosen;

                if (character != o.Character)
                {
                    Guard("배역", () => _session.Editor.SetObjectCharacter(null, o.Id, character));
                }
            }
        };

        foreach (TextBox box in new[] { _objX, _objY, _objRot, _objSx, _objSy })
        {
            OnCommit(box, _ => CommitObjectPlacement());
        }

        _objLayer.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _objLayer.SelectedItem is string layer && _viewport.SelectedObject is { } o && o.Layer != layer)
            {
                Guard("레이어", () => _session.Editor.SetObjectLayer(null, o.Id, layer));
            }
        };
        _objVisible.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing && _viewport.SelectedObject is { } o)
            {
                Guard("보이기", () => _session.Editor.SetObjectVisible(null, o.Id, _objVisible.IsChecked == true));
            }
        };
        _viewport.ObjectSelected += (_, _) => RefreshObjects();

        RightPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { Small("카메라(보기) zoom"), _camZoom, Small("pan X"), _camPanX, Small("Y"), _camPanY },
        });
        ToolTip.SetTip(_camZoom, "샷 zoom — 1당 배율 +0.05(코어 규약). 보기만 바뀐다");

        foreach (TextBox box in new[] { _camZoom, _camPanX, _camPanY })
        {
            OnCommit(box, _ => CommitCamera());
        }

        RightPanel.Children.Add(Header("모션"));
        RightPanel.Children.Add(Labeled("이름", _motionName));
        RightPanel.Children.Add(Labeled("길이(초)", _motionDuration));
        RightPanel.Children.Add(Labeled("끝", _motionEnd));
        RightPanel.Children.Add(Labeled("기본 mode", _motionDefaultMode));
        RightPanel.Children.Add(_motionNotes);

        OnCommit(_motionName, text => WithMotion(m =>
        {
            if (text != m.Name)
            {
                _session.Editor.RenameMotion(m.Name, text);
                _motionList.SelectedItem = text;
            }
        }));
        OnCommit(_motionDuration, text => WithMotion(m => { if (TryFloat(text, out float v) && v != m.Duration) _session.Editor.SetDuration(m.Name, v); }));
        _motionEnd.SelectionChanged += (_, _) => WithMotion(m =>
        {
            if (_motionEnd.SelectedIndex >= 0 && (MotionEnd)_motionEnd.SelectedIndex != m.End)
            {
                _session.Editor.SetEnd(m.Name, (MotionEnd)_motionEnd.SelectedIndex);
            }
        });
        _motionDefaultMode.SelectionChanged += (_, _) => WithMotion(m =>
        {
            if (_motionDefaultMode.SelectedIndex >= 0 && (MotionChannelMode)_motionDefaultMode.SelectedIndex != m.DefaultMode)
            {
                _session.Editor.SetDefaultMode(m.Name, (MotionChannelMode)_motionDefaultMode.SelectedIndex);
            }
        });
        OnCommit(_motionNotes, text => WithMotion(m => { if ((m.Notes ?? "") != text) _session.Editor.SetNotes(m.Name, text); }));

        RightPanel.Children.Add(Header("키"));
        string[] toolLabels = { "선택 (Q)", "이동 (W)", "스케일 (R)", "리전" };

        for (int i = 0; i < toolLabels.Length; i++)
        {
            var tool = (GraphTool)i;
            Button button = SmallButton(toolLabels[i], (_, _) =>
            {
                _graph.Tool = tool;
                RefreshToolButtons();
            });
            _toolButtons[i] = button;
        }

        RightPanel.Children.Add(_selectionSummary);
        RightPanel.Children.Add(Labeled("in 기울기", _keyIn));
        RightPanel.Children.Add(Labeled("out 기울기", _keyOut));
        RightPanel.Children.Add(_keyBroken);
        RightPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                SmallButton("복사 (Ctrl+C)", (_, _) => _graph.CopySelection()),
                SmallButton("잘라내기 (Ctrl+X)", (_, _) => _graph.CutSelection()),
                SmallButton("붙여넣기 (Ctrl+V)", (_, _) => _graph.Paste()),
                SmallButton("삭제 (Del)", (_, _) => _graph.DeleteSelection()),
            },
        });
        RightPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "현재 시각", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 },
                SmallButton("복사 (Shift+W)", (_, _) => Guard("키 복사", CopyKeysAtCurrentTime)),
                SmallButton("붙여넣기 (Shift+E)", (_, _) => Guard("키 붙여넣기", PasteKeysAtCurrentTime)),
                SmallButton("지우기 (Shift+F)", (_, _) => Guard("키 지우기", ClearKeysAtCurrentTime)),
                SmallButton("인비트윈 − (Shift+1)", (_, _) => Guard("인비트윈 빼기", RemoveInbetween)),
                SmallButton("인비트윈 + (Shift+2)", (_, _) => Guard("인비트윈 넣기", AddInbetween)),
            },
        });

        OnCommit(_keyTime, text => ApplyToSelectedKeys(text, time: true));
        OnCommit(_keyValue, text => ApplyToSelectedKeys(text, time: false));
        OnCommit(_keyIn, text => WithSingleKey((key, k) =>
        {
            if (TryFloat(text, out float v) && v != k.InTangent)
            {
                _session.Editor.SetTangents(_graph.MotionName!, key, v, k.Broken ? k.OutTangent : v);
            }
        }));
        OnCommit(_keyOut, text => WithSingleKey((key, k) =>
        {
            if (TryFloat(text, out float v) && v != k.OutTangent)
            {
                _session.Editor.SetTangents(_graph.MotionName!, key, k.Broken ? k.InTangent : v, v);
            }
        }));
        _keyBroken.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                _graph.SetBroken(_keyBroken.IsChecked == true);
            }
        };

        RightPanel.Children.Add(Header("뷰포트 조작"));
        RightPanel.Children.Add(Labeled("노드", _nodeCombo));
        _nodeCombo.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _nodeCombo.SelectedItem is string node)
            {
                _viewport.SelectedNode = node;
                _viewport.InvalidateVisual();
            }
        };
        RightPanel.Children.Add(_followSelection);
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        string[] modeLabels = { "선택 (Q)", "이동 (W)", "회전 (E)", "배율 (R)" };

        for (int i = 0; i < modeLabels.Length; i++)
        {
            var mode = (ManipulatorMode)i;
            Button button = SmallButton(modeLabels[i], (_, _) =>
            {
                _viewport.Manipulator = mode;
                _viewport.InvalidateVisual();
                RefreshManipulatorButtons();
            });
            _manipulatorButtons[i] = button;
            modes.Children.Add(button);
        }

        RightPanel.Children.Add(modes);
        _autoKey.IsCheckedChanged += (_, _) =>
        {
            _viewport.AutoKey = _autoKey.IsChecked == true;
            _autoKeyToggle.IsChecked = _autoKey.IsChecked;
            _autoKeyToggle.Foreground = _autoKey.IsChecked == true ? AutoKeyOnBrush : null;

            if (!_refreshing)
            {
                _settings.AutoKey = _viewport.AutoKey;
            }
        };
        _autoKeyToggle.IsCheckedChanged += (_, _) => _autoKey.IsChecked = _autoKeyToggle.IsChecked;
        ToolTip.SetTip(_autoKeyToggle, "Auto Key — 매니퓰레이터를 놓는 순간 키가 들어간다(마야 빨간 키)");
        RightPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _autoKey, SmallButton("키 놓기 (S)", (_, _) => _viewport.SetKey()), SmallButton("버리기 (Esc)", (_, _) => _viewport.DiscardPending()) },
        });
        RightPanel.Children.Add(_pendingText);
        _viewport.PendingChanged += (_, _) => RefreshPending();
        RefreshManipulatorButtons();
        RefreshToolButtons();

        RightPanel.Children.Add(Header("보기"));
        _snap.IsCheckedChanged += (_, _) =>
        {
            if (_refreshing)
            {
                return;
            }

            _settings.SnapEnabled = _snap.IsChecked == true;
            PushSettingsToViews();
        };
        _snapStepCombo.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _snapStepCombo.SelectedIndex >= 0)
            {
                _settings.SnapStep = SnapSteps[_snapStepCombo.SelectedIndex];
                PushSettingsToViews();
            }
        };
        _onionFrames.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _onionFrames.SelectedIndex >= 0)
            {
                _settings.OnionSkinFrames = new[] { 0, 1, 2, 3, 4, 6 }[_onionFrames.SelectedIndex];
                PushSettingsToViews();
            }
        };
        _autoSaveCombo.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _autoSaveCombo.SelectedIndex >= 0)
            {
                _settings.AutoSaveMinutes = AutoSaveMinutes[_autoSaveCombo.SelectedIndex];
                PushSettingsToViews();
            }
        };
        RightPanel.Children.Add(Labeled("스냅 간격", _snapStepCombo));
        RightPanel.Children.Add(Labeled("어니언 프레임", _onionFrames));
        RightPanel.Children.Add(Labeled("자동 저장", _autoSaveCombo));
        RightPanel.Children.Add(Labeled("시간 표시", _timeDisplayCombo));
        RightPanel.Children.Add(Labeled("새 키 탄젠트", _newKeyTangent));
        _viewMode.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _viewMode.SelectedIndex >= 0)
            {
                _graph.ViewMode = (GraphViewMode)_viewMode.SelectedIndex;
            }
        };
        _newKeyTangent.SelectionChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                _settings.DefaultTangent = DefaultTangent;
                PushSettingsToViews();
            }
        };
        OnCommit(_currentTime, text => { if (TimeText.TryParse(text, Display, out double v)) _graph.CurrentTime = v; });
        _timeDisplayCombo.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _timeDisplayCombo.SelectedIndex >= 0)
            {
                _settings.TimeDisplay = (TimeDisplay)_timeDisplayCombo.SelectedIndex;
                PushSettingsToViews();
            }
        };
        RightPanel.Children.Add(new TextBlock
        {
            FontSize = 10,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            Text = "그래프(마야): 아웃라이너 행 클릭 = 그 커브만, Shift/Ctrl = 더하기 · LMB 선택 · MMB 이동 · Alt+MMB 팬 · Alt+RMB 줌 · 위 눈금자 = 스크럽 · " +
                   "더블클릭 = 커브 위에 키 · 핸들 드래그 = 기울기 · Stats 칸 +=3 · Ctrl+Z/Y\n" +
                   "도프 시트: 블록 드래그 = 시간 이동 · Alt+드래그 = 복제 · 범위 띠 양끝 = 시간 스케일 · 더블클릭 = 키 · 눈금자 = 스크럽 · 노드 행 = 접기",
        });
    }

    // ── 문서·목록 갱신 ─────────────────────────────────────────────────

    private void OnDocumentReplaced()
    {
        _session.Editor.Changed += (_, _) => OnEditorChanged();
        BindMotion(null);
        RefreshAll();
        RefreshAssets();
    }

    private void OnEditorChanged()
    {
        RefreshMotionList();
        RefreshMotionInspector();
        RefreshKeyInspector();
        RefreshProblems();
        RefreshNewForms();
        RefreshObjects();
    }

    /// <summary>오브젝트 목록·채널 박스·노드 목록을 선택 오브젝트에 맞춘다.</summary>
    private void RefreshObjects()
    {
        _refreshDepth++;

        try
        {
            RebuildHierarchy();

            SceneObject? selected = _viewport.SelectedObject;
            SceneAsset? scene = _viewport.Scene;
            bool has = selected is not null;

            var characters = new List<string> { NoCharacterToken };
            characters.AddRange(_session.Focus?.CharacterKeys ?? Enumerable.Empty<string>());

            if (selected?.Character is { } own && !characters.Contains(own))
            {
                characters.Add(own);
            }

            _objCharacter.ItemsSource = characters;
            _objCharacter.SelectedIndex = selected?.Character is { } ch ? characters.IndexOf(ch) : 0;

            foreach (Control c in new Control[] { _objX, _objY, _objRot, _objSx, _objSy, _objVisible, _objId, _objParent, _objParentNode, _objCharacter })
            {
                c.IsEnabled = has;
            }

            // 자식의 무대·층·자리는 부모가 정한다.
            bool topLevel = has && selected!.Parent is null;
            _objLayer.IsEnabled = topLevel;
            _objStage.IsEnabled = topLevel;
            _objPlace.IsEnabled = topLevel;
            _objPlaceFocus.IsEnabled = topLevel && selected!.Place is not null;
            _objParentNode.IsEnabled = has && selected!.Parent is not null;

            _objSummary.Text = selected is null
                ? "오브젝트 없음 — 왼쪽 하이어아키에서 층(Content)을 우클릭해 만든다"
                : $"{selected.Id} · {selected.Rig} · {StageNodes.ContentOf(selected.Stage, selected.Layer)}"
                  + (selected.Parent is null ? "" : $" · {selected.Parent}/{selected.ParentNode ?? "루트"}의 자식")
                  + (_viewport.Motion is null || _viewport.CanWear(selected) ? "" : " · 지금 모션은 이 리그에 안 맞는다");
            _objId.Text = selected?.Id ?? "";
            _objStage.SelectedIndex = selected is null ? -1 : StageNodes.Stages.ToList().IndexOf(selected.Stage);

            var places = new List<string> { NoPlaceToken };
            places.AddRange(ScenePreview.ScreenPoints);
            _objPlace.ItemsSource = places;
            _objPlace.SelectedIndex = selected?.Place is { } place && places.IndexOf(place) >= 0 ? places.IndexOf(place) : 0;
            _objPlaceFocus.SelectedIndex = selected is null ? -1 : Math.Max(0, ScenePreview.FocusPresets.ToList().IndexOf(selected.PlaceFocus ?? SceneObject.DefaultPlaceFocus));

            var parents = new List<string> { NoParentToken };

            if (selected is not null && scene is not null)
            {
                parents.AddRange(scene.Objects.Where(o => !scene.IsAncestorOrSelf(selected.Id, o.Id)).Select(o => o.Id));
            }

            _objParent.ItemsSource = parents;
            _objParent.SelectedIndex = selected?.Parent is { } p && parents.IndexOf(p) >= 0 ? parents.IndexOf(p) : 0;

            var parentNodes = new List<string> { RootNodeToken };

            if (selected?.Parent is { } parentId && scene?.Find(parentId) is { } parentObject
                && _session.Rig is { } rigFile && rigFile.TryGetRig(parentObject.Rig, out RigSchemaRigDto parentDto))
            {
                parentNodes.AddRange(parentDto.nodes.Select(n => n.id).Where(id => id != RigSchemaLoader.RootKey));
            }

            _objParentNode.ItemsSource = parentNodes;
            _objParentNode.SelectedIndex = selected?.ParentNode is { } pn && parentNodes.IndexOf(pn) >= 0 ? parentNodes.IndexOf(pn) : 0;
            _objX.Text = selected is null ? "" : F(selected.PositionX);
            _objY.Text = selected is null ? "" : F(selected.PositionY);
            _objRot.Text = selected is null ? "" : F(selected.RotationZ);
            _objSx.Text = selected is null ? "" : F(selected.ScaleX);
            _objSy.Text = selected is null ? "" : F(selected.ScaleY);
            _objLayer.SelectedIndex = selected is null ? -1 : SceneLayers.IndexOf(selected.Layer);
            _objVisible.IsChecked = selected?.Visible ?? false;
            CameraSettings? camera = _viewport.Scene?.Camera;
            _camZoom.Text = camera is null ? "" : F(camera.Zoom);
            _camPanX.Text = camera is null ? "" : F(camera.PanX);
            _camPanY.Text = camera is null ? "" : F(camera.PanY);
        }
        finally
        {
            _refreshDepth--;
        }

        RefreshNodeChoices();
        RefreshNewForms();
    }

    // ── 하이어아키 ─────────────────────────────────────────────────────

    /// <summary>트리 항목의 꼬리표 — 오브젝트 또는 오브젝트의 리그 노드.</summary>
    private sealed record HierarchyNode(string ObjectId, string? NodeId);

    /// <summary>폴더 항목의 꼬리표 — Scene(둘 다 null) · stage · layer(Content). 만들기·드롭의 자리가 된다.</summary>
    private sealed record HierarchyFolder(string? Stage, string? Layer);

    /// <summary>무대 뼈대의 다른 노드(Shot/Pan/Zoom/Mask/Framing…) — 보기만, 자리가 아니다.</summary>
    private sealed record SkeletonNode(string NodeId);

    /// <summary>
    /// 유니티 <c>PresentataionUIRoot</c> 그대로: 뼈대 노드를 덤프의 parent대로 중첩하고, 최상위 오브젝트는 그 무대·층의
    /// <c>Stage{NN}Depth_{Layer}_Content</c> 밑에, 리그 노드는 덤프의 parent 그대로, 자식 오브젝트는 붙은 노드 밑에.
    /// 압축 보기는 뼈대를 Stage › 층(Content)만 남긴다(데이터는 같다). 펼침 상태는 키로 기억한다(오브젝트는 기본 접힘).
    /// </summary>
    private void RebuildHierarchy()
    {
        SceneAsset? scene = _viewport.Scene;
        bool nodeWasSelected = _hierarchy.SelectedItem is TreeViewItem { Tag: HierarchyNode { NodeId: not null } };
        _hierarchy.Items.Clear();
        _hierarchyParents.Clear();

        if (scene is null)
        {
            return;
        }

        TreeViewItem? selectedItem = null;
        var root = Folder($"Scene  {scene.Name}  ·  PresentataionUIRoot", "scene");
        root.Tag = new HierarchyFolder(null, null);
        Attach(root, null);

        // 뼈대: 덤프 순서(부모 먼저)로 중첩 — 유니티 PresentataionUIRoot 그대로. 오브젝트가 선 가지(Content까지의 조상)만 기본 펼침,
        // 나머지 가지는 기본 접힘(유니티처럼 필요할 때 펼친다).
        IReadOnlyList<RigSchemaNodeDto> skeleton = BuiltinTuning.StageRig.nodes;
        var parentOf = skeleton.ToDictionary(n => n.id, n => string.IsNullOrEmpty(n.parent) ? RigSchemaLoader.RootKey : n.parent, StringComparer.Ordinal);
        var occupied = new HashSet<string>(StringComparer.Ordinal);

        foreach (SceneObject o in scene.TopLevel())
        {
            // Content에서 루트까지 올라가며 표시한다. __root는 자기 자신을 부모로 가리키므로 거기서 멈춘다.
            for (string id = StageNodes.ContentOf(o.Stage, o.Layer); id != RigSchemaLoader.RootKey && parentOf.ContainsKey(id); id = parentOf[id])
            {
                occupied.Add(id);
            }
        }

        var itemsByNode = new Dictionary<string, TreeViewItem>(StringComparer.Ordinal) { [RigSchemaLoader.RootKey] = root };
        var contentItems = new Dictionary<(string Stage, string Layer), TreeViewItem>();

        foreach (RigSchemaNodeDto node in skeleton)
        {
            if (node.id == RigSchemaLoader.RootKey)
            {
                continue;
            }

            TreeViewItem parentItem = itemsByNode.GetValueOrDefault(parentOf[node.id]) ?? root;
            bool isStageRoot = StageNodes.Stages.Any(st => StageNodes.StageRootOf(st) == node.id);
            bool isContent = StageNodes.TryParseContent(node.id, out string stage, out string layer);
            string header = node.id;

            if (node.id == StageNodes.ZoomRoot)
            {
                header += $"  ·  zoom {scene.Camera.Zoom:0.##}";
            }
            else if (node.id == StageNodes.PanRoot)
            {
                header += $"  ·  pan {scene.Camera.PanX:0},{scene.Camera.PanY:0}";
            }

            TreeViewItem item = Folder(header, "skel:" + node.id, defaultExpanded: occupied.Contains(node.id));
            item.Tag = isContent ? new HierarchyFolder(stage, layer)
                : isStageRoot ? new HierarchyFolder(StageNodes.Stages.First(st => StageNodes.StageRootOf(st) == node.id), null)
                : new SkeletonNode(node.id);
            Attach(item, parentItem);
            itemsByNode[node.id] = item;

            if (isContent)
            {
                contentItems[(stage, layer)] = item;
            }
        }

        foreach (SceneObject o in scene.TopLevel())
        {
            TreeViewItem under = contentItems.GetValueOrDefault((o.Stage, o.Layer)) ?? root;
            BuildObjectItem(scene, o, under, nodeWasSelected, ref selectedItem);
        }

        _hierarchy.Items.Add(root);

        // 고른 항목이 보이도록 조상을 펼친다(유니티가 선택을 드러내듯).
        for (TreeViewItem? up = selectedItem is null ? null : _hierarchyParents.GetValueOrDefault(selectedItem); up is not null; up = _hierarchyParents.GetValueOrDefault(up))
        {
            up.IsExpanded = true;
        }

        _hierarchy.SelectedItem = selectedItem;
    }

    private void Attach(TreeViewItem item, TreeViewItem? parent)
    {
        _hierarchyParents[item] = parent;
        parent?.Items.Add(item);
    }

    /// <summary>오브젝트 항목 + 리그 노드 트리 + 자식 오브젝트(붙은 노드 밑). 깊이 우선.</summary>
    private void BuildObjectItem(SceneAsset scene, SceneObject o, TreeViewItem parentItem, bool nodeWasSelected, ref TreeViewItem? selectedItem)
    {
        string kind = o.Rig;
        string suffix = (o.Visible ? "" : "  ·  숨김") + (o.Parent is not null && o.ParentNode is not null ? $"  @ {o.ParentNode}" : "");
        TreeViewItem objectItem = Folder($"{o.Id}  ·  {kind}{suffix}", "object:" + o.Id, defaultExpanded: false);
        objectItem.Tag = new HierarchyNode(o.Id, null);
        objectItem.FontWeight = FontWeight.SemiBold;
        objectItem.Opacity = scene.IsShown(o) ? 1 : 0.55;
        Attach(objectItem, parentItem);

        var itemsByNode = new Dictionary<string, TreeViewItem>(StringComparer.Ordinal);

        if (_session.Rig is { } rig && rig.TryGetRig(o.Rig, out RigSchemaRigDto dto))
        {
            foreach (RigSchemaNodeDto node in dto.nodes)
            {
                if (node.id == RigSchemaLoader.RootKey)
                {
                    continue; // 오브젝트 항목이 곧 루트다.
                }

                TreeViewItem nodeParent = string.IsNullOrEmpty(node.parent) || node.parent == RigSchemaLoader.RootKey || !itemsByNode.TryGetValue(node.parent, out TreeViewItem? found)
                    ? objectItem
                    : found;
                bool forbidden = PlacementNodes.IsForbidden(node.id);
                TreeViewItem nodeItem = Folder(node.id, $"node:{o.Id}/{node.id}");
                nodeItem.Tag = new HierarchyNode(o.Id, node.id);
                nodeItem.FontWeight = FontWeight.Normal;
                nodeItem.Foreground = forbidden ? ForbiddenNodeBrush : PlacementNodes.IsPlacementAxis(node.id) ? PlacementNodeBrush : ActingNodeBrush;
                nodeItem.FontStyle = forbidden ? FontStyle.Italic : FontStyle.Normal;
                ToolTip.SetTip(nodeItem, forbidden ? "키잉 금지 노드(코어 규칙) — 채널을 둘 수 없다" : PlacementNodes.IsPlacementAxis(node.id) ? "배치 축 — 런타임 place/size가 움직인다. 모션 채널로 쓰면 겹칠 수 있다" : "연기 축 — 모션 채널의 자리");
                Attach(nodeItem, nodeParent);
                itemsByNode[node.id] = nodeItem;

                if (nodeWasSelected && o.Id == _viewport.SelectedObjectId && node.id == _viewport.SelectedNode)
                {
                    selectedItem = nodeItem;
                }
            }
        }

        if (selectedItem is null && o.Id == _viewport.SelectedObjectId && !nodeWasSelected)
        {
            selectedItem = objectItem;
        }

        foreach (SceneObject child in scene.ChildrenOf(o.Id))
        {
            TreeViewItem under = child.ParentNode is { } pn && itemsByNode.TryGetValue(pn, out TreeViewItem? at) ? at : objectItem;
            BuildObjectItem(scene, child, under, nodeWasSelected, ref selectedItem);
        }
    }

    private TreeViewItem Folder(string header, string key, bool defaultExpanded = true)
    {
        bool expanded = defaultExpanded ? !_collapsedHierarchy.Contains(key) : _expandedHierarchy.Contains(key);
        var item = new TreeViewItem { Header = header, FontSize = 11, IsExpanded = expanded };
        item.PropertyChanged += (_, e) =>
        {
            if (e.Property == TreeViewItem.IsExpandedProperty)
            {
                if (item.IsExpanded == defaultExpanded)
                {
                    _collapsedHierarchy.Remove(key);
                    _expandedHierarchy.Remove(key);
                }
                else if (defaultExpanded)
                {
                    _collapsedHierarchy.Add(key);
                }
                else
                {
                    _expandedHierarchy.Add(key);
                }
            }
        };
        return item;
    }

    /// <summary>오브젝트의 노드 항목을 전부 펼치거나 접는다(유니티 Alt+클릭).</summary>
    private void ExpandObjectNodes(string objectId, bool expand)
    {
        foreach (TreeViewItem item in _hierarchyParents.Keys.Where(i => i.Tag is HierarchyNode n && n.ObjectId == objectId).ToList())
        {
            item.IsExpanded = expand;
        }
    }

    // ── 하이어아키 메뉴·단축키·드래그 ──────────────────────────────────

    private static TreeViewItem? ItemUnder(object? source)
        => (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);

    private void OnHierarchyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPointProperties props = e.GetCurrentPoint(_hierarchy).Properties;
        TreeViewItem? item = ItemUnder(e.Source);

        if (props.IsRightButtonPressed)
        {
            _menuTarget = item?.Tag;

            if (item?.Tag is HierarchyNode)
            {
                _hierarchy.SelectedItem = item;
            }

            return;
        }

        if (props.IsLeftButtonPressed && item?.Tag is HierarchyNode { NodeId: null } node)
        {
            _dragObjectId = node.ObjectId;
            _dragStart = e.GetPosition(_hierarchy);
            _dragging = false;
        }
    }

    private void OnHierarchyPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragObjectId is null)
        {
            return;
        }

        if (_dragging)
        {
            ShowDropTarget(DeepestItemAt(e.GetPosition(_hierarchy)));
            return;
        }

        Avalonia.Point now = e.GetPosition(_hierarchy);

        if (Math.Abs(now.X - _dragStart.X) + Math.Abs(now.Y - _dragStart.Y) > 6)
        {
            _dragging = true;
            _hierarchy.Cursor = new Cursor(StandardCursorType.DragMove);
        }
    }

    /// <summary>드래그 중 놓을 자리 표시 — 그 항목의 헤더 앞에 ⇥. 자기 자신·camera 줄은 자리가 아니다.</summary>
    private void ShowDropTarget(TreeViewItem? item)
    {
        if (item is not null && (item.Tag is null || item.Tag is HierarchyNode n && n.ObjectId == _dragObjectId))
        {
            item = null;
        }

        if (ReferenceEquals(item, _dropItem))
        {
            return;
        }

        if (_dropItem is not null)
        {
            _dropItem.Header = _dropHeader;
        }

        _dropItem = item;
        _dropHeader = item?.Header;

        if (item is not null)
        {
            item.Header = "⇥ " + item.Header;
        }
    }

    private void OnHierarchyPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        string? dragged = _dragObjectId;
        bool dragging = _dragging;
        ShowDropTarget(null);
        _dragObjectId = null;
        _dragging = false;
        _hierarchy.Cursor = Cursor.Default;

        if (dragged is null || !dragging)
        {
            return;
        }

        object? target = DeepestItemAt(e.GetPosition(_hierarchy))?.Tag;
        Guard("드래그", () => DropObject(dragged, target));
        e.Handled = true;
    }

    /// <summary>그 자리의 가장 깊은(안쪽) 항목 — 그려진 위치로 찾는다(렌더러 히트 테스트 없이).</summary>
    private TreeViewItem? DeepestItemAt(Avalonia.Point at)
    {
        _hierarchy.UpdateLayout();
        TreeViewItem? best = null;
        int bestDepth = -1;

        foreach (TreeViewItem item in _hierarchyParents.Keys)
        {
            if (!item.IsEffectivelyVisible || item.TranslatePoint(new Avalonia.Point(0, 0), _hierarchy) is not { } origin)
            {
                continue;
            }

            if (!new Avalonia.Rect(origin, item.Bounds.Size).Contains(at))
            {
                continue;
            }

            int depth = 0;

            for (TreeViewItem? up = item; up is not null; up = _hierarchyParents.GetValueOrDefault(up))
            {
                depth++;
            }

            if (depth > bestDepth)
            {
                best = item;
                bestDepth = depth;
            }
        }

        return best;
    }

    /// <summary>드롭: 오브젝트/노드 위 → 그 자식, 레이어 폴더 → 그 레이어의 최상위, Scene/stage → 최상위.</summary>
    private void DropObject(string objectId, object? target)
    {
        switch (target)
        {
            case HierarchyNode node when node.ObjectId != objectId:
                _session.Editor.SetObjectParent(null, objectId, node.ObjectId, node.NodeId, null, _viewport.PlacementKeepingWorld(objectId, node.ObjectId, node.NodeId));
                break;
            case HierarchyFolder folder:
                _session.Editor.SetObjectParent(null, objectId, null, null, folder.Layer, _viewport.PlacementKeepingWorld(objectId, null, null), folder.Stage);
                break;
        }
    }

    private void OnHierarchyKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers == KeyModifiers.Control;
        bool ctrlShift = e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift);

        switch (e.PhysicalKey)
        {
            case PhysicalKey.Delete when e.KeyModifiers == KeyModifiers.None:
                Guard("오브젝트 삭제", RemoveObject);
                break;
            case PhysicalKey.D when ctrl:
                Guard("복제", DuplicateSelectedObject);
                break;
            case PhysicalKey.N when ctrlShift:
                Guard("캐릭터 리그 만들기", () => CreateObject(SceneObject.DefaultRig, _hierarchy.SelectedItem is TreeViewItem { Tag: { } tag } ? tag : null));
                break;
            case PhysicalKey.F2 when e.KeyModifiers == KeyModifiers.None:
                _objId.Focus();
                _objId.SelectAll();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>유니티 하이어아키 우클릭 메뉴. 우클릭한 자리(_menuTarget)에 따라 달라진다.</summary>
    private void RebuildHierarchyMenu()
    {
        _hierarchyMenu.Items.Clear();
        SceneAsset? scene = _viewport.Scene;
        object? target = _menuTarget;
        SceneObject? o = target is HierarchyNode n && scene is not null ? scene.Find(n.ObjectId) : null;

        MenuItem Item(string header, Action action, KeyGesture? gesture = null, bool enabled = true)
        {
            var item = new MenuItem { Header = header, FontSize = 11, InputGesture = gesture, IsEnabled = enabled };
            item.Click += (_, _) => Guard(header, action);
            _hierarchyMenu.Items.Add(item);
            return item;
        }

        if (o is not null)
        {
            Item("이름 바꾸기", () =>
            {
                _objId.Focus();
                _objId.SelectAll();
            }, new KeyGesture(Key.F2));
            Item("복제", DuplicateSelectedObject, new KeyGesture(Key.D, KeyModifiers.Control));
            Item("삭제", RemoveObject, new KeyGesture(Key.Delete));
            _hierarchyMenu.Items.Add(new Separator());
            Item(o.Visible ? "숨기기" : "보이기", () => _session.Editor.SetObjectVisible(null, o.Id, !o.Visible));

            var layer = new MenuItem { Header = "레이어", FontSize = 11, IsEnabled = o.Parent is null };

            foreach (string l in SceneLayers.Order)
            {
                var pick = new MenuItem { Header = l + (l == o.Layer ? "  ✓" : ""), FontSize = 11 };
                pick.Click += (_, _) => Guard("레이어", () => _session.Editor.SetObjectLayer(null, o.Id, l));
                layer.Items.Add(pick);
            }

            _hierarchyMenu.Items.Add(layer);

            if (o.Parent is not null)
            {
                Item("부모에서 떼기 (최상위로, 월드 자세 유지)", () => _session.Editor.SetObjectParent(null, o.Id, null, null, null, _viewport.PlacementKeepingWorld(o.Id, null, null)));
            }

            _hierarchyMenu.Items.Add(new Separator());
        }

        string where = target switch
        {
            HierarchyNode at => $" — {at.ObjectId}{(at.NodeId is null ? "" : "/" + at.NodeId)}의 자식으로",
            HierarchyFolder { Stage: { } fs, Layer: { } fl } => $" — {StageNodes.ContentOf(fs, fl)}에",
            HierarchyFolder { Stage: { } fs } => $" — {StageNodes.StageName(fs)} Mid에",
            _ => "",
        };
        Item("캐릭터 리그 만들기" + where, () => CreateObject(SceneObject.DefaultRig, target), new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Shift), enabled: target is not SkeletonNode);

        if (o is not null)
        {
            _hierarchyMenu.Items.Add(new Separator());
            Item("노드 전부 펼치기", () => ExpandObjectNodes(o.Id, true));
            Item("노드 전부 접기", () => ExpandObjectNodes(o.Id, false));
        }
    }

    /// <summary>비어 있는 다음 id — character는 s1·s2…(Yarn 슬롯 키 꼴), 그 밖은 rigKind+번호. 채널 박스에서 바꾼다.</summary>
    private string NewObjectId(string rig)
    {
        string stem = rig == SceneObject.DefaultRig ? "s" : MotionNames.Sanitize(rig);
        SceneAsset? scene = _viewport.Scene;
        int n = 1;

        while (scene?.Find($"{stem}{n}") is not null)
        {
            n++;
        }

        return $"{stem}{n}";
    }

    /// <summary>
    /// 만들기 — 자리(target)가 오브젝트/노드면 그 자식으로, 레이어 폴더면 그 레이어의 최상위로,
    /// 그 밖이면 선택 오브젝트의 레이어(없으면 mid)의 최상위로. 만든 것을 고른다.
    /// </summary>
    private string CreateObject(string rig, object? target, string? id = null)
    {
        string? parent = null;
        string? parentNode = null;
        SceneObject? selectedRoot = _viewport.SelectedObject is { } selected ? _viewport.Scene!.RootOf(selected) : null;
        string layer = selectedRoot?.Layer ?? SceneLayers.Mid;
        string stage = selectedRoot?.Stage ?? SceneObject.DefaultStage;

        switch (target)
        {
            case HierarchyNode node:
                parent = node.ObjectId;
                parentNode = node.NodeId;
                break;
            case HierarchyFolder { Stage: { } fs, Layer: { } l }:
                stage = fs;
                layer = l;
                break;
            case HierarchyFolder { Stage: { } fs }:
                stage = fs;
                layer = SceneLayers.Mid;
                break;
        }

        id ??= NewObjectId(rig);
        _session.Editor.AddSceneObject(null, id, rig, layer, parent, parentNode, stage);
        _viewport.SelectedObjectId = id;
        RefreshAssets();
        return id;
    }

    private void OnHierarchySelected()
    {
        if (_refreshing || _hierarchy.SelectedItem is not TreeViewItem { Tag: HierarchyNode node })
        {
            return;
        }

        if (node.NodeId is null)
        {
            _viewport.SelectedObjectId = node.ObjectId;
            return;
        }

        SelectNodeInHierarchy(node);
    }

    /// <summary>노드 항목: 그 오브젝트를 고르고, 매니퓰레이터 노드와 채널 추가 대상을 그 노드로. 키 따라가기는 끈다(사용자가 직접 골랐다).</summary>
    private void SelectNodeInHierarchy(HierarchyNode node)
    {
        _viewport.SelectedObjectId = node.ObjectId;
        _followSelection.IsChecked = false;
        _viewport.SelectedNode = node.NodeId;
        _viewport.InvalidateVisual();
        RefreshNodeChoices();

        if (_newChannelNode.ItemsSource is IEnumerable<string> choices && choices.Contains(node.NodeId!))
        {
            _newChannelNode.SelectedItem = node.NodeId;
        }
    }

    /// <summary>테스트 손잡이 — 하이어아키에서 노드(또는 오브젝트)를 고른 것과 같다.</summary>
    internal void SelectInHierarchy(string objectId, string? nodeId)
    {
        var node = new HierarchyNode(objectId, nodeId);

        if (nodeId is null)
        {
            _viewport.SelectedObjectId = objectId;
        }
        else
        {
            SelectNodeInHierarchy(node);
        }
    }

    /// <summary>테스트 손잡이 — 노드 더블클릭(그 노드에 채널 추가).</summary>
    internal void AddChannelFromHierarchy(string objectId, string nodeId)
    {
        SelectNodeInHierarchy(new HierarchyNode(objectId, nodeId));
        AddChannel();
    }

    internal int HierarchyObjectCount => _viewport.SceneObjects.Count;

    internal string? NewChannelNode => _newChannelNode.SelectedItem as string;

    private void CommitCamera()
    {
        if (_refreshing || _viewport.Scene is not { } scene)
        {
            return;
        }

        if (!TryFloat(_camZoom.Text ?? "", out float zoom) || !TryFloat(_camPanX.Text ?? "", out float panX) || !TryFloat(_camPanY.Text ?? "", out float panY))
        {
            return;
        }

        Guard("카메라", () => _session.Editor.SetCamera(scene.Name, zoom, panX, panY));
    }

    internal void CommitCameraFromForm(float zoom, float panX, float panY)
    {
        _camZoom.Text = F(zoom);
        _camPanX.Text = F(panX);
        _camPanY.Text = F(panY);
        CommitCamera();
    }

    private void RemoveObject()
    {
        if (_viewport.SelectedObjectId is { } id)
        {
            _session.Editor.RemoveSceneObject(null, id);
            RefreshAssets();
        }
    }

    private void DuplicateSelectedObject()
    {
        if (_viewport.SelectedObjectId is { } id)
        {
            SceneObject copy = _session.Editor.DuplicateSceneObject(null, id);
            _viewport.SelectedObjectId = copy.Id;
            RefreshAssets();
        }
    }

    /// <summary>테스트 손잡이 — 채널 박스의 부모 콤보(월드 위치 유지)와 배역 콤보.</summary>
    internal void SetParentFromForm(string? parent) => _objParent.SelectedItem = parent ?? NoParentToken;

    internal void SetParentNodeFromForm(string? node) => _objParentNode.SelectedItem = node ?? RootNodeToken;

    internal void SetCharacterFromForm(string? character) => _objCharacter.SelectedItem = character ?? NoCharacterToken;

    private void RenameSelectedObject()
    {
        if (_refreshing || _viewport.SelectedObject is not { } o)
        {
            return;
        }

        string newId = (_objId.Text ?? "").Trim();

        if (newId.Length == 0 || newId == o.Id)
        {
            _objId.Text = o.Id;
            return;
        }

        Guard("이름 바꾸기", () =>
        {
            _session.Editor.RenameSceneObject(null, o.Id, newId);
            _viewport.SelectedObjectId = newId;
            RefreshAssets();
        });
    }

    private void CommitObjectPlacement()
    {
        if (_refreshing || _viewport.SelectedObject is not { } o)
        {
            return;
        }

        if (!TryFloat(_objX.Text ?? "", out float x) || !TryFloat(_objY.Text ?? "", out float y) || !TryFloat(_objRot.Text ?? "", out float rot)
            || !TryFloat(_objSx.Text ?? "", out float sx) || !TryFloat(_objSy.Text ?? "", out float sy))
        {
            return;
        }

        Guard("배치", () => _session.Editor.SetObjectPlacement(null, o.Id, x, y, rot, sx, sy));
    }

    /// <summary>테스트 손잡이 — 오브젝트 추가(최상위, 그 레이어)와 채널 박스.</summary>
    internal void AddObjectFromForm(string id, string rig, string layer, string stage = SceneObject.DefaultStage)
        => CreateObject(rig, new HierarchyFolder(stage, layer), id);

    internal void SetPlaceFromForm(string? screenPoint, string? focus = null)
    {
        _objPlace.SelectedItem = screenPoint ?? NoPlaceToken;

        if (focus is not null)
        {
            _objPlaceFocus.SelectedItem = focus;
        }
    }

    /// <summary>테스트 손잡이 — 우클릭 메뉴 "만들기"(오브젝트/노드 위면 그 자식). 새 id를 돌려준다.</summary>
    internal string CreateObjectFromMenu(string rig, string? atObjectId, string? atNodeId)
        => CreateObject(rig, atObjectId is null ? null : new HierarchyNode(atObjectId, atNodeId));

    internal void DuplicateSelectedFromMenu() => DuplicateSelectedObject();

    internal void RenameSelectedFromForm(string newId)
    {
        _objId.Text = newId;
        RenameSelectedObject();
    }

    /// <summary>테스트 손잡이 — 하이어아키 항목의 경로(Scene부터 헤더 순). 없으면 빈 목록.</summary>
    internal IReadOnlyList<string> HierarchyPathOf(string objectId, string? nodeId)
    {
        TreeViewItem? item = _hierarchyParents.Keys.FirstOrDefault(i => i.Tag is HierarchyNode n && n.ObjectId == objectId && n.NodeId == nodeId);
        var path = new List<string>();

        for (TreeViewItem? up = item; up is not null; up = _hierarchyParents.GetValueOrDefault(up))
        {
            path.Insert(0, up.Header?.ToString() ?? "");
        }

        return path;
    }

    /// <summary>테스트 손잡이 — 항목의 창 좌표 가운데(드래그 테스트용). 접혀 있으면 조상을 펼친다.</summary>
    internal Avalonia.Point? HierarchyItemCenter(string? objectId, string? nodeId, string? stage = null, string? layer = null)
    {
        TreeViewItem? item = objectId is null
            ? _hierarchyParents.Keys.FirstOrDefault(i => i.Tag is HierarchyFolder f && f.Stage == stage && f.Layer == layer)
            : _hierarchyParents.Keys.FirstOrDefault(i => i.Tag is HierarchyNode n && n.ObjectId == objectId && n.NodeId == nodeId);

        if (item is null)
        {
            return null;
        }

        for (TreeViewItem? up = _hierarchyParents.GetValueOrDefault(item); up is not null; up = _hierarchyParents.GetValueOrDefault(up))
        {
            up.IsExpanded = true;
        }

        _hierarchy.UpdateLayout();
        // 헤더(글자) 가운데 — 자식 영역·들여쓰기와 무관하게 그 항목의 줄이다. 헤더 프레젠터가 아직 없으면 항목의 첫 줄.
        Control header = item.HeaderPresenter ?? (Control)item;
        return header == item
            ? item.TranslatePoint(new Avalonia.Point(Math.Min(60, item.Bounds.Width / 2), 10), this)
            : header.TranslatePoint(new Avalonia.Point(header.Bounds.Width / 2, header.Bounds.Height / 2), this);
    }

    internal void CommitObjectPlacementFromForm(float x, float y, float rotationZ, float scaleX, float scaleY)
    {
        _objX.Text = F(x);
        _objY.Text = F(y);
        _objRot.Text = F(rotationZ);
        _objSx.Text = F(scaleX);
        _objSy.Text = F(scaleY);
        CommitObjectPlacement();
    }

    private static TextBlock Small(string text) => new() { Text = text, FontSize = 10, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };

    private void RefreshAll()
    {
        RefreshMotionList();
        RefreshChannels();
        RefreshMotionInspector();
        RefreshKeyInspector();
        RefreshProblems();
        RefreshNewForms();
        StatusText.Text = _session.Status;
    }

    private void RefreshMotionList()
    {
        _refreshDepth++;

        try
        {
            string? selected = SelectedMotion ?? _graph.MotionName;
            // rigKind로 묶어 보인다(모션의 주인은 rigKind) — 같은 리그끼리 이름순.
            List<string> names = _session.Editor.Document.Motions
                .OrderBy(m => m.Rig, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal)
                .Select(m => m.Name).ToList();
            _motionList.ItemsSource = names;
            _motionList.ItemTemplate ??= new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) =>
            {
                MotionAsset? motion = _session.Editor.Document.Find(name);
                bool fits = motion is null || _viewport.SelectedObject is not { } o || _session.Rig is null || ScenePreview.CanWear(_session.Rig, o, motion);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Opacity = string.IsNullOrEmpty(name) ? 0.6 : fits ? 1 : 0.5 };
                row.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(name) ? "(이름 없음 — 아래 문제 칸의 '이름 고치기')" : name, FontSize = 11 });
                row.Children.Add(new TextBlock { Text = motion is null ? "" : motion.Rig + (fits ? "" : " · 안 맞음"), FontSize = 10, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
                return row;
            });
            int index = selected is null ? -1 : names.IndexOf(selected);
            _motionList.SelectedIndex = index;

            if (index < 0 && _graph.MotionName is not null)
            {
                BindMotion(null);
            }
            else if (index >= 0 && _graph.MotionName != names[index])
            {
                BindMotion(names[index]);
            }
        }
        finally
        {
            _refreshDepth--;
        }
    }

    private void OnMotionSelected()
    {
        if (_refreshing)
        {
            return;
        }

        BindMotion(SelectedMotion);
        RefreshMotionInspector();
        RefreshKeyInspector();
        RefreshNewForms();
    }

    /// <summary>
    /// 마야 그래프 에디터 아웃라이너 — 노드 › 채널 › 성분. 행 클릭 = 그 커브만 보인다, Shift/Ctrl+클릭 = 더하기/빼기.
    /// 없는 성분은 흐리고, 클릭하면 현재 시각의 중립값 키 하나로 생긴다.
    /// </summary>
    private void RefreshChannels()
    {
        _channelRows.Children.Clear();
        MotionAsset? motion = _graph.Motion;

        if (motion is null)
        {
            _channelRows.Children.Add(new TextBlock { Text = "(모션 없음)", FontSize = 11, Opacity = 0.6, Margin = new Avalonia.Thickness(6, 4) });
            return;
        }

        MotionAsset current = motion;
        string query = (_outlinerSearch.Text ?? "").Trim();

        foreach (ChannelAsset channel in current.Channels)
        {
            string node = channel.Node;
            MotionChannelKind kind = channel.Kind;

            if (query.Length > 0 && !node.Contains(query, StringComparison.OrdinalIgnoreCase) && !ChannelKindLabel.Of(kind).Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Axis[] possibleAxes = kind == MotionChannelKind.Euler ? new[] { Axis.X, Axis.Y, Axis.Z } : new[] { Axis.X, Axis.Y };
            CurveRef[] axesOfChannel = possibleAxes.Select(a => new CurveRef(node, kind, a)).ToArray();
            bool anyLocked = axesOfChannel.Any(_flags.IsLocked);
            bool anyMuted = axesOfChannel.Any(_flags.IsMuted);

            // 채널 행: node · kind · mode + 잠금 · 뮤트 · 삭제. 글자 클릭 = 이 채널의 커브만.
            var header = new DockPanel { Margin = new Avalonia.Thickness(2, 3, 2, 0) };
            Button remove = SmallButton("×", (_, _) => Guard("채널 삭제", () => _session.Editor.RemoveChannel(current.Name, node, kind)));
            Button lockButton = SmallButton(anyLocked ? "🔒" : "🔓", (_, _) =>
            {
                foreach (CurveRef c in axesOfChannel)
                {
                    _flags.SetLocked(c, !anyLocked);
                }
            });
            ToolTip.SetTip(lockButton, anyLocked ? "잠금 풀기 — 다시 고르고 끌 수 있다" : "잠금 — 보이되 고르거나 끌 수 없다");
            Button muteButton = SmallButton(anyMuted ? "🔇" : "🔊", (_, _) =>
            {
                foreach (CurveRef c in axesOfChannel)
                {
                    _flags.SetMuted(c, !anyMuted);
                }
            });
            ToolTip.SetTip(muteButton, anyMuted ? "뮤트 풀기" : "뮤트 — 뷰포트 재생에서 이 채널을 뺀다(저장되지 않는다)");
            Button modeButton = SmallButton(channel.Mode == MotionChannelMode.Relative ? "rel" : "abs", (_, _) => Guard("채널 mode", () =>
                _session.Editor.SetChannelMode(current.Name, node, kind, channel.Mode == MotionChannelMode.Relative ? MotionChannelMode.Absolute : MotionChannelMode.Relative)));
            ToolTip.SetTip(modeButton, "클릭: relative ↔ absolute");

            foreach (Button b in new[] { remove, muteButton, lockButton, modeButton })
            {
                DockPanel.SetDock(b, Dock.Right);
                header.Children.Add(b);
            }

            var title = new TextBlock
            {
                Text = $"{node} · {ChannelKindLabel.Of(kind)}",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(title, $"{node}\n{kind} · {channel.Mode}\n클릭: 이 채널의 커브만 · Shift/Ctrl: 더하기");
            title.PointerPressed += (_, e) => ShowCurves(axesOfChannel, additive: e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control));
            header.Children.Add(title);
            _channelRows.Children.Add(header);

            // 성분 행: 색 네모 + 이름. 보이는 행은 파랗게(마야 선택).
            foreach (Axis axis in possibleAxes)
            {
                var curveRef = new CurveRef(node, kind, axis);
                CurveSlot? slot = _graph.Curves.FirstOrDefault(s => s.Curve == curveRef);
                bool visible = slot?.Visible == true;
                Color color = slot?.Color ?? Color.FromRgb(0x80, 0x80, 0x80);
                var row = new Border
                {
                    Padding = new Avalonia.Thickness(18, 1, 4, 1),
                    Background = visible ? OutlinerRowSelected : Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = curveRef,
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Opacity = slot is null ? 0.45 : 1,
                        Children =
                        {
                            new Border { Width = 8, Height = 8, Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Text = $"{ChannelKindLabel.Of(kind)} {axis.ToString().ToUpperInvariant()}", FontSize = 11, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center },
                        },
                    },
                };
                ToolTip.SetTip(row, slot is null
                    ? "성분이 없다 — 클릭하면 현재 시각에 중립값 키 하나로 생긴다"
                    : "클릭: 이 커브만 · Shift/Ctrl+클릭: 더하기/빼기");
                row.PointerPressed += (_, e) =>
                {
                    bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
                    OutlinerClick(curveRef, additive);
                    e.Handled = true;
                };
                _channelRows.Children.Add(row);
            }
        }

        if (current.Channels.Count == 0)
        {
            _channelRows.Children.Add(new TextBlock { Text = "(채널 없음 — 왼쪽 '채널 추가'에서 노드를 골라 추가)", FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(6, 4) });
        }
    }

    /// <summary>아웃라이너 성분 행 클릭 — 없는 성분은 만들고, 있는 성분은 그 커브만(additive면 토글).</summary>
    internal void OutlinerClick(CurveRef curve, bool additive)
    {
        MotionAsset? motion = _graph.Motion;

        if (motion is null)
        {
            return;
        }

        if (curve.Resolve(motion) is null)
        {
            Guard("성분 추가", () => _session.Editor.AddKey(motion.Name, curve.Node, curve.Kind, curve.Axis, new KeyAsset
            {
                T = (float)_graph.CurrentTime,
                V = MotionMath.Neutral(curve.Kind),
                Tangent = DefaultTangent,
            }));
            return;
        }

        if (additive)
        {
            bool wasVisible = _graph.Curves.FirstOrDefault(s => s.Curve == curve)?.Visible == true;
            _graph.SetVisible(curve, !wasVisible);
            return;
        }

        _graph.SetVisibleAll(c => c == curve);
    }

    private void ShowCurves(IReadOnlyCollection<CurveRef> curves, bool additive)
    {
        if (additive)
        {
            _graph.SetVisibleAll(c => curves.Contains(c) || (_graph.Curves.FirstOrDefault(s => s.Curve == c)?.Visible == true));
            return;
        }

        _graph.SetVisibleAll(curves.Contains);
    }

    internal int OutlinerRowCount => _channelRows.Children.Count;

    internal string CurrentTimeText => _currentTime.Text ?? "";

    internal string OutlinerQuery
    {
        get => _outlinerSearch.Text ?? "";
        set
        {
            _outlinerSearch.Text = value;
            RefreshChannels();
        }
    }

    private void RefreshMotionInspector()
    {
        _refreshDepth++;

        try
        {
            MotionAsset? motion = _graph.Motion;
            bool has = motion is not null;
            _motionName.IsEnabled = has;
            _motionDuration.IsEnabled = has;
            _motionEnd.IsEnabled = has;
            _motionDefaultMode.IsEnabled = has;
            _motionNotes.IsEnabled = has;

            _motionName.Text = motion?.Name ?? "";
            _motionDuration.Text = motion is null ? "" : motion.Duration.ToString("0.###", CultureInfo.InvariantCulture);
            _motionEnd.SelectedIndex = motion is null ? -1 : (int)motion.End;
            _motionDefaultMode.SelectedIndex = motion is null ? -1 : (int)motion.DefaultMode;
            _motionNotes.Text = motion?.Notes ?? "";
        }
        finally
        {
            _refreshDepth--;
        }
    }

    private void RefreshKeyInspector()
    {
        _refreshDepth++;

        try
        {
            MotionAsset? motion = _graph.Motion;
            int count = _graph.Selection.Count;
            KeyAsset? single = count == 1 && motion is not null ? _graph.Selection.First().Resolve(motion) : null;
            bool editable = single is not null;

            _selectionSummary.Text = count == 0 ? "선택 없음" : count == 1 ? $"키 1개 — {_graph.Selection.First().Curve}" : $"키 {count}개";
            _keyTime.IsEnabled = count > 0;
            _keyValue.IsEnabled = count > 0;
            _keyIn.IsEnabled = editable;
            _keyOut.IsEnabled = editable;
            _keyBroken.IsEnabled = count > 0;

            _keyTime.Text = single is null ? (count > 0 ? "+=0" : "") : TimeText.Format(single.T, Display);
            _keyValue.Text = single is null ? (count > 0 ? "+=0" : "") : F(single.V);
            _keyIn.Text = single is null ? "" : F(single.InTangent);
            _keyOut.Text = single is null ? "" : F(single.OutTangent);
            _keyBroken.IsChecked = single?.Broken ?? false;
        }
        finally
        {
            _refreshDepth--;
        }
    }

    private void RefreshProblems()
    {
        IReadOnlyList<MotionProblem> problems = _session.Problems;
        ProblemsPanel.Children.Clear();
        ProblemsPanel.IsVisible = problems.Count > 0;

        foreach (MotionProblem problem in problems)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock
            {
                Text = "⚠ " + problem,
                FontSize = 11,
                Foreground = Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 700,
            });

            foreach (MotionFix fix in MotionFixes.Suggest(problem))
            {
                string motionName = problem.MotionName;
                MotionFix captured = fix;
                row.Children.Add(SmallButton(MotionFixes.Label(fix), (_, _) => Guard("돕는 손", () =>
                {
                    if (!MotionFixes.Apply(_session.Editor, motionName, captured))
                    {
                        _session.SetStatus($"{MotionFixes.Label(captured)}: 바꿀 것이 없다.");
                    }
                })));
            }

            ProblemsPanel.Children.Add(row);
        }
    }

    /// <summary>테스트가 문제 행의 돕는 손을 누른 것과 같다.</summary>
    internal bool ApplyFix(string motionName, MotionFix fix) => MotionFixes.Apply(_session.Editor, motionName, fix);

    internal int ProblemRowsShown => ProblemsPanel.Children.Count;

    private void RefreshNewForms()
    {
        _refreshDepth++;

        try
        {
            string? nodeSelected = _newChannelNode.SelectedItem as string;
            MotionAsset? motion = _graph.Motion;
            List<string> nodes = new();

            // 채널 mode 콤보는 모션을 고를 때 그 모션의 기본 mode로 시작한다. 그 뒤로는 사용자가 고른 값을 지킨다.
            if (motion?.Name != _channelModeInitializedFor)
            {
                _channelModeInitializedFor = motion?.Name;
                _newChannelMode.SelectedIndex = motion?.DefaultMode == MotionChannelMode.Absolute ? 1 : 0;
            }

            if (motion is not null && _session.Rig is not null && _session.Rig.NodeIds.TryGetValue(motion.Rig, out ICollection<string>? ids))
            {
                nodes = ids.Where(id => !MotionRules.ForbiddenNodes.Contains(id) && id != "__root").OrderBy(id => id, StringComparer.Ordinal).ToList();
            }

            _newChannelNode.ItemsSource = nodes;
            _newChannelNode.SelectedIndex = nodes.Count == 0 ? -1 : Math.Max(0, nodeSelected is null ? 0 : nodes.IndexOf(nodeSelected));
            _newChannelNode.IsEnabled = nodes.Count > 0;
            ToolTip.SetTip(_newChannelNode, nodes.Count == 0 ? "리그 덤프를 먼저 싣고 모션을 고른다" : "채널을 둘 노드");
        }
        finally
        {
            _refreshDepth--;
        }
    }

    // ── 명령 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 모션 추가 — 이름은 motion, motion_2…, 리그는 선택 오브젝트의 rigKind(없으면 character), 길이 1초, 끝 restore.
    /// 전부 오른쪽 인스펙터에서 바꾼다. 테스트 손잡이는 값을 준다.
    /// </summary>
    private void AddMotion(string? name = null, string? rig = null, float duration = 1f, MotionEnd end = MotionEnd.Restore)
    {
        name = (name ?? "").Trim();

        // 이름을 비우면 motion, motion_2…로 만든다 — 빈 이름의 모션은 목록에서 안 보여 규칙 위반이 "안 없어지는" 것처럼 보인다.
        if (name.Length == 0)
        {
            name = MotionNames.Unique(_session.Editor.Document, MotionNames.Fallback);
        }

        rig ??= _viewport.SelectedObject?.Rig ?? SceneObject.DefaultRig;
        _session.Editor.AddMotion(name, rig, duration, end, end == MotionEnd.Restore ? MotionChannelMode.Relative : MotionChannelMode.Absolute);
        _motionList.SelectedItem = name;
    }

    private void RemoveMotion()
    {
        if (SelectedMotion is { } name)
        {
            _session.Editor.RemoveMotion(name);
        }
    }

    private void AddChannel()
    {
        MotionAsset motion = _graph.Motion ?? throw new InvalidOperationException("먼저 모션을 고른다.");
        string node = _newChannelNode.SelectedItem as string ?? throw new InvalidOperationException("노드를 고른다 — 리그 덤프가 필요하다.");
        var kind = (MotionChannelKind)Math.Max(0, _newChannelKind.SelectedIndex);
        MotionChannelMode mode = _newChannelMode.SelectedIndex == 1 ? MotionChannelMode.Absolute : MotionChannelMode.Relative;
        _session.Editor.AddChannel(motion.Name, node, kind, mode);
    }

    /// <summary>
    /// 시각·값 칸: 숫자는 절대값, <c>+=</c>·<c>-=</c>·<c>*=</c>·<c>/=</c>는 상대. 선택 키 전부에 적용한다.
    /// 시각의 절대값은 키 하나일 때만(여러 키를 한 시각에 겹칠 수 없다).
    /// </summary>
    private void ApplyToSelectedKeys(string text, bool time)
    {
        if (_refreshing || _graph.Motion is not { } motion || _graph.Selection.Count == 0 || !RelativeInput.TryParse(text, out Func<float, float> apply))
        {
            return;
        }

        List<KeyRef> keys = _graph.Selection.ToList();

        Guard(time ? "키 시각" : "키 값", () =>
        {
            if (!time)
            {
                _session.Editor.SetKeyValues(motion.Name, keys, k => apply(k.V));
                return;
            }

            bool relative = text.TrimStart().Length >= 2 && text.TrimStart()[1] == '=';

            if (relative)
            {
                float probe = 1f;
                float delta = (float)TimeText.ToSeconds(apply(probe) - probe, Display); // +=·-=는 표시 단위의 상수 변위; *=·/=는 시각에 쓰지 않는다
                _graph.Select(_session.Editor.MoveKeys(motion.Name, keys, delta, _graph.SnapEnabled ? t => (float)TimeSnap.Snap(t, _graph.SnapStep) : null)
                    .Select(m => new KeyRef(m.Curve, m.Curve.Resolve(_session.Editor.Document.Find(motion.Name)!)!.Keys.FindIndex(k => Math.Abs(k.T - m.T) <= MotionRules.Tolerance)))
                    .Where(k => k.Index >= 0), additive: false);
                return;
            }

            if (keys.Count == 1 && keys[0].Resolve(motion) is { } single)
            {
                float t = (float)TimeText.ToSeconds(apply((float)TimeText.FromSeconds(single.T, Display)), Display);

                if (Math.Abs(t - single.T) > 1e-7f)
                {
                    UpdateKey(keys[0], single, t: t);
                }
            }
        });
    }

    private void UpdateKey(KeyRef key, KeyAsset current, float? t = null, float? value = null)
    {
        _session.Editor.UpdateKey(_graph.MotionName!, key.Curve.Node, key.Curve.Kind, key.Curve.Axis, key.Index, new KeyAsset
        {
            T = t ?? current.T,
            V = value ?? current.V,
            InTangent = current.InTangent,
            OutTangent = current.OutTangent,
            Tangent = current.Tangent,
            Broken = current.Broken,
        });
        _graph.Select(new[] { key }, additive: false);
    }

    private void WithMotion(Action<MotionAsset> action)
    {
        if (_refreshing || _graph.Motion is not { } motion)
        {
            return;
        }

        Guard("모션 편집", () => action(motion));
    }

    private void WithSingleKey(Action<KeyRef, KeyAsset> action)
    {
        if (_refreshing || _graph.Motion is not { } motion || _graph.Selection.Count != 1)
        {
            return;
        }

        KeyRef key = _graph.Selection.First();

        if (key.Resolve(motion) is { } asset)
        {
            Guard("키 편집", () => action(key, asset));
        }
    }

    /// <summary>잘못된 입력·없는 파일은 상태 줄로 — 창이 죽지 않는다.</summary>
    private void Guard(string context, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            _session.SetStatus($"{context} 실패: {exception.Message}");
        }
    }

    // ── 파일 ──────────────────────────────────────────────────────────

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickOpenAsync("저작 원본 열기", new FilePickerFileType("모션 저작 원본") { Patterns = new[] { "*" + AuthoringFormat.AuthoringExtension, "*.json" } });

            if (path is not null)
            {
                OpenDocument(path);
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"열기 실패: {exception.Message}");
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_session.DocumentPath is null)
            {
                await SaveAsAsync();
                return;
            }

            _session.Save();
        }
        catch (Exception exception)
        {
            _session.SetStatus($"저장 실패: {exception.Message}");
        }
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await SaveAsAsync();
        }
        catch (Exception exception)
        {
            _session.SetStatus($"저장 실패: {exception.Message}");
        }
    }

    private async Task SaveAsAsync()
    {
        string? path = await PickSaveAsync("저작 원본 저장", "motions" + AuthoringFormat.AuthoringExtension,
            new FilePickerFileType("모션 저작 원본") { Patterns = new[] { "*" + AuthoringFormat.AuthoringExtension } });

        if (path is not null)
        {
            _session.Save(path);
            _settings.Touch(path);
            RefreshRecentMenu();
            SaveSettings();
        }
    }

    private async void OnLoadRigClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickOpenAsync("리그 덤프 (rig-schemas.json)", new FilePickerFileType("리그 덤프") { Patterns = new[] { "rig-schemas.json", "*.json" } });

            if (path is not null)
            {
                _session.LoadRig(path);
                RefreshNewForms();
                RefreshProblems();
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"리그 덤프 실패: {exception.Message}");
        }
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickOpenAsync("motions.json 가져오기", new FilePickerFileType("motions.json") { Patterns = new[] { "motions.json", "*.json" } });

            if (path is not null)
            {
                _session.ImportMotions(path);
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"가져오기 실패: {exception.Message}");
        }
    }

    /// <summary>툴바 범위 콤보 → 모션 거르기. 오브젝트가 없으면 전체.</summary>
    internal (Func<MotionAsset, bool>? Filter, string? Label) ExportFilter()
    {
        SceneObject? o = _viewport.SelectedObject;

        if (o is null || _session.Rig is not { } rig)
        {
            return (null, null);
        }

        return _exportScope.SelectedIndex switch
        {
            1 => (m => m.Rig == o.Rig, $"rigKind {o.Rig}"),
            2 => (m => ScenePreview.CanWear(rig, o, m), $"{o.Id}가 입는 모션"),
            _ => (null, null),
        };
    }

    internal int ExportScopeIndex
    {
        get => _exportScope.SelectedIndex;
        set => _exportScope.SelectedIndex = value;
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickSaveAsync("motions.json 내보내기", "motions.json", new FilePickerFileType("motions.json") { Patterns = new[] { "*.json" } });

            if (path is not null)
            {
                (Func<MotionAsset, bool>? filter, string? label) = ExportFilter();
                _session.Export(path, filter, label);
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"내보내기 실패: {exception.Message}");
        }
    }

    private async Task<string?> PickOpenAsync(string title, FilePickerFileType type)
    {
        IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;

        if (storage is null || !storage.CanOpen)
        {
            _session.SetStatus("이 환경에서는 파일 선택 창을 열 수 없다.");
            return null;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { type },
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task<string?> PickSaveAsync(string title, string suggested, FilePickerFileType type)
    {
        IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;

        if (storage is null || !storage.CanSave)
        {
            _session.SetStatus("이 환경에서는 저장 창을 열 수 없다.");
            return null;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggested,
            FileTypeChoices = new[] { type },
        });

        return file?.Path.LocalPath;
    }

    // ── 작은 도우미 ───────────────────────────────────────────────────

    private static bool TryFloat(string text, out float value)
        => float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static void OnCommit(TextBox box, Action<string> apply)
    {
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !box.AcceptsReturn)
            {
                apply(box.Text ?? "");
                e.Handled = true;
            }
        };
        box.LostFocus += (_, _) => apply(box.Text ?? "");
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        Margin = new Avalonia.Thickness(0, 6, 0, 0),
    };

    private static Control Labeled(string label, Control control)
    {
        var row = new DockPanel();
        var text = new TextBlock { Text = label, FontSize = 11, Width = 76, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        DockPanel.SetDock(text, Dock.Left);
        row.Children.Add(text);
        row.Children.Add(control);
        return row;
    }

    private static Button ToolButton(string text, EventHandler<RoutedEventArgs> onClick)
    {
        var button = new Button { Content = text, FontSize = 11, Padding = new Avalonia.Thickness(8, 3) };
        button.Click += onClick;
        return button;
    }

    private static Button SmallButton(string text, EventHandler<RoutedEventArgs> onClick)
    {
        var button = new Button { Content = text, FontSize = 10, Padding = new Avalonia.Thickness(6, 2) };
        button.Click += onClick;
        return button;
    }

    private static Control Separator() => new Border
    {
        Width = 1,
        Margin = new Avalonia.Thickness(4, 2),
        Background = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)),
    };
}
