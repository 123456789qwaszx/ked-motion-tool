using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 저작본 ↔ 코어 클립. 수학·검증은 코어 것이므로 여기서는 모양만 바꾼다.
///
/// 내보낼 때 relative 채널의 전 구간 중립값 성분은 뺀다 — "아무 변화 없음"이라 빼도 뜻이 같고,
/// Unity 내보내기(<c>MotionClipExporter</c>)가 같은 규칙을 쓰므로 두 생산자의 산출물이 갈리지 않는다.
/// absolute는 "이 값으로"라는 뜻이 있으므로 그대로 둔다.
/// </summary>
public static class MotionAssetCodec
{
    public static MotionClip ToClip(MotionAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var channels = new MotionChannel[asset.Channels.Count];

        for (int i = 0; i < channels.Length; i++)
        {
            ChannelAsset c = asset.Channels[i];
            float neutral = MotionMath.Neutral(c.Kind);

            channels[i] = new MotionChannel
            {
                Node = c.Node,
                Kind = c.Kind,
                Mode = c.Mode,
                X = ToKeys(c.X, c.Mode, neutral),
                Y = ToKeys(c.Y, c.Mode, neutral),
                Z = ToKeys(c.Z, c.Mode, neutral),
            };
        }

        return new MotionClip
        {
            Name = asset.Name,
            Rig = asset.Rig,
            Duration = asset.Duration,
            End = asset.End,
            Channels = channels,
        };
    }

    /// <summary>가져온 키는 전부 <see cref="TangentMode.Free"/>다 — 파일의 탄젠트 값을 그대로 지킨다.</summary>
    public static MotionAsset FromClip(MotionClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        var asset = new MotionAsset
        {
            Name = clip.Name ?? "",
            Rig = clip.Rig ?? "",
            Duration = clip.Duration,
            End = clip.End,
            DefaultMode = GuessDefaultMode(clip),
        };

        if (clip.Channels is not null)
        {
            foreach (MotionChannel ch in clip.Channels)
            {
                asset.Channels.Add(new ChannelAsset
                {
                    Node = ch.Node ?? "",
                    Kind = ch.Kind,
                    Mode = ch.Mode,
                    X = FromKeys(ch.X),
                    Y = FromKeys(ch.Y),
                    Z = FromKeys(ch.Z),
                });
            }
        }

        return asset;
    }

    /// <summary>relative 성분이 전 구간 중립값(탄젠트 0)인가 — 내보낼 때 빠진다. 화면은 이것을 흐리게 보인다.</summary>
    public static bool IsNeutralConstant(ChannelAsset channel, Axis axis)
    {
        ArgumentNullException.ThrowIfNull(channel);

        ComponentCurve? curve = channel.Get(axis);

        return channel.Mode == MotionChannelMode.Relative
            && curve is not null
            && curve.Keys.Count > 0
            && IsConstant(curve, MotionMath.Neutral(channel.Kind));
    }

    private static CurveKey[]? ToKeys(ComponentCurve? curve, MotionChannelMode mode, float neutral)
    {
        if (curve is null || curve.Keys.Count == 0)
        {
            return null;
        }

        if (mode == MotionChannelMode.Relative && IsConstant(curve, neutral))
        {
            return null;
        }

        var keys = new CurveKey[curve.Keys.Count];

        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = curve.Keys[i].ToCurveKey();
        }

        return keys;
    }

    private static ComponentCurve? FromKeys(CurveKey[]? keys)
    {
        if (keys is null || keys.Length == 0)
        {
            return null;
        }

        var curve = new ComponentCurve();

        foreach (CurveKey key in keys)
        {
            curve.Keys.Add(KeyAsset.FromCurveKey(key));
        }

        return curve;
    }

    // Unity 내보내기의 IsConstant와 같은 판정: 값은 허용 오차 안, 탄젠트는 정확히 0.
    private static bool IsConstant(ComponentCurve curve, float value)
    {
        foreach (KeyAsset k in curve.Keys)
        {
            if (Math.Abs(k.V - value) > MotionRules.Tolerance || k.InTangent != 0f || k.OutTangent != 0f)
            {
                return false;
            }
        }

        return true;
    }

    // 채널 과반의 mode를 기본값으로 — 저작 편의일 뿐 산출물에는 영향이 없다.
    private static MotionChannelMode GuessDefaultMode(MotionClip clip)
    {
        if (clip.Channels is null || clip.Channels.Length == 0)
        {
            return MotionChannelMode.Relative;
        }

        int absolute = clip.Channels.Count(c => c.Mode == MotionChannelMode.Absolute);
        return absolute * 2 > clip.Channels.Length ? MotionChannelMode.Absolute : MotionChannelMode.Relative;
    }
}
