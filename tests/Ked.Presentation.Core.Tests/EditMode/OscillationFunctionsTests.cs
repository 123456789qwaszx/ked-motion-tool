using Ked.Presentation.Core;
using NUnit.Framework;

// gesture의 진동 평가 — 정본이 코어라는 것을 값으로 못 박는다.
// 툴 프리뷰가 같은 함수를 쓰므로 여기 값이 곧 "보이는 모양 = 재생하는 모양"의 씨앗이다.
public class OscillationFunctionsTests
{
    private const float Eps = 1e-4f;

    [TestCase(0f, 0f)]
    [TestCase(0.25f, 0.70710678f)]
    [TestCase(0.5f, 1f)]
    [TestCase(0.75f, 0.70710678f)]
    [TestCase(1f, 0f)]
    public void 기본_혹은_sin_파이t다(float t, float expected)
    {
        Assert.AreEqual(expected, OscillationFunctions.Bump(t), Eps, $"t={t}");
    }

    [Test]
    public void 곡선이_없으면_기본_혹으로_평가한다()
    {
        for (int i = 0; i <= 16; i++)
        {
            float t = i / 16f;

            Assert.AreEqual(OscillationFunctions.Bump(t), OscillationFunctions.Evaluate(null, t), 0f);
            Assert.AreEqual(
                OscillationFunctions.Bump(t), OscillationFunctions.Evaluate(new CurveKey[0], t), 0f);
        }
    }

    [Test]
    public void 양_끝은_언제나_제자리다()
    {
        // 순변위 0이 이 커맨드의 정체다 — 기본 혹도, 진동 곡선도 양 끝이 0이어야 한다.
        CurveKey[] custom =
        {
            new(0f, 0f, 0f, 4f),
            new(0.3f, 1f, 0f, 0f),
            new(0.65f, -0.4f, 0f, 0f),
            new(1f, 0f, -1f, 0f),
        };

        Assert.AreEqual(0f, OscillationFunctions.Evaluate(custom, 0f), Eps);
        Assert.AreEqual(0f, OscillationFunctions.Evaluate(custom, 1f), Eps);

        // 중간은 자유다 — 음수(반대 방향)도 정상이다.
        Assert.Less(OscillationFunctions.Evaluate(custom, 0.65f), 0f);
        Assert.Greater(OscillationFunctions.Evaluate(custom, 0.3f), 0f);
    }

    // ── 핑퐁: 표준 이징을 왕복의 절반으로 읽는다 ─────────────────

    private static readonly EaseKind[] Sampled =
    {
        EaseKind.Linear, EaseKind.OutQuad, EaseKind.OutCubic, EaseKind.InOutSine,
        EaseKind.OutBack, EaseKind.OutBounce, EaseKind.OutElastic,
    };

    [Test]
    public void 핑퐁은_양_끝이_0이고_가운데가_최대다()
    {
        // 순변위 0이 수식으로 지켜지는 자리다 — 진폭을 곱해도 시작·끝이 제자리다.
        foreach (EaseKind kind in Sampled)
        {
            Assert.AreEqual(0f, OscillationFunctions.PingPong(kind, 0f), Eps, $"{kind} t=0");
            Assert.AreEqual(0f, OscillationFunctions.PingPong(kind, 1f), Eps, $"{kind} t=1");

            // 가운데는 이징의 종점이다(전부 1로 끝난다 — Flash 계열만 예외).
            Assert.AreEqual(
                EaseFunctions.Evaluate(kind, 1f),
                OscillationFunctions.PingPong(kind, 0.5f),
                Eps, $"{kind} t=0.5");
        }
    }

    [Test]
    public void 핑퐁의_중간값이_EaseFunctions와_일치한다()
    {
        // 전반부는 이징을 2배속으로, 후반부는 그 되감기로 읽는다.
        foreach (EaseKind kind in Sampled)
        {
            for (int i = 0; i <= 32; i++)
            {
                float t = i / 64f;   // 전반부 [0,0.5]

                Assert.AreEqual(
                    EaseFunctions.Evaluate(kind, t * 2f),
                    OscillationFunctions.PingPong(kind, t),
                    Eps, $"{kind} t={t}");

                // 좌우 대칭 — 나갔다 그대로 돌아온다.
                Assert.AreEqual(
                    OscillationFunctions.PingPong(kind, t),
                    OscillationFunctions.PingPong(kind, 1f - t),
                    Eps, $"{kind} 대칭 t={t}");
            }
        }
    }

    [Test]
    public void 진동_재료는_곡선_이징_기본혹_순으로_이긴다()
    {
        CurveKey[] keys = { new(0f, 0f, 0f, 0f), new(0.5f, 0.5f, 0f, 0f), new(1f, 0f, 0f, 0f) };

        // 곡선이 있으면 이징이 있어도 곡선이 이긴다.
        OscillationSource both = OscillationSource.FromCurve(keys);
        both.UseEase = true;
        both.Ease = EaseKind.OutBounce;

        Assert.AreEqual(
            CurveFunctions.Evaluate(keys, 0.5f),
            OscillationFunctions.Evaluate(both, 0.5f), Eps, "곡선이 이긴다");

        // 곡선이 없으면 이징의 핑퐁.
        Assert.AreEqual(
            OscillationFunctions.PingPong(EaseKind.OutBounce, 0.3f),
            OscillationFunctions.Evaluate(OscillationSource.FromEase(EaseKind.OutBounce), 0.3f),
            Eps, "이징이면 핑퐁");

        // 둘 다 없으면 기본 혹.
        Assert.AreEqual(
            OscillationFunctions.Bump(0.3f),
            OscillationFunctions.Evaluate(OscillationSource.Default, 0.3f),
            Eps, "기본은 sin πt다");
    }

    [Test]
    public void 진동_곡선과_이동_곡선이_끝값으로_갈린다()
    {
        CurveKey[] oscillation = { new(0f, 0f, 0f, 0f), new(1f, 0f, 0f, 0f) };
        CurveKey[] motion = { new(0f, 0f, 0f, 1f), new(1f, 1f, 1f, 0f) };
        CurveKey[] neither = { new(0f, 0f, 0f, 0f), new(1f, 0.5f, 0f, 0f) };

        Assert.IsTrue(CurveKindRules.TryClassify(oscillation, out CurveKind oscKind, out _));
        Assert.AreEqual(CurveKind.Oscillation, oscKind);

        Assert.IsTrue(CurveKindRules.TryClassify(motion, out CurveKind motionKind, out _));
        Assert.AreEqual(CurveKind.Motion, motionKind);

        Assert.IsFalse(CurveKindRules.TryClassify(neither, out _, out string why));
        Assert.That(why, Does.Contain("1도 0도"));
    }
}
