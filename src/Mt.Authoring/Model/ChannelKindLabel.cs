using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 채널 종류의 화면 이름. 파일 규약(<c>motions/1</c>)은 <c>position · scale · euler</c>를 그대로 쓰고,
/// 화면만 <c>rotate</c>로 부른다(소유자 요청). 저작본·산출물 JSON에는 쓰지 않는다.
/// </summary>
public static class ChannelKindLabel
{
    public static readonly string[] All = { "position", "scale", "rotate" };

    public static string Of(MotionChannelKind kind) => kind switch
    {
        MotionChannelKind.Position => "position",
        MotionChannelKind.Scale => "scale",
        MotionChannelKind.Euler => "rotate",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
