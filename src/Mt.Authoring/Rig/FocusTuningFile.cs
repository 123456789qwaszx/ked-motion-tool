using System.Text.Json;
using Ked.Presentation.Core;

namespace Mt.Authoring.Rig;

/// <summary>
/// <c>presets/focus.json</c>(ExportedTuning) — focus 오프셋 표: baseOffsets + 배역(characterKey)별 entries. 툴은 배역을 두지 않으므로 base + bust만 쓴다.
/// 깊이·place 폴드의 focus 보존에 배역 키 없이(빈 문자열) 넘긴다(코어 <see cref="FocusOffsetMath"/>).
/// 덤프 옆 <c>presets/focus.json</c> → <c>focus.json</c> → <c>presets/focus-tuning.json</c> 순으로 찾는다.
/// </summary>
public sealed class FocusTuningFile
{
    public static readonly string[] FileNames = { "focus.json", "focus-tuning.json" };

    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private FocusTuningFile(FocusTuningBodyDto body)
    {
        Body = body;
    }

    /// <summary>코어 폴드에 그대로 넘기는 표.</summary>
    public FocusTuningBodyDto Body { get; }

    public static FocusTuningFile Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllText(path));
    }

    public static FocusTuningFile Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        FocusTuningFileDto dto = JsonSerializer.Deserialize<FocusTuningFileDto>(json, Options) ?? throw new JsonException("빈 focus.json이다.");
        FocusTuningBodyDto body = dto.MonoBehaviour ?? throw new JsonException("focus.json에 MonoBehaviour가 없다.");
        body.entries ??= new List<FocusEntryDto>();
        return new FocusTuningFile(body);
    }

    /// <summary>덤프 옆에서 찾는다. 없으면 null.</summary>
    public static string? TryFindBeside(string rigSchemasPath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(rigSchemasPath));

        if (directory is null)
        {
            return null;
        }

        foreach (string name in FileNames)
        {
            foreach (string candidate in new[] { Path.Combine(directory, "presets", name), Path.Combine(directory, name) })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
