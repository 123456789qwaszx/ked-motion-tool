namespace Ked.Presentation.Core
{
    /// <summary>
    /// motion 커맨드의 mode 인자 — <c>relative</c> | <c>absolute</c>, 생략하면 relative.
    /// 파서(재생)와 리듀서(정지 프레임)가 같은 낱말을 같은 뜻으로 읽는다. 모르는 낱말은 false — 호출부가 소리 낸다.
    /// </summary>
    public static class MotionModeToken
    {
        public const MotionChannelMode Default = MotionChannelMode.Relative;

        public const string Relative = "relative";
        public const string Absolute = "absolute";

        public static bool TryParse(string token, out MotionChannelMode mode)
        {
            switch ((token ?? "").Trim().ToLowerInvariant())
            {
                case "":
                    mode = Default;
                    return true;
                case Relative:
                    mode = MotionChannelMode.Relative;
                    return true;
                case Absolute:
                    mode = MotionChannelMode.Absolute;
                    return true;
                default:
                    mode = Default;
                    return false;
            }
        }

        public static string Of(MotionChannelMode mode)
            => mode == MotionChannelMode.Absolute ? Absolute : Relative;
    }
}
