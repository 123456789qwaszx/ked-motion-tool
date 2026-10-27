using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Serialization;

namespace Mt.App.Tests;

public class MainWindowTests
{
    /// <summary>테스트는 이 컴퓨터의 설정 파일을 건드리지 않는다 — 임시 경로.</summary>
    private static MainWindow NewWindow()
        => new(Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"settings-{Guid.NewGuid():N}.json"));

    [Fact]
    public void 주_창이_헤드리스에서_열리고_그려진다()
    {
        HeadlessUi.Run(() =>
        {
            Assert.True(HeadlessUi.OnUiThread);

            MainWindow window = NewWindow();
            window.Show();

            try
            {
                Assert.Equal("ked-motion-tool", window.Title);

                var status = window.FindControl<TextBlock>("StatusText");
                Assert.NotNull(status);
                Assert.True(status!.Bounds.Width > 0, "상태 줄이 배치되지 않았다");
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>M2의 완료 기준: 빈 문서에서 폼·그래프 명령만으로 nod를 처음부터 만들고, 내보낸 바이트가 Unity 산출물과 같다.</summary>
    [Fact]
    public void 빈_문서에서_nod를_처음부터_만들어_내보내면_Unity_산출물과_같다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();
            string export = Path.Combine(Path.GetTempPath(), $"ked-motion-tool-{Guid.NewGuid():N}.json");

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                window.AddMotionFromForm("nod", "character", "0.6", MotionEnd.Restore);
                Assert.Equal("nod", window.SelectedMotion);
                Assert.Equal("nod", window.Graph.MotionName);

                window.AddChannelFromForm("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position);
                Assert.Empty(window.Graph.Curves); // 성분은 키가 생기면 생긴다

                var y = new CurveRef("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);
                window.Graph.CurrentTime = 0;
                window.EnsureComponent(y.Node, y.Kind, y.Axis);
                Assert.Single(window.Graph.Curves);

                window.Graph.CurrentTime = 0.3;
                window.Graph.InsertKeyAtCurrentTime();
                window.CommitKeyValueFromInspector("-18");

                window.Graph.CurrentTime = 0.6;
                window.Graph.InsertKeyAtCurrentTime();
                window.CommitKeyValueFromInspector("0");

                window.Graph.SelectAll();
                window.Graph.SetTangentMode(TangentMode.Flat);

                Assert.Empty(window.ProblemsShown);
                Assert.True(window.Session.Export(export), window.Session.Status);

                // 견본의 nod만 정규 직렬화한 것과 바이트까지 같다.
                Assert.True(MotionsJson.TryRead(File.ReadAllText(TestDocuments.FixturePath("motions.nod-bow.json")), out List<MotionClip> clips, out _));
                byte[] expected = MotionsJson.WriteBytes(clips.Where(c => c.Name == "nod").ToList());
                Assert.Equal(expected, File.ReadAllBytes(export));
            }
            finally
            {
                window.Close();
                File.Delete(export);
            }
        });
    }

    [Fact]
    public void 뷰포트와_그래프의_현재_시각은_하나이고_리그를_실으면_뷰포트에_초상이_선다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                window.AddMotionFromForm("nod", "character", "0.6", MotionEnd.Restore);

                Assert.Equal("nod", window.Viewport.MotionName);
                Assert.True(window.Viewport.HasRig);
                Assert.NotNull(window.Viewport.CurrentPortraitQuad());

                window.Viewport.ScrubTo(0.25);
                Assert.Equal(0.25, window.Graph.CurrentTime, 9);

                window.Graph.CurrentTime = 0.5;
                Assert.Equal(0.5, window.Viewport.Time, 9);

                // 길이 밖은 뷰포트가 길이로 죈다 — 그래프는 그대로.
                window.Graph.CurrentTime = 2;
                Assert.Equal(0.6f, window.Viewport.Time, 6); // 길이는 float(0.6f)다
                Assert.Equal(2, window.Graph.CurrentTime, 9);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 그래프와_도프시트는_선택이_하나이고_현재_시각도_하나다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                window.AddMotionFromForm("nod", "character", "0.6", MotionEnd.Restore);
                var y = new CurveRef("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);
                window.AddChannelFromForm(y.Node, y.Kind);
                window.EnsureComponent(y.Node, y.Kind, y.Axis);

                Assert.Same(window.Graph.Selection, window.Dope.Selection);
                Assert.Same(window.SharedSelection, window.Graph.Selection);
                Assert.Equal("nod", window.Dope.MotionName);
                Assert.Equal(3, window.Dope.Rows.Count);

                window.Graph.Select(new[] { new KeyRef(y, 0) }, additive: false);
                Assert.Single(window.Dope.Selection);

                // 클립보드도 하나다 — 그래프에서 복사해 도프시트에서 붙인다.
                window.Graph.CopySelection();
                window.Dope.CurrentTime = 0.25;
                window.Dope.Paste();
                Assert.Equal(2, y.Resolve(window.Session.Editor.Document.Find("nod")!)!.Keys.Count);

                window.Dope.Selection.Clear();
                Assert.Empty(window.Graph.Selection);

                window.Dope.CurrentTime = 0.2;
                Assert.Equal(0.2, window.Graph.CurrentTime, 9);
                Assert.Equal(0.2, window.Viewport.Time, 9);

                window.Viewport.ScrubTo(0.4);
                Assert.Equal(0.4, window.Dope.CurrentTime, 9);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 뷰포트_드래그만으로_키가_생기고_노드_목록이_선다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                window.AddMotionFromForm("wave", "character", "0.5", MotionEnd.Restore);
                Assert.Contains("CharacterPortrait_Track_Move_X", window.Viewport.NodeChoices);
                Assert.DoesNotContain("CharSlot_Track_Focus", window.Viewport.NodeChoices);

                window.Viewport.SelectedNode = "CharacterPortrait_Track_Move_X";
                window.Viewport.Manipulator = ManipulatorMode.Move;
                window.AutoKeyChecked = true;
                Assert.True(window.Viewport.AutoKey);

                window.Viewport.ScrubTo(0.25);
                Point center = window.Viewport.TranslatePoint(window.Viewport.GizmoCenter()!.Value, window)!.Value;
                var target = new Point(center.X + 40, center.Y);
                window.MouseDown(center, MouseButton.Left);
                window.MouseMove(target, RawInputModifiers.LeftMouseButton);
                window.MouseUp(target, MouseButton.Left);

                ChannelAsset channel = window.Session.Editor.Document.Find("wave")!.FindChannel("CharacterPortrait_Track_Move_X", MotionChannelKind.Position)!;
                Assert.Equal(MotionChannelMode.Relative, channel.Mode);
                Assert.Single(channel.X!.Keys);
                Assert.True(channel.X.Keys[0].V > 0);
                Assert.Equal(3, window.Dope.Rows.Count);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 문제_행의_돕는_손이_문제를_없애고_설정은_파일로_남고_자동_저장은_문서_옆이다()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "settings.json");

        HeadlessUi.Run(() =>
        {
            var window = new MainWindow(settingsPath);
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                window.AddMotionFromForm("nod", "character", "0.5", MotionEnd.Restore);
                var y = new CurveRef("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);
                window.AddChannelFromForm(y.Node, y.Kind);
                window.Session.Editor.SetKeys("nod", y.Node, y.Kind, y.Axis, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.3f, -18f), TestDocuments.Key(0.6f, 0f) });

                Assert.True(window.ProblemRowsShown >= 1); // 마지막 키 0.6 ≠ 길이 0.5
                Assert.True(window.ApplyFix("nod", Mt.Authoring.Validation.MotionFix.DurationToLastKey));
                Assert.Equal(0, window.ProblemRowsShown);
                Assert.Equal(0.6f, window.Session.Editor.Document.Find("nod")!.Duration, 4);

                // 설정: 스냅을 끄고 창을 닫으면 파일에 남는다.
                window.Settings.SnapEnabled = false;
                string documentPath = Path.Combine(directory, "doc.motion-authoring.json");
                window.Session.Save(documentPath);
                window.Session.Editor.SetDuration("nod", 0.7f);
                string? autosaved = window.Session.AutoSave();
                Assert.NotNull(autosaved);
                Assert.Equal(Path.Combine(directory, "doc.autosave.motion-authoring.json"), autosaved);
                Assert.True(File.Exists(autosaved!));
                Assert.True(window.Session.IsDirty);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(File.Exists(settingsPath));
        Assert.Contains("\"snapEnabled\": false", File.ReadAllText(settingsPath), StringComparison.Ordinal);
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void 전역_단축키_이전_다음_키_프레임_복사_붙여넣기_지우기_인비트윈()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                window.AddMotionFromForm("nod", "character", "0.6", MotionEnd.Restore);
                var y = new CurveRef("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);
                window.AddChannelFromForm(y.Node, y.Kind);
                window.Session.Editor.SetKeys("nod", y.Node, y.Kind, y.Axis, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.3f, -18f), TestDocuments.Key(0.6f, 0f) });
                MotionAsset Nod() => window.Session.Editor.Document.Find("nod")!;

                // 2 다음 키 · 1 이전 키 (물리 키로 — 포커스가 어디 있든 듣는다)
                window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.None);
                Assert.Equal(0.3, window.Viewport.Time, 4);
                window.NextKey();
                Assert.Equal(0.6, window.Viewport.Time, 4);
                window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.None);
                Assert.Equal(0.3, window.Viewport.Time, 4);

                // Alt+2 / Alt+1 한 프레임(24fps)
                window.StepFrame(+1);
                Assert.Equal(0.3 + 1.0 / 24.0, window.Viewport.Time, 4);
                window.StepFrame(-1);
                Assert.Equal(0.3, window.Viewport.Time, 4);

                // Shift+W 복사 → 0.45로 가서 Shift+E 붙여넣기
                window.CopyKeysAtCurrentTime();
                window.Viewport.ScrubTo(0.45);
                window.PasteKeysAtCurrentTime();
                Assert.Equal(4, y.Resolve(Nod())!.Keys.Count);
                Assert.Equal(-18f, y.Resolve(Nod())!.Keys[2].V);

                // Shift+F 지우기
                window.ClearKeysAtCurrentTime();
                Assert.Equal(3, y.Resolve(Nod())!.Keys.Count);

                // Shift+2 인비트윈 넣기 / Shift+1 빼기 — 0.3 뒤의 키만 움직인다
                window.Viewport.ScrubTo(0.3);
                window.AddInbetween();
                Assert.Equal(0.6f + 1f / 24f, y.Resolve(Nod())!.Keys[2].T, 4);
                window.RemoveInbetween();
                Assert.Equal(0.6f, y.Resolve(Nod())!.Keys[2].T, 4);
                Assert.Equal(0.3f, y.Resolve(Nod())!.Keys[1].T, 4);

                // Alt+Q 재생 토글
                window.KeyPressQwerty(PhysicalKey.KeyQ, RawInputModifiers.Alt);
                Assert.True(window.Viewport.IsPlaying);
                window.KeyPressQwerty(PhysicalKey.KeyQ, RawInputModifiers.Alt);
                Assert.False(window.Viewport.IsPlaying);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 되돌리기는_툴바_없이도_편집기를_지난다_그리고_문제는_아래에_보인다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.AddMotionFromForm("a", "character", "1", MotionEnd.Restore);
                Assert.NotNull(window.Session.Editor.Document.Find("a"));

                // 채널이 없는 모션은 규칙 위반 — 아래 문제 칸에 보인다.
                Assert.Contains(window.ProblemsShown, p => p.MotionName == "a");

                window.Session.Editor.Undo();
                Assert.Null(window.Session.Editor.Document.Find("a"));
                Assert.Null(window.Graph.MotionName);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
