using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Graph;

/// <summary>
/// 탄젠트 모드의 산수. 키가 움직이면 <see cref="TangentMode.Free"/>가 아닌 키의 탄젠트를 다시 센다 —
/// 편집기가 키를 바꾸는 길마다 부르므로 "auto인데 안 따라오는" 키가 없다.
///
/// - flat: 0
/// - linear: in은 앞 키와의 할선, out은 뒤 키와의 할선
/// - auto: 마야의 auto와 같은 뜻 — 이웃 둘을 잇는 할선 기울기, 극값(양 이웃이 같은 쪽)이면 0, 끝 키는 0.
///   할선은 이웃 구간 어느 쪽도 넘지 않게 죈다(오버슈트 억제).
/// </summary>
public static class TangentMath
{
    public static void Recompute(List<KeyAsset> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        for (int i = 0; i < keys.Count; i++)
        {
            KeyAsset key = keys[i];

            switch (key.Tangent)
            {
                case TangentMode.Flat:
                    key.InTangent = 0f;
                    key.OutTangent = 0f;
                    key.Broken = false;
                    break;

                case TangentMode.Linear:
                {
                    float toPrev = i > 0 ? Secant(keys[i - 1], key) : 0f;
                    float toNext = i < keys.Count - 1 ? Secant(key, keys[i + 1]) : 0f;
                    key.InTangent = i > 0 ? toPrev : toNext;
                    key.OutTangent = i < keys.Count - 1 ? toNext : toPrev;
                    key.Broken = key.InTangent != key.OutTangent;
                    break;
                }

                case TangentMode.Auto:
                {
                    float slope = AutoSlope(keys, i);
                    key.InTangent = slope;
                    key.OutTangent = slope;
                    key.Broken = false;
                    break;
                }

                case TangentMode.Spline:
                {
                    float slope = SplineSlope(keys, i);
                    key.InTangent = slope;
                    key.OutTangent = slope;
                    key.Broken = false;
                    break;
                }

                case TangentMode.Plateau:
                {
                    float slope = PlateauSlope(keys, i);
                    key.InTangent = slope;
                    key.OutTangent = slope;
                    key.Broken = false;
                    break;
                }

                case TangentMode.Free:
                default:
                    break;
            }
        }
    }

    /// <summary>auto 기울기 — 끝 키·극값은 0, 그 밖은 이웃 할선을 양쪽 구간 기울기 안으로 죈 값.</summary>
    public static float AutoSlope(IReadOnlyList<KeyAsset> keys, int index)
    {
        if (index <= 0 || index >= keys.Count - 1)
        {
            return 0f;
        }

        KeyAsset prev = keys[index - 1];
        KeyAsset key = keys[index];
        KeyAsset next = keys[index + 1];

        float left = Secant(prev, key);
        float right = Secant(key, next);

        // 극값: 이웃이 양쪽 다 위거나 다 아래 → 평평하게. 오르다 내리는 자리에서 넘치지 않는다.
        if (left * right <= 0f)
        {
            return 0f;
        }

        float secant = Secant(prev, next);

        // 가파른 쪽 구간의 3배를 넘지 않게 — 모노톤 보간의 Fritsch–Carlson 한계와 같은 발상.
        float limit = 3f * Math.Min(Math.Abs(left), Math.Abs(right));
        return Math.Clamp(secant, -limit, limit);
    }

    /// <summary>spline — 이웃 둘을 잇는 할선 그대로. 끝 키는 하나뿐인 이웃과의 할선(마야 spline 끝과 같다). 넘칠 수 있다.</summary>
    public static float SplineSlope(IReadOnlyList<KeyAsset> keys, int index)
    {
        if (keys.Count < 2)
        {
            return 0f;
        }

        if (index <= 0)
        {
            return Secant(keys[0], keys[1]);
        }

        if (index >= keys.Count - 1)
        {
            return Secant(keys[^2], keys[^1]);
        }

        return Secant(keys[index - 1], keys[index + 1]);
    }

    /// <summary>
    /// plateau — auto처럼 극값·끝은 평평하고, 이웃과 같은 값이어도 평평하다. 가운데는 이웃 할선을
    /// 양쪽 구간 기울기 중 작은 쪽을 넘지 않게 눌러 이웃 값을 절대 넘지 않는다.
    /// </summary>
    public static float PlateauSlope(IReadOnlyList<KeyAsset> keys, int index)
    {
        if (index <= 0 || index >= keys.Count - 1)
        {
            return 0f;
        }

        KeyAsset prev = keys[index - 1];
        KeyAsset key = keys[index];
        KeyAsset next = keys[index + 1];

        float left = Secant(prev, key);
        float right = Secant(key, next);

        if (left * right <= 0f || Math.Abs(prev.V - key.V) <= MotionRules.Tolerance || Math.Abs(next.V - key.V) <= MotionRules.Tolerance)
        {
            return 0f;
        }

        float secant = Secant(prev, next);
        float limit = Math.Min(Math.Abs(left), Math.Abs(right));
        return Math.Clamp(secant, -limit, limit);
    }

    public static float Secant(KeyAsset a, KeyAsset b)
    {
        float dt = b.T - a.T;
        return dt <= 0f ? 0f : (b.V - a.V) / dt;
    }

    /// <summary>곡선 위 시각 t의 값과 접선 — 키를 "곡선 위에" 끼워 넣을 때 쓴다(끼운 직후 모양이 거의 안 변한다).</summary>
    public static (float Value, float Slope) OnCurve(IReadOnlyList<KeyAsset> keys, float t)
    {
        CurveKey[] core = ToCurveKeys(keys);

        if (core.Length == 0)
        {
            return (0f, 0f);
        }

        float value = CurveFunctions.Evaluate(core, t);
        const float h = 1e-3f;
        float slope = (CurveFunctions.Evaluate(core, t + h) - CurveFunctions.Evaluate(core, t - h)) / (2f * h);
        return (value, slope);
    }

    public static CurveKey[] ToCurveKeys(IReadOnlyList<KeyAsset> keys)
    {
        var core = new CurveKey[keys.Count];

        for (int i = 0; i < core.Length; i++)
        {
            core[i] = keys[i].ToCurveKey();
        }

        return core;
    }
}
