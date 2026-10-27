using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Graph;

/// <summary>
/// 복사한 키들 — 커브마다 키 사본, 시각은 복사 당시 그대로이고 <see cref="Origin"/>(가장 이른 키)을 기준으로 붙인다.
/// 붙여넣기는 현재 시각에 Origin을 맞춘다(마야 paste의 time offset). 커브가 없는 모션에 붙이면 채널·성분이 생긴다.
/// </summary>
public sealed class KeyClipboard
{
    private KeyClipboard(IReadOnlyList<(CurveRef Curve, IReadOnlyList<KeyAsset> Keys)> items, float origin)
    {
        Items = items;
        Origin = origin;
    }

    public IReadOnlyList<(CurveRef Curve, IReadOnlyList<KeyAsset> Keys)> Items { get; }

    /// <summary>복사한 키 중 가장 이른 시각.</summary>
    public float Origin { get; }

    public int Count => Items.Sum(i => i.Keys.Count);

    /// <summary>선택을 복사한다. 키가 하나도 없으면 null.</summary>
    public static KeyClipboard? Copy(MotionAsset motion, IEnumerable<KeyRef> keys)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(keys);

        var byCurve = new Dictionary<CurveRef, List<KeyAsset>>();
        float origin = float.PositiveInfinity;

        foreach (KeyRef key in keys.Distinct())
        {
            if (key.Resolve(motion) is not { } asset)
            {
                continue;
            }

            if (!byCurve.TryGetValue(key.Curve, out List<KeyAsset>? list))
            {
                list = new List<KeyAsset>();
                byCurve[key.Curve] = list;
            }

            list.Add(Clone(asset));
            origin = Math.Min(origin, asset.T);
        }

        if (byCurve.Count == 0)
        {
            return null;
        }

        var items = byCurve
            .Select(pair => (pair.Key, (IReadOnlyList<KeyAsset>)pair.Value.OrderBy(k => k.T).ToList()))
            .ToList();
        return new KeyClipboard(items, origin);
    }

    private static KeyAsset Clone(KeyAsset key) => new()
    {
        T = key.T,
        V = key.V,
        InTangent = key.InTangent,
        OutTangent = key.OutTangent,
        Tangent = key.Tangent,
        Broken = key.Broken,
    };
}

/// <summary>클립보드 자리 — 그래프·도프시트가 같은 객체를 들어 서로 붙여넣을 수 있다.</summary>
public sealed class KeyClipboardSlot
{
    public KeyClipboard? Current { get; set; }

    public bool IsEmpty => Current is null || Current.Count == 0;
}
