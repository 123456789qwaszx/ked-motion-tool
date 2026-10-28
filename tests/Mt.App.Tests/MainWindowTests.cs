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
    {
        string path = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"settings-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ \"snapStep\": 0.016666666666666666 }"); // 이 테스트들의 기대값은 1/60 스냅 기준 — 기본값은 1/24(GraphMathTests)
        return new(path);
    }

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

                // 첫 키가 깊이(absolute 묶음)면 재생 기준(relative/absolute)가 absolute로 — 키 값은 level 20의 배율(2.2) 그대로, X·Y 둘.
                window.AutoKeyChecked = true;
                window.Viewport.ScrubTo(0.25);
                window.SetControllerValue("depth", Axis.X, "20");
                MotionAsset step = window.Session.Editor.Document.Find("step")!;
                Assert.Equal(MotionChannelMode.Absolute, step.PreviewMode);
                ChannelAsset depth = step.FindChannel("CharSlot_DepthScale", MotionChannelKind.Scale)!;
                Assert.Equal(2.2, depth.X!.Keys.Single().V, 3);
                Assert.Equal(2.2, depth.Y!.Keys.Single().V, 3);
                Assert.Equal("20", window.ControllerValueText("depth", Axis.X));
                // 같은 손잡이가 DepthY도 굽는다 — 얼굴(bust 820) 제자리: −820 × (2.2 − 1) = −984. 되돌리기 하나.
                Assert.Equal(-984.0, step.FindChannel("CharSlot_DepthY", MotionChannelKind.Position)!.Y!.Keys.Single().V, 2);
                window.Session.Editor.Undo();
                Assert.Null(window.Session.Editor.Document.Find("step")!.FindChannel("CharSlot_DepthY", MotionChannelKind.Position));
                Assert.Null(window.Session.Editor.Document.Find("step")!.FindChannel("CharSlot_DepthScale", MotionChannelKind.Scale));
                window.Session.Editor.Redo();
                step = window.Session.Editor.Document.Find("step")!;
                Assert.Equal(-984.0, step.FindChannel("CharSlot_DepthY", MotionChannelKind.Position)!.Y!.Keys.Single().V, 2);

                // 숙임 15 → CharacterPortrait_SwayPivot euler Z 키(absolute 미리보기라 값 그대로).
                window.SetControllerValue("bow", Axis.Z, "15");
                step = window.Session.Editor.Document.Find("step")!;
                Assert.Equal(15.0, step.FindChannel("CharacterPortrait_SwayPivot", MotionChannelKind.Euler)!.Z!.Keys.Single().V, 3);
                Assert.Equal("15", window.ControllerValueText("bow", Axis.Z));
                Assert.DoesNotContain(window.ProblemsShown, p => !p.Blocking); // absolute 미리보기 — 경고 없음

                // relative로 돌리면 자리·깊이 컨트롤러 경고(막지 않음) → 두 모션으로 나누기: 깊이는 step_place로, 숙임은 남는다.
                window.Session.Editor.SetPreviewMode("step", MotionChannelMode.Relative);
                MotionProblem warning = Assert.Single(window.ProblemsShown, p => p.MotionName == "step" && !p.Blocking);
                Assert.False(warning.Blocking);
                Assert.True(window.ApplyFix("step", Mt.Authoring.Validation.MotionFix.SplitByGroup));
                MotionAsset place = window.Session.Editor.Document.Find("step_place")!;
                Assert.Equal(MotionChannelMode.Absolute, place.PreviewMode);
                Assert.NotNull(place.FindChannel("CharSlot_DepthScale", MotionChannelKind.Scale));
                Assert.NotNull(place.FindChannel("CharSlot_DepthY", MotionChannelKind.Position)); // 깊이 Y도 깊이 묶음
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
    public void 아웃라이너는_모션의_컨트롤러_전부_표_순서이고_흐린_성분_클릭은_만들지_않으며_키_없는_컨트롤러를_고르면_그래프가_비고_S가_고른_컨트롤러를_키한다()
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

                // 컨트롤러 10개 전부, 표 순서 그대로(선택·키와 무관 — 순서가 바뀌면 헷갈린다).
                // 행 수 = 컨트롤러 10 + 표가 쓰는 성분(B): 몸 2 · 자리 2 · 슬롯 기울기 1 · 자세 배율 2 · 깊이 3(배율 X·Y + 깊이 Y) · 기울기 1 · 숙임 1 · 떨림 2 · 크기 2 · 방향 1 = 17.
                string[] tableOrder = { "place", "slotLean", "stance", "depth", "body", "lean", "bow", "shake", "squash", "facing" };
                Assert.Equal(tableOrder, window.OutlinerOrder);
                Assert.Equal(27, window.OutlinerRowCount);
                Assert.Single(window.Graph.Curves);

                // 흐린 성분(몸 X) 클릭은 성분을 만들지 않는다(A2) — 그 컨트롤러를 고를 뿐.
                window.OutlinerClick(x, additive: false);
                Assert.Single(window.Graph.Curves);
                Assert.Null(window.Session.Editor.Document.Find("nod")!.FindChannel(x.Node, x.Kind)!.X);
                Assert.Equal("CharacterPortrait_Track_Move_Y", window.Viewport.SelectedNode);

                // 있는 성분 클릭은 그 커브만 · 더하기.
                window.Session.Editor.SetKeys("nod", x.Node, x.Kind, x.Axis, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.6f, 0f) });
                Assert.Equal(2, window.Graph.Curves.Count);
                window.OutlinerClick(y, additive: false);
                Assert.True(window.Graph.Curves.Single(c => c.Curve == y).Visible);
                Assert.False(window.Graph.Curves.Single(c => c.Curve == x).Visible);
                window.OutlinerClick(x, additive: true);
                Assert.All(window.Graph.Curves, c => Assert.True(c.Visible));

                // 하이어아키에서 키 없는 숙임을 고르면 순서는 그대로, 그래프는 빈다(전에 보던 몸 커브를 남기지 않는다). 몸을 다시 고르면 몸 커브만.
                window.SelectControllerInHierarchy("s1", "bow");
                Assert.Equal(tableOrder, window.OutlinerOrder);
                Assert.All(window.Graph.Curves, c => Assert.False(c.Visible));
                window.SelectControllerInHierarchy("s1", "body");
                Assert.True(window.Graph.Curves.Single(c => c.Curve == y).Visible); // 몸 Y(별칭 Track_Move_Y)
                Assert.False(window.Graph.Curves.Single(c => c.Curve == x).Visible); // Track_Move_Y의 X는 몸의 성분이 아니다(별칭은 Y만)
                window.SelectControllerInHierarchy("s1", "bow");

                // 검색은 컨트롤러 이름·id·노드로 거른다.
                window.OutlinerQuery = "없는노드";
                Assert.Equal(0, window.OutlinerRowCount);
                window.OutlinerQuery = "몸";
                Assert.Equal(3, window.OutlinerRowCount); // 몸 + X·Y
                window.OutlinerQuery = "";

                // 성분 하나 삭제는 다른 성분을 남기고, 마지막 성분이면 채널이 없어진다.
                window.RemoveComponentFromOutliner(x);
                ChannelAsset moveY = window.Session.Editor.Document.Find("nod")!.FindChannel(y.Node, y.Kind)!;
                Assert.Null(moveY.X);
                Assert.NotNull(moveY.Y);
                window.RemoveComponentFromOutliner(y);
                Assert.Null(window.Session.Editor.Document.Find("nod")!.FindChannel(y.Node, y.Kind));

                // S = 고른 컨트롤러(숙임)의 지금 값을 현재 시각에 키 — 키 없는 포즈가 없어도.
                window.Viewport.ScrubTo(0.25);
                window.SetKeyFromShell();
                KeyAsset bowKey = window.Session.Editor.Document.Find("nod")!.FindChannel("CharacterPortrait_SwayPivot", MotionChannelKind.Euler)!.Z!.Keys.Single();
                Assert.Equal(0.25f, bowKey.T, 3);
                Assert.Equal(0f, bowKey.V, 3);
                Assert.Equal(tableOrder, window.OutlinerOrder);

                // 깊이를 고르고 S(단축키) — 배율 X·Y와 깊이 Y가 한 번에(되돌리기 하나). 키 값은 재생 기준(relative/absolute)대로(absolute): s1은 mid라 1.708 · −580.56.
                window.Session.Editor.SetPreviewMode("nod", MotionChannelMode.Absolute);
                window.SelectControllerInHierarchy("s1", "depth");
                long revision = window.Session.Editor.Revision;
                window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.None);
                Assert.Equal(revision + 1, window.Session.Editor.Revision);
                MotionAsset nod = window.Session.Editor.Document.Find("nod")!;
                Assert.Equal(1.708, nod.FindChannel("CharSlot_DepthScale", MotionChannelKind.Scale)!.X!.Keys.Single().V, 2);
                Assert.Equal(-580.56, nod.FindChannel("CharSlot_DepthY", MotionChannelKind.Position)!.Y!.Keys.Single().V, 1);
                Assert.Equal(tableOrder, window.OutlinerOrder);

                // 시간 표시는 프레임이 기본: 0.5초 = 12프레임.
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

                // ` 첫 프레임 · Alt+` 처음부터 재생(토글이 아니다 — 재생 중에 누르면 처음으로 되감는다) · Alt+Q 토글
                window.Viewport.ScrubTo(0.3);
                window.KeyPressQwerty(PhysicalKey.Backquote, RawInputModifiers.None);
                Assert.Equal(0, window.Viewport.Time, 6);
                window.Viewport.ScrubTo(0.3);
                window.KeyPressQwerty(PhysicalKey.Backquote, RawInputModifiers.Alt);
                Assert.True(window.Viewport.IsPlaying);
                Assert.InRange(window.Viewport.Time, 0, 0.15); // 처음부터(0.3이 아니라) — 재생 시계가 이미 한 틱 갔을 수 있다
                window.KeyPressQwerty(PhysicalKey.Backquote, RawInputModifiers.Alt);
                Assert.True(window.Viewport.IsPlaying);
                window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.Alt);
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
    public void 하이어아키_기본은_오브젝트_밑에_자리_깊이_연기_컨트롤러이고_고르면_그_노드가_손잡이다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                Assert.False(window.ShowRigNodes);

                // 오브젝트 › 묶음(라벨 · mode) › 컨트롤러(라벨 · 종류 성분). 노드 항목은 없다.
                IReadOnlyList<string> body = window.HierarchyPathOfController("s1", "body");
                Assert.Equal(new[] { "s1  ·  character", "연기  ·  상대값", "몸  ·  position XY" }, body.TakeLast(3));
                Assert.Equal("자리  ·  절대값", window.HierarchyPathOfController("s1", "place")[^2]);
                Assert.Equal("깊이  ·  절대값", window.HierarchyPathOfController("s1", "depth")[^2]);
                Assert.Equal("발 기준 회전  ·  rotate Z", window.HierarchyPathOfController("s1", "bow")[^1]);
                Assert.Empty(window.HierarchyPathOf("s1", "CharacterPortrait_Track_Move"));

                // 컨트롤러를 고르면 그 오브젝트 + 주 노드 + 종류의 매니퓰레이터(선택(Q)이면 그대로).
                window.Viewport.Manipulator = ManipulatorMode.Move;
                window.SelectControllerInHierarchy("s1", "bow");
                Assert.Equal("s1", window.Viewport.SelectedObjectId);
                Assert.Equal("CharacterPortrait_SwayPivot", window.Viewport.SelectedNode);
                Assert.Equal(ManipulatorMode.Rotate, window.Manipulator);
                window.Viewport.Manipulator = ManipulatorMode.Select;
                window.SelectControllerInHierarchy("s1", "squash");
                Assert.Equal(ManipulatorMode.Select, window.Manipulator);
                Assert.Equal("CharacterPortrait_ActingScale", window.Viewport.SelectedNode);

                // W/E/R(C4): 고른 컨트롤러가 그 종류를 받으면 그 노드 그대로(자리 X·Y), 아니면 표의 기본 손잡이(몸·숙임·크기)로.
                window.SelectControllerInHierarchy("s1", "place");
                Assert.Equal("CharSlot_Track", window.Viewport.SelectedNode);
                window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
                Assert.Equal("CharSlot_Track", window.Viewport.SelectedNode);
                window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);
                Assert.Equal(ManipulatorMode.Rotate, window.Manipulator);
                Assert.Equal("CharacterPortrait_SwayPivot", window.Viewport.SelectedNode);
                window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
                Assert.Equal("CharacterPortrait_ActingScale", window.Viewport.SelectedNode);
                window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
                Assert.Equal("CharacterPortrait_Track_Move", window.Viewport.SelectedNode);
                window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.None);
                Assert.Equal("CharacterPortrait_Track_Move", window.Viewport.SelectedNode); // Q는 노드를 안 옮긴다

                // 컨트롤러 위에서 만들기 = 그 오브젝트와 같은 무대·층에(오브젝트끼리 부모·자식은 없다). 트리에서는 s1 옆.
                string s2 = window.CreateObjectAtController("character", "s1", "body");
                Assert.Equal(s2, window.Viewport.SelectedObjectId);
                Assert.Equal("mid", window.Viewport.SelectedObject!.Layer);
                Assert.Equal(window.HierarchyPathOf("s1", null)[^2], window.HierarchyPathOf(s2, null)[^2]);

                // 프리셋에서 추가(C5): 선택 오브젝트의 리그로, 표가 노드를 푼다. 목록이 그 모션을 고른다. 문제 없음.
                window.SelectInHierarchy("s1", null);
                Assert.Equal("nod", window.AddPresetFromForm("nod"));
                Assert.Equal("nod", window.SelectedMotion);
                MotionAsset nod = window.Session.Editor.Document.Find("nod")!;
                Assert.Equal("character", nod.Rig);
                Assert.NotNull(nod.FindChannel("CharacterPortrait_Track_Move", MotionChannelKind.Position)!.Y);
                Assert.Empty(window.ProblemsShown);
                Assert.Equal("nod_2", window.AddPresetFromForm("nod"));
                window.Session.Editor.Undo();
                Assert.Null(window.Session.Editor.Document.Find("nod_2"));

                // "리그 노드 보기"면 덤프 노드 26단. 끄면 다시 컨트롤러.
                window.ShowRigNodes = true;
                Assert.Empty(window.HierarchyPathOfController("s1", "body"));
                Assert.NotEmpty(window.HierarchyPathOf("s1", "CharacterPortrait_Track_Move"));
                window.ShowRigNodes = false;
                Assert.Equal("몸  ·  position XY", window.HierarchyPathOfController("s1", "body")[^1]);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 인스펙터는_탭_셋이고_마지막_탭은_설정에_남는다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                Assert.Equal(3, window.InspectorTabCount);
                Assert.Equal(0, window.InspectorTab);
                window.InspectorTab = 2;
                Assert.Equal(2, window.Settings.InspectorTab);
                window.InspectorTab = 0;
            }
            finally
            {
                window.Close();
            }
        });
    }

    // 설정 창·단축키 창은 헤드리스 세션(창 하나 전제)에서 두 번째 창을 열면 UI 스레드가 멈춰 여기서 열지 않는다 — 데스크톱에서 확인한다.

    [Fact]
    public void 채널_박스_묶음은_접히고_접힌_묶음에도_값이_들어가며_무대_뼈대는_기본_숨김이다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                window.AddMotionFromForm("step", "character", "0.5", MotionEnd.Commit);
                window.SelectInHierarchy("s1", null);
                window.ToggleChannelBoxGroup("placement");
                Assert.Contains("placement", window.Settings.CollapsedChannelBoxGroups);
                window.AutoKeyChecked = true;
                window.SetControllerValue("place", Axis.X, "40");
                Assert.Equal(40.0, window.Session.Editor.Document.Find("step")!.FindChannel("CharSlot_Track", MotionChannelKind.Position)!.X!.Keys.Single().V, 3);
                window.ToggleChannelBoxGroup("placement");
                Assert.DoesNotContain("placement", window.Settings.CollapsedChannelBoxGroups);

                // 뼈대는 기본 숨김: Scene › Stage00 › mid › s1. 켜면 26단 뼈대.
                Assert.Equal(new[] { "Scene  scene  ·  PresentataionUIRoot", "Stage00", "mid" }, window.HierarchyPathOf("s1", null).Take(3));
                window.ShowStageSkeleton = true;
                Assert.Contains("StageZoom_Root  ·  zoom 0", window.HierarchyPathOf("s1", null));
                window.ShowStageSkeleton = false;
                Assert.Equal(4, window.HierarchyPathOf("s1", null).Count);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 하이어아키는_덤프의_부모자식_계층_그대로이고_메뉴로_만들며_이름바꾸기_복제가_된다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                window.ShowRigNodes = true; // 기본은 컨트롤러 보기 — 이 테스트는 리거의 노드 보기
                window.ShowStageSkeleton = true; // 기본은 Scene › 무대 › 층만 — 이 테스트는 뼈대 전체

                // 리그 노드는 연기/배치 폴더가 아니라 덤프의 parent 사슬이다. 오브젝트 항목이 곧 __root.
                IReadOnlyList<string> path = window.HierarchyPathOf("s1", "CharacterPortrait_Track_Move_Y");
                Assert.Equal(new[] { "Scene  scene  ·  PresentataionUIRoot", "StageViewportLayer", "StageShot_Root", "StagePan_Root  ·  pan 0,0", "StageZoom_Root  ·  zoom 0", "Stage00_Root", "Stage00Mask_Root", "Stage00DepthSystem_Root", "Stage00Depth_Mid_Root", "Stage00Depth_Mid_FramingTransform", "Stage00Depth_Mid_FramingScale", "Stage00Depth_Mid_Content", "s1  ·  character", "CharSlot_Track_Focus", "CharSlot_DepthY", "CharSlot_DepthScale", "CharSlot_Track_Idle", "CharSlot_Track", "CharSlot_Track_X", "CharSlot_Track_Y", "CharSlot_Rotation", "CharSlot_SwayPivot", "CharSlot_Scale", "CharacterPortrait_VisualOffset", "CharacterPortrait_Track", "CharacterPortrait_Rotation", "CharacterPortrait_Track_Move", "CharacterPortrait_Track_Move_X", "CharacterPortrait_Track_Move_Y" }, path);
                Assert.DoesNotContain("연기 축", window.HierarchyPathOf("s1", "CharacterPortrait_SwayPivot"));

                // 노드 위에서 "만들기" = 그 오브젝트와 같은 무대·층(오브젝트끼리 부모·자식은 없다). 트리에서는 같은 Content 밑.
                string prop = window.CreateObjectFromMenu("character", "s1", "CharacterPortrait_Track_Move");
                Assert.Equal("s2", prop);
                Assert.Equal(prop, window.Viewport.SelectedObjectId);
                IReadOnlyList<string> propPath = window.HierarchyPathOf("s2", null);
                Assert.Equal("Stage00Depth_Mid_Content", propPath[^2]);
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

                // 복제 — 새 것이 선택된다.
                window.SelectInHierarchy("s1", null);
                window.DuplicateSelectedFromMenu();
                Assert.Equal("s2", window.Viewport.SelectedObjectId);
                Assert.Equal(3, window.HierarchyObjectCount);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 자리_콤보는_place_폴드를_탄다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                window.CommitObjectPlacementFromForm(200f, -40f, 0f, 1f, 1f); // s1

                // 자리(place) — 씬 탭 콤보로. Track_Focus가 코어 place 폴드로 풀린다.
                window.SelectInHierarchy("s1", null);
                window.SetPlaceFromForm("right", "face");
                Mt.Authoring.Model.SceneObject s1 = window.Viewport.Scene!.Find("s1")!;
                Assert.Equal("right", s1.Place);
                Assert.Equal("face", s1.PlaceFocus);
                // place는 오프셋을 모른다(런타임 대본 순서: place → move_by). focus 점이 화면 오른쪽 지점(0.24·1920)에 서고, 오프셋(move) 200은 그 위에 더해진다.
                Assert.Equal(0.24f * 1920f, window.Viewport.CurrentPose()!.GetState("s1/CharSlot_Track_Focus").AnchoredPosition.X, 1);
                Assert.Equal(200f, window.Viewport.CurrentPose()!.GetState("s1/CharSlot_Track").AnchoredPosition.X, 1);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
