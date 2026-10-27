using Ked.Presentation.Core;
using NUnit.Framework;

// 모션 샘플링·합성·클레임 — 정본이 코어라는 것을 값으로 못 박는다.
// 재생 샘플러·툴 프리뷰·리듀서가 전부 이 함수를 지난다.
public class MotionMathTests
{
    private const float Eps = 1e-4f;

    // 0초 0 → 1초 10, 탄젠트 0 (ease-in-out 모양)
    private static CurveKey[] Rise10 => new[] { new CurveKey(0f, 0f, 0f, 0f), new CurveKey(1f, 10f, 0f, 0f) };

    // 0초 5 → 1초 7
    private static CurveKey[] From5To7 => new[] { new CurveKey(0f, 5f, 0f, 0f), new CurveKey(1f, 7f, 0f, 0f) };

    private static MotionChannel Channel(MotionChannelKind kind, MotionChannelMode mode, CurveKey[] x = null, CurveKey[] y = null, CurveKey[] z = null)
        => new() { Node = "CharacterPortrait_Track_Move", Kind = kind, Mode = mode, X = x, Y = y, Z = z };

    [Test]
    public void 샘플은_성분마다_CurveFunctions이고_없는_성분은_0이다()
    {
        MotionChannel ch = Channel(MotionChannelKind.Position, MotionChannelMode.Relative, y: Rise10);

        for (int i = 0; i <= 8; i++)
        {
            float t = i / 8f;
            Vec3 v = MotionMath.Sample(ch, t);

            Assert.AreEqual(0f, v.X, 0f, "X는 없다 → 중립값 0");
            Assert.AreEqual(CurveFunctions.Evaluate(Rise10, t), v.Y, 0f, $"t={t}");
            Assert.AreEqual(0f, v.Z, 0f);
        }
    }

    [Test]
    public void 범위_밖은_끝값으로_클램프된다()
    {
        MotionChannel ch = Channel(MotionChannelKind.Position, MotionChannelMode.Relative, x: Rise10);

        Assert.AreEqual(0f, MotionMath.Sample(ch, -1f).X, Eps);
        Assert.AreEqual(10f, MotionMath.Sample(ch, 5f).X, Eps);
    }

    [Test]
    public void relative는_시작값에_더하고_없는_성분은_시작값을_둔다()
    {
        MotionChannel ch = Channel(MotionChannelKind.Position, MotionChannelMode.Relative, x: Rise10);
        Vec3 start = new(100f, 50f, 0f);

        Vec3 v = MotionMath.Compose(ch, start, 1f);

        Assert.AreEqual(110f, v.X, Eps);
        Assert.AreEqual(50f, v.Y, Eps, "Y는 없다 → 시작값");
    }

    [Test]
    public void absolute는_값_그_자체이고_없는_성분은_시작값을_둔다()
    {
        MotionChannel ch = Channel(MotionChannelKind.Euler, MotionChannelMode.Absolute, z: From5To7);
        Vec3 start = new(1f, 2f, 30f);

        Vec3 v0 = MotionMath.Compose(ch, start, 0f);
        Vec3 v1 = MotionMath.Compose(ch, start, 1f);

        Assert.AreEqual(5f, v0.Z, Eps, "t=0부터 키 값이다 — 시작값 30은 무시된다");
        Assert.AreEqual(7f, v1.Z, Eps);
        Assert.AreEqual(1f, v1.X, Eps, "X는 없다 → 시작값");
        Assert.AreEqual(2f, v1.Y, Eps);
    }

    [Test]
    public void relative_배율은_곱셈이고_없는_성분은_1이라_시작값_그대로다()
    {
        // 1 → 1.5 (relative 배율의 첫 키는 중립값 1)
        CurveKey[] up = { new CurveKey(0f, 1f, 0f, 0f), new CurveKey(1f, 1.5f, 0f, 0f) };
        MotionChannel ch = Channel(MotionChannelKind.Scale, MotionChannelMode.Relative, x: up);
        Vec3 start = new(2f, 3f, 1f);

        Vec3 v = MotionMath.Compose(ch, start, 1f);

        Assert.AreEqual(3f, v.X, Eps, "2 × 1.5");
        Assert.AreEqual(3f, v.Y, Eps, "Y는 없다 → ×1");
        Assert.AreEqual(1f, v.Z, Eps);
        Assert.AreEqual(1f, MotionMath.Neutral(MotionChannelKind.Scale), 0f);
        Assert.AreEqual(0f, MotionMath.Neutral(MotionChannelKind.Position), 0f);
        Assert.AreEqual(0f, MotionMath.Neutral(MotionChannelKind.Euler), 0f);
    }

    [Test]
    public void 끝값은_마지막_키를_합성한_것이고_길이에서의_Compose와_같다()
    {
        MotionChannel offset = Channel(MotionChannelKind.Position, MotionChannelMode.Relative, x: Rise10, y: Rise10);
        MotionChannel absolute = Channel(MotionChannelKind.Scale, MotionChannelMode.Absolute, x: From5To7);
        Vec3 start = new(3f, 4f, 1f);

        Assert.AreEqual(MotionMath.Compose(offset, start, 1f), MotionMath.EndValue(offset, start));
        Assert.AreEqual(MotionMath.Compose(absolute, start, 1f), MotionMath.EndValue(absolute, start));
        Assert.AreEqual(13f, MotionMath.EndValue(offset, start).X, Eps);
        Assert.AreEqual(1f, MotionMath.EndValue(absolute, start).Z, Eps, "Scale의 z는 시작값 그대로");
    }

    [Test]
    public void 클레임은_종류대로_나가고_값은_끝값이다()
    {
        Vec3 start = new(1f, 2f, 3f);

        StageNodeClaim pos = MotionMath.ClaimFor(Channel(MotionChannelKind.Position, MotionChannelMode.Relative, x: Rise10), "c1/N", start);
        StageNodeClaim scale = MotionMath.ClaimFor(Channel(MotionChannelKind.Scale, MotionChannelMode.Absolute, x: From5To7, y: From5To7), "c1/N", start);
        StageNodeClaim euler = MotionMath.ClaimFor(Channel(MotionChannelKind.Euler, MotionChannelMode.Absolute, z: From5To7), "c1/N", start);

        Assert.AreEqual(StageNodeClaimKind.AnchoredPosition, pos.Kind);
        Assert.AreEqual(new Vec2(11f, 2f), pos.Value.XY);

        Assert.AreEqual(StageNodeClaimKind.LocalScaleXY, scale.Kind);
        Assert.AreEqual(new Vec2(7f, 7f), scale.Value.XY);

        Assert.AreEqual(StageNodeClaimKind.LocalEulerAngles, euler.Kind);
        Assert.AreEqual(new Vec3(1f, 2f, 7f), euler.Value);
        Assert.AreEqual("c1/N", euler.NodeKey);
    }

    [Test]
    public void 시작값은_종류에_맞는_노드_상태다()
    {
        RectNodeState state = RectNodeState.StretchFull
            .WithAnchoredPosition(new Vec2(10f, 20f))
            .WithLocalScale(new Vec3(2f, 3f, 1f))
            .WithLocalEuler(new Vec3(0f, 0f, 45f));

        Assert.AreEqual(new Vec3(10f, 20f, 0f), MotionMath.StartOf(Channel(MotionChannelKind.Position, MotionChannelMode.Relative), state));
        Assert.AreEqual(new Vec3(2f, 3f, 1f), MotionMath.StartOf(Channel(MotionChannelKind.Scale, MotionChannelMode.Relative), state));
        Assert.AreEqual(new Vec3(0f, 0f, 45f), MotionMath.StartOf(Channel(MotionChannelKind.Euler, MotionChannelMode.Relative), state));
    }
}
