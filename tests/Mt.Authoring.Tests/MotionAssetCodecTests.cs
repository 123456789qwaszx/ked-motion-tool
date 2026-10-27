using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Tests;

public class MotionAssetCodecTests
{
    [Fact]
    public void relative_채널의_전_구간_중립값_성분은_내보낼_때_빠진다_absolute는_남는다()
    {
        var relative = new ChannelAsset
        {
            Node = "N", Kind = MotionChannelKind.Scale, Mode = MotionChannelMode.Relative,
            X = Curve(Fixtures.Key(0f, 1f), Fixtures.Key(1f, 1f)),
            Y = Curve(Fixtures.Key(0f, 1f), Fixtures.Key(1f, 1.2f)),
        };
        var absolute = new ChannelAsset
        {
            Node = "N", Kind = MotionChannelKind.Euler, Mode = MotionChannelMode.Absolute,
            X = Curve(Fixtures.Key(0f, 0f), Fixtures.Key(1f, 0f)),
        };
        var asset = new MotionAsset { Name = "a", Rig = "character", Duration = 1f, End = MotionEnd.Commit };
        asset.Channels.Add(relative);
        asset.Channels.Add(absolute);

        MotionClip clip = MotionAssetCodec.ToClip(asset);

        Assert.Null(clip.Channels[0].X);
        Assert.NotNull(clip.Channels[0].Y);
        Assert.NotNull(clip.Channels[1].X);
        Assert.True(MotionAssetCodec.IsNeutralConstant(relative, Axis.X));
        Assert.False(MotionAssetCodec.IsNeutralConstant(relative, Axis.Y));
        Assert.False(MotionAssetCodec.IsNeutralConstant(absolute, Axis.X));
    }

    [Fact]
    public void 탄젠트가_0이_아니면_값이_중립이어도_상수가_아니다()
    {
        var channel = new ChannelAsset
        {
            Node = "N", Kind = MotionChannelKind.Position, Mode = MotionChannelMode.Relative,
            X = Curve(Fixtures.Key(0f, 0f, 0f, 5f), Fixtures.Key(1f, 0f, -5f, 0f)),
        };

        Assert.False(MotionAssetCodec.IsNeutralConstant(channel, Axis.X));
    }

    [Fact]
    public void 클립에서_올린_키는_전부_free이고_값을_그대로_지킨다()
    {
        var clip = new MotionClip
        {
            Name = "a", Rig = "character", Duration = 1f, End = MotionEnd.Commit,
            Channels = new[]
            {
                new MotionChannel
                {
                    Node = "N", Kind = MotionChannelKind.Position, Mode = MotionChannelMode.Absolute,
                    X = new[] { new CurveKey(0f, 1f, 2f, 3f), new CurveKey(1f, 4f, 5f, 5f) },
                },
            },
        };

        MotionAsset asset = MotionAssetCodec.FromClip(clip);

        Assert.Equal(MotionChannelMode.Absolute, asset.DefaultMode);
        ChannelAsset ch = Assert.Single(asset.Channels);
        Assert.Null(ch.Y);
        Assert.Null(ch.Z);
        Assert.All(ch.X!.Keys, k => Assert.Equal(TangentMode.Free, k.Tangent));
        Assert.True(ch.X.Keys[0].Broken);
        Assert.False(ch.X.Keys[1].Broken);
        Assert.Equal(new CurveKey(0f, 1f, 2f, 3f), ch.X.Keys[0].ToCurveKey());
    }

    private static ComponentCurve Curve(params KeyAsset[] keys) => new() { Keys = keys.ToList() };
}
