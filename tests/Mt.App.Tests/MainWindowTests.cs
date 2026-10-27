using Avalonia.Controls;
using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Serialization;

namespace Mt.App.Tests;

public class MainWindowTests
{
    [Fact]
    public void 주_창이_헤드리스에서_열리고_그려진다()
    {
        HeadlessUi.Run(() =>
        {
            Assert.True(HeadlessUi.OnUiThread);

            var window = new MainWindow();
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
            var window = new MainWindow();
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
            var window = new MainWindow();
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
                Assert.Equal(0.6, window.Viewport.Time, 9);
                Assert.Equal(2, window.Graph.CurrentTime, 9);
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
            var window = new MainWindow();
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
