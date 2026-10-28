using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ked.Presentation.Core;

namespace Mt.Authoring.Serialization;

/// <summary>
/// 산출물 <c>motions.json</c>(스키마 <c>motions/2</c>)의 정규 쓰기와 관용 읽기.
///
/// <b>쓰기는 바이트까지 정해져 있다</b> (무대 런타임 <c>docs/motion-contract.md</c> §7): 필드 순서 ·
/// 2칸 들여쓰기 · 한 키 한 줄 · invariant "R" · 음수 0은 0 · UTF-8(BOM 없음) · LF · 끝 개행 하나.
/// 기준 구현은 Unity <c>MotionClipExporter.ToJson</c>이고, 같은 키면 같은 바이트가 나와야 한다 —
/// 그래야 파일에 "무엇으로 만들었는지"가 남지 않는다. 등가의 심판은 재생 저장소 견본
/// (<c>Samples/Dialogue/motions.json</c>)과의 바이트 비교 테스트다.
///
/// <b>읽기는 런타임 로더(<c>MotionLibrary</c>)만큼 관용적이다</b>: 빈 성분 배열은 "없음", 토큰은
/// 대소문자·공백 무시, end는 필수(기본값 없음). 채널에 mode는 없다 — motions/1의 mode는 무시한다(뜻은 커맨드가 정한다). 어긋난 모션은 사유와 함께 건너뛴다 —
/// 조용히 빠뜨리지 않는다. 검증(<c>MotionRules</c>)은 여기서 하지 않는다; 호출부가 한다.
/// </summary>
public static class MotionsJson
{
    public static string Write(IReadOnlyList<MotionClip> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);

        var sb = new StringBuilder();
        sb.Append("{\n  \"schema\": \"").Append(AuthoringFormat.ExportSchema).Append("\",\n  \"motions\": [");

        for (int m = 0; m < clips.Count; m++)
        {
            MotionClip clip = clips[m];
            sb.Append(m > 0 ? ",\n" : "\n");
            sb.Append("    { \"name\": \"").Append(clip.Name).Append("\", \"rig\": \"").Append(clip.Rig)
              .Append("\", \"duration\": ").Append(F(clip.Duration))
              .Append(", \"end\": \"").Append(clip.End == MotionEnd.Restore ? "restore" : "commit").Append("\",\n      \"channels\": [");

            MotionChannel[] channels = clip.Channels ?? Array.Empty<MotionChannel>();

            for (int c = 0; c < channels.Length; c++)
            {
                MotionChannel ch = channels[c];
                sb.Append(c > 0 ? ",\n" : "\n");
                sb.Append("        { \"node\": \"").Append(ch.Node).Append("\", \"kind\": \"").Append(KindToken(ch.Kind)).Append('"');
                AppendComponent(sb, "x", ch.X);
                AppendComponent(sb, "y", ch.Y);
                AppendComponent(sb, "z", ch.Z);
                sb.Append(" }");
            }

            sb.Append("\n      ] }");
        }

        sb.Append("\n  ]\n}\n");
        return sb.ToString();
    }

    /// <summary>정규 바이트 — 파일에 쓰는 것은 이것이다. UTF-8, BOM 없음.</summary>
    public static byte[] WriteBytes(IReadOnlyList<MotionClip> clips)
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Write(clips));

    /// <summary>
    /// 읽는다. 깨진 JSON이면 false이고 <paramref name="problems"/>에 사유가 하나 있다.
    /// 모션 단위로 어긋난 것은 건너뛰고 사유를 쌓는다(런타임 로더와 같은 문구).
    /// </summary>
    public static bool TryRead(string json, out List<MotionClip> clips, out List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(json);

        clips = new List<MotionClip>();
        problems = new List<string>();
        FileDto? file;

        try
        {
            file = JsonSerializer.Deserialize<FileDto>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            problems.Add($"JSON을 읽지 못했다 — {e.Message}");
            return false;
        }

        if (file is null)
        {
            problems.Add("빈 문서다.");
            return false;
        }

        if (string.Equals(file.Schema, AuthoringFormat.ExportSchemaV1, StringComparison.Ordinal))
        {
            problems.Add($"schema '{file.Schema}' — 채널의 mode는 무시한다(motions/2: relative/absolute는 motion 커맨드가 정한다). 키 값은 그대로 읽는다.");
        }
        else if (!string.Equals(file.Schema, AuthoringFormat.ExportSchema, StringComparison.Ordinal))
        {
            problems.Add($"schema '{file.Schema}' (기대 '{AuthoringFormat.ExportSchema}'). 읽기는 시도한다.");
        }

        if (file.Motions is null)
        {
            return true;
        }

        foreach (MotionDto? dto in file.Motions)
        {
            if (TryConvert(dto, problems, out MotionClip? clip))
            {
                clips.Add(clip);
            }
        }

        return true;
    }

    private static bool TryConvert(MotionDto? dto, List<string> problems, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MotionClip? clip)
    {
        clip = null;

        if (dto is null)
        {
            problems.Add("모션 '(null)' 무시 — 항목이 비어 있다.");
            return false;
        }

        string name = string.IsNullOrEmpty(dto.Name) ? "(이름 없음)" : dto.Name;

        if (!TryParseEnd(dto.End, out MotionEnd end))
        {
            problems.Add($"모션 '{name}' 무시 — end는 restore|commit 이어야 한다 ('{dto.End}') — 기본값은 없다.");
            return false;
        }

        var channels = new MotionChannel[dto.Channels?.Count ?? 0];

        for (int i = 0; i < channels.Length; i++)
        {
            ChannelDto? c = dto.Channels![i];

            if (c is null)
            {
                problems.Add($"모션 '{name}' 무시 — 채널 {i}가 비어 있다.");
                return false;
            }

            if (!TryParseKind(c.Kind, out MotionChannelKind kind))
            {
                problems.Add($"모션 '{name}' 무시 — 채널 {i}: kind는 position|scale|euler 여야 한다 ('{c.Kind}').");
                return false;
            }

            channels[i] = new MotionChannel
            {
                Node = c.Node ?? "",
                Kind = kind,
                X = ToKeys(c.X),
                Y = ToKeys(c.Y),
                Z = ToKeys(c.Z),
            };
        }

        clip = new MotionClip
        {
            Name = dto.Name ?? "",
            Rig = dto.Rig ?? "",
            Duration = dto.Duration,
            End = end,
            Channels = channels,
        };

        return true;
    }

    // 없는 배열과 빈 배열은 같다: "성분 없음"(null). 런타임 로더와 같은 해석이다.
    private static CurveKey[]? ToKeys(List<KeyDto>? keys)
    {
        if (keys is null || keys.Count == 0)
        {
            return null;
        }

        var result = new CurveKey[keys.Count];

        for (int i = 0; i < result.Length; i++)
        {
            KeyDto k = keys[i];
            result[i] = new CurveKey(k.T, k.V, k.InTangent, k.OutTangent);
        }

        return result;
    }

    private static bool TryParseKind(string? token, out MotionChannelKind kind)
    {
        switch ((token ?? "").Trim().ToLowerInvariant())
        {
            case "position": kind = MotionChannelKind.Position; return true;
            case "scale": kind = MotionChannelKind.Scale; return true;
            case "euler": kind = MotionChannelKind.Euler; return true;
            default: kind = default; return false;
        }
    }

    private static bool TryParseEnd(string? token, out MotionEnd end)
    {
        switch ((token ?? "").Trim().ToLowerInvariant())
        {
            case "restore": end = MotionEnd.Restore; return true;
            case "commit": end = MotionEnd.Commit; return true;
            default: end = default; return false;
        }
    }

    private static void AppendComponent(StringBuilder sb, string axis, CurveKey[]? keys)
    {
        if (keys is null || keys.Length == 0)
        {
            return;
        }

        sb.Append(",\n          \"").Append(axis).Append("\": [");

        for (int i = 0; i < keys.Length; i++)
        {
            sb.Append(i > 0 ? ", " : " ");
            sb.Append("{ \"t\": ").Append(F(keys[i].Time)).Append(", \"v\": ").Append(F(keys[i].Value))
              .Append(", \"inTangent\": ").Append(F(keys[i].InTangent)).Append(", \"outTangent\": ").Append(F(keys[i].OutTangent)).Append(" }");
        }

        sb.Append(" ]");
    }

    private static string KindToken(MotionChannelKind kind)
        => kind switch { MotionChannelKind.Position => "position", MotionChannelKind.Scale => "scale", _ => "euler" };

    /// <summary>invariant "R" — 정수는 소수점 없이, 음수 0은 0 (규약 §7).</summary>
    public static string F(float v)
    {
        if (v == 0f)
        {
            // -0f == 0f 이므로 여기서 부호를 떨군다.
            v = 0f;
        }

        return v.ToString("R", CultureInfo.InvariantCulture);
    }

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // ── 읽기 DTO — 프로퍼티명이 곧 스키마(대소문자 무시) ─────────

    private sealed class FileDto
    {
        public string? Schema { get; set; }
        public List<MotionDto?>? Motions { get; set; }
    }

    private sealed class MotionDto
    {
        public string? Name { get; set; }
        public string? Rig { get; set; }
        public float Duration { get; set; }
        public string? End { get; set; }
        public List<ChannelDto?>? Channels { get; set; }
    }

    private sealed class ChannelDto
    {
        public string? Node { get; set; }
        public string? Kind { get; set; }
        public string? Mode { get; set; }
        public List<KeyDto>? X { get; set; }
        public List<KeyDto>? Y { get; set; }
        public List<KeyDto>? Z { get; set; }
    }

    private sealed class KeyDto
    {
        public float T { get; set; }
        public float V { get; set; }
        public float InTangent { get; set; }
        public float OutTangent { get; set; }
    }
}
