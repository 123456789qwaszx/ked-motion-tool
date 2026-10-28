using System.Reflection;
using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Rig;

/// <summary>
/// 내장 기본값 — 런타임 <c>ExportedTuning/</c>의 사본(리그 덤프·기준 해상도·깊이 커브·focus 표)과
/// <c>PresentataionUIRoot.prefab</c>에서 뽑은 무대 뼈대. 덤프 파일을 싣지 않은 문서는 이것으로 시작한다.
/// 사본은 손으로 고치지 않는다(<see cref="Source"/>에 원본을 적고, 갱신은 통째 교체).
/// </summary>
public static class BuiltinTuning
{
    /// <summary>원본 — ked-presentation-runtime 커밋과 익스포트 시각(export-report.json).</summary>
    public const string Source = "ked-presentation-runtime 4b27e23 · ExportedTuning 2026-08-21";

    private const string Prefix = "Defaults/";

    private static readonly Lazy<RigSchemaFile> RigSchemasLazy = new(() => RigSchemaFile.Parse(Bytes("ExportedTuning/rig-schemas.json")));
    private static readonly Lazy<RigSchemaFile> StageSkeletonLazy = new(() => RigSchemaFile.Parse(Bytes("stage-skeleton.json")));
    private static readonly Lazy<DepthTuningFile> DepthLazy = new(() => DepthTuningFile.Parse(Text("ExportedTuning/presets/depth.json")));
    private static readonly Lazy<FocusTuningFile> FocusLazy = new(() => FocusTuningFile.Parse(Text("ExportedTuning/presets/focus-tuning.json")));
    private static readonly Lazy<Vec2> BaseResolutionLazy = new(() => BaseResolutionFile.Parse(Text("ExportedTuning/base-resolution.json")));

    /// <summary>리그 덤프(character · background · screenEffect).</summary>
    public static RigSchemaFile RigSchemas => RigSchemasLazy.Value;

    /// <summary>무대 뼈대 — rigKind "stage" 하나. 노드 id는 유니티 이름 그대로(<c>Stage00Depth_Mid_Content</c>…), 전부 항등.</summary>
    public static RigSchemaFile StageSkeleton => StageSkeletonLazy.Value;

    public static DepthTuningFile Depth => DepthLazy.Value;

    public static FocusTuningFile Focus => FocusLazy.Value;

    public static Vec2 BaseResolution => BaseResolutionLazy.Value;

    /// <summary>뼈대 리그 DTO — <c>RigSchemaLoader.AddRigTo</c>에 바로 넣는다.</summary>
    public static RigSchemaRigDto StageRig
        => StageSkeleton.TryGetRig(StageNodes.RigKind, out RigSchemaRigDto rig) ? rig : throw new InvalidOperationException("내장 뼈대에 stage 리그가 없다.");

    private static Stream Open(string relative)
    {
        Assembly assembly = typeof(BuiltinTuning).Assembly;
        string wanted = Prefix + relative;

        // LogicalName의 구분자는 빌드 OS를 따른다 — 양쪽 다 받아 준다.
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (string.Equals(name.Replace('\\', '/'), wanted, StringComparison.Ordinal))
            {
                return assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"내장 리소스를 열 수 없다: {name}");
            }
        }

        throw new InvalidOperationException($"내장 리소스가 없다: {wanted}");
    }

    private static byte[] Bytes(string relative)
    {
        using Stream stream = Open(relative);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string Text(string relative)
    {
        using Stream stream = Open(relative);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
