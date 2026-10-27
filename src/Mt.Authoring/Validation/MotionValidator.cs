using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Validation;

/// <summary>검증 문제 하나. 모션 이름이 비어 있으면 문서 수준의 문제다.</summary>
public sealed record MotionProblem(string MotionName, string Message)
{
    public override string ToString()
        => string.IsNullOrEmpty(MotionName) ? Message : $"'{MotionName}': {Message}";
}

/// <summary>
/// 편집 중 계속 돌리는 검증. 규칙은 코어 <see cref="MotionRules"/> 한 벌뿐이다 — 런타임 로더와 Unity 내보내기가
/// 같은 규칙을 쓰므로 "툴에서는 되는데 재생에서 사라지는" 모션이 안 생긴다. 문구도 코어의 것을 그대로 보인다.
///
/// 여기서 더 보는 것은 문서 수준뿐이다: 같은 이름 둘, 리그 덤프에 없는 rigKind.
/// </summary>
public static class MotionValidator
{
    /// <param name="rigNodeIds">rigKind → 노드 id 목록. null이면 노드 존재 검사를 생략한다(덤프가 아직 없는 자리).</param>
    public static IReadOnlyList<MotionProblem> Validate(
        MotionDocument document,
        IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var problems = new List<MotionProblem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (MotionAsset motion in document.Motions)
        {
            if (!string.IsNullOrEmpty(motion.Name) && !seen.Add(motion.Name))
            {
                problems.Add(new MotionProblem(motion.Name, "같은 이름이 둘이다"));
            }

            problems.AddRange(ValidateMotion(motion, rigNodeIds));
        }

        return problems;
    }

    public static IReadOnlyList<MotionProblem> ValidateMotion(
        MotionAsset motion,
        IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds = null)
    {
        ArgumentNullException.ThrowIfNull(motion);

        var problems = new List<MotionProblem>();
        ICollection<string>? nodeIds = null;

        if (rigNodeIds is not null)
        {
            if (!rigNodeIds.TryGetValue(motion.Rig ?? "", out nodeIds))
            {
                problems.Add(new MotionProblem(
                    motion.Name,
                    $"리그 덤프에 rigKind '{motion.Rig}'가 없다. 있는 것: [{string.Join(", ", rigNodeIds.Keys)}]"));
                return problems;
            }
        }

        MotionClip clip = MotionAssetCodec.ToClip(motion);

        if (!MotionRules.TryValidate(clip, nodeIds, out string reason))
        {
            problems.Add(new MotionProblem(motion.Name, reason));
        }

        return problems;
    }
}
