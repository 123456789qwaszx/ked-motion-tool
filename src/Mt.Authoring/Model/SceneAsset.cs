using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 씬 — 미리보기용 무대 배치(<c>PLAN-scene.md</c>). 런타임에 내보내지 않는다.
/// 오브젝트 = 리그 인스턴스(슬롯). 노드 키는 코어 규약대로 <c>{id}/{nodeId}</c>다.
/// </summary>
public sealed class SceneAsset
{
    public const string DefaultName = "scene";

    public string Name { get; set; } = DefaultName;

    public CameraSettings Camera { get; set; } = new();

    public List<SceneObject> Objects { get; set; } = new();

    public SceneObject? Find(string id)
        => Objects.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal));

    /// <summary>최상위 오브젝트(부모 없음) — 목록 순.</summary>
    public IEnumerable<SceneObject> TopLevel() => Objects.Where(o => o.Parent is null);

    /// <summary>바로 밑 자식들 — 목록 순.</summary>
    public IEnumerable<SceneObject> ChildrenOf(string id)
        => Objects.Where(o => string.Equals(o.Parent, id, StringComparison.Ordinal));

    /// <summary>자손 전부(깊이 우선, 부모 먼저).</summary>
    public IEnumerable<SceneObject> DescendantsOf(string id)
    {
        foreach (SceneObject child in ChildrenOf(id))
        {
            yield return child;

            foreach (SceneObject grandchild in DescendantsOf(child.Id))
            {
                yield return grandchild;
            }
        }
    }

    /// <summary>a가 b의 조상(또는 b 자신)인가 — 부모 설정의 순환 검사.</summary>
    public bool IsAncestorOrSelf(string a, string b)
    {
        SceneObject? current = Find(b);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (current is not null && seen.Add(current.Id))
        {
            if (string.Equals(current.Id, a, StringComparison.Ordinal))
            {
                return true;
            }

            current = current.Parent is null ? null : Find(current.Parent);
        }

        return false;
    }

    /// <summary>최상위 조상 — 자식의 stage·layer·깊이는 이것을 따른다.</summary>
    public SceneObject RootOf(SceneObject o)
    {
        SceneObject current = o;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (current.Parent is not null && seen.Add(current.Id) && Find(current.Parent) is { } parent)
        {
            current = parent;
        }

        return current;
    }

    /// <summary>보이나 — 자신과 조상이 모두 보이면.</summary>
    public bool IsShown(SceneObject o)
    {
        SceneObject? current = o;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (current is not null && seen.Add(current.Id))
        {
            if (!current.Visible)
            {
                return false;
            }

            current = current.Parent is null ? null : Find(current.Parent);
        }

        return true;
    }

    /// <summary>
    /// 그리는 순서 — 최상위는 먼 레이어부터(far → close)·같은 레이어는 목록 순, 자식은 부모 바로 뒤(부모 위에 그려진다).
    /// 부모가 자식보다 항상 먼저이므로 이 순서로 트리를 세울 수 있다. 부모가 없는 오브젝트(끊긴 참조)는 최상위로 본다.
    /// </summary>
    public IEnumerable<SceneObject> DrawOrder()
    {
        IEnumerable<SceneObject> roots = Objects
            .Where(o => o.Parent is null || Find(o.Parent) is null)
            .OrderBy(o => SceneLayers.IndexOf(o.Layer))
            .ThenBy(o => Objects.IndexOf(o));

        foreach (SceneObject root in roots)
        {
            yield return root;

            foreach (SceneObject descendant in DescendantsOf(root.Id))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>캐릭터 리그 하나(s1, stage00/mid)를 세운 기본 씬 — 씬이 없던 문서(예전 판)와 새 문서가 이것으로 시작한다.</summary>
    public static SceneAsset Default(string? portrait = null) => new()
    {
        Objects = { new SceneObject { Id = SceneObject.DefaultId, Rig = SceneObject.DefaultRig, Portrait = portrait } },
    };
}

/// <summary>샷(카메라) — 보기 전용. 코어 <c>ShotIntentState</c>와 같은 뜻(zoom 1당 배율 0.05, pan은 리그 공간 px).</summary>
public sealed class CameraSettings
{
    public float Zoom { get; set; }

    public float PanX { get; set; }

    public float PanY { get; set; }
}

/// <summary>
/// 씬의 오브젝트 하나 = 리그 인스턴스(슬롯). 자리는 런타임과 같은 두 겹이다:
/// <see cref="Place"/>(화면 지점 × focus 프리셋 → <c>CharSlot_Track_Focus</c>, 코어 place 폴드)와
/// 오프셋(<see cref="PositionX"/>/<see cref="PositionY"/> → <c>CharSlot_Track</c>, move) · 회전 → <c>CharSlot_SwayPivot</c> · 배율 → <c>CharSlot_Scale</c>.
/// 그 노드가 없는 리그는 루트 노드에 들어간다.
/// 최상위 오브젝트는 무대 뼈대의 <c>Stage{NN}Depth_{Layer}_Content</c>에 서고, 다른 오브젝트의 노드 밑에 붙을 수도 있다(<see cref="Parent"/>·<see cref="ParentNode"/>).
/// </summary>
public sealed class SceneObject
{
    public const string DefaultId = "s1";
    public const string DefaultRig = "character";
    public const string DefaultStage = "stage00";
    public const string DefaultPlaceFocus = "bust";

    /// <summary>슬롯 키. <c>[a-z0-9_]+</c>. 노드 키 접두사가 된다(유니티 이름 <c>s1_노드</c>와 같은 뜻).</summary>
    public string Id { get; set; } = DefaultId;

    /// <summary>리그 덤프의 rigKind. 지금은 character만 만든다.</summary>
    public string Rig { get; set; } = DefaultRig;

    /// <summary>부모 오브젝트 id. null이면 최상위. 자식은 부모의 stage·layer·깊이를 따른다.</summary>
    public string? Parent { get; set; }

    /// <summary>부모 리그에서 붙는 노드 id. null이면 부모 루트.</summary>
    public string? ParentNode { get; set; }

    /// <summary>무대(stage00·stage01·stage02) — 런타임 slot 커맨드의 어휘. 뼈대의 <c>Stage{NN}_Root</c>.</summary>
    public string Stage { get; set; } = DefaultStage;

    /// <summary>깊이 레이어(far · back · mid · front · close). 그리는 순서를 정한다.</summary>
    public string Layer { get; set; } = SceneLayers.Mid;

    /// <summary>
    /// 자리(place) — 화면 지점 이름(center·left·right·top·bottom·tl·tr·bl·br). null이면 place 없음(Track_Focus 그대로).
    /// 코어 <c>PlaceFocusStageReduction</c>이 <see cref="PlaceFocus"/> 점이 그 지점에 오도록 <c>CharSlot_Track_Focus</c>를 푼다 — 런타임 place와 같은 함수.
    /// </summary>
    public string? Place { get; set; }

    /// <summary>place가 맞추는 focus 프리셋(bust·face·body·feet·faceAura·handLeft·handRight). null이면 bust.</summary>
    public string? PlaceFocus { get; set; }

    /// <summary>오프셋(move) px, 부모 공간(가운데 원점·y 위) — <c>CharSlot_Track</c>.</summary>
    public float PositionX { get; set; }

    public float PositionY { get; set; }

    /// <summary>도.</summary>
    public float RotationZ { get; set; }

    public float ScaleX { get; set; } = 1f;

    public float ScaleY { get; set; } = 1f;

    /// <summary>미리보기 초상 이미지(문서 기준 상대 경로). 산출물과 무관.</summary>
    public string? Portrait { get; set; }

    public bool Visible { get; set; } = true;

    /// <summary>노드 키 접두사 — 코어 <c>StageState.NodeKeyOf</c>와 같은 꼴.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Prefix => Id + "/";

    public string KeyOf(string nodeId) => Prefix + nodeId;
}

public static class SceneLayers
{
    public const string Far = "far";
    public const string Back = "back";
    public const string Mid = "mid";
    public const string Front = "front";
    public const string Close = "close";

    public static readonly IReadOnlyList<string> Order = new[] { Far, Back, Mid, Front, Close };

    /// <summary>모르는 레이어는 mid 자리로.</summary>
    public static int IndexOf(string? layer)
    {
        int index = layer is null ? -1 : Array.IndexOf((string[])Order, layer);
        return index < 0 ? 2 : index;
    }

    public static bool IsKnown(string? layer) => layer is not null && Array.IndexOf((string[])Order, layer) >= 0;
}

/// <summary>배치 한 벌(오프셋·회전·배율) — 부모를 바꿀 때 월드 자세를 지키는 값을 한 번에 넘긴다.</summary>
public readonly record struct Placement(float X, float Y, float RotZ, float ScaleX, float ScaleY)
{
    public static Placement Of(SceneObject o) => new(o.PositionX, o.PositionY, o.RotationZ, o.ScaleX, o.ScaleY);
}

/// <summary>배치 값을 트리에 넣는 자리 — 런타임의 move/rotate/scale 폴드와 같은 노드.</summary>
public static class PlacementNodes
{
    public const string Position = "CharSlot_Track";
    public const string Rotation = "CharSlot_SwayPivot";
    public const string Scale = "CharSlot_Scale";

    /// <summary>배치 축 노드들 — 모션 채널로 쓰면 런타임 place/size와 겹칠 수 있어 툴이 "고급"으로 접는다.</summary>
    public static bool IsPlacementAxis(string nodeId) => nodeId.StartsWith("CharSlot_", StringComparison.Ordinal);

    /// <summary>키잉 금지 노드(코어 규칙) — 채널 추가 후보에서 뺀다.</summary>
    public static bool IsForbidden(string nodeId) => MotionRules.ForbiddenNodes.Contains(nodeId);
}
