using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Ked.Presentation.Core;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;

namespace Mt.App.Tests;

public class TimeSliderViewTests
{
    private const string Node = "CharacterPortrait_Track_Move_Y";
    private static readonly CurveRef Y = new(Node, MotionChannelKind.Position, Axis.Y);
    private const double Frame = 1.0 / 24.0;

    private static (Window Window, TimeSliderView Slider, ViewportView Viewport, MotionEditor Editor) Show()
    {
        MotionEditor editor = TestDocuments.Nod();
        var viewport = new ViewportView { Height = 200 };
        var slider = new TimeSliderView();
        var panel = new StackPanel { Children = { viewport, slider } };
        var window = new Window { Width = 900, Height = 400, Content = panel };
        window.Show();
        viewport.SetRig(RigSchemaFile.Load(TestDocuments.FixturePath("rig-schemas.json")));
        viewport.Bind(editor, "nod");
        slider.Viewport = viewport;
        slider.Bind(editor, "nod");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, slider, viewport, editor);
    }

    private static Point At(Window window, TimeSliderView slider, double seconds)
        => slider.TranslatePoint(new Point(slider.XAtTime(seconds), slider.Bounds.Height / 2), window) ?? throw new InvalidOperationException("슬라이더가 창에 없다");

    private static IReadOnlyList<KeyAsset> Keys(MotionEditor editor) => Y.Resolve(editor.Document.Find("nod")!)!.Keys;

    [Fact]
    public void 클릭은_스크럽이고_눈금은_재생_범위를_꽉_채운다() => HeadlessUi.Run(() =>
    {
        (Window window, TimeSliderView slider, ViewportView viewport, _) = Show();

        try
        {
            Assert.Equal(slider.Track.X, slider.XAtTime(viewport.Clock.RangeStart), 3);
            Assert.Equal(slider.Track.Right, slider.XAtTime(viewport.Clock.RangeEnd), 3);

            Point p = At(window, slider, 0.3);
            window.MouseDown(p, MouseButton.Left);
            window.MouseUp(p, MouseButton.Left);

            Assert.Equal(0.3, viewport.Time, 3);
            Assert.False(viewport.IsPlaying);
            Assert.Null(slider.SelectedRange);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Shift_드래그는_프레임_범위를_고르고_MMB_드래그가_범위의_키를_옮긴다() => HeadlessUi.Run(() =>
    {
        (Window window, TimeSliderView slider, _, MotionEditor editor) = Show();

        try
        {
            Point from = At(window, slider, 5 * Frame);
            Point to = At(window, slider, 10 * Frame);
            window.MouseDown(from, MouseButton.Left, RawInputModifiers.Shift);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
            window.MouseUp(to, MouseButton.Left, RawInputModifiers.Shift);

            (double start, double end) = slider.SelectedRange ?? throw new InvalidOperationException("범위가 없다");
            Assert.Equal(5 * Frame, start, 6);
            Assert.Equal(10 * Frame, end, 6);

            long before = editor.Revision;
            Point grab = At(window, slider, 7 * Frame);
            Point drop = At(window, slider, 9 * Frame);
            window.MouseDown(grab, MouseButton.Middle);
            window.MouseMove(drop, RawInputModifiers.MiddleMouseButton);
            window.MouseUp(drop, MouseButton.Middle);

            Assert.Equal(before + 1, editor.Revision);
            Assert.Equal(0.3f + 2f / 24f, Keys(editor)[1].T, 3);
            Assert.Equal(7 * Frame, slider.SelectedRange!.Value.Start, 6);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 범위_끝을_끌면_반대쪽_끝_기준으로_시간_스케일이다() => HeadlessUi.Run(() =>
    {
        (Window window, TimeSliderView slider, _, MotionEditor editor) = Show();

        try
        {
            slider.SelectedRange = (0, 0.3);
            slider.InvalidateVisual();
            Point handle = At(window, slider, 0.3 + Frame);
            Point target = At(window, slider, 0.17);
            window.MouseDown(handle, MouseButton.Left);
            window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            window.MouseUp(target, MouseButton.Left);

            IReadOnlyList<KeyAsset> keys = Keys(editor);
            Assert.Equal(3, keys.Count);
            Assert.Equal(0f, keys[0].T);
            Assert.InRange(keys[1].T, 0.12f, 0.19f); // 0.3 × 약 0.49
            Assert.Equal(0.6f, keys[2].T, 6); // 범위 밖은 그대로
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void MMB_스크럽은_포즈를_든_채_시각만_옮기고_S가_그_포즈를_키한다() => HeadlessUi.Run(() =>
    {
        (Window window, TimeSliderView slider, ViewportView viewport, MotionEditor editor) = Show();

        try
        {
            viewport.ScrubTo(0.3);
            string key = "s1/" + Node; // 씬 트리의 노드 키는 오브젝트 접두사를 갖는다
            float heldY = PoseEdit.Component(MotionChannelKind.Position, viewport.PoseAt(0.3)!.GetState(key), Axis.Y);
            float restY = PoseEdit.Component(MotionChannelKind.Position, viewport.PoseAt(0.45)!.GetState(key), Axis.Y);
            Assert.NotEqual(heldY, restY);

            Point from = At(window, slider, 0.3);
            Point to = At(window, slider, 0.45);
            window.MouseDown(from, MouseButton.Middle);
            window.MouseMove(to, RawInputModifiers.MiddleMouseButton);
            window.MouseUp(to, MouseButton.Middle);

            Assert.Equal(0.45, viewport.Time, 3);
            Assert.True(viewport.IsHoldingPose);
            Assert.True(viewport.HasPending);
            Assert.Equal(heldY, PoseEdit.Component(MotionChannelKind.Position, viewport.CurrentPose()!.GetState(key), Axis.Y), 3);

            long before = editor.Revision;
            viewport.SetKey();

            Assert.Equal(before + 1, editor.Revision);
            Assert.False(viewport.IsHoldingPose);
            IReadOnlyList<KeyAsset> keys = Keys(editor);
            Assert.Equal(4, keys.Count);
            Assert.Equal(0.45f, keys[2].T, 3);
            Assert.Equal(-18f, keys[2].V, 3);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 메뉴_명령은_범위의_키를_복사_붙여넣기_삭제_스냅하고_재생_범위를_선택으로_한다() => HeadlessUi.Run(() =>
    {
        (Window window, TimeSliderView slider, ViewportView viewport, MotionEditor editor) = Show();

        try
        {
            slider.SelectedRange = (0.3, 0.3);
            Assert.True(slider.CopyScopeKeys());
            viewport.ScrubTo(0.45);
            Assert.True(slider.PasteAtCurrentTime());
            Assert.Equal(4, Keys(editor).Count);
            Assert.Equal(-18f, Keys(editor)[2].V);

            slider.SelectedRange = (0.45, 0.45);
            Assert.True(slider.DeleteScopeKeys());
            Assert.Equal(3, Keys(editor).Count);
            Assert.False(slider.DeleteScopeKeys());

            slider.SelectedRange = (0.25, 0.35); // 0.3초 = 7.2프레임 → 7프레임
            Assert.True(slider.SnapScopeKeysToFrames());
            Assert.Equal(7f / 24f, Keys(editor)[1].T, 4);
            Assert.False(slider.SnapScopeKeysToFrames());

            slider.SelectedRange = (0.1, 0.5);
            Assert.True(slider.SetPlaybackRangeToSelection());
            Assert.Equal(0.1, viewport.Clock.RangeStart, 6);
            Assert.Equal(0.5, viewport.Clock.RangeEnd, 6);

            slider.SelectedRange = null;
            viewport.ScrubTo(7f / 24f);
            Assert.True(slider.CutScopeKeys()); // 범위가 없으면 현재 프레임
            Assert.Equal(2, Keys(editor).Count);
        }
        finally
        {
            window.Close();
        }
    });
}
