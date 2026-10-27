using System.Globalization;
using Avalonia.Controls;
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
using Mt.Authoring.Validation;

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
    private static readonly string[] KindTokens = { "position", "scale", "euler" };
    private static readonly string[] ChannelModeTokens = { "(모션 기본)", "relative", "absolute" };

    private readonly DocumentSession _session = new();
    private readonly GraphEditorView _graph = new();
    private readonly DopeSheetView _dope = new();
    private readonly ViewportView _viewport = new();

    /// <summary>그래프와 도프시트가 같은 선택을 든다 — 한 모델, 두 뷰.</summary>
    private readonly KeySelection _selection = new();

    // 트랜스포트
    private readonly Button _playButton = new() { Content = "▶ 재생 (Space)", FontSize = 11, Padding = new Avalonia.Thickness(8, 3) };
    private readonly ComboBox _playMode = new() { ItemsSource = new[] { "한 번", "루프", "왕복" }, SelectedIndex = 1, FontSize = 11 };
    private readonly ComboBox _playSpeed = new() { ItemsSource = new[] { "×0.25", "×0.5", "×1", "×2" }, SelectedIndex = 2, FontSize = 11 };
    private readonly TextBox _rangeStart = new() { Text = "0", Width = 56, FontSize = 11 };
    private readonly TextBox _rangeEnd = new() { Text = "1", Width = 56, FontSize = 11 };
    private readonly CheckBox _onion = new() { Content = "어니언 스킨", FontSize = 11 };
    private readonly TextBlock _stageInfo = new() { FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
    private Bitmap? _portrait;
    private bool _syncingTime;

    // 왼쪽
    private readonly ListBox _motionList = new() { MinHeight = 120 };
    private readonly TextBox _newMotionName = new() { PlaceholderText = "이름 (a-z0-9_)", FontSize = 11 };
    private readonly ComboBox _newMotionRig = new() { FontSize = 11, MinWidth = 100 };
    private readonly TextBox _newMotionDuration = new() { Text = "1", Width = 56, FontSize = 11 };
    private readonly ComboBox _newMotionEnd = new() { ItemsSource = EndTokens, SelectedIndex = 0, FontSize = 11 };
    private readonly StackPanel _channelRows = new() { Spacing = 2 };
    private readonly ComboBox _newChannelNode = new() { FontSize = 11, MinWidth = 180 };
    private readonly ComboBox _newChannelKind = new() { ItemsSource = KindTokens, SelectedIndex = 0, FontSize = 11 };
    private readonly ComboBox _newChannelMode = new() { ItemsSource = ChannelModeTokens, SelectedIndex = 0, FontSize = 11 };

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

    public MainWindow()
    {
        InitializeComponent();
        BuildToolbar();
        BuildLeft();
        BuildRight();
        _graph.Selection = _selection;
        _dope.Selection = _selection;
        _viewport.Selection = _selection;
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
            _currentTime.Text = _graph.CurrentTime.ToString("0.###", CultureInfo.InvariantCulture);
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
        _viewport.PlayStateChanged += (_, _) => _playButton.Content = _viewport.IsPlaying ? "❚❚ 일시정지 (Space)" : "▶ 재생 (Space)";

        OnDocumentReplaced();
    }

    internal DocumentSession Session => _session;

    internal GraphEditorView Graph => _graph;

    internal ViewportView Viewport => _viewport;

    internal DopeSheetView Dope => _dope;

    internal KeySelection SharedSelection => _selection;

    internal string? SelectedMotion => _motionList.SelectedItem as string;

    /// <summary>헤드리스 테스트가 폼을 채우는 손잡이 — 화면과 같은 명령 경로를 지난다.</summary>
    internal void AddMotionFromForm(string name, string rig, string duration, MotionEnd end)
    {
        _newMotionName.Text = name;
        _newMotionRig.SelectedItem = rig;
        _newMotionDuration.Text = duration;
        _newMotionEnd.SelectedIndex = (int)end;
        AddMotion();
    }

    internal void AddChannelFromForm(string node, MotionChannelKind kind)
    {
        _newChannelNode.SelectedItem = node;
        _newChannelKind.SelectedIndex = (int)kind;
        _newChannelMode.SelectedIndex = 0;
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
            Tangent = TangentMode.Auto,
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
        var grid = new Grid { RowDefinitions = new RowDefinitions("2*,Auto,4,3*") };

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
        _playSpeed.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _playSpeed.SelectedIndex >= 0)
            {
                _viewport.Speed = new[] { 0.25, 0.5, 1, 2 }[_playSpeed.SelectedIndex];
            }
        };
        OnCommit(_rangeStart, text => Guard("재생 범위", () => { if (TryFloat(text, out float v)) _viewport.SetRange(v, _viewport.Clock.RangeEnd); }));
        OnCommit(_rangeEnd, text => Guard("재생 범위", () => { if (TryFloat(text, out float v)) _viewport.SetRange(_viewport.Clock.RangeStart, v); }));
        _viewport.RangeChanged += (_, _) => RefreshTransport();
        _onion.IsCheckedChanged += (_, _) =>
        {
            _viewport.OnionSkinEnabled = _onion.IsChecked == true;
            _viewport.InvalidateVisual();
        };

        var transport = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Avalonia.Thickness(4, 2),
            Children =
            {
                _playButton,
                SmallButton("■ 처음으로", (_, _) => _viewport.Stop()),
                SmallButton("|◀ 이전 키 (,)", (_, _) => _viewport.JumpToPreviousKey()),
                SmallButton("다음 키 (.) ▶|", (_, _) => _viewport.JumpToNextKey()),
                _playMode,
                _playSpeed,
                new TextBlock { Text = "범위", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 },
                _rangeStart,
                _rangeEnd,
                SmallButton("전체", (_, _) => _viewport.ResetRange()),
                _onion,
                SmallButton("초상 PNG…", OnPickPortraitClick),
                _stageInfo,
            },
        };
        Grid.SetRow(transport, 1);
        grid.Children.Add(transport);

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Background = Brushes.Transparent };
        Grid.SetRow(splitter, 2);
        grid.Children.Add(splitter);

        var tabs = new TabControl
        {
            Padding = new Avalonia.Thickness(0),
            ItemsSource = new[]
            {
                new TabItem { Header = "그래프", Content = _graph, FontSize = 12 },
                new TabItem { Header = "도프 시트", Content = _dope, FontSize = 12 },
            },
        };
        Grid.SetRow(tabs, 3);
        grid.Children.Add(tabs);
        return grid;
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
            _rangeStart.Text = _viewport.Clock.RangeStart.ToString("0.###", CultureInfo.InvariantCulture);
            _rangeEnd.Text = _viewport.Clock.RangeEnd.ToString("0.###", CultureInfo.InvariantCulture);
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
        RefreshSelectedNode();
    }

    /// <summary>뷰포트가 축을 그릴 노드 — 선택한 키의 노드, 없으면 첫 채널의 노드.</summary>
    private void RefreshSelectedNode()
    {
        MotionAsset? motion = _graph.Motion;
        string? node = _graph.Selection.Count > 0
            ? _graph.Selection.First().Curve.Node
            : motion?.Channels.FirstOrDefault()?.Node;
        _viewport.SelectedNode = node;
        _viewport.InvalidateVisual();
    }

    private void RefreshAssets()
    {
        _viewport.SetRig(_session.Rig);
        _viewport.StageSize = _session.StageSize;
        _portrait = null;

        if (_session.PortraitPath is { } portraitPath)
        {
            try
            {
                _portrait = new Bitmap(portraitPath);
            }
            catch (Exception exception)
            {
                _session.SetStatus($"초상을 읽지 못했다: {exception.Message}");
            }
        }

        _viewport.SetPortrait(_portrait);
        _stageInfo.Text = $"무대 {_session.StageSize.X:0}×{_session.StageSize.Y:0}"
                          + (_session.Rig is null ? " · 리그 없음" : "")
                          + (_portrait is null ? " · 초상 없음(자리표시)" : $" · 초상 {_portrait.PixelSize.Width}×{_portrait.PixelSize.Height}");
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
                _session.SetPortrait(path);
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
        Toolbar.Children.Add(ToolButton("저장", OnSaveClick));
        Toolbar.Children.Add(ToolButton("다른 이름으로…", OnSaveAsClick));
        Toolbar.Children.Add(Separator());
        Toolbar.Children.Add(ToolButton("리그 덤프…", OnLoadRigClick));
        Toolbar.Children.Add(ToolButton("motions.json 가져오기…", OnImportClick));
        Toolbar.Children.Add(ToolButton("motions.json 내보내기…", OnExportClick));
        Toolbar.Children.Add(Separator());
        Toolbar.Children.Add(ToolButton("되돌리기", (_, _) => _session.Editor.Undo()));
        Toolbar.Children.Add(ToolButton("다시하기", (_, _) => _session.Editor.Redo()));
    }

    private void BuildLeft()
    {
        LeftPanel.Children.Add(Header("모션"));
        _motionList.SelectionChanged += (_, _) => OnMotionSelected();
        LeftPanel.Children.Add(_motionList);

        var addMotion = new StackPanel { Spacing = 4 };
        addMotion.Children.Add(_newMotionName);
        addMotion.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { _newMotionRig, _newMotionDuration, new TextBlock { Text = "초", FontSize = 11, VerticalAlignment = VerticalAlignment.Center }, _newMotionEnd },
        });
        addMotion.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { SmallButton("모션 추가", (_, _) => Guard("모션 추가", AddMotion)), SmallButton("모션 삭제", (_, _) => Guard("모션 삭제", RemoveMotion)) },
        });
        LeftPanel.Children.Add(addMotion);

        LeftPanel.Children.Add(Header("채널 (보이기: x · y · z)"));
        LeftPanel.Children.Add(_channelRows);

        var addChannel = new StackPanel { Spacing = 4 };
        addChannel.Children.Add(_newChannelNode);
        addChannel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { _newChannelKind, _newChannelMode, SmallButton("채널 추가", (_, _) => Guard("채널 추가", AddChannel)) },
        });
        LeftPanel.Children.Add(addChannel);
    }

    private void BuildRight()
    {
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
        RightPanel.Children.Add(_selectionSummary);
        RightPanel.Children.Add(Labeled("시각(초)", _keyTime));
        RightPanel.Children.Add(Labeled("값", _keyValue));
        RightPanel.Children.Add(Labeled("in 기울기", _keyIn));
        RightPanel.Children.Add(Labeled("out 기울기", _keyOut));
        RightPanel.Children.Add(_keyBroken);
        RightPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                SmallButton("auto (1)", (_, _) => _graph.SetTangentMode(TangentMode.Auto)),
                SmallButton("flat (2)", (_, _) => _graph.SetTangentMode(TangentMode.Flat)),
                SmallButton("linear (3)", (_, _) => _graph.SetTangentMode(TangentMode.Linear)),
                SmallButton("free (4)", (_, _) => _graph.SetTangentMode(TangentMode.Free)),
            },
        });
        RightPanel.Children.Add(SmallButton("선택 키 삭제 (Delete)", (_, _) => _graph.DeleteSelection()));

        OnCommit(_keyTime, text => WithSingleKey((key, k) => { if (TryFloat(text, out float v) && v != k.T) UpdateKey(key, k, t: v); }));
        OnCommit(_keyValue, text => WithSingleKey((key, k) => { if (TryFloat(text, out float v) && v != k.V) UpdateKey(key, k, value: v); }));
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

        RightPanel.Children.Add(Header("보기"));
        _snap.IsCheckedChanged += (_, _) =>
        {
            _graph.SnapEnabled = _snap.IsChecked == true;
            _dope.SnapEnabled = _snap.IsChecked == true;
        };
        RightPanel.Children.Add(_snap);
        RightPanel.Children.Add(Labeled("현재 시각", _currentTime));
        OnCommit(_currentTime, text => { if (TryFloat(text, out float v)) _graph.CurrentTime = v; });
        RightPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                SmallButton("전체 (A)", (_, _) => _graph.FitAll()),
                SmallButton("선택 (F)", (_, _) => _graph.FitSelection()),
                SmallButton("키 끼우기 (I)", (_, _) => _graph.InsertKeyAtCurrentTime()),
            },
        });
        RightPanel.Children.Add(new TextBlock
        {
            FontSize = 10,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            Text = "그래프: 클릭 선택 · Shift 추가 · 박스 선택 · 드래그(Shift=축 잠금) · 더블클릭=커브 위에 키 · 핸들 드래그=기울기 · " +
                   "휠 줌(Ctrl=시간만·Shift=값만) · 가운데 버튼/Alt+드래그 팬 · Ctrl+Z/Y\n" +
                   "도프 시트: 블록 드래그=시간 이동 · Alt+드래그=복제 · 범위 띠 양끝=시간 스케일 · 더블클릭=키 · 눈금자=스크럽 · 노드 행=접기",
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
    }

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
            List<string> names = _session.Editor.Document.Motions.Select(m => m.Name).ToList();
            _motionList.ItemsSource = names;
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

    private void RefreshChannels()
    {
        _channelRows.Children.Clear();
        MotionAsset? motion = _graph.Motion;

        if (motion is null)
        {
            _channelRows.Children.Add(new TextBlock { Text = "(모션 없음)", FontSize = 11, Opacity = 0.6 });
            return;
        }

        MotionAsset current = motion;

        foreach (ChannelAsset channel in current.Channels)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            string node = channel.Node;
            MotionChannelKind kind = channel.Kind;

            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                var curveRef = new CurveRef(node, kind, axis);
                CurveSlot? slot = _graph.Curves.FirstOrDefault(s => s.Curve == curveRef);
                // Position·Scale에는 z가 없다(규칙) — 아예 두지 않는다.
                if (axis == Axis.Z && kind != MotionChannelKind.Euler)
                {
                    continue;
                }

                var toggle = new CheckBox
                {
                    Content = axis.ToString().ToLowerInvariant(),
                    FontSize = 10,
                    IsChecked = slot?.Visible ?? false,
                    MinWidth = 0,
                    Padding = new Avalonia.Thickness(2, 0),
                    Opacity = slot is null ? 0.55 : 1,
                };

                if (slot is not null)
                {
                    Color c = slot.Color;
                    toggle.Foreground = new SolidColorBrush(c);
                }

                ToolTip.SetTip(toggle, slot is null
                    ? "성분이 없다 — 켜면 현재 시각에 중립값 키 하나로 성분이 생긴다 (성분은 키가 생기면 생긴다)"
                    : "그래프에 보이기");

                toggle.IsCheckedChanged += (_, _) =>
                {
                    if (_refreshing)
                    {
                        return;
                    }

                    if (slot is null)
                    {
                        if (toggle.IsChecked == true)
                        {
                            Guard("성분 추가", () => _session.Editor.AddKey(current.Name, node, kind, axis, new KeyAsset
                            {
                                T = (float)_graph.CurrentTime,
                                V = MotionMath.Neutral(kind),
                                Tangent = TangentMode.Auto,
                            }));
                        }

                        return;
                    }

                    _graph.SetVisible(curveRef, toggle.IsChecked == true);
                };
                row.Children.Add(toggle);
            }

            string modeToken = channel.Mode == MotionChannelMode.Relative ? "rel" : "abs";
            var label = new TextBlock
            {
                Text = $"{node} · {kind.ToString().ToLowerInvariant()} · {modeToken}",
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 150,
            };
            ToolTip.SetTip(label, $"{node}\n{kind} · {channel.Mode}\n클릭: mode 바꾸기");
            MotionChannelMode flipped = channel.Mode == MotionChannelMode.Relative ? MotionChannelMode.Absolute : MotionChannelMode.Relative;
            label.PointerPressed += (_, _) => Guard("채널 mode", () => _session.Editor.SetChannelMode(current.Name, node, kind, flipped));
            row.Children.Add(label);
            row.Children.Add(SmallButton("×", (_, _) => Guard("채널 삭제", () => _session.Editor.RemoveChannel(current.Name, node, kind))));
            _channelRows.Children.Add(row);
        }

        if (current.Channels.Count == 0)
        {
            _channelRows.Children.Add(new TextBlock { Text = "(채널 없음 — 아래에서 노드를 골라 추가)", FontSize = 11, Opacity = 0.6 });
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
            _keyTime.IsEnabled = editable;
            _keyValue.IsEnabled = editable;
            _keyIn.IsEnabled = editable;
            _keyOut.IsEnabled = editable;
            _keyBroken.IsEnabled = count > 0;

            _keyTime.Text = single is null ? "" : F(single.T);
            _keyValue.Text = single is null ? "" : F(single.V);
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
        ProblemsText.IsVisible = problems.Count > 0;
        ProblemsText.Text = problems.Count == 0 ? "" : string.Join("\n", problems.Select(p => "⚠ " + p));
    }

    private void RefreshNewForms()
    {
        _refreshDepth++;

        try
        {
            string? rigSelected = _newMotionRig.SelectedItem as string;
            List<string> rigs = _session.Rig?.RigKinds.ToList() ?? new List<string> { "character" };
            _newMotionRig.ItemsSource = rigs;
            _newMotionRig.SelectedIndex = Math.Max(0, rigSelected is null ? 0 : rigs.IndexOf(rigSelected));

            string? nodeSelected = _newChannelNode.SelectedItem as string;
            MotionAsset? motion = _graph.Motion;
            List<string> nodes = new();

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

    private void AddMotion()
    {
        string name = (_newMotionName.Text ?? "").Trim();
        string rig = _newMotionRig.SelectedItem as string ?? "character";

        if (!TryFloat(_newMotionDuration.Text ?? "", out float duration))
        {
            throw new InvalidOperationException("길이는 초 단위 숫자다.");
        }

        MotionEnd end = (MotionEnd)Math.Max(0, _newMotionEnd.SelectedIndex);
        _session.Editor.AddMotion(name, rig, duration, end, end == MotionEnd.Restore ? MotionChannelMode.Relative : MotionChannelMode.Absolute);
        _newMotionName.Text = "";
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
        MotionChannelMode? mode = _newChannelMode.SelectedIndex switch { 1 => MotionChannelMode.Relative, 2 => MotionChannelMode.Absolute, _ => null };
        _session.Editor.AddChannel(motion.Name, node, kind, mode);
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
                _session.Open(path);
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

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickSaveAsync("motions.json 내보내기", "motions.json", new FilePickerFileType("motions.json") { Patterns = new[] { "*.json" } });

            if (path is not null)
            {
                _session.Export(path);
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
