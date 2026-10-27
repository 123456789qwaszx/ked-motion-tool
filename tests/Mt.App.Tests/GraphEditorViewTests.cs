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
