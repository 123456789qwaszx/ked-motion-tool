using System;

namespace Ked.Presentation.Core
{
    /// <summary>채널이 쓰는 노드 속성. StageNodeClaimKind의 위치·배율·회전과 1:1이다.</summary>
    public enum MotionChannelKind
    {
        /// <summary>anchoredPosition (X·Y). 리그 로컬 px.</summary>
        Position,

        /// <summary>localScale (X·Y). z는 건드리지 않는다.</summary>
        Scale,

        /// <summary>localEulerAngles (X·Y·Z). 도.</summary>
        Euler,
    }

    /// <summary>
    /// 채널 값이 시작값에 대한 변화인지(relative), 그 자체인지(absolute).
    /// 배치 축은 "지금 자리에서"가 자연스럽고(relative), 연기 축은 "이 포즈로"가 자연스럽다(absolute).
    /// relative의 뜻은 종류마다 다르다 — 위치·회전은 **덧셈**, 배율은 **곱셈**(MotionMath.Neutral: 0 · 0 · 1).
    /// 채널마다 정한다 — 파일에 기본값은 없다. 빠지면 로더가 거부한다.
    /// </summary>
    public enum MotionChannelMode
    {
        Relative,
        Absolute,
    }

    /// <summary>
    /// 모션이 끝난 뒤 무엇이 남는가. 정지 프레임(리듀서)·장부·Skip·Load·Rollback이 전부 이 선언을 본다.
    /// </summary>
    public enum MotionEnd
    {
        /// <summary>
        /// 아무것도 남기지 않는다 — 흔들기·끄덕임. 모든 채널이 relative이고 마지막 키가 중립값이어야 한다(규칙이 검증).
        /// 그래서 끝에서 튀지 않고, 리듀서는 무변으로 접고, 장부에 게시할 것이 없다(gesture와 같다).
        /// </summary>
        Restore,

        /// <summary>
        /// 마지막 키가 남는다 — 고개 숙인 채로, 자세 바꾸기. 채널마다 마지막 키가 그 노드의 클레임이다.
        /// 중립값으로 끝나는 채널은 자연히 아무것도 남기지 않으므로, "어깨는 남고 고개는 돌아오는" 모션도 commit이다.
        /// </summary>
        Commit,
    }

    /// <summary>
    /// 모션의 채널 하나 — 노드 하나의 속성 하나를 시간에 따라 움직인다.
    /// 성분(X·Y·Z)은 따로 키를 갖고, null인 성분은 "건드리지 않는다"(시작값 유지).
    /// 키의 Time은 **초**다(이징 곡선의 정규화 0~1과 다르다). 마지막 키의 Time = 클립 길이.
    ///
    /// readonly가 아닌 이유: 커맨드 스펙([Serializable])에 실려 유니티 직렬화를 타야 한다 — CurveKey와 같은 사정.
    /// </summary>
    [Serializable]
    public struct MotionChannel
    {
        /// <summary>리그 스키마의 노드 id (예: CharacterPortrait_Track_Move_Y). 슬롯 접두사는 없다.</summary>
        public string Node;

        public MotionChannelKind Kind;
        public MotionChannelMode Mode;

        public CurveKey[] X;
        public CurveKey[] Y;

        /// <summary>Euler에서만 쓴다. Position·Scale이면 null이어야 한다(규칙).</summary>
        public CurveKey[] Z;

        public bool HasX => X is { Length: > 0 };
        public bool HasY => Y is { Length: > 0 };
        public bool HasZ => Z is { Length: > 0 };
    }

    /// <summary>
    /// 모션 하나 — 이름 · 어느 리그용인지 · 길이(초) · 채널들.
    ///
    /// 이징 곡선(curves.json)이 **모양**이라면 모션은 **내용**이다: 어느 노드의 어느 값이 몇 초에 얼마인지, 여러 채널이 동시에.
    /// 키 하나의 수학은 같다(CurveKey · CurveFunctions). 값은 리그 로컬 단위(px·배율·도)이고,
    /// 원근(DepthScale)·시차·카메라는 계층 위에서 곱해지므로 모션은 셋을 모른다.
    ///
    /// 종점은 평가 결과가 아니라 **키 값**이다 — commit이면 채널마다 마지막 키가 그 노드의 클레임이 되고(MotionMath.ClaimFor),
    /// restore면 남는 것이 없다. 그래서 "이징은 종점에 관여하지 않는다"가 그대로 서고, 리듀서는 샘플링 없이 정지 프레임을 낸다.
    /// </summary>
    [Serializable]
    public sealed class MotionClip
    {
        public string Name;

        /// <summary>리그 스키마 이름(rig-schemas.json의 rigKind). 로더가 노드 id를 이 스키마로 검증한다.</summary>
        public string Rig;

        /// <summary>초. 모든 채널의 모든 성분이 이 시각에 마지막 키를 둔다(규칙).</summary>
        public float Duration;

        /// <summary>끝난 뒤 무엇이 남는가. 파일에 기본값은 없다 — 빠지면 로더가 거부한다.</summary>
        public MotionEnd End;

        public MotionChannel[] Channels;
    }
}
