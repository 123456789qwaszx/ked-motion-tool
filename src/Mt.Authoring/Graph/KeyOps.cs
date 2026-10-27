using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Graph;

/// <summary>
/// 키 묶음에 대한 편집의 순수 산수 — 이동·시간 스케일·복제·곡선 위 끼우기. 결과는 커브마다 새 키 목록이고,
/// <c>MotionEditor.SetKeysMany</c>가 한 번의 조작으로 적용한다. 그래프와 도프시트가 같은 함수를 쓴다.
///
/// 마야와 같은 규칙: 움직인 키가 안 움직인 키의 시각에 닿으면 **움직인 키가 이긴다**(안 움직인 키가 사라진다).
/// 시각은 0 아래로 내려가지 않는다.
/// </summary>
public static class KeyOps
{
    /// <summary>
    /// 곡선 위의 값·접선으로 키를 끼운 목록(마야 Insert Key — 모양이 안 변한다). 같은 시각에 키가 있으면 그 키를 대신한다.
    /// <paramref name="newKeyTangent"/>를 주면 그 모드로(키가 없던 커브의 첫 키도 그 모드). null이면 접선을 지키는 free.
    /// </summary>
    public static List<KeyAsset> WithKeyOnCurve(IReadOnlyList<KeyAsset> keys, float t, TangentMode? newKeyTangent = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        (float value, float slope) = TangentMath.OnCurve(keys, t);
        List<KeyAsset> result = keys.Where(k => Math.Abs(k.T - t) > MotionRules.Tolerance).Select(Clone).ToList();

        result.Add(new KeyAsset
        {
            T = t,
            V = value,
            InTangent = slope,
            OutTangent = slope,
            Tangent = newKeyTangent ?? (keys.Count == 0 ? TangentMode.Auto : TangentMode.Free),
        });

        return result;
    }

    /// <summary>선택 키의 시각에 delta를 더한다.</summary>
    public static List<(CurveRef Curve, IEnumerable<KeyAsset> Keys)> Move(
        MotionAsset motion, IEnumerable<KeyRef> keys, float deltaSeconds, Func<float, float>? snap = null)
        => Transform(motion, keys, t => t + deltaSeconds, snap, keepOriginals: false);

    /// <summary>pivot을 기준으로 선택 키의 시각을 factor배 늘린다. factor는 양수여야 한다.</summary>
    public static List<(CurveRef Curve, IEnumerable<KeyAsset> Keys)> Scale(
        MotionAsset motion, IEnumerable<KeyRef> keys, float pivotSeconds, float factor, Func<float, float>? snap = null)
    {
        if (!(factor > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "시간 스케일은 양수여야 한다");
        }

        return Transform(motion, keys, t => pivotSeconds + (t - pivotSeconds) * factor, snap, keepOriginals: false);
    }

    /// <summary>선택 키의 사본을 offset만큼 옮겨 둔다. 원본은 그대로.</summary>
    public static List<(CurveRef Curve, IEnumerable<KeyAsset> Keys)> Duplicate(
        MotionAsset motion, IEnumerable<KeyRef> keys, float offsetSeconds, Func<float, float>? snap = null)
        => Transform(motion, keys, t => t + offsetSeconds, snap, keepOriginals: true);

    /// <summary>선택 키의 값만 바꾼다(시각은 그대로). 값 스케일·상대 입력이 쓴다.</summary>
    public static List<(CurveRef Curve, IEnumerable<KeyAsset> Keys)> MapValues(
        MotionAsset motion, IEnumerable<KeyRef> keys, Func<KeyAsset, float> map)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(map);

        var byCurve = new Dictionary<CurveRef, HashSet<int>>();

        foreach (KeyRef key in keys)
        {
            if (key.Resolve(motion) is null)
            {
                continue;
            }

            if (!byCurve.TryGetValue(key.Curve, out HashSet<int>? indices))
            {
                indices = new HashSet<int>();
                byCurve[key.Curve] = indices;
            }

            indices.Add(key.Index);
        }

        var edits = new List<(CurveRef, IEnumerable<KeyAsset>)>();

        foreach ((CurveRef curveRef, HashSet<int> indices) in byCurve)
        {
            ComponentCurve curve = curveRef.Resolve(motion)!;
            var result = new List<KeyAsset>();

            for (int i = 0; i < curve.Keys.Count; i++)
            {
                KeyAsset copy = Clone(curve.Keys[i]);

                if (indices.Contains(i))
                {
                    copy.V = map(curve.Keys[i]);
                }

                result.Add(copy);
            }

            edits.Add((curveRef, result));
        }

        return edits;
    }

    /// <summary>변환 뒤 선택이 가리킬 (커브, 시각) — 편집 뒤 자리를 다시 푸는 데 쓴다.</summary>
    public static List<(CurveRef Curve, float T)> ResultingTimes(
        MotionAsset motion, IEnumerable<KeyRef> keys, Func<float, float> map, Func<float, float>? snap = null)
    {
        var result = new List<(CurveRef, float)>();

        foreach (KeyRef key in keys.Distinct())
        {
            if (key.Resolve(motion) is { } asset)
            {
                result.Add((key.Curve, Clamp(map(asset.T), snap)));
            }
        }

        return result;
    }

    private static List<(CurveRef Curve, IEnumerable<KeyAsset> Keys)> Transform(
        MotionAsset motion, IEnumerable<KeyRef> keys, Func<float, float> map, Func<float, float>? snap, bool keepOriginals)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(keys);

        var byCurve = new Dictionary<CurveRef, HashSet<int>>();

        foreach (KeyRef key in keys)
        {
            if (key.Resolve(motion) is null)
            {
                continue;
            }

            if (!byCurve.TryGetValue(key.Curve, out HashSet<int>? indices))
            {
                indices = new HashSet<int>();
                byCurve[key.Curve] = indices;
            }

            indices.Add(key.Index);
        }

        var edits = new List<(CurveRef, IEnumerable<KeyAsset>)>();

        foreach ((CurveRef curveRef, HashSet<int> indices) in byCurve)
        {
            ComponentCurve curve = curveRef.Resolve(motion)!;
            var moved = new List<KeyAsset>();

            for (int i = 0; i < curve.Keys.Count; i++)
            {
                if (indices.Contains(i))
                {
                    KeyAsset copy = Clone(curve.Keys[i]);
                    copy.T = Clamp(map(copy.T), snap);
                    moved.Add(copy);
                }
            }

            var result = new List<KeyAsset>();

            for (int i = 0; i < curve.Keys.Count; i++)
            {
                bool selected = indices.Contains(i);

                if (selected && !keepOriginals)
                {
                    continue;
                }

                // 움직인 키가 닿은 자리의 키는 사라진다 — 움직인 키가 이긴다.
                if (moved.Any(m => Math.Abs(m.T - curve.Keys[i].T) <= MotionRules.Tolerance))
                {
                    continue;
                }

                result.Add(Clone(curve.Keys[i]));
            }

            // 움직인 키끼리 겹치면 뒤의 것이 이긴다(시각 스케일 0에 가까울 때).
            foreach (KeyAsset m in moved)
            {
                result.RemoveAll(k => Math.Abs(k.T - m.T) <= MotionRules.Tolerance);
                result.Add(m);
            }

            edits.Add((curveRef, result));
        }

        return edits;
    }

    private static float Clamp(float t, Func<float, float>? snap)
    {
        float snapped = snap is null ? t : snap(t);
        return Math.Max(0f, snapped);
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
