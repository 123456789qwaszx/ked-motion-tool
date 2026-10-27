using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Graph;

/// <summary>모션 안의 성분 커브 하나를 가리킨다 — 노드 × 종류 × 축. 그래프·도프시트·인스펙터가 같은 손잡이를 쓴다.</summary>
public readonly record struct CurveRef(string Node, MotionChannelKind Kind, Axis Axis)
{
    public override string ToString() => $"{Node}/{Kind}.{Axis}";

    public ComponentCurve? Resolve(MotionAsset motion)
        => motion.FindChannel(Node, Kind)?.Get(Axis);
}

/// <summary>커브 안의 키 하나. Index는 시각 오름차순 자리라 편집 뒤에는 다시 풀어야 한다.</summary>
public readonly record struct KeyRef(CurveRef Curve, int Index)
{
    public KeyAsset? Resolve(MotionAsset motion)
    {
        ComponentCurve? curve = Curve.Resolve(motion);
        return curve is not null && Index >= 0 && Index < curve.Keys.Count ? curve.Keys[Index] : null;
    }
}
