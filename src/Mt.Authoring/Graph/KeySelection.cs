using Mt.Authoring.Model;

namespace Mt.Authoring.Graph;

/// <summary>
/// 선택된 키들 — 그래프와 도프시트가 **같은 객체**를 든다(한 모델, 두 뷰). 바뀌면 <see cref="Changed"/> 한 번.
/// 여러 키를 한 번에 넣을 때는 <see cref="Set"/>·<see cref="AddRange"/>를 써서 이벤트가 키마다 나가지 않게 한다.
/// </summary>
public sealed class KeySelection : IReadOnlyCollection<KeyRef>
{
    private readonly HashSet<KeyRef> _keys = new();

    public event EventHandler? Changed;

    public int Count => _keys.Count;

    public bool Contains(KeyRef key) => _keys.Contains(key);

    public IEnumerator<KeyRef> GetEnumerator() => _keys.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Add(KeyRef key)
    {
        if (!_keys.Add(key))
        {
            return false;
        }

        Raise();
        return true;
    }

    public bool Remove(KeyRef key)
    {
        if (!_keys.Remove(key))
        {
            return false;
        }

        Raise();
        return true;
    }

    /// <summary>있으면 빼고 없으면 넣는다(Shift+클릭). 넣었으면 true.</summary>
    public bool Toggle(KeyRef key)
    {
        bool added = !_keys.Remove(key) && _keys.Add(key);
        Raise();
        return added;
    }

    public void Clear()
    {
        if (_keys.Count == 0)
        {
            return;
        }

        _keys.Clear();
        Raise();
    }

    /// <summary>통째로 바꾼다. 같은 집합이면 조용하다.</summary>
    public void Set(IEnumerable<KeyRef> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var next = new HashSet<KeyRef>(keys);

        if (next.SetEquals(_keys))
        {
            return;
        }

        _keys.Clear();
        _keys.UnionWith(next);
        Raise();
    }

    public void AddRange(IEnumerable<KeyRef> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        int before = _keys.Count;
        _keys.UnionWith(keys);

        if (_keys.Count != before)
        {
            Raise();
        }
    }

    public int RemoveWhere(Predicate<KeyRef> predicate)
    {
        int removed = _keys.RemoveWhere(predicate);

        if (removed > 0)
        {
            Raise();
        }

        return removed;
    }

    /// <summary>문서가 바뀐 뒤 더는 없는 키를 턴다(자리가 밀렸거나 지워졌다).</summary>
    public int Prune(MotionAsset? motion)
        => RemoveWhere(k => motion is null || k.Resolve(motion) is null);

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
