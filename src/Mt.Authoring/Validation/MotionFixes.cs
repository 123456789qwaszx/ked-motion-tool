using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;

namespace Mt.Authoring.Validation;

/// <summary>규칙 위반을 한 번에 고치는 돕는 손(계획 2.4). 각각 편집기 명령 하나 = 되돌리기 하나.</summary>
public enum MotionFix
{
    /// <summary>relative 채널의 첫 키 값을 중립값으로.</summary>
    FirstKeysToNeutral,

    /// <summary>마지막 키 값을 중립값으로(restore).</summary>
    LastKeysToNeutral,

    /// <summary>첫 키를 시각 0으로 옮긴다.</summary>
    FirstKeysToZero,

    /// <summary>마지막 키를 길이로 옮긴다.</summary>
    EndKeysToDuration,

    /// <summary>길이를 가장 늦은 키에 맞춘다.</summary>
    DurationToLastKey,

    /// <summary>모든 채널을 relative로.</summary>
    ChannelsToRelative,

    /// <summary>끝 규칙을 commit으로.</summary>
    EndToCommit,
}

public static class MotionFixes
{
    public static string Label(MotionFix fix) => fix switch
    {
        MotionFix.FirstKeysToNeutral => "첫 키를 중립값으로",
        MotionFix.LastKeysToNeutral => "마지막 키를 중립값으로",
        MotionFix.FirstKeysToZero => "첫 키를 0초로",
        MotionFix.EndKeysToDuration => "끝 키를 길이로 옮기기",
        MotionFix.DurationToLastKey => "길이를 마지막 키에 맞추기",
        MotionFix.ChannelsToRelative => "채널을 relative로",
        MotionFix.EndToCommit => "끝을 commit으로",
        _ => fix.ToString(),
    };

    /// <summary>코어 <c>MotionRules</c>의 문구에서 고칠 손을 고른다. 문구가 바뀌면 여기만 따라간다.</summary>
    public static IReadOnlyList<MotionFix> Suggest(MotionProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        string m = problem.Message;

        if (m.Contains("마지막 키 시각이 길이와 다르다", StringComparison.Ordinal))
        {
            return new[] { MotionFix.EndKeysToDuration, MotionFix.DurationToLastKey };
        }

        if (m.Contains("첫 키 시각이 0이 아니다", StringComparison.Ordinal))
        {
            return new[] { MotionFix.FirstKeysToZero };
        }

        if (m.Contains("relative 채널의 첫 키 값이 중립값", StringComparison.Ordinal))
        {
            return new[] { MotionFix.FirstKeysToNeutral };
        }

        if (m.Contains("restore 모션의 마지막 키 값이 중립값", StringComparison.Ordinal))
        {
            return new[] { MotionFix.LastKeysToNeutral, MotionFix.EndToCommit };
        }

        if (m.Contains("restore 모션의 채널은 relative여야 한다", StringComparison.Ordinal))
        {
            return new[] { MotionFix.ChannelsToRelative, MotionFix.EndToCommit };
        }

        return Array.Empty<MotionFix>();
    }

    /// <summary>고친다. 할 것이 없으면 false(편집기 조작도 없다).</summary>
    public static bool Apply(MotionEditor editor, string motionName, MotionFix fix)
    {
        ArgumentNullException.ThrowIfNull(editor);
        MotionAsset motion = editor.Document.Find(motionName) ?? throw new KeyNotFoundException($"모션 '{motionName}'이 없다.");

        switch (fix)
        {
            case MotionFix.DurationToLastKey:
            {
                List<float> times = KeyTimes.All(motion);

                if (times.Count == 0 || Math.Abs(times[^1] - motion.Duration) <= MotionRules.Tolerance)
                {
                    return false;
                }

                editor.SetDuration(motionName, times[^1]);
                return true;
            }

            case MotionFix.EndToCommit:
                if (motion.End == MotionEnd.Commit)
                {
                    return false;
                }

                editor.SetEnd(motionName, MotionEnd.Commit);
                return true;

            case MotionFix.ChannelsToRelative:
            {
                var edits = motion.Channels.Where(c => c.Mode != MotionChannelMode.Relative).ToList();

                if (edits.Count == 0)
                {
                    return false;
                }

                foreach (ChannelAsset channel in edits)
                {
                    editor.SetChannelMode(motionName, channel.Node, channel.Kind, MotionChannelMode.Relative);
                }

                return true;
            }

            default:
            {
                var edits = new List<(CurveRef Curve, IEnumerable<KeyAsset> Keys)>();

                foreach (ChannelAsset channel in motion.Channels)
                {
                    float neutral = MotionMath.Neutral(channel.Kind);

                    foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
                    {
                        if (channel.Get(axis) is not { } curve || curve.Keys.Count == 0)
                        {
                            continue;
                        }

                        List<KeyAsset> keys = curve.Keys.Select(Clone).ToList();
                        bool changed = ApplyToCurve(fix, keys, channel.Mode, motion, neutral);

                        if (changed)
                        {
                            edits.Add((new CurveRef(channel.Node, channel.Kind, axis), keys));
                        }
                    }
                }

                if (edits.Count == 0)
                {
                    return false;
                }

                editor.SetKeysMany(motionName, edits);
                return true;
            }
        }
    }

    private static bool ApplyToCurve(MotionFix fix, List<KeyAsset> keys, MotionChannelMode mode, MotionAsset motion, float neutral)
    {
        KeyAsset first = keys[0];
        KeyAsset last = keys[^1];

        switch (fix)
        {
            case MotionFix.FirstKeysToNeutral:
                if (mode != MotionChannelMode.Relative || Math.Abs(first.V - neutral) <= MotionRules.Tolerance)
                {
                    return false;
                }

                first.V = neutral;
                return true;

            case MotionFix.LastKeysToNeutral:
                if (Math.Abs(last.V - neutral) <= MotionRules.Tolerance)
                {
                    return false;
                }

                last.V = neutral;
                return true;

            case MotionFix.FirstKeysToZero:
                if (Math.Abs(first.T) <= MotionRules.Tolerance)
                {
                    return false;
                }

                first.T = 0f;
                keys.RemoveAll(k => !ReferenceEquals(k, first) && Math.Abs(k.T) <= MotionRules.Tolerance);
                return true;

            case MotionFix.EndKeysToDuration:
                if (Math.Abs(last.T - motion.Duration) <= MotionRules.Tolerance)
                {
                    return false;
                }

                last.T = motion.Duration;
                keys.RemoveAll(k => !ReferenceEquals(k, last) && Math.Abs(k.T - motion.Duration) <= MotionRules.Tolerance);
                // 길이를 줄였으면 길이 밖의 키는 사라진다 — 끝 키가 마지막이어야 한다.
                keys.RemoveAll(k => !ReferenceEquals(k, last) && k.T > motion.Duration);
                return true;

            default:
                return false;
        }
    }

    private static KeyAsset Clone(KeyAsset key) => new()
    {
        T = key.T,
        V = key.V,
        InTangent = key.InTangent,
        OutTangent = key.OutTangent,
        Tangent = key.Tangent,
        Broken = key.Broken,
    };
}
