using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 저작 원본 한 벌 (<c>*.motion-authoring.json</c>, 스키마 <see cref="AuthoringFormat.AuthoringSchema"/>).
///
/// 산출물(<c>motions/1</c>)에 들어가지 않는 제작 정보 — 탄젠트 모드·끊김·모션 기본 mode·메모·미리보기 —를
/// 함께 든다. 내보내기는 그것을 벗기고 <see cref="MotionAsset"/>을 코어 <see cref="MotionClip"/>으로 바꾼다.
///
/// 전부 가변 클래스다. 편집은 <see cref="Editing.MotionEditor"/>만 하고, 되돌리기는 문서 전체 스냅샷이다
/// (VnTool <c>ProjectEditor</c>의 방식) — 명령마다 역연산을 짓지 않는다.
/// </summary>
public sealed class MotionDocument
{
    public string Schema { get; set; } = AuthoringFormat.AuthoringSchema;

    /// <summary>리그 덤프의 출처. 열 때 해시가 다르면 "리그가 바뀌었다"를 알린다 — 조용히 맞는 척하지 않는다.</summary>
    public RigSchemasReference? RigSchemas { get; set; }

    /// <summary>기준 해상도 덤프(<c>base-resolution.json</c>)의 경로. 뷰포트가 쓴다. v1에서는 없어도 된다.</summary>
    public FileReference? BaseResolution { get; set; }

    public PreviewSettings? Preview { get; set; }

    public List<MotionAsset> Motions { get; set; } = new();

    public MotionAsset? Find(string name)
        => Motions.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));
}

public sealed class RigSchemasReference
{
    /// <summary>문서 파일 기준 상대 경로 또는 절대 경로. 구분자는 <c>/</c>.</summary>
    public string Path { get; set; } = "";

    /// <summary>파일 바이트의 SHA-256, 소문자 16진수. 비어 있으면 아직 검증한 적이 없다.</summary>
    public string Sha256 { get; set; } = "";
}

public sealed class FileReference
{
    public string Path { get; set; } = "";
}

public sealed class PreviewSettings
{
    /// <summary>뷰포트에 세울 초상화 이미지. 산출물과 무관한 미리보기 전용.</summary>
    public string? Portrait { get; set; }
}

/// <summary>모션 하나의 저작본. 수치는 코어 <see cref="MotionClip"/>과 같고, 제작 정보만 더 든다.</summary>
public sealed class MotionAsset
{
    public string Name { get; set; } = "";

    /// <summary>리그 덤프의 rigKind (예: character).</summary>
    public string Rig { get; set; } = "";

    /// <summary>초.</summary>
    public float Duration { get; set; }

    public MotionEnd End { get; set; } = MotionEnd.Restore;

    /// <summary>새 채널의 mode 기본값. 파일(산출물)에는 채널마다 명시돼 나간다 — 이 값은 저작 편의다.</summary>
    public MotionChannelMode DefaultMode { get; set; } = MotionChannelMode.Relative;

    public List<ChannelAsset> Channels { get; set; } = new();

    public string? Notes { get; set; }

    public ChannelAsset? FindChannel(string node, MotionChannelKind kind)
        => Channels.FirstOrDefault(c => string.Equals(c.Node, node, StringComparison.Ordinal) && c.Kind == kind);
}

/// <summary>노드 하나의 속성 하나. 성분이 null이면 "건드리지 않는다"(코어와 같은 뜻).</summary>
public sealed class ChannelAsset
{
    public string Node { get; set; } = "";
    public MotionChannelKind Kind { get; set; }
    public MotionChannelMode Mode { get; set; }

    public ComponentCurve? X { get; set; }
    public ComponentCurve? Y { get; set; }
    public ComponentCurve? Z { get; set; }

    public ComponentCurve? Get(Axis axis) => axis switch
    {
        Axis.X => X,
        Axis.Y => Y,
        Axis.Z => Z,
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "모르는 축"),
    };

    public void Set(Axis axis, ComponentCurve? curve)
    {
        switch (axis)
        {
            case Axis.X: X = curve; break;
            case Axis.Y: Y = curve; break;
            case Axis.Z: Z = curve; break;
            default: throw new ArgumentOutOfRangeException(nameof(axis), axis, "모르는 축");
        }
    }
}

public enum Axis
{
    X,
    Y,
    Z,
}

/// <summary>성분 하나의 키 목록. 시각 오름차순을 편집기가 지킨다.</summary>
public sealed class ComponentCurve
{
    public List<KeyAsset> Keys { get; set; } = new();
}

/// <summary>
/// 키 하나. 수치 넷은 코어 <see cref="CurveKey"/>와 1:1이고, <see cref="Tangent"/>·<see cref="Broken"/>은
/// 그래프 편집기의 손잡이 상태다 — 산출물에는 탄젠트 **값**만 나간다.
/// </summary>
public sealed class KeyAsset
{
    /// <summary>초.</summary>
    public float T { get; set; }

    public float V { get; set; }
    public float InTangent { get; set; }
    public float OutTangent { get; set; }

    /// <summary>탄젠트를 누가 정하는가. 가져온 키는 <see cref="TangentMode.Free"/>다 — 값을 그대로 지킨다.</summary>
    public TangentMode Tangent { get; set; } = TangentMode.Free;

    /// <summary>in·out 탄젠트가 따로 노는가(마야의 break tangents).</summary>
    public bool Broken { get; set; }

    public CurveKey ToCurveKey() => new(T, V, InTangent, OutTangent);

    public static KeyAsset FromCurveKey(in CurveKey key) => new()
    {
        T = key.Time,
        V = key.Value,
        InTangent = key.InTangent,
        OutTangent = key.OutTangent,
        Tangent = TangentMode.Free,
        Broken = key.InTangent != key.OutTangent,
    };
}

/// <summary>마야의 탄젠트 종류 중 v1이 아는 것. auto·flat·linear의 산수는 M2(그래프 편집기)에서 온다.</summary>
public enum TangentMode
{
    Auto,
    Flat,
    Linear,
    Free,
}
