using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Serialization;

namespace Mt.Authoring.Validation;

/// <summary>
/// 문서 → <c>motions.json</c>. 문제가 0건일 때만 나간다 — 런타임이 로그와 함께 버릴 모션을 파일에 싣지 않는다.
/// </summary>
public static class MotionExport
{
    public static bool TryExport(
        MotionDocument document,
        IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds,
        out string json,
        out IReadOnlyList<MotionProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(document);

        problems = MotionValidator.Validate(document, rigNodeIds);

        if (problems.Count > 0)
        {
            json = "";
            return false;
        }

        json = MotionsJson.Write(ToClips(document));
        return true;
    }

    /// <summary>검증 없이 바꾼다 — 미리보기·테스트용. 파일로 내보내는 길은 <see cref="TryExport"/>다.</summary>
    public static List<MotionClip> ToClips(MotionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Motions.Select(MotionAssetCodec.ToClip).ToList();
    }
}
