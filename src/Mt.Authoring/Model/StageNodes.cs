using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 무대 뼈대(<c>PresentataionUIRoot</c>)의 이름 규약 — 툴의 stage/layer 라벨(<c>stage00</c>·<c>mid</c>, 런타임 slot 커맨드와 같은 어휘)을
/// 유니티 노드 이름(<c>Stage00Depth_Mid_Content</c>)으로 잇는다. 뼈대 노드는 오브젝트가 아니다 — 만들기·드롭의 자리일 뿐이다.
/// </summary>
public static class StageNodes
{
    public const string RigKind = "stage";

    /// <summary>뼈대 노드의 키 접두사 — 오브젝트 키(<c>s1/…</c>)와 겹치지 않는다.</summary>
    public const string Prefix = "stage/";

    public const string ShotRoot = "StageShot_Root";
    public const string PanRoot = "StagePan_Root";
    public const string ZoomRoot = "StageZoom_Root";

    public static readonly string[] Stages = { "stage00", "stage01", "stage02" };

    public static bool IsKnownStage(string? stage) => stage is not null && Stages.Contains(stage);

    /// <summary>무대의 차례(0·1·2) — 그리는 순서. 모르는 무대는 −1(맨 뒤에 그린다).</summary>
    public static int IndexOf(string? stage) => stage is null ? -1 : Array.IndexOf(Stages, stage);

    /// <summary>"stage00" → "Stage00". 모르는 꼴은 그대로 머리글자만 올린다.</summary>
    public static string StageName(string stage)
        => stage.Length == 0 ? stage : char.ToUpperInvariant(stage[0]) + stage[1..];

    /// <summary>"mid" → "Mid".</summary>
    public static string LayerName(string layer)
        => layer.Length == 0 ? layer : char.ToUpperInvariant(layer[0]) + layer[1..];

    public static string StageRootOf(string stage) => $"{StageName(stage)}_Root";

    public static string LayerRootOf(string stage, string layer) => $"{StageName(stage)}Depth_{LayerName(layer)}_Root";

    public static string FramingTransformOf(string stage, string layer) => $"{StageName(stage)}Depth_{LayerName(layer)}_FramingTransform";

    public static string FramingScaleOf(string stage, string layer) => $"{StageName(stage)}Depth_{LayerName(layer)}_FramingScale";

    /// <summary>오브젝트가 서는 자리 — 런타임 <c>GetDepthContent(stage, layer)</c>와 같은 노드.</summary>
    public static string ContentOf(string stage, string layer) => $"{StageName(stage)}Depth_{LayerName(layer)}_Content";

    /// <summary>뼈대 트리 키.</summary>
    public static string KeyOf(string nodeId) => Prefix + nodeId;

    /// <summary>Content 노드 이름 → (stage, layer). 아니면 false.</summary>
    public static bool TryParseContent(string nodeId, out string stage, out string layer)
    {
        stage = "";
        layer = "";

        foreach (string s in Stages)
        {
            foreach (string l in SceneLayers.Order)
            {
                if (string.Equals(ContentOf(s, l), nodeId, StringComparison.Ordinal))
                {
                    stage = s;
                    layer = l;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 깊이 층의 샷 응답(패럴랙스) — 런타임 <c>PresentationResponseProfile.Depth*</c>의 상수 그대로.
    /// pan을 따라가는 비율(x,y)과 zoom 배율의 지수. focus 산개(<c>focusSpreadPixelsPerZoom</c>)는 focus 점이 (0,0)이고
    /// 층 루트도 (0,0)이라 항상 0 — 툴은 focus 점을 갖지 않으므로 넣지 않는다.
    /// 원본: ked-presentation-runtime <c>Runtime/ShotResponse/Core/PresentationResponseProfile.cs</c>.
    /// </summary>
    public static (Vec2 PanResponse, float ZoomScaleResponse) ResponseOf(string layer) => layer switch
    {
        SceneLayers.Far => (new Vec2(-0.04f, 0f), -0.15f),
        SceneLayers.Back => (new Vec2(-0.02f, 0f), -0.08f),
        SceneLayers.Front => (new Vec2(0.02f, 0f), 0.04f),
        SceneLayers.Close => (new Vec2(0.2f, 0f), 0.16f),
        _ => (Vec2.Zero, 0f),
    };
}
