using System.Text.Json;
using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;

namespace Mt.Authoring.Rig;

/// <summary>값을 어떻게 굽는가 — 컨트롤러의 값이 노드 값 그대로가 아닐 때.</summary>
public enum ControllerBake
{
    /// <summary>값 = 노드 성분 그대로.</summary>
    None,

    /// <summary>값 = 깊이 level(0 far … 20 close). <c>depth.json</c> 커브로 배율을 구해 노드의 X·Y 배율에 같이 쓴다.</summary>
    DepthLevel,
}

/// <summary>컨트롤러 묶음 — 자리 · 깊이 · 연기. <see cref="Mode"/>는 이 묶음의 모션을 재생할 때 권장하는 mode다.</summary>
public sealed record ControllerGroup(string Id, string Label, MotionChannelMode Mode, string? Note);

/// <summary>옛 모션·축별 노드가 같은 손잡이로 보이게 하는 별칭 — (노드, 성분) → 컨트롤러의 같은 성분.</summary>
public sealed record ControllerAlias(string Node, Axis Axis);

/// <summary>
/// 컨트롤러 하나 — 이름 → (노드, 종류, 성분) 고정 대응. 애니메이터가 보는 것은 이것이고, 키는 노드 채널에 그대로 저장된다
/// (규약 <c>motions/2</c>는 안 바뀐다). 리거의 지식이라 리그 덤프 옆 <c>presets/controllers.json</c>에 산다.
/// </summary>
public sealed class Controller
{
    public Controller(
        string id, ControllerGroup group, string label, string node, MotionChannelKind kind, IReadOnlyList<Axis> axes,
        string unit, IReadOnlyList<ControllerAlias> aliases, ControllerBake bake, bool manipulator, string? note)
    {
        Id = id;
        Group = group;
        Label = label;
        Node = node;
        Kind = kind;
        Axes = axes;
        Unit = unit;
        Aliases = aliases;
        Bake = bake;
        Manipulator = manipulator;
        Note = note;
    }

    public string Id { get; }

    public ControllerGroup Group { get; }

    public string Label { get; }

    /// <summary>키가 들어가는 노드(스키마 id).</summary>
    public string Node { get; }

    public MotionChannelKind Kind { get; }

    public IReadOnlyList<Axis> Axes { get; }

    /// <summary>px · deg · x(배) · sign · level — 화면 표기용.</summary>
    public string Unit { get; }

    public IReadOnlyList<ControllerAlias> Aliases { get; }

    public ControllerBake Bake { get; }

    /// <summary>W/E/R 매니퓰레이터가 그 종류의 기본 손잡이로 고르는 컨트롤러인가.</summary>
    public bool Manipulator { get; }

    public string? Note { get; }

    /// <summary>성분 하나의 표시 이름 — 성분이 하나면 컨트롤러 이름 그대로("숙임"), 여럿이면 "몸 X".</summary>
    public string AttributeLabel(Axis axis)
        => Axes.Count == 1 ? Label : $"{Label} {axis.ToString().ToUpperInvariant()}";

    /// <summary>이 컨트롤러 성분의 주 커브(키를 새로 놓을 자리).</summary>
    public CurveRef Primary(Axis axis) => new(Node, Kind, axis);

    /// <summary>이 성분으로 읽히는 커브 전부 — 주 노드와 별칭 노드. 표시는 합치고, 키는 있는 채널에 놓는다.</summary>
    public IEnumerable<CurveRef> CurvesOf(Axis axis)
    {
        yield return Primary(axis);

        foreach (ControllerAlias alias in Aliases)
        {
            if (alias.Axis == axis)
            {
                yield return new CurveRef(alias.Node, Kind, axis);
            }
        }
    }
}

/// <summary>리그 하나의 컨트롤러 표.</summary>
public sealed class ControllerRig
{
    private readonly Dictionary<(string Node, MotionChannelKind Kind, Axis Axis), (Controller Controller, Axis Axis)> _byCurve = new();

    public ControllerRig(string rigKind, IReadOnlyList<ControllerGroup> groups, IReadOnlyList<Controller> controllers)
    {
        RigKind = rigKind;
        Groups = groups;
        Controllers = controllers;

        // 주 커브가 별칭을 이긴다 — facing의 주 노드(ActingScale_X)가 다른 컨트롤러의 별칭에 가려지지 않게.
        foreach (Controller c in controllers)
        {
            foreach (Axis axis in c.Axes)
            {
                _byCurve[(c.Node, c.Kind, axis)] = (c, axis);
            }
        }

        foreach (Controller c in controllers)
        {
            foreach (ControllerAlias alias in c.Aliases)
            {
                _byCurve.TryAdd((alias.Node, c.Kind, alias.Axis), (c, alias.Axis));
            }
        }
    }

    public string RigKind { get; }

    public IReadOnlyList<ControllerGroup> Groups { get; }

    public IReadOnlyList<Controller> Controllers { get; }

    public IEnumerable<Controller> InGroup(ControllerGroup group) => Controllers.Where(c => ReferenceEquals(c.Group, group));

    public Controller? Find(string id) => Controllers.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    /// <summary>커브 → 컨트롤러 성분. 주 노드든 별칭이든. 표에 없으면 false.</summary>
    public bool TryResolve(CurveRef curve, out Controller controller, out Axis axis)
    {
        if (_byCurve.TryGetValue((curve.Node, curve.Kind, curve.Axis), out (Controller Controller, Axis Axis) hit))
        {
            controller = hit.Controller;
            axis = hit.Axis;
            return true;
        }

        controller = null!;
        axis = default;
        return false;
    }

    /// <summary>"연기 · 몸 Y" — 표에 없으면 null(호출부가 노드 이름을 쓴다).</summary>
    public string? LabelOf(CurveRef curve)
        => TryResolve(curve, out Controller c, out Axis axis) ? $"{c.Group.Label} · {c.AttributeLabel(axis)}" : null;

    /// <summary>노드가 속한 컨트롤러(주 노드 또는 별칭) — 노드 이름만 아는 자리(도프 시트 노드 행)용.</summary>
    public Controller? OfNode(string node)
        => Controllers.FirstOrDefault(c => c.Node == node) ?? Controllers.FirstOrDefault(c => c.Aliases.Any(a => a.Node == node));

    /// <summary>W/E/R의 기본 손잡이 — manipulator 표시가 있는 그 종류의 컨트롤러. 없으면 그 종류의 첫 컨트롤러.</summary>
    public Controller? DefaultFor(MotionChannelKind kind)
        => Controllers.FirstOrDefault(c => c.Manipulator && c.Kind == kind) ?? Controllers.FirstOrDefault(c => c.Kind == kind);

    /// <summary>표가 가리키는 노드가 리그에 없으면 사유를 낸다 — 덤프가 바뀌었는데 표가 옛것일 때.</summary>
    public IReadOnlyList<string> Validate(ICollection<string> rigNodeIds)
    {
        ArgumentNullException.ThrowIfNull(rigNodeIds);
        var problems = new List<string>();

        foreach (Controller c in Controllers)
        {
            if (!rigNodeIds.Contains(c.Node))
            {
                problems.Add($"컨트롤러 '{c.Label}'({c.Id})의 노드 '{c.Node}'가 리그 '{RigKind}'에 없다");
            }

            foreach (ControllerAlias alias in c.Aliases)
            {
                if (!rigNodeIds.Contains(alias.Node))
                {
                    problems.Add($"컨트롤러 '{c.Label}'({c.Id})의 별칭 노드 '{alias.Node}'가 리그 '{RigKind}'에 없다");
                }
            }
        }

        return problems;
    }
}

/// <summary>
/// <c>controllers.json</c>(스키마 <c>controllers/1</c>) — 리그 덤프 옆 <c>presets/controllers.json</c> 또는 <c>controllers.json</c>.
/// 없으면 내장본(<see cref="BuiltinTuning.Controllers"/>). 모르는 리그는 표가 없는 것이고, 그 리그는 노드로 다룬다.
/// </summary>
public sealed class ControllersFile
{
    public const string Schema = "controllers/1";
    public const string FileName = "controllers.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, ControllerRig> _rigs;

    private ControllersFile(Dictionary<string, ControllerRig> rigs, string? source)
    {
        _rigs = rigs;
        Source = source;
    }

    public string? Source { get; }

    public IReadOnlyCollection<string> RigKinds => _rigs.Keys;

    public bool TryGetRig(string? rigKind, out ControllerRig rig)
    {
        if (rigKind is not null && _rigs.TryGetValue(rigKind, out ControllerRig? found))
        {
            rig = found;
            return true;
        }

        rig = null!;
        return false;
    }

    public ControllerRig? Rig(string? rigKind) => TryGetRig(rigKind, out ControllerRig rig) ? rig : null;

    public static ControllersFile Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllText(path));
    }

    /// <summary>덤프 옆 <c>presets/controllers.json</c> → <c>controllers.json</c> 순으로 찾는다. 없으면 null.</summary>
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

    /// <summary>읽는다. 모양이 틀리면 <see cref="JsonException"/> — 조용히 빈 표가 되지 않는다.</summary>
    public static ControllersFile Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        FileDto file = JsonSerializer.Deserialize<FileDto>(json, Options) ?? throw new JsonException("빈 controllers.json이다.");

        if (!string.Equals(file.Schema, Schema, StringComparison.Ordinal))
        {
            throw new JsonException($"controllers.json schema '{file.Schema}'는 읽을 수 없다 (기대 '{Schema}').");
        }

        var rigs = new Dictionary<string, ControllerRig>(StringComparer.Ordinal);

        foreach (RigDto rigDto in file.Rigs ?? new List<RigDto>())
        {
            if (string.IsNullOrEmpty(rigDto.RigKind))
            {
                throw new JsonException("rigKind가 비어 있는 리그가 있다.");
            }

            var groups = new List<ControllerGroup>();

            foreach (GroupDto g in rigDto.Groups ?? new List<GroupDto>())
            {
                if (string.IsNullOrEmpty(g.Id) || !MotionModeToken.TryParse(g.Mode, out MotionChannelMode mode) || string.IsNullOrEmpty(g.Mode))
                {
                    throw new JsonException($"리그 '{rigDto.RigKind}': 묶음 '{g.Id}'의 id·mode(relative|absolute)가 있어야 한다.");
                }

                groups.Add(new ControllerGroup(g.Id, string.IsNullOrEmpty(g.Label) ? g.Id : g.Label, mode, g.Note));
            }

            var controllers = new List<Controller>();
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (ControllerDto c in rigDto.Controllers ?? new List<ControllerDto>())
            {
                if (string.IsNullOrEmpty(c.Id) || !ids.Add(c.Id))
                {
                    throw new JsonException($"리그 '{rigDto.RigKind}': 컨트롤러 id가 비었거나 겹친다 ('{c.Id}').");
                }

                ControllerGroup group = groups.FirstOrDefault(g => g.Id == c.Group)
                    ?? throw new JsonException($"컨트롤러 '{c.Id}': 모르는 묶음 '{c.Group}'.");

                if (string.IsNullOrEmpty(c.Node))
                {
                    throw new JsonException($"컨트롤러 '{c.Id}': 노드가 비어 있다.");
                }

                if (!TryParseKind(c.Kind, out MotionChannelKind kind))
                {
                    throw new JsonException($"컨트롤러 '{c.Id}': kind는 position|scale|euler 여야 한다 ('{c.Kind}').");
                }

                var axes = new List<Axis>();

                foreach (string token in c.Axes ?? new List<string>())
                {
                    if (!TryParseAxis(token, out Axis axis))
                    {
                        throw new JsonException($"컨트롤러 '{c.Id}': 성분은 x|y|z 여야 한다 ('{token}').");
                    }

                    if (axis == Axis.Z && kind != MotionChannelKind.Euler)
                    {
                        throw new JsonException($"컨트롤러 '{c.Id}': {kind}에는 Z 성분이 없다(코어 규칙).");
                    }

                    axes.Add(axis);
                }

                if (axes.Count == 0)
                {
                    throw new JsonException($"컨트롤러 '{c.Id}': 성분이 하나는 있어야 한다.");
                }

                var aliases = new List<ControllerAlias>();

                foreach (AliasDto a in c.Aliases ?? new List<AliasDto>())
                {
                    if (string.IsNullOrEmpty(a.Node) || !TryParseAxis(a.Axis, out Axis axis) || !axes.Contains(axis))
                    {
                        throw new JsonException($"컨트롤러 '{c.Id}': 별칭은 노드와 이 컨트롤러에 있는 성분이어야 한다 ('{a.Node}' {a.Axis}).");
                    }

                    aliases.Add(new ControllerAlias(a.Node, axis));
                }

                ControllerBake bake = (c.Bake ?? "").Trim().ToLowerInvariant() switch
                {
                    "" or "none" => ControllerBake.None,
                    "depthlevel" => ControllerBake.DepthLevel,
                    _ => throw new JsonException($"컨트롤러 '{c.Id}': 모르는 bake '{c.Bake}'."),
                };

                if (bake == ControllerBake.DepthLevel && kind != MotionChannelKind.Scale)
                {
                    throw new JsonException($"컨트롤러 '{c.Id}': depthLevel은 scale 노드에만 굽는다.");
                }

                controllers.Add(new Controller(
                    c.Id, group, string.IsNullOrEmpty(c.Label) ? c.Id : c.Label, c.Node, kind, axes,
                    c.Unit ?? "", aliases, bake, c.Manipulator, c.Note));
            }

            rigs[rigDto.RigKind] = new ControllerRig(rigDto.RigKind, groups, controllers);
        }

        return new ControllersFile(rigs, file.Source);
    }

    private static bool TryParseKind(string? token, out MotionChannelKind kind)
    {
        switch ((token ?? "").Trim().ToLowerInvariant())
        {
            case "position": kind = MotionChannelKind.Position; return true;
            case "scale": kind = MotionChannelKind.Scale; return true;
            case "euler": case "rotate": kind = MotionChannelKind.Euler; return true;
            default: kind = default; return false;
        }
    }

    private static bool TryParseAxis(string? token, out Axis axis)
    {
        switch ((token ?? "").Trim().ToLowerInvariant())
        {
            case "x": axis = Axis.X; return true;
            case "y": axis = Axis.Y; return true;
            case "z": axis = Axis.Z; return true;
            default: axis = default; return false;
        }
    }

    private sealed class FileDto
    {
        public string? Schema { get; set; }
        public string? Source { get; set; }
        public List<RigDto>? Rigs { get; set; }
    }

    private sealed class RigDto
    {
        public string? RigKind { get; set; }
        public List<GroupDto>? Groups { get; set; }
        public List<ControllerDto>? Controllers { get; set; }
    }

    private sealed class GroupDto
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public string? Mode { get; set; }
        public string? Note { get; set; }
    }

    private sealed class ControllerDto
    {
        public string? Id { get; set; }
        public string? Group { get; set; }
        public string? Label { get; set; }
        public string? Node { get; set; }
        public string? Kind { get; set; }
        public List<string>? Axes { get; set; }
        public string? Unit { get; set; }
        public List<AliasDto>? Aliases { get; set; }
        public string? Bake { get; set; }
        public bool Manipulator { get; set; }
        public string? Note { get; set; }
    }

    private sealed class AliasDto
    {
        public string? Node { get; set; }
        public string? Axis { get; set; }
    }
}
