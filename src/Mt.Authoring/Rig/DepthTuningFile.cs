using System.Text.Json;
using Ked.Presentation.Core;

namespace Mt.Authoring.Rig;

/// <summary>
/// <c>presets/depth.json</c>(ExportedTuning) — 깊이의 유일한 진실인 level 커브. 레이어 라벨(far…close)은
/// 코어 <see cref="DepthLevelLabels"/>가 커브 위 눈금으로 바꾼다. 덤프 옆 <c>presets/depth.json</c> 또는 <c>depth.json</c>에서 찾는다.
/// </summary>
public sealed class DepthTuningFile
{
    public const string FileName = "depth.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private DepthTuningFile(DepthLevelTuningDto level)
    {
        Level = level;
    }

    public DepthLevelTuningDto Level { get; }

    public static DepthTuningFile Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllText(path));
    }

    public static DepthTuningFile Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        DepthTuningFileDto dto = JsonSerializer.Deserialize<DepthTuningFileDto>(json, Options) ?? throw new JsonException("빈 depth.json이다.");
        DepthLevelTuningDto? level = dto.MonoBehaviour?.level;

        if (level is null || !level.TryResolve(DepthLevelLabels.Mid, out _, out _))
        {
            throw new JsonException("depth.json에 level 커브(yCurve·scaleCurve)가 없다.");
        }

        return new DepthTuningFile(level);
    }

    /// <summary>덤프 옆 <c>presets/depth.json</c> → <c>depth.json</c> 순으로 찾는다. 없으면 null.</summary>
    public static string? TryFindBeside(string rigSchemasPath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(rigSchemasPath));

        if (directory is null)
        {
            return null;
        }

        foreach (string candidate in new[] { Path.Combine(directory, "presets", FileName), Path.Combine(directory, FileName) })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>level(0 far … 20 close)의 끝 — 깊이 컨트롤러 슬라이더의 범위.</summary>
    public const float MinLevel = DepthLevelLabels.Far;

    public const float MaxLevel = DepthLevelLabels.Close;

    /// <summary>level → DepthScale(커브, 런타임과 같은 하한). 커브가 없으면 false.</summary>
    public bool TryScaleOf(float level, out float depthScale) => Level.TryResolve(level, out _, out depthScale);

    /// <summary>
    /// level → (DepthY, DepthScale). Y는 런타임 <c>size</c>의 focus 보존(<see cref="SetDepthStageReduction"/>)이 정지 자세에서
    /// 남기는 값 <c>−focusY × (배율 − 1)</c>이다 — yCurve는 보존에 상쇄되므로 쓰지 않는다. 얼굴(focus 점)은 제자리,
    /// 발은 멀수록 위로(사람 시야의 "먼 것은 위에"). <paramref name="focusY"/>는 보존할 focus 점의 높이(bust 기본 820).
    /// </summary>
    public bool TryDepthOf(float level, float focusY, out float depthY, out float depthScale)
    {
        if (!TryScaleOf(level, out depthScale))
        {
            depthY = 0f;
            return false;
        }

        depthY = -focusY * (depthScale - 1f);
        return true;
    }

    /// <summary>깊이 Y 굽기가 보존하는 focus 프리셋 — 런타임 size 브리지의 기본값과 같다.</summary>
    public const string PreservedFocus = "bust";

    /// <summary>깊이 Y 굽기가 보존하는 focus 점의 높이 — focus 표의 bust 기본 오프셋(캐릭터별 오프셋 없이, 어느 캐릭터에도 같게). 표가 없으면 0(Y를 안 움직인다).</summary>
    public static float FocusHeightOf(FocusTuningFile? focus)
        => focus is null ? 0f : FocusOffsetMath.Resolve(focus.Body, "", PreservedFocus, Vec2.Zero).Y;

    /// <summary>
    /// DepthScale → level — 깊이 컨트롤러가 포즈의 배율을 슬라이더 값으로 되돌릴 때. 커브가 단조라는 가정으로
    /// [MinLevel, MaxLevel]을 이분한다. 범위 밖 배율은 끝으로 잘린다.
    /// </summary>
    public bool TryLevelOf(float depthScale, out float level)
    {
        level = DepthLevelLabels.Mid;

        if (!TryScaleOf(MinLevel, out float low) || !TryScaleOf(MaxLevel, out float high))
        {
            return false;
        }

        bool increasing = high >= low;
        float lo = MinLevel;
        float hi = MaxLevel;

        for (int i = 0; i < 48; i++)
        {
            float mid = (lo + hi) * 0.5f;
            TryScaleOf(mid, out float at);

            if ((at < depthScale) == increasing)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        level = (lo + hi) * 0.5f;
        return true;
    }

    /// <summary>레이어 라벨 → (DepthY, DepthScale). 모르는 라벨이거나 커브가 없으면 false.</summary>
    public bool TryResolveLayer(string? layer, out float depthY, out float depthScale)
    {
        depthY = 0f;
        depthScale = 1f;
        return layer is not null && DepthLevelLabels.TryGetLevel(layer, out float level) && Level.TryResolve(level, out depthY, out depthScale);
    }
}
