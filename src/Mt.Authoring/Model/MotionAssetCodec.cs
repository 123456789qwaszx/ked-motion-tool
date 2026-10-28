using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 저작본 ↔ 코어 클립. 수학·검증은 코어 것이므로 여기서는 모양만 바꾼다.
///
/// 키가 있는 성분은 전부 나간다 — 파일(motions/2)은 mode를 모르므로 "전 구간 중립값인 성분은 무변"이라는 판단을
/// 낼 때 할 수 없다(absolute로 쓰면 그 값으로 가는 뜻이 있다). Unity 내보내기도 같은 규칙이다.
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

            channels[i] = new MotionChannel
            {
                Node = c.Node,
                Kind = c.Kind,
                X = ToKeys(c.X),
                Y = ToKeys(c.Y),
                Z = ToKeys(c.Z),
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

    /// <summary>가져온 키는 전부 <see cref="TangentMode.Free"/>다 — 파일의 탄젠트 값을 그대로 지킨다. 재생 기준(relative/absolute)는 relative(커맨드의 생략값과 같다).</summary>
    public static MotionAsset FromClip(MotionClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        var asset = new MotionAsset
        {
            Name = clip.Name ?? "",
            Rig = clip.Rig ?? "",
            Duration = clip.Duration,
            End = clip.End,
            PreviewMode = MotionModeToken.Default,
        };

        if (clip.Channels is not null)
        {
            foreach (MotionChannel ch in clip.Channels)
            {
                asset.Channels.Add(new ChannelAsset
                {
                    Node = ch.Node ?? "",
                    Kind = ch.Kind,
                    X = FromKeys(ch.X),
                    Y = FromKeys(ch.Y),
                    Z = FromKeys(ch.Z),
                });
            }
        }

        return asset;
    }

    private static CurveKey[]? ToKeys(ComponentCurve? curve)
    {
        if (curve is null || curve.Keys.Count == 0)
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
}
