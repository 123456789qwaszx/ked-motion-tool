using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;

namespace Mt.Authoring.Tests;

internal static class Fixtures
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static byte[] BytesOf(string name) => File.ReadAllBytes(PathOf(name));

    public static string TextOf(string name) => File.ReadAllText(PathOf(name));

    /// <summary>
    /// 재생 저장소에서 Unity Animation 창으로 만들어 내보낸 그 두 모션을 편집기 명령으로 똑같이 짓는다.
    /// nod: Track_Move_Y 위치 relative, y 0 → -18 → 0 (restore 0.6s). bow: SwayPivot 회전 absolute, z 0 → 8 (commit 0.4s).
    /// bow의 x·y는 Unity가 세 성분을 묶어 기록해 상수 0으로 딸려 나온 것이고 absolute라 파일에 남는다.
    /// </summary>
    public static MotionDocument NodBowDocument()
    {
        var editor = new MotionEditor(new MotionDocument());

        editor.AddMotion("nod", "character", 0.6f, MotionEnd.Restore, MotionChannelMode.Relative);
        editor.AddChannel("nod", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position);
        editor.SetKeys("nod", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y, new[]
        {
            Key(0f, 0f), Key(0.3f, -18f), Key(0.6f, 0f),
        });

        editor.AddMotion("bow", "character", 0.4f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("bow", "CharacterPortrait_SwayPivot", MotionChannelKind.Euler);
        editor.SetKeys("bow", "CharacterPortrait_SwayPivot", MotionChannelKind.Euler, Axis.X, new[] { Key(0f, 0f), Key(0.4f, 0f) });
        editor.SetKeys("bow", "CharacterPortrait_SwayPivot", MotionChannelKind.Euler, Axis.Y, new[] { Key(0f, 0f), Key(0.4f, 0f) });
        editor.SetKeys("bow", "CharacterPortrait_SwayPivot", MotionChannelKind.Euler, Axis.Z, new[] { Key(0f, 0f), Key(0.4f, 8f) });

        return editor.Document;
    }

    public static KeyAsset Key(float t, float v, float inTangent = 0f, float outTangent = 0f) => new()
    {
        T = t,
        V = v,
        InTangent = inTangent,
        OutTangent = outTangent,
        Tangent = TangentMode.Flat,
    };
}
