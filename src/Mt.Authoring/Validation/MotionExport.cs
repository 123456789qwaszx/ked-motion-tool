using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Serialization;

namespace Mt.Authoring.Validation;

/// <summary>
/// 문서 → <c>motions.json</c>. 막는 문제가 0건일 때만 나간다 — 런타임이 로그와 함께 버릴 모션을 파일에 싣지 않는다. 경고(튐)는 막지 않는다.
/// </summary>
public static class MotionExport
{
    /// <param name="filter">내보낼 모션 고르기(null이면 전부). 고른 모션의 문제만 막는다 — 문서 수준 문제(빈 이름)는 언제나 막는다.</param>
    public static bool TryExport(
        MotionDocument document,
        IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds,
        out string json,
        out IReadOnlyList<MotionProblem> problems,
        Func<MotionAsset, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<MotionAsset> selected = document.Motions.Where(m => filter is null || filter(m)).ToList();
        var names = new HashSet<string>(selected.Select(m => m.Name), StringComparer.Ordinal);
        problems = MotionValidator.Validate(document, rigNodeIds)
            .Where(p => p.Blocking && (string.IsNullOrEmpty(p.MotionName) || names.Contains(p.MotionName)))
            .ToList();

        if (problems.Count > 0)
        {
            json = "";
            return false;
        }

        json = MotionsJson.Write(selected.Select(MotionAssetCodec.ToClip).ToList());
        return true;
    }

    /// <summary>검증 없이 바꾼다 — 미리보기·테스트용. 파일로 내보내는 길은 <see cref="TryExport"/>다.</summary>
    public static List<MotionClip> ToClips(MotionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Motions.Select(MotionAssetCodec.ToClip).ToList();
    }
}
