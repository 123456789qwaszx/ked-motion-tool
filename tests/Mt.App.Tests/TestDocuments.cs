using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;

namespace Mt.App.Tests;

internal static class TestDocuments
{
    public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>재생 저장소의 nod(끄덕임): Track_Move_Y 위치 relative, y 0 → -18 → 0, restore 0.6s.</summary>
    public static MotionEditor Nod()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("nod", "character", 0.6f, MotionEnd.Restore, MotionChannelMode.Relative);
        editor.AddChannel("nod", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position);
        editor.SetKeys("nod", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y, new[]
        {
            Key(0f, 0f), Key(0.3f, -18f), Key(0.6f, 0f),
        });
        return editor;
    }

    public static KeyAsset Key(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Flat };
}
