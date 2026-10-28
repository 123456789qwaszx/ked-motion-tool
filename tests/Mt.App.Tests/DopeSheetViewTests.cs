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

public class DopeSheetViewTests
{
    private static readonly CurveRef Y = new("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);

    private static (Window Window, DopeSheetView View, MotionEditor Editor) Show()
    {
        MotionEditor editor = TestDocuments.Nod();
        var view = new DopeSheetView();
        view.SnapStep = 1.0 / 60; // 기대값은 1/60 스냅 기준(기본값은 1/24)
        var window = new Window { Width = 900, Height = 400, Content = view };
        window.Show();
        view.Bind(editor, "nod");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, view, editor);
    }

    private static Point BlockAt(Window window, DopeSheetView view, int rowIndex, int blockIndex)
    {
        DopeRow row = view.Rows[rowIndex];
        Point local = view.BlockPosition(row, row.Blocks[blockIndex]) ?? throw new InvalidOperationException("블록이 없다");
        return view.TranslatePoint(local, window) ?? throw new InvalidOperationException("뷰가 창에 없다");
    }

    [Fact]
    public void 행은_노드_채널_성분이고_블록은_레인_안에_있다() => HeadlessUi.Run(() =>
    {
        (Window window, DopeSheetView view, _) = Show();

        try
        {
            Assert.Equal(3, view.Rows.Count);
            Assert.Equal(DopeRowLevel.Node, view.Rows[0].Level);
            Assert.Equal(DopeRowLevel.Component, view.Rows[2].Level);
            Assert.Equal(3, view.Rows[2].Blocks.Count);

            foreach (DopeBlock block in view.Rows[2].Blocks)
            {
                Point p = view.BlockPosition(view.Rows[2], block)!.Value;
                Assert.InRange(p.X, DopeSheetView.LabelWidth, view.Bounds.Width);
            }

            view.ToggleCollapsed(view.Rows[0].Node);
            Assert.Single(view.Rows);
            view.ToggleCollapsed(view.Rows[0].Node);
            Assert.Equal(3, view.Rows.Count);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 블록_클릭은_선택이고_요약_블록은_밑의_키_전부다() => HeadlessUi.Run(() =>
    {
        (Window window, DopeSheetView view, _) = Show();

        try
        {
            Point middle = BlockAt(window, view, 2, 1);
            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle, MouseButton.Left);
            Assert.Equal(new[] { new KeyRef(Y, 1) }, view.Selection);

            Point summary = BlockAt(window, view, 0, 0);
            window.MouseDown(summary, MouseButton.Left, RawInputModifiers.Shift);
            window.MouseUp(summary, MouseButton.Left, RawInputModifiers.Shift);
            Assert.Equal(2, view.Selection.Count);
            Assert.Contains(new KeyRef(Y, 0), view.Selection);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 블록_드래그는_놓을_때_시간_이동_한_번이다() => HeadlessUi.Run(() =>
    {
        (Window window, DopeSheetView view, MotionEditor editor) = Show();

        try
        {
            Point middle = BlockAt(window, view, 2, 1);
            double dx = 0.1 * view.PixelsPerSecond;
            long before = editor.Revision;

            window.MouseDown(middle, MouseButton.Left);
            window.MouseMove(new Point(middle.X + dx / 2, middle.Y), RawInputModifiers.LeftMouseButton);
            Assert.Equal(before, editor.Revision);
            window.MouseMove(new Point(middle.X + dx, middle.Y), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(middle.X + dx, middle.Y), MouseButton.Left);

            Assert.Equal(before + 1, editor.Revision);
            List<KeyAsset> keys = Y.Resolve(editor.Document.Find("nod")!)!.Keys;
            Assert.Equal(new[] { 0f, 0.4f, 0.6f }, keys.Select(k => (float)Math.Round(k.T, 3)));
            Assert.Equal(new[] { new KeyRef(Y, 1) }, view.Selection);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 스케일과_복제와_끼우기는_편집기_명령이다() => HeadlessUi.Run(() =>
    {
        (Window window, DopeSheetView view, MotionEditor editor) = Show();

        try
        {
            view.Selection.Set(new[] { new KeyRef(Y, 1), new KeyRef(Y, 2) });
            Assert.NotNull(view.SelectionRange());

            view.ScaleSelection(0, 0.5);
            Assert.Equal(new[] { 0f, 0.15f, 0.3f }, Y.Resolve(editor.Document.Find("nod")!)!.Keys.Select(k => (float)Math.Round(k.T, 3)));
            Assert.Equal(2, view.Selection.Count);

            view.DuplicateSelection(0.3);
            Assert.Equal(5, Y.Resolve(editor.Document.Find("nod")!)!.Keys.Count);
            Assert.Equal(2, view.Selection.Count);

            view.SnapEnabled = false;
            view.InsertKey(view.Rows[2], 0.5);
            Assert.Equal(6, Y.Resolve(editor.Document.Find("nod")!)!.Keys.Count);
            Assert.Single(view.Selection);

            view.InsertKey(view.Rows[0], 0.7); // 요약 행은 안 된다
            Assert.Equal(6, Y.Resolve(editor.Document.Find("nod")!)!.Keys.Count);

            view.DeleteSelection();
            Assert.Equal(5, Y.Resolve(editor.Document.Find("nod")!)!.Keys.Count);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 눈금자_클릭은_스크럽이고_박스는_블록을_고른다() => HeadlessUi.Run(() =>
    {
        (Window window, DopeSheetView view, _) = Show();

        try
        {
            var rulerLocal = new Point(view.XAtTime(0.3), view.RulerRect.Center.Y);
            Point ruler = view.TranslatePoint(rulerLocal, window)!.Value;
            window.MouseDown(ruler, MouseButton.Left);
            window.MouseUp(ruler, MouseButton.Left);
            Assert.Equal(0.3, view.CurrentTime, 3);

            Point first = BlockAt(window, view, 2, 0);
            Point last = BlockAt(window, view, 2, 2);
            var start = new Point(first.X - 10, first.Y - 8);
            var end = new Point(last.X - 10, last.Y + 8);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);

            Assert.Equal(2, view.Selection.Count);
            Assert.DoesNotContain(new KeyRef(Y, 2), view.Selection);
        }
        finally
        {
            window.Close();
        }
    });
}
