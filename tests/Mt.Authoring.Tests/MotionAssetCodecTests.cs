using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Tests;

public class MotionAssetCodecTests
{
    [Fact]
    public void 키가_있는_성분은_전부_나간다_전_구간_중립값인_상수도()
    {
        // motions/2는 mode를 모른다 — 상수 1이 "무변"(relative)인지 "1로"(absolute)인지 낼 때 정할 수 없으니 그대로 싣는다.
        var scale = new ChannelAsset
        {
            Node = "N", Kind = MotionChannelKind.Scale,
            X = Curve(Fixtures.Key(0f, 1f), Fixtures.Key(1f, 1f)),
            Y = Curve(Fixtures.Key(0f, 1f), Fixtures.Key(1f, 1.2f)),
        };
        var euler = new ChannelAsset
        {
            Node = "N", Kind = MotionChannelKind.Euler,
            X = Curve(Fixtures.Key(0f, 0f), Fixtures.Key(1f, 0f)),
        };
        var asset = new MotionAsset { Name = "a", Rig = "character", Duration = 1f, End = MotionEnd.Commit };
        asset.Channels.Add(scale);
        asset.Channels.Add(euler);

        MotionClip clip = MotionAssetCodec.ToClip(asset);

        Assert.NotNull(clip.Channels[0].X);
        Assert.NotNull(clip.Channels[0].Y);
        Assert.Null(clip.Channels[0].Z);
        Assert.NotNull(clip.Channels[1].X);
        Assert.Equal(1f, clip.Channels[0].X![1].Value);
    }

    [Fact]
    public void 클립에서_올린_키는_전부_free이고_값을_그대로_지키며_미리보기_mode는_relative다()
    {
        var clip = new MotionClip
        {
            Name = "a", Rig = "character", Duration = 1f, End = MotionEnd.Commit,
            Channels = new[]
            {
                new MotionChannel
                {
                    Node = "N", Kind = MotionChannelKind.Position,
                    X = new[] { new CurveKey(0f, 1f, 2f, 3f), new CurveKey(1f, 4f, 5f, 5f) },
                },
            },
        };

        MotionAsset asset = MotionAssetCodec.FromClip(clip);

        Assert.Equal(MotionChannelMode.Relative, asset.PreviewMode); // 커맨드의 생략값과 같다
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
