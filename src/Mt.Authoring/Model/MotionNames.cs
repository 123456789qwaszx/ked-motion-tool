using System.Text;
using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>모션 이름 규칙(<c>MotionRules.NameRule</c>: <c>[a-z0-9_]+</c>)을 셸·편집기가 같은 말로 다루는 자리.</summary>
public static class MotionNames
{
    public const string Fallback = "motion";

    public static bool IsValid(string? name) => !string.IsNullOrEmpty(name) && MotionRules.NameRule.IsMatch(name);

    public static string RuleMessage(string? name) => $"이름은 영문 소문자·숫자·_ 만 쓸 수 있다 ('{name}')";

    /// <summary>규칙에 맞게 고친다 — 소문자로, 허용 밖 글자는 _로, 비면 <see cref="Fallback"/>.</summary>
    public static string Sanitize(string? name)
    {
        var sb = new StringBuilder();

        foreach (char c in (name ?? "").Trim().ToLowerInvariant())
        {
            sb.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' ? c : '_');
        }

        string result = sb.ToString().Trim('_');

        while (result.Contains("__", StringComparison.Ordinal))
        {
            result = result.Replace("__", "_", StringComparison.Ordinal);
        }

        return result.Length == 0 ? Fallback : result;
    }

    /// <summary>문서에 없는 이름 — 겹치면 _2, _3…을 붙인다. <paramref name="except"/>는 제 이름(바꾸는 중)이라 겹침으로 안 본다.</summary>
    public static string Unique(MotionDocument document, string baseName, string? except = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        string candidate = baseName;
        int n = 2;

        while (document.Motions.Any(m => m.Name == candidate && m.Name != except))
        {
            candidate = $"{baseName}_{n++}";
        }

        return candidate;
    }
}
