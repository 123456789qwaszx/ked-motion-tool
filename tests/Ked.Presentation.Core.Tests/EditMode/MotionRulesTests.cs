using System.Collections.Generic;
using Ked.Presentation.Core;
using NUnit.Framework;

// 모션 검증 규칙 — 내보내기·로더·저작 도구가 같은 함수를 쓴다. 어긋난 모션은 로그 + 무시(조용히 빠뜨리지 않는다).
// 규칙은 모양만 본다 — 값의 뜻(relative/absolute)은 커맨드가 정하므로(motions/2) 중립값 규칙이 없다.
public class MotionRulesTests
{
    private static CurveKey[] Keys(params (float t, float v)[] keys)
    {
        CurveKey[] result = new CurveKey[keys.Length];

        for (int i = 0; i < keys.Length; i++)
            result[i] = new CurveKey(keys[i].t, keys[i].v, 0f, 0f);

        return result;
    }

    private static MotionChannel Channel(string node = "CharacterPortrait_Track_Move_Y", MotionChannelKind kind = MotionChannelKind.Position,
        CurveKey[] x = null, CurveKey[] y = null, CurveKey[] z = null)
        => new() { Node = node, Kind = kind, X = x, Y = y, Z = z };

    private static MotionClip Clip(float duration = 0.6f, params MotionChannel[] channels)
        => new() { Name = "nod", Rig = "character", Duration = duration, End = MotionEnd.Commit, Channels = channels };

    private static MotionClip Restore(float duration = 0.6f, params MotionChannel[] channels)
        => new() { Name = "nod", Rig = "character", Duration = duration, End = MotionEnd.Restore, Channels = channels };

    private static MotionChannel Nod => Channel(y: Keys((0f, 0f), (0.3f, -18f), (0.6f, 0f)));

    private static readonly HashSet<string> Rig = new() { "CharacterPortrait_Track_Move_Y", "CharacterPortrait_SwayPivot", "CharSlot_Track_X", "CharSlot_Track_Focus" };

    [Test]
    public void 유효한_클립은_통과한다_스키마가_있어도_없어도()
    {
        MotionClip clip = Clip(0.6f, Nod, Channel("CharacterPortrait_SwayPivot", MotionChannelKind.Euler, z: Keys((0f, 3f), (0.6f, 0f))));

        Assert.IsTrue(MotionRules.TryValidate(clip, null, out string reason), reason);
        Assert.IsTrue(MotionRules.TryValidate(clip, Rig, out reason), reason);
    }

    [TestCase("Nod", "이름")]
    [TestCase("nod-1", "이름")]
    [TestCase("", "이름")]
    public void 이름_규칙(string name, string expect)
    {
        MotionClip clip = Clip(0.6f, Nod);
        clip.Name = name;

        Assert.IsFalse(MotionRules.TryValidate(clip, null, out string reason));
        StringAssert.Contains(expect, reason);
    }

    [Test]
    public void 길이는_0보다_커야_하고_채널은_하나_이상이다()
    {
        Assert.IsFalse(MotionRules.TryValidate(Clip(0f, Nod), null, out string reason));
        StringAssert.Contains("길이", reason);

        Assert.IsFalse(MotionRules.TryValidate(Clip(0.6f), null, out reason));
        StringAssert.Contains("채널", reason);
    }

    [Test]
    public void Track_Focus는_키잉할_수_없다()
    {
        MotionClip clip = Clip(0.6f, Channel("CharSlot_Track_Focus", x: Keys((0f, 0f), (0.6f, 10f))));

        Assert.IsFalse(MotionRules.TryValidate(clip, Rig, out string reason));
        StringAssert.Contains("Track_Focus", reason);
    }

    [Test]
    public void 스키마에_없는_노드는_거부한다_스키마를_줬을_때만()
    {
        MotionClip clip = Clip(0.6f, Channel("NoSuchNode", x: Keys((0f, 0f), (0.6f, 10f))));

        Assert.IsTrue(MotionRules.TryValidate(clip, null, out _));
        Assert.IsFalse(MotionRules.TryValidate(clip, Rig, out string reason));
        StringAssert.Contains("스키마", reason);
    }

    [Test]
    public void Position_Scale에는_Z가_없고_성분은_하나는_있어야_한다()
    {
        Assert.IsFalse(MotionRules.TryValidate(Clip(0.6f, Channel(z: Keys((0f, 0f), (0.6f, 1f)))), null, out string reason));
        StringAssert.Contains("Z", reason);

        Assert.IsFalse(MotionRules.TryValidate(Clip(0.6f, Channel()), null, out reason));
        StringAssert.Contains("성분", reason);
    }

    [Test]
    public void 성분_키_규칙_둘_이상_0에서_시작_길이에서_끝_오름차순()
    {
        Assert.IsFalse(MotionRules.TryValidate(Clip(0.6f, Channel(y: Keys((0f, 0f)))), null, out string reason));
        StringAssert.Contains("2개", reason);

        Assert.IsFalse(MotionRules.TryValidate(Clip(0.6f, Channel(y: Keys((0.1f, 0f), (0.6f, 0f)))), null, out reason));
        StringAssert.Contains("첫 키", reason);

        Assert.IsFalse(MotionRules.TryValidate(Clip(0.6f, Channel(y: Keys((0f, 0f), (0.5f, 0f)))), null, out reason));
        StringAssert.Contains("길이", reason);

        Assert.IsFalse(MotionRules.TryValidate(Clip(0.6f, Channel(y: Keys((0f, 0f), (0.4f, 1f), (0.4f, 2f), (0.6f, 0f)))), null, out reason));
        StringAssert.Contains("오름차순", reason);
    }

    [Test]
    public void 첫_키와_마지막_키의_값은_규칙이_보지_않는다_뜻은_커맨드가_정한다()
    {
        // 0프레임 140 → 길이에서 360: absolute면 140→360, relative면 지금 값+140 → +360. 둘 다 유효한 모션이다.
        CurveKey[] from140 = Keys((0f, 140f), (0.6f, 360f));

        Assert.IsTrue(MotionRules.TryValidate(Clip(0.6f, Channel(y: from140)), null, out string reason), reason);
        Assert.IsTrue(MotionRules.TryValidate(Restore(0.6f, Channel(y: from140)), null, out reason), reason);

        // 배율도 같다 — 0에서 시작하는 배율도 모양으로는 유효하다(absolute로 쓰면 뜻이 있다).
        Assert.IsTrue(MotionRules.TryValidate(Clip(0.6f, Channel(kind: MotionChannelKind.Scale, x: Keys((0f, 0f), (0.6f, 1f)))), null, out reason), reason);
    }

    [Test]
    public void restore와_commit은_모양_규칙이_같다()
    {
        // 돌아오는 흔들림 — 통과.
        Assert.IsTrue(MotionRules.TryValidate(Restore(0.6f, Nod), null, out string reason), reason);

        // 마지막 키가 남는 키라도 restore로 선언할 수 있다 — 재생이 끝에서 시작값을 되쓴다(튐은 작가의 선택, 툴이 경고).
        Assert.IsTrue(MotionRules.TryValidate(Restore(0.6f, Channel(y: Keys((0f, 0f), (0.6f, -18f)))), null, out reason), reason);
        Assert.IsTrue(MotionRules.TryValidate(Clip(0.6f, Channel(y: Keys((0f, 0f), (0.6f, -18f)))), null, out reason), reason);

        Assert.IsTrue(MotionRules.TryValidate(Restore(0.6f, Channel(kind: MotionChannelKind.Scale, x: Keys((0f, 1f), (0.3f, 1.2f), (0.6f, 1f)))), null, out reason), reason);
    }

    [Test]
    public void end는_restore_또는_commit이어야_한다()
    {
        MotionClip clip = Clip(0.6f, Nod);
        clip.End = (MotionEnd)7;

        Assert.IsFalse(MotionRules.TryValidate(clip, null, out string reason));
        StringAssert.Contains("end", reason);
    }

    [Test]
    public void 같은_노드_같은_종류의_채널은_둘일_수_없다()
    {
        MotionClip clip = Clip(0.6f, Nod, Channel(x: Keys((0f, 0f), (0.6f, 4f))));

        Assert.IsFalse(MotionRules.TryValidate(clip, null, out string reason));
        StringAssert.Contains("둘", reason);

        // 종류가 다르면 같은 노드라도 된다 — 위치와 회전을 한 노드에 같이 건다.
        clip.Channels[1] = Channel(kind: MotionChannelKind.Euler, z: Keys((0f, 0f), (0.6f, 4f)));
        Assert.IsTrue(MotionRules.TryValidate(clip, null, out reason), reason);
    }

    [Test]
    public void 허용_오차_안의_잡음은_통과한다()
    {
        MotionClip clip = Clip(0.6f, Channel(y: Keys((0.00001f, 0.00001f), (0.59999f, 3f))));

        Assert.IsTrue(MotionRules.TryValidate(clip, null, out string reason), reason);
    }
}
