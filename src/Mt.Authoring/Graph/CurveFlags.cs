namespace Mt.Authoring.Graph;

/// <summary>
/// 커브의 잠금(보이되 못 만짐)·뮤트(재생에서 뺌). 그래프·도프시트·뷰포트가 같은 객체를 든다.
/// 문서에 저장하지 않는 보기 상태다(마야의 템플릿/뮤트와 달리 장면에 남기지 않는다).
/// </summary>
public sealed class CurveFlags
{
    private readonly HashSet<CurveRef> _locked = new();
    private readonly HashSet<CurveRef> _muted = new();

    public event EventHandler? Changed;

    public bool IsLocked(CurveRef curve) => _locked.Contains(curve);

    public bool IsMuted(CurveRef curve) => _muted.Contains(curve);

    public IReadOnlyCollection<CurveRef> Locked => _locked;

    public IReadOnlyCollection<CurveRef> Muted => _muted;

    public void SetLocked(CurveRef curve, bool locked) => Set(_locked, curve, locked);

    public void SetMuted(CurveRef curve, bool muted) => Set(_muted, curve, muted);

    public void Clear()
    {
        if (_locked.Count == 0 && _muted.Count == 0)
        {
            return;
        }

        _locked.Clear();
        _muted.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Set(HashSet<CurveRef> set, CurveRef curve, bool on)
    {
        bool changed = on ? set.Add(curve) : set.Remove(curve);

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
