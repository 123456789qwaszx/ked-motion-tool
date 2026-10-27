using Ked.Presentation.Core;

namespace Mt.Authoring.Tests;

public class AuthoringFormatTests
{
    [Fact]
    public void 내보내기_스키마는_무대_런타임_규약과_같다()
    {
        // 산출물 스키마는 툴이 정하지 않는다 — ked-stage-runtime docs/motion-contract.md의
        // motions/1을 그대로 따른다. 규약이 올라가면 이 글자부터 바꾼다.
        Assert.Equal("motions/1", AuthoringFormat.ExportSchema);
    }

    [Fact]
    public void 저작_원본_스키마는_산출물_스키마와_다르다()
    {
        Assert.NotEqual(AuthoringFormat.AuthoringSchema, AuthoringFormat.ExportSchema);
        Assert.EndsWith(".json", AuthoringFormat.AuthoringExtension);
    }

    [Fact]
    public void 반입_코어의_모션_규칙이_보인다()
    {
        // Mt.Authoring → Ked.Presentation.Core 참조가 살아 있는지. 초점 트랙은 모션이 건드릴 수 없다.
        Assert.Contains("CharSlot_Track_Focus", MotionRules.ForbiddenNodes);
    }
}
