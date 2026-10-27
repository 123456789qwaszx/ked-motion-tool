using System;

namespace Ked.Presentation.Core
{
    public static class SlideMotion
    {
        public enum Kind
        {
            // 현재 위치가 <b>도착점</b>. 정착 상태는 항등.
            In,

            // 현재 위치가 <b>출발점</b>. 정착 상태가 방향 × 거리만큼 움직인다.
            Out,
        }

        public const string InCommand = "slide_in";
        public const string OutCommand = "slide_out";

        public static Kind? KindOf(string outputCommand)
        {
            switch (outputCommand)
            {
                case InCommand: return Kind.In;
                case OutCommand: return Kind.Out;
                default: return null;
            }
        }

        public static string DefaultDirection(Kind kind)
            => kind == Kind.In ? DefaultInDirection : DefaultOutDirection;

        public static EaseKind EaseOf(Kind kind) => kind == Kind.In ? InEase : OutEase;

        public static float PunchPixels(Kind kind) => kind == Kind.In ? InPunchPixels : OutPunchPixels;

        public static float Punch(Kind kind, float eased)
            => kind == Kind.In 
                ? PunchTowardEnd(eased) 
                : PunchFromStart(eased);

        public const string DefaultInDirection = "left";
        public const string DefaultOutDirection = "right";

        public const string DefaultDistanceToken = "12u";

        public const EaseKind InEase = EaseKind.OutCubic;
        public const EaseKind OutEase = EaseKind.InCubic;
        
        public const float InPunchPixels = 24f;
        // OverShoot
        public const float OutPunchPixels = 14f;

        public static Vec2 DirectionVector(string direction)
        {
            switch (direction?.Trim().ToLowerInvariant())
            {
                case "right":
                case "r":
                    return new Vec2(+1f, 0f);

                case "up":
                case "u":
                case "top":
                case "t":
                    return new Vec2(0f, +1f);

                case "down":
                case "d":
                case "bottom":
                case "b":
                    return new Vec2(0f, -1f);

                default:
                    return new Vec2(-1f, 0f);
            }
        }

        public static float PunchTowardEnd(float eased)
        {
            eased = Clamp01(eased);

            return (float)Math.Sin(Math.PI * eased) * (eased * eased);
        }

        public static float PunchFromStart(float eased)
        {
            eased = Clamp01(eased);

            float oneMinus = 1f - eased;

            return (float)Math.Sin(Math.PI * eased) * (oneMinus * oneMinus);
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}