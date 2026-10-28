using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Graph;

public enum DopeRowLevel
{
    /// <summary>노드 — 그 노드의 모든 채널·성분 키를 요약한다.</summary>
    Node,

    /// <summary>채널(노드 × 종류) — 성분 키를 요약한다.</summary>
    Channel,

    /// <summary>성분(x·y·z) — 키 그 자체.</summary>
    Component,
}

/// <summary>한 시각의 블록. 요약 행에서는 여러 키(성분·채널)를 한 블록이 덮는다.</summary>
public sealed record DopeBlock(float Time, IReadOnlyList<KeyRef> Keys);

/// <summary>도프시트의 한 줄. 성분 행만 <see cref="Curve"/>가 있다.</summary>
public sealed record DopeRow(
    DopeRowLevel Level,
    string Node,
    MotionChannelKind? Kind,
    Axis? Axis,
    string Label,
    IReadOnlyList<DopeBlock> Blocks,
    CurveRef? Curve)
{
    public int Depth => (int)Level;

    public bool Covers(KeyRef key)
        => key.Curve.Node == Node && (Kind is null || key.Curve.Kind == Kind) && (Axis is null || key.Curve.Axis == Axis);
}

/// <summary>
/// 모션 → 도프시트 행들. 노드 › 채널 › 성분 트리를 평평하게 편 것이고, 접힌 노드는 요약 행만 남긴다.
/// 블록은 허용 오차 안의 같은 시각끼리 묶는다 — 요약 행의 블록 하나를 끌면 그 밑의 키가 전부 간다.
/// </summary>
public static class DopeSheetRows
{
    /// <param name="controllers">컨트롤러 표 — 있으면 노드 행의 라벨이 "연기 · 몸"(별칭 노드는 "연기 · 몸 Y")이다. 없으면 노드 이름.</param>
    public static List<DopeRow> Build(MotionAsset motion, Func<string, bool>? isCollapsed = null, ControllerRig? controllers = null)
    {
        ArgumentNullException.ThrowIfNull(motion);

        var rows = new List<DopeRow>();

        foreach (IGrouping<string, ChannelAsset> node in motion.Channels.GroupBy(c => c.Node, StringComparer.Ordinal))
        {
            var nodeKeys = new List<KeyRef>();
            var channelRows = new List<DopeRow>();

            foreach (ChannelAsset channel in node)
            {
                var channelKeys = new List<KeyRef>();
                var componentRows = new List<DopeRow>();

                foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
                {
                    if (channel.Get(axis) is not { } curve)
                    {
                        continue;
                    }

                    var curveRef = new CurveRef(channel.Node, channel.Kind, axis);
                    var keys = new List<KeyRef>();

                    for (int i = 0; i < curve.Keys.Count; i++)
                    {
                        keys.Add(new KeyRef(curveRef, i));
                    }

                    channelKeys.AddRange(keys);
                    componentRows.Add(new DopeRow(
                        DopeRowLevel.Component, channel.Node, channel.Kind, axis,
                        axis.ToString().ToLowerInvariant(), Group(motion, keys), curveRef));
                }

                nodeKeys.AddRange(channelKeys);
                channelRows.Add(new DopeRow(
                    DopeRowLevel.Channel, channel.Node, channel.Kind, null,
                    ChannelKindLabel.Of(channel.Kind), Group(motion, channelKeys), null));
                channelRows.AddRange(componentRows);
            }

            rows.Add(new DopeRow(DopeRowLevel.Node, node.Key, null, null, NodeLabel(node.Key, controllers), Group(motion, nodeKeys), null));

            if (isCollapsed is null || !isCollapsed(node.Key))
            {
                rows.AddRange(channelRows);
            }
        }

        return rows;
    }

    /// <summary>노드 행의 글자 — 표가 있으면 "묶음 · 컨트롤러", 별칭 노드(한 성분만 맡는 노드)는 그 성분까지. 표에 없으면 노드 이름.</summary>
    public static string NodeLabel(string node, ControllerRig? controllers)
    {
        if (controllers?.OfNode(node) is not { } c)
        {
            return node;
        }

        if (c.Node == node)
        {
            return $"{c.Group.Label} · {c.Label}";
        }

        if (c.DepthYNode == node)
        {
            return $"{c.Group.Label} · {c.Label} Y";
        }

        Axis axis = c.Aliases.First(a => a.Node == node).Axis;
        return $"{c.Group.Label} · {c.AttributeLabel(axis)}";
    }

    private static List<DopeBlock> Group(MotionAsset motion, List<KeyRef> keys)
    {
        var blocks = new List<(float Time, List<KeyRef> Keys)>();

        foreach (KeyRef key in keys.OrderBy(k => k.Resolve(motion)?.T ?? 0f))
        {
            float t = key.Resolve(motion)?.T ?? 0f;

            if (blocks.Count > 0 && Math.Abs(blocks[^1].Time - t) <= MotionRules.Tolerance)
            {
                blocks[^1].Keys.Add(key);
            }
            else
            {
                blocks.Add((t, new List<KeyRef> { key }));
            }
        }

        return blocks.Select(b => new DopeBlock(b.Time, b.Keys)).ToList();
    }
}
