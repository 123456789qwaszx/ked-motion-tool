using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Validation;

/// <summary>
/// 검증 문제 하나. 모션 이름이 비어 있으면 문서 수준의 문제다.
/// <see cref="Blocking"/>이 false면 **경고**다 — 내보내기를 막지 않는다(규칙이 아니라 "튄다"는 알림).
/// </summary>
public sealed record MotionProblem(string MotionName, string Message, bool Blocking = true)
{
    public override string ToString()
        => string.IsNullOrEmpty(MotionName) ? Message : $"'{MotionName}': {Message}";
}

/// <summary>
/// 편집 중 계속 돌리는 검증. 규칙은 코어 <see cref="MotionRules"/> 한 벌뿐이다 — 런타임 로더와 Unity 내보내기가
/// 같은 규칙을 쓰므로 "툴에서는 되는데 재생에서 사라지는" 모션이 안 생긴다. 문구도 코어의 것을 그대로 보인다.
///
/// 여기서 더 보는 것은 문서 수준(같은 이름 둘, 리그 덤프에 없는 rigKind)과 **경고** 하나다:
/// restore인데 마지막 키가 중립값이 아니면 재생이 끝에서 시작값을 되써 튄다 — 규칙(motions/2)은 막지 않으므로
/// 경고로만 보이고 내보내기는 된다. 첫 키가 중립값이 아닌 것은 알리지 않는다 — "지금 값 +140에서 시작"은 뜻이다.
/// </summary>
public static class MotionValidator
{
    /// <summary>컨트롤러 경고의 문구 머리 — 돕는 손이 이것으로 고른다.</summary>
    public const string AbsoluteControllerWarning = "absolute로 재생하는 컨트롤러";

    /// <param name="rigNodeIds">rigKind → 노드 id 목록. null이면 노드 존재 검사를 생략한다(덤프가 아직 없는 자리).</param>
    /// <param name="controllers">컨트롤러 표. 주면 자리·깊이(absolute) 컨트롤러가 든 relative 미리보기 모션에 경고한다.</param>
    public static IReadOnlyList<MotionProblem> Validate(
        MotionDocument document,
        IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds = null,
        ControllersFile? controllers = null)
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

            problems.AddRange(ValidateMotion(motion, rigNodeIds, controllers));
        }

        return problems;
    }

    public static IReadOnlyList<MotionProblem> ValidateMotion(
        MotionAsset motion,
        IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds = null,
        ControllersFile? controllers = null)
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

        // 경고는 모양 규칙과 무관하게 본다 — 고치는 순서를 작가가 고른다.

        if (motion.End == MotionEnd.Restore)
        {
            for (int i = 0; i < motion.Channels.Count; i++)
            {
                ChannelAsset channel = motion.Channels[i];
                float neutral = MotionMath.Neutral(channel.Kind);

                foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
                {
                    if (channel.Get(axis) is { Keys.Count: > 0 } curve && Math.Abs(curve.Keys[^1].V - neutral) > MotionRules.Tolerance)
                    {
                        problems.Add(new MotionProblem(
                            motion.Name,
                            $"채널 {i}: {axis}: restore인데 마지막 키 값이 중립값 {neutral}이 아니다 ({curve.Keys[^1].V}) — 끝에서 시작값으로 튄다. 남기려면 commit",
                            Blocking: false));
                    }
                }
            }
        }

        // 자리·깊이 컨트롤러는 absolute로 재생한다(결정 D1·그룹 mode). relative 미리보기면 자리가 누적돼 보인다 — 경고.
        if (motion.PreviewMode == MotionChannelMode.Relative && controllers?.Rig(motion.Rig) is { } table)
        {
            List<string> absolute = AbsoluteControllersIn(motion, table).Select(c => $"{c.Group.Label} · {c.Label}").Distinct().ToList();

            if (absolute.Count > 0)
            {
                problems.Add(new MotionProblem(
                    motion.Name,
                    $"{AbsoluteControllerWarning}({string.Join(", ", absolute)})가 있다 — 재생 기준이 상대값(relative)이면 자리가 누적돼 보인다. 재생은 <<motion slot {motion.Name} absolute>>로. 연기와 섞였으면 두 모션으로 나눈다",
                    Blocking: false));
            }
        }

        return problems;
    }

    /// <summary>모션의 채널 중 absolute 묶음(자리·깊이)의 컨트롤러로 풀리는 것들.</summary>
    public static IEnumerable<Controller> AbsoluteControllersIn(MotionAsset motion, ControllerRig table)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(table);

        foreach (ChannelAsset channel in motion.Channels)
        {
            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is not null && table.TryResolve(new CurveRef(channel.Node, channel.Kind, axis), out Controller c, out _) && c.Group.Mode == MotionChannelMode.Absolute)
                {
                    yield return c;
                    break;
                }
            }
        }
    }

    /// <summary>채널이 absolute 묶음의 컨트롤러인가 — 두 모션으로 나눌 때의 기준.</summary>
    public static bool IsAbsoluteController(ChannelAsset channel, ControllerRig table)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(table);

        foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
        {
            if (table.TryResolve(new CurveRef(channel.Node, channel.Kind, axis), out Controller c, out _))
            {
                return c.Group.Mode == MotionChannelMode.Absolute;
            }
        }

        return false;
    }

    /// <summary>내보내기를 막는 것만.</summary>
    public static IEnumerable<MotionProblem> Blocking(IEnumerable<MotionProblem> problems)
        => problems.Where(p => p.Blocking);
}
