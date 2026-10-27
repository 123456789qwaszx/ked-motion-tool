using System.Text.Json;
using Ked.Presentation.Core;

namespace Mt.Authoring.Rig;

/// <summary>
/// <c>ExportedTuning/base-resolution.json</c> — 기준 해상도. 무대 크기이자 코어 <c>RectSpace</c>의 크기.
/// 없으면 1920×1080(덤프의 <c>capturedUnderParentSize</c>와 같은 값)이고 그렇다고 말한다.
/// </summary>
public static class BaseResolutionFile
{
    public const string FileName = "base-resolution.json";

    public static readonly Vec2 Default = new(1920f, 1080f);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Vec2 Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllText(path));
    }

    public static Vec2 Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Dto dto = JsonSerializer.Deserialize<Dto>(json, Options) ?? throw new JsonException("빈 문서다.");

        if (dto.ReferenceResolution is null || !(dto.ReferenceResolution.X > 0) || !(dto.ReferenceResolution.Y > 0))
        {
            throw new JsonException("referenceResolution이 없거나 0이다.");
        }

        return new Vec2(dto.ReferenceResolution.X, dto.ReferenceResolution.Y);
    }

    /// <summary>리그 덤프 옆(같은 폴더)의 base-resolution.json 경로. 있을 때만.</summary>
    public static string? TryFindBeside(string rigSchemasPath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(rigSchemasPath));

        if (directory is null)
        {
            return null;
        }

        string candidate = Path.Combine(directory, FileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private sealed class Dto
    {
        public Float2? ReferenceResolution { get; set; }
    }

    private sealed class Float2
    {
        public float X { get; set; }
        public float Y { get; set; }
    }
}
