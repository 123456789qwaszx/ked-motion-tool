using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mt.Authoring.Model;

namespace Mt.Authoring.Serialization;

/// <summary>
/// 저작 원본(<c>*.motion-authoring.json</c>)의 읽기·쓰기. 산출물이 아니라 툴의 살림이므로
/// 바이트 정규성은 요구하지 않는다 — 다만 같은 문서면 같은 글자가 나와야 되돌리기 스냅샷과
/// "바뀌었나" 비교가 선다(System.Text.Json은 결정적이다).
///
/// 산출물(<c>motions/2</c>)은 <see cref="MotionsJson"/>이 따로 쓴다 — 두 포맷을 섞지 않는다.
/// 옛 판(<c>motion-authoring/1</c>)도 읽는다: 채널 <c>mode</c>는 버리고 모션 <c>defaultMode</c>는 <c>previewMode</c>가 된다 — 키 값은 그대로다.
/// </summary>
public static class MotionDocumentJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // 한글 메모가 \uXXXX로 깨져 보이지 않게.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Write(MotionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, Options) + "\n";
    }

    /// <summary>
    /// 스키마가 다르면 거부한다 — 모르는 판을 읽는 척하지 않는다.
    /// 깨진 JSON은 <see cref="JsonException"/>으로 올라간다; 호출부가 사람에게 보인다.
    /// </summary>
    public static MotionDocument Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        MotionDocument document = JsonSerializer.Deserialize<MotionDocument>(json, Options)
            ?? throw new JsonException("빈 문서다.");

        bool legacy = string.Equals(document.Schema, AuthoringFormat.AuthoringSchemaV1, StringComparison.Ordinal);

        if (!legacy && !string.Equals(document.Schema, AuthoringFormat.AuthoringSchema, StringComparison.Ordinal))
        {
            throw new JsonException(
                $"schema '{document.Schema}'는 읽을 수 없다 (기대 '{AuthoringFormat.AuthoringSchema}').");
        }

        document.Schema = AuthoringFormat.AuthoringSchema;

        // 역직렬화가 null을 남길 수 있는 목록은 빈 목록으로 — 모델의 불변 조건(목록은 null이 아니다)을 지킨다.
        document.Motions ??= new List<MotionAsset>();
        document.Scenes ??= new List<SceneAsset>();

        foreach (SceneAsset scene in document.Scenes)
        {
            scene.Objects ??= new List<SceneObject>();
            scene.Camera ??= new CameraSettings();
        }

        document.EnsureDefaultScene();

        foreach (MotionAsset motion in document.Motions)
        {
            motion.Channels ??= new List<ChannelAsset>();

            // v1의 defaultMode는 "새 채널의 mode"였다 — 지금은 뷰포트의 렌즈(previewMode)로 이어진다.
            if (motion.LegacyDefaultMode is { } defaultMode)
            {
                motion.PreviewMode = defaultMode;
                motion.LegacyDefaultMode = null;
            }

            foreach (ChannelAsset channel in motion.Channels)
            {
                Normalize(channel);
            }
        }

        return document;
    }

    private static void Normalize(ChannelAsset channel)
    {
        channel.X = NormalizeCurve(channel.X);
        channel.Y = NormalizeCurve(channel.Y);
        channel.Z = NormalizeCurve(channel.Z);
    }

    // 키가 없는 성분은 "건드리지 않는다"(null)로 — 빈 목록과 null을 두 가지 뜻으로 두지 않는다.
    private static ComponentCurve? NormalizeCurve(ComponentCurve? curve)
    {
        if (curve is null)
        {
            return null;
        }

        curve.Keys ??= new List<KeyAsset>();
        return curve.Keys.Count == 0 ? null : curve;
    }
}
