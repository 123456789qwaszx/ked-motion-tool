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
        view.SnapStep = 1.0 / 60; // 기대값은 1/60 스냅 기준(기본값은 1/24)
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
    public void 복사_붙여넣기와_버퍼_커브_바꾸기() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, MotionEditor editor) = Show();

        try
        {
            view.Select(new[] { new KeyRef(Y, 1) }, additive: false);
            view.CopySelection();
            Assert.False(view.Clipboard.IsEmpty);

            view.CurrentTime = 0.45;
            view.Paste();
            List<KeyAsset> keys = Y.Resolve(editor.Document.Find("nod")!)!.Keys;
            Assert.Equal(4, keys.Count);
            Assert.Contains(keys, k => Math.Abs(k.T - 0.45f) < 1e-4 && k.V == -18f);
            Assert.Single(view.Selection);

            // 다른 커브만 띄우고(아웃라이너처럼) 붙이면 그 커브에 들어간다 — y의 키를 x에.
            var x = new CurveRef(Y.Node, Y.Kind, Axis.X);
            editor.SetKeys("nod", x.Node, x.Kind, x.Axis, new[] { TestDocuments.Key(0f, 0f) }); // x 성분을 만든다
            view.SetVisibleAll(c => c == x);
            view.CurrentTime = 0.2;
            view.Paste();
            Assert.Contains(x.Resolve(editor.Document.Find("nod")!)!.Keys, k => Math.Abs(k.T - 0.2f) < 1e-4 && k.V == -18f);
            Assert.Equal(4, Y.Resolve(editor.Document.Find("nod")!)!.Keys.Count); // y에는 안 붙었다
            view.SetVisibleAll(_ => true);

            view.SnapshotBuffer();
            Assert.True(view.HasBuffer);
            view.Select(new[] { new KeyRef(Y, 1) }, additive: false);
            view.CutSelection();
            Assert.Equal(3, Y.Resolve(editor.Document.Find("nod")!)!.Keys.Count);

            long before = editor.Revision;
            view.SwapBuffer();
            Assert.Equal(before + 1, editor.Revision);
            Assert.Equal(4, Y.Resolve(editor.Document.Find("nod")!)!.Keys.Count); // 버퍼(4개)가 돌아왔다
            view.SwapBuffer();
            Assert.Equal(3, Y.Resolve(editor.Document.Find("nod")!)!.Keys.Count); // 다시 바꾸면 3개

            view.ShowCycle = true;
            view.InvalidateVisual();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            view.ClearBuffer();
            Assert.False(view.HasBuffer);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 리전_툴은_상자_모서리를_끌어_그_축만_스케일하고_값_스냅은_정수로_끈다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, MotionEditor editor) = Show();

        try
        {
            view.Tool = GraphTool.Region;
            view.SelectAll();
            (double t0, double t1, double v0, double v1) = view.Region ?? throw new InvalidOperationException("리전이 없다");
            Assert.Equal(0, t0, 6);
            Assert.Equal(0.6, t1, 3);
            Assert.Equal(-18, v0, 3);
            Assert.Equal(0, v1, 3);

            // 오른쪽 모서리를 왼쪽으로 — 왼쪽 끝(0) 기준 시간만 줄어든다.
            Point last = At(view, 2);
            Point middle = At(view, 1);
            Point edge = InWindow(window, view, new Point(last.X + 8, (last.Y + middle.Y) / 2));
            Point target = InWindow(window, view, new Point(last.X + 8 - (last.X - At(view, 0).X) / 2, (last.Y + middle.Y) / 2));
            long before = editor.Revision;
            window.MouseDown(edge, MouseButton.Left);
            window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            window.MouseUp(target, MouseButton.Left);

            Assert.Equal(before + 1, editor.Revision);
            List<KeyAsset> keys = Y.Resolve(editor.Document.Find("nod")!)!.Keys;
            Assert.Equal(0f, keys[0].T);
            Assert.InRange(keys[2].T, 0.25f, 0.35f);
            Assert.InRange(keys[1].T, keys[2].T / 2 - 0.02f, keys[2].T / 2 + 0.02f);
            Assert.Equal(-18f, keys[1].V, 3); // 시간 모서리는 값을 안 건드린다
            Assert.NotNull(view.Region);

            // 값 스냅: MMB로 조금 끌어도 값은 정수로.
            view.Tool = GraphTool.Select;
            view.ValueSnapEnabled = true;
            view.Select(new[] { new KeyRef(Y, 1) }, additive: false);
            Point mid = InWindow(window, view, At(view, 1));
            window.MouseDown(mid, MouseButton.Middle);
            window.MouseMove(new Point(mid.X, mid.Y - 7), RawInputModifiers.MiddleMouseButton);
            window.MouseUp(new Point(mid.X, mid.Y - 7), MouseButton.Middle);
            float v = Y.Resolve(editor.Document.Find("nod")!)!.Keys[1].V;
            Assert.Equal(MathF.Round(v), v, 5);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 커브_선_클릭은_커브_선택이고_MMB_클릭이_키를_끼우며_RMB_드래그는_한_축으로만_옮긴다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, MotionEditor editor) = Show();

        try
        {
            // flat 탄젠트 키 (0,0)·(0.3,-18) 사이 한가운데는 정확히 (0.15, -9) — 화면에선 두 키의 중점.
            Point k0 = At(view, 0);
            Point k1 = At(view, 1);
            Point onCurve = InWindow(window, view, new Point((k0.X + k1.X) / 2, (k0.Y + k1.Y) / 2));
            window.MouseDown(onCurve, MouseButton.Left);
            window.MouseUp(onCurve, MouseButton.Left);

            Assert.Contains(Y, view.SelectedCurves);
            Assert.Empty(view.Selection);

            // MMB 클릭(안 움직임) → 선택한 커브 위 그 시각에 키.
            List<KeyAsset> before = Y.Resolve(editor.Document.Find("nod")!)!.Keys.Select(k => new KeyAsset { T = k.T, V = k.V, InTangent = k.InTangent, OutTangent = k.OutTangent, Tangent = k.Tangent }).ToList();
            (float expectedValue, _) = TangentMath.OnCurve(before, 0.1f);
            Point insertAt = InWindow(window, view, new Point(k0.X + (k1.X - k0.X) / 3, (k0.Y + k1.Y) / 2 + 30));
            long revision = editor.Revision;
            window.MouseDown(insertAt, MouseButton.Middle);
            window.MouseUp(insertAt, MouseButton.Middle);

            Assert.Equal(revision + 1, editor.Revision);
            List<KeyAsset> keys = Y.Resolve(editor.Document.Find("nod")!)!.Keys;
            Assert.Equal(4, keys.Count);
            Assert.Equal(0.1f, keys[1].T, 3);
            Assert.Equal(expectedValue, keys[1].V, 2);
            Assert.Single(view.Selection);

            // RMB 드래그: 가로로 끌면 시각만 바뀐다.
            float valueBefore = keys[1].V;
            Point grab = InWindow(window, view, At(view, 1));
            window.MouseDown(grab, MouseButton.Right);
            window.MouseMove(new Point(grab.X + 40, grab.Y + 3), RawInputModifiers.RightMouseButton);
            window.MouseUp(new Point(grab.X + 40, grab.Y + 3), MouseButton.Right);

            keys = Y.Resolve(editor.Document.Find("nod")!)!.Keys;
            Assert.True(keys[1].T > 0.1f);
            Assert.Equal(valueBefore, keys[1].V, 4);

            // Shift+RMB: 정수 프레임에 스냅.
            view.Select(new[] { new KeyRef(Y, 1) }, additive: false);
            grab = InWindow(window, view, At(view, 1));
            window.MouseDown(grab, MouseButton.Right, RawInputModifiers.Shift);
            window.MouseMove(new Point(grab.X + 37, grab.Y), RawInputModifiers.RightMouseButton | RawInputModifiers.Shift);
            window.MouseUp(new Point(grab.X + 37, grab.Y), MouseButton.Right, RawInputModifiers.Shift);

            keys = Y.Resolve(editor.Document.Find("nod")!)!.Keys;
            double frames = keys[1].T * 24.0;
            Assert.Equal(Math.Round(frames), frames, 3);

            // 빈 자리 클릭은 커브 선택도 푼다.
            Point empty = InWindow(window, view, new Point(k0.X + 20, GraphEditorView.PadTop + 10));
            window.MouseDown(empty, MouseButton.Left);
            window.MouseUp(empty, MouseButton.Left);
            Assert.Empty(view.SelectedCurves);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 박스에_키가_들면_키만_고르고_커브는_하얘지지_않으며_핸들_끝_박스는_핸들을_골라_MMB로_돌린다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, MotionEditor editor) = Show();

        try
        {
            // 키 둘레를 박스로(커브도 지난다) → 키만 고른다. 커브 선택(하얀 선)은 없다.
            Point k1 = At(view, 1);
            Point a = InWindow(window, view, new Point(k1.X - 12, k1.Y - 12));
            Point b = InWindow(window, view, new Point(k1.X + 12, k1.Y + 12));
            window.MouseDown(a, MouseButton.Left);
            window.MouseMove(b, RawInputModifiers.LeftMouseButton);
            window.MouseUp(b, MouseButton.Left);

            var key1 = new KeyRef(Y, 1);
            Assert.Contains(key1, view.Selection);
            Assert.Empty(view.SelectedCurves);
            Assert.Empty(view.SelectedHandles);

            // 키를 고른 채 out 탄젠트 핸들 끝 둘레를 박스로 → 핸들이 고른 것이 되고 키 선택은 그대로(마야).
            KeyAsset before = Y.Resolve(editor.Document.Find("nod")!)!.Keys[1];
            // 박스는 핸들 끝을 품되, 누르는 점은 커브 선에서 떨어진 아래쪽(키 1이 골짜기라 커브는 핸들 위쪽을 지난다).
            Pixel end = HandleMath.HandleEnd(new Pixel(k1.X, k1.Y), before.OutTangent, inward: false, view.Camera);
            Point ha = InWindow(window, view, new Point(end.X - 8, end.Y + 14));
            Point hb = InWindow(window, view, new Point(end.X + 8, end.Y - 4));
            window.MouseDown(ha, MouseButton.Left);
            window.MouseMove(hb, RawInputModifiers.LeftMouseButton);
            window.MouseUp(hb, MouseButton.Left);

            Assert.Contains((key1, false), view.SelectedHandles);
            Assert.Contains(key1, view.Selection);
            Assert.Empty(view.SelectedCurves);

            // MMB 드래그 = 고른 핸들을 돌린다(편집 하나). 끊기지 않은 키라 in·out이 같이 간다. 핸들은 고른 채로 남는다.
            float outBefore = before.OutTangent;
            long revision = editor.Revision;
            Point grab = InWindow(window, view, new Point(end.X, end.Y));
            window.MouseDown(grab, MouseButton.Middle);
            window.MouseMove(new Point(grab.X, grab.Y - 20), RawInputModifiers.MiddleMouseButton);
            window.MouseUp(new Point(grab.X, grab.Y - 20), MouseButton.Middle);

            KeyAsset after = Y.Resolve(editor.Document.Find("nod")!)!.Keys[1];
            Assert.Equal(revision + 1, editor.Revision);
            Assert.NotEqual(outBefore, after.OutTangent);
            Assert.Equal(after.InTangent, after.OutTangent);
            Assert.Contains((key1, false), view.SelectedHandles);

            // 빈 자리 클릭은 핸들 선택도 푼다.
            Point empty = InWindow(window, view, new Point(At(view, 0).X + 20, GraphEditorView.PadTop + 10));
            window.MouseDown(empty, MouseButton.Left);
            window.MouseUp(empty, MouseButton.Left);
            Assert.Empty(view.SelectedHandles);
            Assert.Empty(view.Selection);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 박스_드래그가_커브를_지나면_커브가_선택되고_빈_자리_RMB_드래그는_팬이다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, _) = Show();

        try
        {
            Point k0 = At(view, 0);
            Point k1 = At(view, 1);
            var mid = new Point((k0.X + k1.X) / 2, (k0.Y + k1.Y) / 2);
            Point a = InWindow(window, view, new Point(mid.X - 10, mid.Y - 10));
            Point b = InWindow(window, view, new Point(mid.X + 10, mid.Y + 10));
            window.MouseDown(a, MouseButton.Left);
            window.MouseMove(b, RawInputModifiers.LeftMouseButton);
            window.MouseUp(b, MouseButton.Left);

            Assert.Contains(Y, view.SelectedCurves);
            Assert.Empty(view.Selection); // 상자 안에 키는 없다

            // 빈 자리 RMB 드래그 = 팬.
            GraphCamera before = view.Camera;
            Point empty = InWindow(window, view, new Point(k0.X + 20, GraphEditorView.PadTop + 10));
            window.MouseDown(empty, MouseButton.Right);
            window.MouseMove(new Point(empty.X + 50, empty.Y), RawInputModifiers.RightMouseButton);
            window.MouseUp(new Point(empty.X + 50, empty.Y), MouseButton.Right);

            Assert.True(view.Camera.TimeOrigin < before.TimeOrigin);
            Assert.Equal(before.PixelsPerSecond, view.Camera.PixelsPerSecond, 6);

            // 키를 골라도 선택 둘레 밖의 RMB 드래그는 팬이고 키는 그대로다.
            view.Select(new[] { new KeyRef(Y, 1) }, additive: false);
            GraphCamera panned = view.Camera;
            // 첫 팬으로 키 2는 화면 밖일 수 있다 — 플롯 안쪽, 선택 키(가운데)에서 먼 자리를 쓴다.
            Point far = InWindow(window, view, new Point(GraphEditorView.PadLeft + 60, GraphEditorView.PadTop + 10));
            window.MouseDown(far, MouseButton.Right);
            window.MouseMove(new Point(far.X - 40, far.Y), RawInputModifiers.RightMouseButton);
            window.MouseUp(new Point(far.X - 40, far.Y), MouseButton.Right);
            Assert.True(view.Camera.TimeOrigin > panned.TimeOrigin);

            // 선택 둘레 밖의 MMB 드래그도 팬이고 키는 그대로다(뷰포트와 같이 MMB·RMB 둘 다 — 소유자).
            float keyBefore = Y.Resolve(editor.Document.Find("nod")!)!.Keys[1].V;
            long revisionBefore = editor.Revision;
            GraphCamera beforeMiddle = view.Camera;
            window.MouseDown(far, MouseButton.Middle);
            window.MouseMove(new Point(far.X + 30, far.Y), RawInputModifiers.MiddleMouseButton);
            window.MouseUp(new Point(far.X + 30, far.Y), MouseButton.Middle);
            Assert.True(view.Camera.TimeOrigin < beforeMiddle.TimeOrigin);
            Assert.Equal(revisionBefore, editor.Revision);
            Assert.Equal(keyBefore, Y.Resolve(editor.Document.Find("nod")!)!.Keys[1].V);
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
    public void 위_눈금자_클릭은_스크럽이고_점은_키로_점프한다() => HeadlessUi.Run(() =>
    {
        (Window window, GraphEditorView view, _) = Show();

        try
        {
            Point middle = At(view, 1);
            Point ruler = InWindow(window, view, new Point(middle.X, GraphEditorView.PadTop / 2));
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
