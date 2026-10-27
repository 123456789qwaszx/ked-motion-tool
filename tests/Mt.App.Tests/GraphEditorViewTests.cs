using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Ked.Presentation.Core;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;

namespace Mt.App.Tests;

public class GraphEditorViewTests
{
    private static readonly CurveRef Y = new("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);

    private static (Window Window, GraphEditorView View, MotionEditor Editor) Show()
    {
        MotionEditor editor = TestDocuments.Nod();
        var view = new GraphEditorView();
        var window = new Window { Width = 900, Height = 500, Content = view };
        window.Show();
        view.Bind(editor, "nod");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, view, editor);
    }

    private static Point InWindow(Window window, GraphEditorView view, Point local)
        => view.TranslatePoint(local, window) ?? throw new InvalidOperationException("뷰가 창에 붙어 있지 않다");

    private static Point At(GraphEditorView view, int index)
        => view.ScreenPositionOf(new KeyRef(Y, index)) ?? throw new InvalidOperationException($"키 {index}가 없다");

    [Fact]
    public void 모션을_묶으면_성분마다_슬롯이_서고_전체_맞춤_뒤_키가_플롯_안에_있다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, _) = Show();

        try
        {
            CurveSlot slot = Assert.Single(view.Curves);
            Assert.Equal(Y, slot.Curve);
            Assert.True(slot.Visible);

            for (int i = 0; i < 3; i++)
            {
                Point p = At(view, i);
                Assert.InRange(p.X, 0, view.Bounds.Width);
                Assert.InRange(p.Y, 0, view.Bounds.Height);
            }

            // 값이 낮은 가운데 키(-18)는 화면에서 더 아래(큰 y)다.
            Assert.True(At(view, 1).Y > At(view, 0).Y);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 키를_클릭하면_선택되고_Shift_클릭은_더한다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, _) = Show();

        try
        {
            Point first = InWindow(window, view, At(view, 0));
            Point middle = InWindow(window, view, At(view, 1));

            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle, MouseButton.Left);
            Assert.Equal(new[] { new KeyRef(Y, 1) }, view.Selection);

            window.MouseDown(first, MouseButton.Left, RawInputModifiers.Shift);
            window.MouseUp(first, MouseButton.Left, RawInputModifiers.Shift);
            Assert.Equal(2, view.Selection.Count);

            // 빈 자리 클릭은 선택을 비운다.
            Point empty = InWindow(window, view, new Point(view.Bounds.Width - 20, 20));
            window.MouseDown(empty, MouseButton.Left);
            window.MouseUp(empty, MouseButton.Left);
            Assert.Empty(view.Selection);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 키_드래그는_놓을_때_한_번의_편집이고_값이_따라온다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, MotionEditor editor) = Show();

        try
        {
            view.Tool = GraphTool.Move; // 이동 툴(W)에서만 LMB 드래그가 이동이다
            Point middle = InWindow(window, view, At(view, 1));
            long before = editor.Revision;
            float valueBefore = Y.Resolve(editor.Document.Find("nod")!)!.Keys[1].V;

            window.MouseDown(middle, MouseButton.Left);
            window.MouseMove(new Point(middle.X, middle.Y - 20), RawInputModifiers.LeftMouseButton);
            Assert.Equal(before, editor.Revision); // 드래그 중간값은 렌더만
            window.MouseMove(new Point(middle.X, middle.Y - 40), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(middle.X, middle.Y - 40), MouseButton.Left);

            Assert.Equal(before + 1, editor.Revision);
            List<KeyAsset> keys = Y.Resolve(editor.Document.Find("nod")!)!.Keys;
            Assert.Equal(3, keys.Count);
            Assert.True(keys[1].V > valueBefore, "위로 끌었는데 값이 안 올랐다");
            Assert.Equal(0.3, keys[1].T, 4); // 시간은 그대로(수직 드래그 + 스냅)
            Assert.Equal(new[] { new KeyRef(Y, 1) }, view.Selection);

            editor.Undo();
            Assert.Equal(valueBefore, Y.Resolve(editor.Document.Find("nod")!)!.Keys[1].V);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 선택_툴에서_LMB_드래그는_옮기지_않고_MMB_드래그가_옮긴다_마야_관습() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, MotionEditor editor) = Show();

        try
        {
            Assert.Equal(GraphTool.Select, view.Tool);
            Point middle = InWindow(window, view, At(view, 1));
            long before = editor.Revision;

            window.MouseDown(middle, MouseButton.Left);
            window.MouseMove(new Point(middle.X, middle.Y - 40), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(middle.X, middle.Y - 40), MouseButton.Left);
            Assert.Equal(before, editor.Revision);
            Assert.Equal(new[] { new KeyRef(Y, 1) }, view.Selection);

            window.MouseDown(middle, MouseButton.Middle);
            window.MouseMove(new Point(middle.X, middle.Y - 40), RawInputModifiers.MiddleMouseButton);
            window.MouseUp(new Point(middle.X, middle.Y - 40), MouseButton.Middle);
            Assert.Equal(before + 1, editor.Revision);
            Assert.True(Y.Resolve(editor.Document.Find("nod")!)!.Keys[1].V > -18f);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Alt_MMB는_팬이고_Alt_RMB는_줌_드래그다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, _) = Show();

        try
        {
            GraphCamera before = view.Camera;
            Point start = InWindow(window, view, new Point(view.Bounds.Width / 2, view.Bounds.Height / 2));

            window.MouseDown(start, MouseButton.Middle, RawInputModifiers.Alt);
            window.MouseMove(new Point(start.X + 50, start.Y), RawInputModifiers.Alt | RawInputModifiers.MiddleMouseButton);
            window.MouseUp(new Point(start.X + 50, start.Y), MouseButton.Middle, RawInputModifiers.Alt);
            Assert.True(view.Camera.TimeOrigin < before.TimeOrigin);
            Assert.Equal(before.PixelsPerSecond, view.Camera.PixelsPerSecond, 6);

            GraphCamera panned = view.Camera;
            window.MouseDown(start, MouseButton.Right, RawInputModifiers.Alt);
            window.MouseMove(new Point(start.X + 60, start.Y), RawInputModifiers.Alt | RawInputModifiers.RightMouseButton);
            window.MouseUp(new Point(start.X + 60, start.Y), MouseButton.Right, RawInputModifiers.Alt);
            Assert.True(view.Camera.PixelsPerSecond > panned.PixelsPerSecond);
            Assert.Equal(panned.PixelsPerValue, view.Camera.PixelsPerValue, 6);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 스케일_툴은_선택_중심_기준으로_값을_늘리고_라소는_다각형으로_고른다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, MotionEditor editor) = Show();

        try
        {
            view.SelectAll();
            view.Tool = GraphTool.Scale;
            Point start = InWindow(window, view, new Point(view.Bounds.Width / 2, view.Bounds.Height / 2));
            long before = editor.Revision;

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X, start.Y - 50), RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
            window.MouseUp(new Point(start.X, start.Y - 50), MouseButton.Left, RawInputModifiers.Shift);

            Assert.Equal(before + 1, editor.Revision);
            List<KeyAsset> keys = Y.Resolve(editor.Document.Find("nod")!)!.Keys;
            Assert.Equal(new[] { 0f, 0.3f, 0.6f }, keys.Select(k => (float)Math.Round(k.T, 4))); // Shift: 값만
            Assert.True(keys[0].V > 0f && keys[1].V < -18f, "피벗(가운데 -9)에서 멀어져야 한다");

            view.Tool = GraphTool.Select;
            view.ClearSelection();
            view.FitAll(); // 스케일로 키가 플롯 밖으로 나갔다 — 라소 점이 컨트롤 안에 있어야 한다
            Point first = At(view, 0);
            Point middle = At(view, 1);
            Point a = InWindow(window, view, new Point(first.X - 12, Math.Min(first.Y, middle.Y) - 12));
            Point b = InWindow(window, view, new Point(middle.X + 12, Math.Min(first.Y, middle.Y) - 12));
            Point c = InWindow(window, view, new Point(middle.X + 12, Math.Max(first.Y, middle.Y) + 12));
            Point d = InWindow(window, view, new Point(first.X - 12, Math.Max(first.Y, middle.Y) + 12));
            window.MouseDown(a, MouseButton.Left, RawInputModifiers.Control);
            window.MouseMove(b, RawInputModifiers.Control | RawInputModifiers.LeftMouseButton);
            window.MouseMove(c, RawInputModifiers.Control | RawInputModifiers.LeftMouseButton);
            window.MouseMove(d, RawInputModifiers.Control | RawInputModifiers.LeftMouseButton);
            window.MouseUp(d, MouseButton.Left, RawInputModifiers.Control);

            Assert.Equal(2, view.Selection.Count);
            Assert.Contains(new KeyRef(Y, 0), view.Selection);
            Assert.Contains(new KeyRef(Y, 1), view.Selection);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 정규화_보기는_모양만_견주고_끌면_값은_실제_단위로_바뀐다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, MotionEditor editor) = Show();

        try
        {
            // 두 번째 커브(배율, 1~1.2)를 더한다 — 절대 보기에선 -18~0 옆에서 납작하다.
            editor.AddChannel("nod", "CharSlot_Scale", MotionChannelKind.Scale, MotionChannelMode.Relative);
            editor.SetKeys("nod", "CharSlot_Scale", MotionChannelKind.Scale, Axis.X, new[] { TestDocuments.Key(0f, 1f), TestDocuments.Key(0.3f, 1.2f), TestDocuments.Key(0.6f, 1f) });
            var sx = new CurveRef("CharSlot_Scale", MotionChannelKind.Scale, Axis.X);

            view.ViewMode = GraphViewMode.Normalized;
            Point yTop = view.ScreenPositionOf(new KeyRef(Y, 0))!.Value;    // y: 0 = 최대 → 보기 1
            Point yBottom = view.ScreenPositionOf(new KeyRef(Y, 1))!.Value; // y: -18 = 최소 → 보기 0
            Point sTop = view.ScreenPositionOf(new KeyRef(sx, 1))!.Value;   // 배율 1.2 = 최대 → 보기 1
            Point sBottom = view.ScreenPositionOf(new KeyRef(sx, 0))!.Value;
            Assert.Equal(yTop.Y, sTop.Y, 3);
            Assert.Equal(yBottom.Y, sBottom.Y, 3);

            // 정규화 보기에서 배율 키를 끌면 값은 배율 단위로 바뀐다(보기 0.5 = 0.1).
            view.Tool = GraphTool.Move;
            view.Select(new[] { new KeyRef(sx, 1) }, additive: false);
            Point middle = InWindow(window, view, sTop);
            double dy = (sBottom.Y - sTop.Y) / 2; // 보기에서 0.5만큼 아래로
            window.MouseDown(middle, MouseButton.Left);
            window.MouseMove(new Point(middle.X, middle.Y + dy), RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
            window.MouseUp(new Point(middle.X, middle.Y + dy), MouseButton.Left, RawInputModifiers.Shift);

            Assert.Equal(1.1, sx.Resolve(editor.Document.Find("nod")!)!.Keys[1].V, 2);

            view.ViewMode = GraphViewMode.Absolute;
            Assert.Equal(CurveViewTransform.Identity, view.ViewTransformOf(sx));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 잠긴_커브는_고르거나_끌_수_없고_뮤트는_뷰포트에서_빠진다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, _) = Show();

        try
        {
            view.Flags.SetLocked(Y, true);
            Point middle = InWindow(window, view, At(view, 1));
            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle, MouseButton.Left);
            Assert.Empty(view.Selection);

            view.Flags.SetLocked(Y, false);
            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle, MouseButton.Left);
            Assert.Single(view.Selection);

            view.Flags.SetLocked(Y, true);
            Assert.Empty(view.Selection); // 잠그면 선택에서 빠진다
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 박스_선택은_안에_든_키를_고른다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, _) = Show();

        try
        {
            Point first = At(view, 0);
            Point middle = At(view, 1);
            Point start = InWindow(window, view, new Point(first.X - 12, first.Y - 12));
            Point end = InWindow(window, view, new Point(middle.X + 12, middle.Y + 12));

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);

            Assert.Equal(2, view.Selection.Count);
            Assert.Contains(new KeyRef(Y, 0), view.Selection);
            Assert.Contains(new KeyRef(Y, 1), view.Selection);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 삭제와_현재_시각_키_끼우기와_탄젠트_모드() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, MotionEditor editor) = Show();

        try
        {
            view.Select(new[] { new KeyRef(Y, 1) }, additive: false);
            view.DeleteSelection();
            Assert.Equal(2, Y.Resolve(editor.Document.Find("nod")!)!.Keys.Count);
            Assert.Empty(view.Selection);

            view.CurrentTime = 0.3;
            view.InsertKeyAtCurrentTime();
            List<KeyAsset> keys = Y.Resolve(editor.Document.Find("nod")!)!.Keys;
            Assert.Equal(3, keys.Count);
            Assert.Equal(0.3, keys[1].T, 4);
            Assert.Equal(0.0, keys[1].V, 4); // 곡선 위(0 → 0 사이)
            Assert.Equal(new[] { new KeyRef(Y, 1) }, view.Selection);

            view.SetTangentMode(TangentMode.Linear);
            Assert.Equal(TangentMode.Linear, Y.Resolve(editor.Document.Find("nod")!)!.Keys[1].Tangent);

            view.SelectAll();
            Assert.Equal(3, view.Selection.Count);
            view.FitSelection();
            Assert.True(view.Camera.PixelsPerSecond > 0);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 아래_눈금자_클릭은_스크럽이고_점은_키로_점프한다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, _) = Show();

        try
        {
            Point middle = At(view, 1);
            Point ruler = InWindow(window, view, new Point(middle.X, view.Bounds.Height - 6));
            window.MouseDown(ruler, MouseButton.Left);
            window.MouseUp(ruler, MouseButton.Left);
            Assert.Equal(0.3, view.CurrentTime, 2);

            Assert.True(view.JumpToKey(forward: true));
            Assert.Equal(0.6f, view.CurrentTime, 6);
            Assert.True(view.JumpToKey(forward: false));
            Assert.Equal(0.3f, view.CurrentTime, 6);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 휠은_커서_아래를_고정한_채_줌이다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, _) = Show();

        try
        {
            Point middleLocal = At(view, 1);
            Point middle = InWindow(window, view, middleLocal);
            double ppsBefore = view.Camera.PixelsPerSecond;

            window.MouseWheel(middle, new Vector(0, 1));

            Assert.True(view.Camera.PixelsPerSecond > ppsBefore);
            Point after = At(view, 1);
            Assert.Equal(middleLocal.X, after.X, 1);
            Assert.Equal(middleLocal.Y, after.Y, 1);
        }
        finally
        {
            window.Close();
        }
    });
}
