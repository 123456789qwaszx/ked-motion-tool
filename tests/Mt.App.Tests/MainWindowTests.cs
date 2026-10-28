using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

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
    public void 내보내기_범위는_선택_오브젝트의_rigKind로_거르고_카메라_칸은_씬에_남는다()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                Assert.NotNull(window.Session.Depth); // 덤프 옆 presets/depth.json
                window.Session.ImportMotions(TestDocuments.FixturePath("motions.nod-bow.json"));
                window.Session.Editor.AddMotion("robot_wave", "robot", 1f);
                window.Session.Editor.AddChannel("robot_wave", "Robot_Arm", MotionChannelKind.Position);

                string export = Path.Combine(directory, "motions.json");
                window.ExportScopeIndex = 0;
                Assert.False(window.Session.Export(export, window.ExportFilter().Filter)); // robot_wave가 막는다
                window.ExportScopeIndex = 1; // 선택 오브젝트 s1(character)의 rigKind만
                Assert.True(window.Session.Export(export, window.ExportFilter().Filter), window.Session.Status);
                string json = File.ReadAllText(export);
                Assert.Contains("\"bow\"", json, StringComparison.Ordinal);
                Assert.DoesNotContain("robot_wave", json, StringComparison.Ordinal);

                window.CommitCameraFromForm(10f, 20f, -5f);
                Mt.Authoring.Model.CameraSettings camera = window.Viewport.Scene!.Camera;
                Assert.Equal(10f, camera.Zoom);
                Assert.Equal(-5f, camera.PanY);
            }
            finally
            {
                window.Close();
            }
        });

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void 채널_박스는_컨트롤러_자리_깊이_연기이고_값을_넣으면_그_노드에_키가_된다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                window.AddMotionFromForm("step", "character", "0.6", MotionEnd.Commit);
                window.AddObjectFromForm("s2", "character", "front");
                Assert.Equal(2, window.HierarchyObjectCount);

                window.SelectInHierarchy("s2", "CharacterPortrait_Track_Move_Y");
                Assert.Equal("s2", window.Viewport.SelectedObjectId);
                Assert.Equal("CharacterPortrait_Track_Move_Y", window.ChannelBoxNode);
                Assert.Equal("ctl:character", window.ChannelBoxLayout);

                // 컨트롤러 값은 포즈다 — 몸 0, 크기 1, 깊이는 층(front = 16)을 level로 되돌린 값.
                Assert.Equal("0", window.ControllerValueText("body", Axis.X));
                Assert.Equal("1", window.ControllerValueText("squash", Axis.X));
                Assert.Equal(16.0, double.Parse(window.ControllerValueText("depth", Axis.X), CultureInfo.InvariantCulture), 2);

                // 첫 키가 깊이(absolute 묶음)면 미리보기 mode가 absolute로 — 키 값은 level 20의 배율(2.2) 그대로, X·Y 둘.
                window.AutoKeyChecked = true;
                window.Viewport.ScrubTo(0.25);
                window.SetControllerValue("depth", Axis.X, "20");
                MotionAsset step = window.Session.Editor.Document.Find("step")!;
                Assert.Equal(MotionChannelMode.Absolute, step.PreviewMode);
                ChannelAsset depth = step.FindChannel("CharSlot_DepthScale", MotionChannelKind.Scale)!;
                Assert.Equal(2.2, depth.X!.Keys.Single().V, 3);
                Assert.Equal(2.2, depth.Y!.Keys.Single().V, 3);
                Assert.Equal("20", window.ControllerValueText("depth", Axis.X));

                // 숙임 15 → CharacterPortrait_SwayPivot euler Z 키(absolute 미리보기라 값 그대로).
                window.SetControllerValue("bow", Axis.Z, "15");
                step = window.Session.Editor.Document.Find("step")!;
                Assert.Equal(15.0, step.FindChannel("CharacterPortrait_SwayPivot", MotionChannelKind.Euler)!.Z!.Keys.Single().V, 3);
                Assert.Equal("15", window.ControllerValueText("bow", Axis.Z));
                Assert.DoesNotContain(window.ProblemsShown, p => !p.Blocking); // absolute 미리보기 — 경고 없음

                // relative로 돌리면 자리·깊이 컨트롤러 경고(막지 않음) → 두 모션으로 나누기: 깊이는 step_place로, 숙임은 남는다.
                window.Session.Editor.SetPreviewMode("step", MotionChannelMode.Relative);
                MotionProblem warning = Assert.Single(window.ProblemsShown.Where(p => p.MotionName == "step" && !p.Blocking));
                Assert.False(warning.Blocking);
                Assert.True(window.ApplyFix("step", Mt.Authoring.Validation.MotionFix.SplitByGroup));
                MotionAsset place = window.Session.Editor.Document.Find("step_place")!;
                Assert.Equal(MotionChannelMode.Absolute, place.PreviewMode);
                Assert.NotNull(place.FindChannel("CharSlot_DepthScale", MotionChannelKind.Scale));
                Assert.Null(window.Session.Editor.Document.Find("step")!.FindChannel("CharSlot_DepthScale", MotionChannelKind.Scale));
                Assert.NotNull(window.Session.Editor.Document.Find("step")!.FindChannel("CharacterPortrait_SwayPivot", MotionChannelKind.Euler));
                Assert.DoesNotContain(window.ProblemsShown, p => !p.Blocking);

                // 옛 모션(nod는 Track_Move_Y)은 "몸 Y"로 보이고, 그 성분에 값을 넣으면 있는 채널(별칭 노드)에 키가 간다.
                window.Session.ImportMotions(TestDocuments.FixturePath("motions.nod-bow.json"));
                window.SelectMotion("nod");
                window.SelectInHierarchy("s2", null);
                window.Viewport.ScrubTo(0.3);
                Assert.Equal("-18", window.ControllerValueText("body", Axis.Y));
                window.SetControllerValue("body", Axis.Y, "-20");
                MotionAsset nod = window.Session.Editor.Document.Find("nod")!;
                Assert.Null(nod.FindChannel("CharacterPortrait_Track_Move", MotionChannelKind.Position));
                Assert.Equal(-20.0, nod.FindChannel("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position)!.Y!.Keys.Single(k => Math.Abs(k.T - 0.3f) < 1e-4).V, 3);

                // ● 는 지금 값으로 지금 시각에 키 — 채널이 없는 성분은 주 노드에 생긴다.
                window.Viewport.ScrubTo(0.1);
                window.KeyControllerFromBox("shake", Axis.X);
                nod = window.Session.Editor.Document.Find("nod")!;
                Assert.Equal(0.0, nod.FindChannel("CharacterPortrait_Shake", MotionChannelKind.Position)!.X!.Keys.Single().V, 3);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 씬에_오브젝트를_더하고_채널_박스로_배치하면_되돌리기_한_번이다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                Assert.Single(window.Viewport.SceneObjects); // 기본 씬 s1

                window.AddObjectFromForm("s2", "character", "front");
                Assert.Equal(2, window.Viewport.SceneObjects.Count);
                Assert.Equal("s2", window.Viewport.SelectedObjectId);

                long before = window.Session.Editor.Revision;
                window.CommitObjectPlacementFromForm(120f, -30f, 5f, 1.2f, 1f);
                Assert.Equal(before + 1, window.Session.Editor.Revision);
                Mt.Authoring.Model.SceneObject s2 = window.Viewport.SelectedObject!;
                Assert.Equal(120f, s2.PositionX);
                Assert.Equal(-30f, s2.PositionY);
                Assert.Equal(5f, s2.RotationZ);
                Assert.Equal(1.2f, s2.ScaleX);

                window.Session.Editor.Undo();
                Assert.Equal(0f, window.Viewport.SelectedObject!.PositionX);

                Assert.Throws<InvalidOperationException>(() => window.AddObjectFromForm("s2", "character", "mid")); // 겹치는 id
                Assert.Throws<InvalidOperationException>(() => window.AddObjectFromForm("C3", "character", "mid")); // 규칙 밖
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 이름을_비우고_모션을_추가하면_motion이_되고_규칙_밖_이름은_상태줄에_남는다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                window.AddMotionFromForm("", "character", "0.6", MotionEnd.Restore);
                window.AddMotionFromForm("  ", "character", "0.6", MotionEnd.Restore);

                Assert.Equal(new[] { "motion", "motion_2" }, window.Session.Editor.Document.Motions.Select(m => m.Name));
                Assert.DoesNotContain(window.ProblemsShown, p => p.Message.Contains("이름은", StringComparison.Ordinal));

                Assert.Throws<InvalidOperationException>(() => window.AddMotionFromForm("Nod", "character", "0.6", MotionEnd.Restore));
                Assert.Equal(2, window.Session.Editor.Document.Motions.Count);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 아웃라이너_행_클릭은_그_커브만_보이고_없는_성분은_만들고_검색은_거른다()
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
                var x = y with { Axis = Axis.X };
                window.AddChannelFromForm(y.Node, y.Kind);
                window.Session.Editor.SetKeys("nod", y.Node, y.Kind, y.Axis, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.3f, -18f), TestDocuments.Key(0.6f, 0f) });

                Assert.Equal(3, window.OutlinerRowCount); // 채널 행 + x · y 성분 행
                Assert.Single(window.Graph.Curves);

                window.OutlinerClick(x, additive: false); // 없는 성분 → 중립값 키로 생긴다
                Assert.Equal(2, window.Graph.Curves.Count);
                Assert.All(window.Graph.Curves, c => Assert.True(c.Visible));

                window.OutlinerClick(y, additive: false); // 그 커브만
                Assert.True(window.Graph.Curves.Single(c => c.Curve == y).Visible);
                Assert.False(window.Graph.Curves.Single(c => c.Curve == x).Visible);

                window.OutlinerClick(x, additive: true); // 더하기
                Assert.All(window.Graph.Curves, c => Assert.True(c.Visible));

                window.OutlinerQuery = "없는노드";
                Assert.Equal(0, window.OutlinerRowCount);
                window.OutlinerQuery = "move_y";
                Assert.Equal(3, window.OutlinerRowCount);

                // 시간 표시는 프레임이 기본: 0.3초 = 7.2프레임.
                window.Viewport.ScrubTo(0.5);
                Assert.Equal("12", window.CurrentTimeText);
            }
            finally
            {
                window.Close();
            }
        });
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
                window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.Alt);
                Assert.True(window.Viewport.IsPlaying);
                window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.Alt);
                Assert.False(window.Viewport.IsPlaying);

                // ` 첫 프레임 · Alt+` 재생 토글
                window.Viewport.ScrubTo(0.3);
                window.KeyPressQwerty(PhysicalKey.Backquote, RawInputModifiers.None);
                Assert.Equal(0, window.Viewport.Time, 6);
                window.KeyPressQwerty(PhysicalKey.Backquote, RawInputModifiers.Alt);
                Assert.True(window.Viewport.IsPlaying);
                window.KeyPressQwerty(PhysicalKey.Backquote, RawInputModifiers.Alt);
                Assert.False(window.Viewport.IsPlaying);

                // W/E/R/Q 매니퓰레이터 — 창 어디에 포커스가 있든.
                window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);
                Assert.Equal(ManipulatorMode.Rotate, window.Manipulator);
                window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
                Assert.Equal(ManipulatorMode.Scale, window.Manipulator);
                window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
                Assert.Equal(ManipulatorMode.Move, window.Manipulator);
                window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.None);
                Assert.Equal(ManipulatorMode.Select, window.Manipulator);
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

                // 채널이 없는 모션은 유효하다(채널은 첫 키에서 생긴다) — 문제 칸에 아무것도 없다. 길이를 0으로 하면 보인다.
                Assert.DoesNotContain(window.ProblemsShown, p => p.MotionName == "a");
                window.Session.Editor.SetDuration("a", 0f);
                Assert.Contains(window.ProblemsShown, p => p.MotionName == "a");
                window.Session.Editor.Undo();

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

    [Fact]
    public void 하이어아키는_덤프의_부모자식_계층_그대로이고_메뉴로_자식을_만들며_이름바꾸기_복제가_된다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));

                // 리그 노드는 연기/배치 폴더가 아니라 덤프의 parent 사슬이다. 오브젝트 항목이 곧 __root.
                IReadOnlyList<string> path = window.HierarchyPathOf("s1", "CharacterPortrait_Track_Move_Y");
                Assert.Equal(new[] { "Scene  scene  ·  PresentataionUIRoot", "StageViewportLayer", "StageShot_Root", "StagePan_Root  ·  pan 0,0", "StageZoom_Root  ·  zoom 0", "Stage00_Root", "Stage00Mask_Root", "Stage00DepthSystem_Root", "Stage00Depth_Mid_Root", "Stage00Depth_Mid_FramingTransform", "Stage00Depth_Mid_FramingScale", "Stage00Depth_Mid_Content", "s1  ·  character", "CharSlot_Track_Focus", "CharSlot_DepthY", "CharSlot_DepthScale", "CharSlot_Track_Idle", "CharSlot_Track", "CharSlot_Track_X", "CharSlot_Track_Y", "CharSlot_Rotation", "CharSlot_SwayPivot", "CharSlot_Scale", "CharacterPortrait_VisualOffset", "CharacterPortrait_Track", "CharacterPortrait_Rotation", "CharacterPortrait_Track_Move", "CharacterPortrait_Track_Move_X", "CharacterPortrait_Track_Move_Y" }, path);
                Assert.DoesNotContain("연기 축", window.HierarchyPathOf("s1", "CharacterPortrait_SwayPivot"));

                // 노드 위에서 "만들기" = 그 노드의 자식. 트리에서는 그 노드 밑에 보인다.
                string prop = window.CreateObjectFromMenu("character", "s1", "CharacterPortrait_Track_Move");
                Assert.Equal("s2", prop);
                Assert.Equal(prop, window.Viewport.SelectedObjectId);
                IReadOnlyList<string> propPath = window.HierarchyPathOf("s2", null);
                Assert.Equal("CharacterPortrait_Track_Move", propPath[^2]);
                Assert.StartsWith("s2  ·  character", propPath[^1], StringComparison.Ordinal);

                // 빈자리에서 만들면 선택 오브젝트의 무대·층의 최상위. 리그 없는 오브젝트는 없다.
                string s3 = window.CreateObjectFromMenu("character", null, null);
                Assert.Equal("s3", s3);
                window.SelectInHierarchy("s3", null);
                Assert.Equal("stage00", window.Viewport.SelectedObject!.Stage);
                Assert.Throws<InvalidOperationException>(() => window.AddObjectFromForm("x1", "", "mid"));
                window.Session.Editor.RemoveSceneObject(null, "s3");

                // 이름 바꾸기(채널 박스 id) — 선택이 따라간다.
                window.SelectInHierarchy("s2", null);
                window.RenameSelectedFromForm("hand");
                Assert.Equal("hand", window.Viewport.SelectedObjectId);
                Assert.Equal("s1", window.Viewport.SelectedObject!.Parent);

                // 복제 — 자손까지, 새 것이 선택된다.
                window.SelectInHierarchy("s1", null);
                window.DuplicateSelectedFromMenu();
                Assert.Equal("s2", window.Viewport.SelectedObjectId);
                Assert.Equal(4, window.HierarchyObjectCount);
                Assert.Equal("s2", window.Viewport.Scene!.Find("hand_2")!.Parent);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 하이어아키에서_끌어다_놓으면_자식이_되거나_레이어가_바뀐다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                window.AddObjectFromForm("s2", "character", "front");
                window.AddObjectFromForm("s3", "character", "close");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 자기 자신에게 놓는 것은 아무 일도 아니다(6px 안은 드래그가 아니다).
                long before = window.Session.Editor.Revision;
                Point from = window.HierarchyItemCenter("s2", null) ?? throw new InvalidOperationException("s2 항목이 없다");
                Drag(window, from, from + new Point(0, 2));
                Assert.Equal(before, window.Session.Editor.Revision);

                // s2를 mid 레이어 폴더에 놓는다 → 그 레이어의 최상위.
                Point to = window.HierarchyItemCenter(null, null, "stage00", "mid") ?? throw new InvalidOperationException("층 항목이 없다");
                Drag(window, from, to);
                Assert.True(window.Viewport.Scene!.Find("s2")!.Layer == "mid", $"층이 안 바뀜 — from {from} to {to} · 상태: {window.Session.Status}");
                Assert.Null(window.Viewport.Scene!.Find("s2")!.Parent);

                // s3를 s1 오브젝트 항목에 놓는다 → 루트 자식(노드 위에 놓으면 그 노드 — 같은 길, 메뉴 테스트가 덮는다).
                // (이 뒤로는 선택된 s3의 조상 s1이 펼쳐져 트리가 길어진다 — 그래서 마지막에 한다.)
                from = window.HierarchyItemCenter("s3", null) ?? throw new InvalidOperationException("s3 항목이 없다");
                to = window.HierarchyItemCenter("s1", null) ?? throw new InvalidOperationException("s1 항목이 없다");
                Drag(window, from, to);
                Mt.Authoring.Model.SceneObject s3 = window.Viewport.Scene!.Find("s3")!;
                Assert.Equal("s1", s3.Parent);
                Assert.Null(s3.ParentNode);
                Assert.Equal("mid", s3.Layer);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void Drag(Window window, Point from, Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Point(0, 10));
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void 부모_콤보는_월드_위치를_지키고_자리_콤보는_place_폴드를_탄다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                window.CommitObjectPlacementFromForm(200f, -40f, 0f, 1f, 1f); // s1
                window.AddObjectFromForm("s2", "character", "front");
                window.CommitObjectPlacementFromForm(-150f, 30f, 0f, 1f, 1f);
                Vec2 RootOfC2() => window.Viewport.CurrentPose()!.TransformPoint("s2/CharSlot_Track", Vec3.Zero).XY; // 배치가 움직이는 노드
                Vec2 before = RootOfC2();

                // 채널 박스 부모 콤보 → s1의 자식(루트), 월드 위치 그대로.
                window.SetParentFromForm("s1");
                Mt.Authoring.Model.SceneObject s2 = window.Viewport.Scene!.Find("s2")!;
                Assert.Equal("s1", s2.Parent);
                Assert.Equal(before.X, RootOfC2().X, 1);
                Assert.Equal(before.Y, RootOfC2().Y, 1);

                // 부모 노드 콤보 → 그 노드 밑으로 옮겨도 월드 위치는 그대로(배치 값은 부모 공간으로 바뀐다).
                window.SetParentNodeFromForm("CharacterPortrait_Track_Move");
                s2 = window.Viewport.Scene!.Find("s2")!;
                Assert.Equal("CharacterPortrait_Track_Move", s2.ParentNode);
                Assert.NotEqual(-150f, s2.PositionX);
                Assert.Equal(before.X, RootOfC2().X, 1);
                Assert.Equal(before.Y, RootOfC2().Y, 1);

                // 자리(place) — 채널 박스 콤보로. Track_Focus가 코어 place 폴드로 풀린다.
                window.SelectInHierarchy("s1", null);
                window.SetPlaceFromForm("right", "face");
                Mt.Authoring.Model.SceneObject s1 = window.Viewport.Scene!.Find("s1")!;
                Assert.Equal("right", s1.Place);
                Assert.Equal("face", s1.PlaceFocus);
                // s1의 오프셋(move) 200이 사슬에 있으므로 place는 그만큼 덜 민다 — focus 점이 화면 오른쪽 지점(0.24·1920)에 온다.
                Assert.Equal(0.24f * 1920f - 200f, window.Viewport.CurrentPose()!.GetState("s1/CharSlot_Track_Focus").AnchoredPosition.X, 1);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
