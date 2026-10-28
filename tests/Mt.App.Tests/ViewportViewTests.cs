using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Ked.Presentation.Core;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;

namespace Mt.App.Tests;

public class ViewportViewTests
{
    private static (Window Window, ViewportView View, MotionEditor Editor) Show()
    {
        MotionEditor editor = TestDocuments.Nod();
        var view = new ViewportView { SnapStep = 1.0 / 60 }; // 기대값은 1/60 스냅 기준(기본값은 1/24)
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.SetRig(RigSchemaFile.Load(TestDocuments.FixturePath("rig-schemas.json")));
        view.Bind(editor, "nod");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, view, editor);
    }

    [Fact]
    public void 휠은_커서_아래_점을_고정한_채_확대이고_MMB_드래그는_보기_이동이며_F는_맞춤이다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            Rect before = view.StageRect;
            var anchor = new Point(before.X + before.Width * 0.25, before.Y + before.Height * 0.25);
            Vec2 under = view.ToRoot(anchor);
            Point inWindow = view.TranslatePoint(anchor, window) ?? throw new InvalidOperationException("뷰가 창에 붙어 있지 않다");

            // 휠 위로 = 확대. 커서 아래의 루트 점은 그 자리에 남는다.
            window.MouseWheel(inWindow, new Vector(0, 2));
            Assert.True(view.ViewZoom > 1);
            Assert.True(view.StageRect.Width > before.Width);
            Point after = view.ToScreen(under);
            Assert.Equal(anchor.X, after.X, 1);
            Assert.Equal(anchor.Y, after.Y, 1);

            // MMB 드래그 = 보기 이동(씬·키는 그대로).
            Vector panBefore = view.ViewPan;
            window.MouseDown(inWindow, MouseButton.Middle);
            window.MouseMove(new Point(inWindow.X + 30, inWindow.Y - 10), RawInputModifiers.MiddleMouseButton);
            window.MouseUp(new Point(inWindow.X + 30, inWindow.Y - 10), MouseButton.Middle);
            Assert.Equal(30, view.ViewPan.X - panBefore.X, 3);
            Assert.Equal(-10, view.ViewPan.Y - panBefore.Y, 3);

            // F = 맞춤(배율 1 · 이동 0) — 무대가 처음 자리로.
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.None);
            Assert.Equal(1, view.ViewZoom, 6);
            Assert.Equal(default, view.ViewPan);
            Assert.Equal(before, view.StageRect);

            // 범위 밖으로는 안 간다.
            for (int i = 0; i < 40; i++)
            {
                window.MouseWheel(inWindow, new Vector(0, 3));
            }

            Assert.Equal(ViewportView.MaxViewZoom, view.ViewZoom, 6);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 리그를_실으면_초상이_서고_포즈가_시각을_따른다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            Assert.True(view.HasRig);
            Quad rest = view.CurrentPortraitQuad() ?? throw new InvalidOperationException("초상이 없다");
            Assert.Equal(0f, rest.Center.Y, 2);

            view.ScrubTo(0.3);
            Quad dipped = view.CurrentPortraitQuad()!.Value;
            Assert.Equal(-18f, dipped.Center.Y, 2);

            // 길이에서는 restore라 시작 상태.
            view.ScrubTo(0.6);
            Assert.Equal(0f, view.CurrentPortraitQuad()!.Value.Center.Y, 2);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 재생은_시계를_밀고_루프를_끄면_끝에서_멈춘다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            int changes = 0;
            view.TimeChanged += (_, _) => changes++;

            view.Play();
            Assert.True(view.IsPlaying);
            view.Advance(0.3);
            Assert.Equal(0.3, view.Time, 9);
            Assert.Equal(1, changes);

            view.Loop = false;
            view.Advance(1);
            Assert.Equal(0.6f, view.Time, 6); // 길이는 float(0.6f)다
            Assert.False(view.IsPlaying);

            view.Stop();
            Assert.Equal(0, view.Time);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 점은_키로_점프하고_거꾸로_재생은_범위_끝에서_처음으로_간다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            Assert.True(view.JumpToNextKey());
            Assert.Equal(0.3, view.Time, 6);
            Assert.True(view.JumpToNextKey());
            Assert.Equal(0.6f, view.Time, 6);
            Assert.False(view.JumpToNextKey());
            Assert.True(view.JumpToPreviousKey());
            Assert.Equal(0.3, view.Time, 6);

            view.Mode = PlaybackMode.Once;
            view.PlayBackward();
            Assert.True(view.IsPlaying);
            Assert.True(view.Clock.IsReversed);
            view.Advance(0.1);
            Assert.Equal(0.2, view.Time, 6);
            view.Advance(1);
            Assert.Equal(0, view.Time, 6);
            Assert.False(view.IsPlaying);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 이동_매니퓰레이터는_AutoKey면_놓을_때_바뀐_성분만_키로_놓는다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, MotionEditor editor) = Show();

        try
        {
            const string node = "CharacterPortrait_Track_Move_Y";
            view.SelectedNode = node;
            view.Manipulator = ManipulatorMode.Move;
            view.AutoKey = true;
            view.ScrubTo(0.3);
            long before = editor.Revision;

            Point centerLocal = view.GizmoCenter() ?? throw new InvalidOperationException("기즈모가 없다");
            Point center = view.TranslatePoint(centerLocal, window)!.Value;
            double scale = view.StageRect.Width / 1920.0;
            var target = new Point(center.X, center.Y + 20);

            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            Assert.Equal(before, editor.Revision); // 드래그 중간값은 렌더만
            window.MouseUp(target, MouseButton.Left);

            Assert.Equal(before + 1, editor.Revision);
            Assert.False(view.HasPending);
            ChannelAsset channel = editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Position)!;
            Assert.Null(channel.X); // 세로로만 끌었으니 x는 건드리지 않는다
            KeyAsset key = channel.Y!.Keys.Single(k => Math.Abs(k.T - 0.3f) < 1e-4);
            Assert.Equal(-18.0 - 20.0 / scale, key.V, 1);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void AutoKey가_꺼져_있으면_키_없는_포즈로_남고_S가_키를_놓고_시각을_옮기면_버린다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, MotionEditor editor) = Show();

        try
        {
            const string node = "CharacterPortrait_Track_Move_Y";
            view.SelectedNode = node;
            view.Manipulator = ManipulatorMode.Move;
            view.AutoKey = false;
            view.ScrubTo(0.15); // 키가 없는 시각
            long before = editor.Revision;
            int pendingChanges = 0;
            view.PendingChanged += (_, _) => pendingChanges++;

            Point center = view.TranslatePoint(view.GizmoCenter()!.Value, window)!.Value;
            var target = new Point(center.X, center.Y - 30);
            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            window.MouseUp(target, MouseButton.Left);

            Assert.Equal(before, editor.Revision);
            Assert.True(view.HasPending);
            Assert.True(pendingChanges > 0);
            Assert.Equal(3, editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Position)!.Y!.Keys.Count);

            view.SetKey();
            Assert.False(view.HasPending);
            Assert.Equal(before + 1, editor.Revision);
            List<KeyAsset> keys = editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Position)!.Y!.Keys;
            Assert.Equal(4, keys.Count);
            Assert.Contains(keys, k => Math.Abs(k.T - 0.15f) < 1e-4 && k.V > -9f);

            // 다시 끌고 시각을 옮기면 버린다.
            center = view.TranslatePoint(view.GizmoCenter()!.Value, window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(new Point(center.X, center.Y - 30), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(center.X, center.Y - 30), MouseButton.Left);
            Assert.False(view.HasPending); // 0.15에 이미 키가 있으니 바로 키가 된다(마야: 키 있는 시각은 덮어쓴다)
            Assert.Equal(before + 2, editor.Revision);

            view.ScrubTo(0.5);
            view.ScrubTo(0.2);
            center = view.TranslatePoint(view.GizmoCenter()!.Value, window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(new Point(center.X, center.Y - 30), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(center.X, center.Y - 30), MouseButton.Left);
            Assert.True(view.HasPending);
            view.ScrubTo(0.4);
            Assert.False(view.HasPending);
            Assert.Equal(before + 2, editor.Revision);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 회전_매니퓰레이터는_고리를_끌어_z_회전_키를_놓는다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, MotionEditor editor) = Show();

        try
        {
            const string node = "CharacterPortrait_SwayPivot";
            view.SelectedNode = node;
            view.Manipulator = ManipulatorMode.Rotate;
            view.AutoKey = true;
            view.ScrubTo(0);
            long before = editor.Revision;

            Point centerLocal = view.GizmoCenter()!.Value;
            Point onRing = view.TranslatePoint(new Point(centerLocal.X + 60, centerLocal.Y), window)!.Value;
            Point rotated = view.TranslatePoint(new Point(centerLocal.X, centerLocal.Y - 60), window)!.Value; // 화면 위 = 루트 y 위 = 반시계 90도

            window.MouseDown(onRing, MouseButton.Left);
            window.MouseMove(rotated, RawInputModifiers.LeftMouseButton);
            window.MouseUp(rotated, MouseButton.Left);

            Assert.Equal(before + 1, editor.Revision);
            ChannelAsset channel = editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Euler)!;
            Assert.Null(channel.X);
            Assert.Null(channel.Y);
            Assert.Equal(90f, channel.Z!.Keys[0].V, 1);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 뮤트된_채널은_재생에서_빠진다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            var y = new Mt.Authoring.Graph.CurveRef("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Mt.Authoring.Model.Axis.Y);
            view.ScrubTo(0.3);
            Assert.Equal(-18f, view.CurrentPortraitQuad()!.Value.Center.Y, 2);

            view.Flags.SetMuted(y, true);
            Assert.Equal(0f, view.CurrentPortraitQuad()!.Value.Center.Y, 2);

            view.Flags.SetMuted(y, false);
            Assert.Equal(-18f, view.CurrentPortraitQuad()!.Value.Center.Y, 2);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 씬의_오브젝트들은_배치대로_서고_클릭으로_고르며_모션은_선택_오브젝트에만_입힌다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, MotionEditor editor) = Show();

        try
        {
            Assert.Equal("s1", view.SelectedObjectId);
            editor.AddSceneObject(null, "s2", "character", SceneLayers.Front);
            editor.SetObjectPlacement(null, "s2", 300f, 0f, 0f, 1f, 1f);

            Quad first = view.ObjectQuad("s1")!.Value;
            Quad second = view.ObjectQuad("s2")!.Value;
            Assert.Equal(300f, second.BottomLeft.X - first.BottomLeft.X, 1);

            // 두 번째 오브젝트의 초상 가운데를 클릭하면 그것이 선택된다.
            Vec2 center = (second.BottomLeft + second.TopRight) * 0.5f;
            Vec2 canvas = PortraitLayout.ToCanvas(center, view.StageSize);
            Rect stage = view.StageRect;
            double scale = stage.Width / view.StageSize.X;
            Point at = view.TranslatePoint(new Point(stage.X + canvas.X * scale, stage.Y + canvas.Y * scale), window)!.Value;
            view.Manipulator = ManipulatorMode.Select;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Assert.Equal("s2", view.SelectedObjectId);

            // nod(y 0 → -18)는 선택 오브젝트(s2)에만 얹힌다.
            view.ScrubTo(0.3);
            Quad c1At = view.ObjectQuad("s1")!.Value;
            Quad c2At = view.ObjectQuad("s2")!.Value;
            Assert.Equal(first.BottomLeft.Y, c1At.BottomLeft.Y, 2);
            Assert.Equal(second.BottomLeft.Y - 18f, c2At.BottomLeft.Y, 1);

            // 오브젝트 삭제는 선택을 남은 것으로 옮긴다.
            editor.RemoveSceneObject(null, "s2");
            Assert.Equal("s1", view.SelectedObjectId);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 카메라는_뼈대의_Zoom_Pan_노드에_들어가고_보기_변환은_항등이다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, MotionEditor editor) = Show();

        try
        {
            Assert.True(view.CameraInTree);
            Point origin = view.ToScreen(Vec2.Zero);
            Quad rest = view.ObjectQuad("s1")!.Value;
            Assert.Equal(0f, rest.Center.X, 1);

            editor.SetCamera(null, 20f, 50f, 0f); // 배율 2, pan +50px
            Assert.Equal(2f, view.CameraScale, 4);
            Assert.Equal(origin, view.ToScreen(Vec2.Zero)); // 격자(StageShot_Root 공간)는 안 움직인다
            Quad shot = view.ObjectQuad("s1")!.Value; // 오브젝트는 pan + 2·(자리) 만큼 — 런타임 StagePan/Zoom과 같다
            Assert.Equal(50f, shot.Center.X, 1);
            Assert.Equal((rest.TopRight.X - rest.TopLeft.X) * 2f, shot.TopRight.X - shot.TopLeft.X, 1);

            Vec2 back = view.ToRoot(view.ToScreen(new Vec2(123f, -45f)));
            Assert.Equal(123f, back.X, 2);
            Assert.Equal(-45f, back.Y, 2);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 초상_비트맵이_종횡비를_정하고_없으면_자리표시다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            Assert.Equal(PortraitLayout.PlaceholderAspect, view.Aspect);

            using var bitmap = new WriteableBitmap(new PixelSize(80, 100), new Vector(96, 96));
            view.SetPortrait(bitmap);

            Assert.Equal(0.8f, view.Aspect, 4);
            Quad quad = view.CurrentPortraitQuad()!.Value;
            Assert.Equal(864f, (quad.BottomRight - quad.BottomLeft).X, 1);
            Assert.Equal(1080f, (quad.TopLeft - quad.BottomLeft).Y, 1);

            view.SetPortrait(null);
            Assert.Equal(PortraitLayout.PlaceholderAspect, view.Aspect);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 리그가_없거나_rigKind가_다르면_초상이_없다_그래도_그려진다() => HeadlessUi.Run(() =>
    {
        MotionEditor editor = TestDocuments.Nod();
        editor.AddMotion("robot_wave", "robot", 1f);
        var view = new ViewportView { SnapStep = 1.0 / 60 }; // 기대값은 1/60 스냅 기준(기본값은 1/24)
        var window = new Window { Width = 600, Height = 400, Content = view };
        window.Show();

        try
        {
            view.Bind(editor, "nod");
            Assert.False(view.HasRig);
            Assert.Null(view.CurrentPortraitQuad());

            view.SetRig(RigSchemaFile.Load(TestDocuments.FixturePath("rig-schemas.json")));
            Assert.True(view.HasRig);

            // 리그가 다른 모션: 오브젝트(s1 character)는 그대로 서고, 모션만 안 맞는다.
            view.Bind(editor, "robot_wave");
            Assert.True(view.HasRig);
            Assert.NotNull(view.CurrentPortraitQuad());
            Assert.False(view.CanWear(view.SelectedObject!));
            view.InvalidateVisual();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 숨긴_오브젝트는_클릭에_안_잡힌다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, MotionEditor editor) = Show();

        try
        {
            editor.AddSceneObject(null, "s2", "character", SceneLayers.Mid);
            Assert.Throws<InvalidOperationException>(() => editor.AddSceneObject(null, "pivot", "", SceneLayers.Front)); // 빈 오브젝트는 없다

            view.Manipulator = ManipulatorMode.Select;
            view.SelectedObjectId = "s2";
            Quad quad = view.ObjectQuad("s2")!.Value;
            Vec2 center = (quad.BottomLeft + quad.TopRight) * 0.5f;
            Point onS2 = view.TranslatePoint(view.ToScreen(center), window)!.Value;
            editor.SetObjectVisible(null, "s2", false);
            editor.SetObjectVisible(null, "s1", false);
            view.SelectedObjectId = null;
            window.MouseDown(onS2, MouseButton.Left);
            window.MouseUp(onS2, MouseButton.Left);
            Assert.Null(view.SelectedObjectId);
        }
        finally
        {
            window.Close();
        }
    });
}
