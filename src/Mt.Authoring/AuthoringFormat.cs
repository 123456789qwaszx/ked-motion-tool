namespace Mt.Authoring;

/// <summary>
/// 저작 원본(<c>*.motion-authoring.json</c>)과 산출물(<c>motions.json</c>)의 스키마 이름.
///
/// 산출물 스키마는 무대 런타임의 <c>docs/motion-contract.md</c>가 정본이고 이 툴은 그것을
/// 따를 뿐이다. 저작 원본 스키마는 이 툴의 것이다 — 탄젠트 모드·미리보기 초상화 등
/// 산출물에 들어가지 않는 정보를 담는다 (PLAN §3).
/// </summary>
public static class AuthoringFormat
{
    /// <summary>저작 원본 파일의 스키마.</summary>
    public const string AuthoringSchema = "motion-authoring/1";

    /// <summary>내보내기 산출물의 스키마 — 무대 런타임 <c>MotionLibrary</c>가 읽는 그것.</summary>
    public const string ExportSchema = "motions/1";

    /// <summary>저작 원본 파일 확장자.</summary>
    public const string AuthoringExtension = ".motion-authoring.json";
}
