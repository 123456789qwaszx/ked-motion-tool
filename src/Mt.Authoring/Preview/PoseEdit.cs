using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Preview;

public enum ManipulatorMode
{
    /// <summary>고르기만(Q).</summary>
    Select,

    /// <summary>이동(W) — anchoredPosition.</summary>
    Move,

    /// <summary>회전(E) — localEulerAngles.z.</summary>
    Rotate,

    /// <summary>배율(R) — localScale.x·y.</summary>
    Scale,
}

/// <summary>
/// 뷰포트 매니퓰레이터의 산수 — 화면(루트 공간) 드래그를 노드 값으로, 노드 값을 채널 키 값으로.
///
/// 이동은 **부모 공간**의 변위다(anchoredPosition이 부모 기준이므로 부모가 회전·배율돼 있으면 화면 변위와 다르다).
/// 회전은 피벗 둘레의 각도 차(도, 반시계 양수 — 유니티 z 회전과 같다). 배율은 피벗까지 거리 비.
/// 키 값은 모션의 재생 기준(relative/absolute)를 따른다: relative 위치·회전은 (포즈 − 시작), relative 배율은 포즈 ÷ 시작, absolute는 포즈 그대로.
/// </summary>
public static class PoseEdit
{
    /// <summary>루트 공간 두 점의 차를 노드 부모 공간의 변위로. 루트 직속이면 루트 공간 그대로.</summary>
    public static Vec2 RootDeltaToLocal(RectNodeTree pose, string node, Vec2 rootFrom, Vec2 rootTo)
    {
        ArgumentNullException.ThrowIfNull(pose);
        string parent = pose.GetParentKey(node);

        if (parent is null)
        {
            return rootTo - rootFrom;
        }

        Vec3 a = pose.InverseTransformPoint(parent, new Vec3(rootFrom, 0f));
        Vec3 b = pose.InverseTransformPoint(parent, new Vec3(rootTo, 0f));
        return (b - a).XY;
    }

    /// <summary>피벗을 중심으로 from→to의 각도 차(도). 반시계가 양수.</summary>
    public static float AngleDelta(Vec2 pivot, Vec2 from, Vec2 to)
    {
        double a0 = Math.Atan2(from.Y - pivot.Y, from.X - pivot.X);
        double a1 = Math.Atan2(to.Y - pivot.Y, to.X - pivot.X);
        double degrees = (a1 - a0) * 180.0 / Math.PI;

        while (degrees > 180)
        {
            degrees -= 360;
        }

        while (degrees < -180)
        {
            degrees += 360;
        }

        return (float)degrees;
    }

    /// <summary>
    /// 배율 손잡이의 배율 — 팔 방향으로 끈 거리를 팔 길이로 나눠 1에 더한다(팔 하나만큼 밖으로 = ×2, 안으로 = ×0).
    /// 거리 비가 아니라 투영이라 가운데를 지나도 커지지 않고, <paramref name="min"/> 아래로는 안 내려가 뒤집히지 않는다.
    /// 팔과 직각으로 끌면 1(아무 일도 없다).
    /// </summary>
    public static float ScaleAlong(Vec2 direction, Vec2 from, Vec2 to, float armLength, float min = 0.01f)
    {
        float length = Distance(Vec2.Zero, direction);

        if (length < 1e-6f || armLength <= 0f)
        {
            return 1f;
        }

        float projected = ((to.X - from.X) * direction.X + (to.Y - from.Y) * direction.Y) / length;
        return Math.Max(min, 1f + projected / armLength);
    }

    /// <summary>포즈 값 → 채널 키 값.</summary>
    public static float KeyValueFor(MotionChannelKind kind, MotionChannelMode mode, float start, float posed)
    {
        if (mode == MotionChannelMode.Absolute)
        {
            return posed;
        }

        if (kind == MotionChannelKind.Scale)
        {
            return Math.Abs(start) < 1e-6f ? posed : posed / start;
        }

        return posed - start;
    }

    /// <summary>채널 종류의 노드 상태 성분 — 시작값·포즈값을 같은 자리에서 읽는다.</summary>
    public static float Component(MotionChannelKind kind, in RectNodeState state, Axis axis)
    {
        return kind switch
        {
            MotionChannelKind.Position => axis == Axis.X ? state.AnchoredPosition.X : state.AnchoredPosition.Y,
            MotionChannelKind.Scale => axis switch { Axis.X => state.LocalScale.X, Axis.Y => state.LocalScale.Y, _ => state.LocalScale.Z },
            MotionChannelKind.Euler => axis switch { Axis.X => state.LocalEulerAngles.X, Axis.Y => state.LocalEulerAngles.Y, _ => state.LocalEulerAngles.Z },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "모르는 채널 종류"),
        };
    }

    /// <summary>노드 상태의 성분 하나를 바꾼 상태(채널 박스 입력). <see cref="Component"/>의 반대.</summary>
    public static RectNodeState WithComponent(MotionChannelKind kind, in RectNodeState state, Axis axis, float value)
    {
        switch (kind)
        {
            case MotionChannelKind.Position:
                return state.WithAnchoredPosition(axis == Axis.X ? new Vec2(value, state.AnchoredPosition.Y) : new Vec2(state.AnchoredPosition.X, value));
            case MotionChannelKind.Scale:
            {
                Vec3 s = state.LocalScale;
                return state.WithLocalScale(axis switch { Axis.X => new Vec3(value, s.Y, s.Z), Axis.Y => new Vec3(s.X, value, s.Z), _ => new Vec3(s.X, s.Y, value) });
            }

            case MotionChannelKind.Euler:
            {
                Vec3 e = state.LocalEulerAngles;
                return state.WithLocalEuler(axis switch { Axis.X => new Vec3(value, e.Y, e.Z), Axis.Y => new Vec3(e.X, value, e.Z), _ => new Vec3(e.X, e.Y, value) });
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "모르는 채널 종류");
        }
    }

    private static float Distance(Vec2 a, Vec2 b)
        => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
