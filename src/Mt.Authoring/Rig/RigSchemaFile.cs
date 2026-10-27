using System.Security.Cryptography;
using System.Text.Json;
using Ked.Presentation.Core;

namespace Mt.Authoring.Rig;

/// <summary>
/// 리그 덤프(<c>ExportedTuning/rig-schemas.json</c>)를 읽은 것. 툴은 리그를 코드가 아니라 데이터로 안다 —
/// 노드 이름·계층·앵커·피벗·크기는 전부 여기서 오고, 하드코딩된 노드 이름은 없다.
///
/// DTO는 코어 <c>Tuning/</c>의 것 그대로다(사본 금지). 필드라서 <see cref="JsonSerializerOptions.IncludeFields"/>가
/// 필요하고, 해석(<see cref="RigSchemaLoader"/>)은 코어가 한다.
/// </summary>
public sealed class RigSchemaFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, ICollection<string>> _nodeIds;

    private RigSchemaFile(RigSchemasFileDto dto, string sha256)
    {
        Dto = dto;
        Sha256 = sha256;
        _nodeIds = new Dictionary<string, ICollection<string>>(StringComparer.Ordinal);

        foreach (RigSchemaRigDto rig in dto.rigs)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (RigSchemaNodeDto node in rig.nodes)
            {
                ids.Add(node.id);
            }

            _nodeIds[rig.rigKind] = ids;
        }
    }

    public RigSchemasFileDto Dto { get; }

    /// <summary>파일 바이트의 SHA-256(소문자 16진수). 문서가 기억해 두고 다음에 열 때 대조한다.</summary>
    public string Sha256 { get; }

    public IEnumerable<string> RigKinds => _nodeIds.Keys;

    /// <summary>rigKind → 노드 id 집합. <c>MotionValidator</c>·<c>MotionRules</c>에 그대로 넘긴다.</summary>
    public IReadOnlyDictionary<string, ICollection<string>> NodeIds => _nodeIds;

    public bool TryGetRig(string rigKind, out RigSchemaRigDto rig)
    {
        foreach (RigSchemaRigDto candidate in Dto.rigs)
        {
            if (string.Equals(candidate.rigKind, rigKind, StringComparison.Ordinal))
            {
                rig = candidate;
                return true;
            }
        }

        rig = null!;
        return false;
    }

    /// <summary>리그 하나를 코어 트리로 세운다 — 뷰포트와 미리보기의 시작값이 여기서 나온다.</summary>
    public RectNodeTree BuildTree(string rigKind, string keyPrefix = "")
        => RigSchemaLoader.BuildTree(Dto, rigKind, keyPrefix);

    public static RigSchemaFile Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllBytes(path));
    }

    /// <summary>
    /// 덤프가 손상됐으면(rigs 없음, 노드 id 없음, 트랜스폼 필드 비어 있음) 예외다 — 0으로 떨어뜨리지 않는다.
    /// 트리를 한 번 세워 보는 것으로 코어의 필드 검사를 전부 지난다.
    /// </summary>
    public static RigSchemaFile Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        RigSchemasFileDto dto = JsonSerializer.Deserialize<RigSchemasFileDto>(bytes, Options)
            ?? throw new JsonException("빈 덤프다.");

        if (dto.rigs is null || dto.rigs.Count == 0)
        {
            throw new JsonException("덤프에 rigs가 없다. 파일이 비었거나 역직렬화가 실패했다.");
        }

        foreach (RigSchemaRigDto rig in dto.rigs)
        {
            if (rig is null || string.IsNullOrEmpty(rig.rigKind))
            {
                throw new JsonException("덤프에 rigKind가 없는 리그가 있다.");
            }

            // 손상 검사는 코어에 맡긴다 — 여기서 같은 검사를 또 짓지 않는다.
            RigSchemaLoader.BuildTree(dto, rig.rigKind);
        }

        return new RigSchemaFile(dto, ComputeSha256(bytes));
    }

    public static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
