using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Validation;

/// <summary>규칙 위반을 한 번에 고치는 돕는 손(계획 2.4). 각각 편집기 명령 하나 = 되돌리기 하나.</summary>
public enum MotionFix
{
    /// <summary>마지막 키 값을 중립값으로(restore 경고).</summary>
    LastKeysToNeutral,

    /// <summary>첫 키를 시각 0으로 옮긴다.</summary>
    FirstKeysToZero,

    /// <summary>마지막 키를 길이로 옮긴다.</summary>
    EndKeysToDuration,

    /// <summary>길이를 가장 늦은 키에 맞춘다.</summary>
    DurationToLastKey,

    /// <summary>끝 규칙을 commit으로.</summary>
    EndToCommit,

    /// <summary>이름을 규칙(소문자·숫자·_)에 맞게, 겹치지 않게 고친다. 빈 이름은 motion.</summary>
    NameToValid,

    /// <summary>길이 자리에 키를 하나 넣는다(값은 곡선 위 — 마지막 키 값). 길이 밖의 키는 사라진다.</summary>
    KeyAtDuration,

    /// <summary>미리보기 mode를 absolute로(자리·깊이 컨트롤러가 든 모션).</summary>
    PreviewModeToAbsolute,

    /// <summary>자리·깊이 컨트롤러 채널을 <c>&lt;이름&gt;_place</c> 모션으로 떼어 낸다(두 클립·두 커맨드).</summary>
    SplitByGroup,
}

public static class MotionFixes
{
    public static string Label(MotionFix fix) => fix switch
    {
        MotionFix.LastKeysToNeutral => "마지막 키를 중립값으로",
        MotionFix.FirstKeysToZero => "첫 키를 0초로",
        MotionFix.EndKeysToDuration => "끝 키를 길이로 옮기기",
        MotionFix.DurationToLastKey => "길이를 마지막 키에 맞추기",
        MotionFix.EndToCommit => "끝을 commit으로",
        MotionFix.NameToValid => "이름 고치기 (소문자·숫자·_)",
        MotionFix.KeyAtDuration => "길이 자리에 키 넣기",
        MotionFix.PreviewModeToAbsolute => "미리보기를 absolute로",
        MotionFix.SplitByGroup => "두 모션으로 나누기 (자리·깊이 → _place)",
        _ => fix.ToString(),
    };

    /// <summary>코어 <c>MotionRules</c>의 문구에서 고칠 손을 고른다. 문구가 바뀌면 여기만 따라간다.</summary>
    public static IReadOnlyList<MotionFix> Suggest(MotionProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        string m = problem.Message;

        if (m.Contains("이름은 [a-z0-9_]+ 여야 한다", StringComparison.Ordinal))
        {
            return new[] { MotionFix.NameToValid };
        }

        if (m.Contains("마지막 키 시각이 길이와 다르다", StringComparison.Ordinal))
        {
            return new[] { MotionFix.EndKeysToDuration, MotionFix.DurationToLastKey, MotionFix.KeyAtDuration };
        }

        if (m.Contains("첫 키 시각이 0이 아니다", StringComparison.Ordinal))
        {
            return new[] { MotionFix.FirstKeysToZero };
        }

        if (m.Contains("restore인데 마지막 키 값이 중립값", StringComparison.Ordinal))
        {
            return new[] { MotionFix.LastKeysToNeutral, MotionFix.EndToCommit };
        }

        if (m.Contains(MotionValidator.AbsoluteControllerWarning, StringComparison.Ordinal))
        {
            return new[] { MotionFix.PreviewModeToAbsolute, MotionFix.SplitByGroup };
        }

        return Array.Empty<MotionFix>();
    }

    /// <summary>고친다. 할 것이 없으면 false(편집기 조작도 없다). <paramref name="controllers"/>는 두 모션으로 나누기가 쓴다.</summary>
    public static bool Apply(MotionEditor editor, string motionName, MotionFix fix, ControllersFile? controllers = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        MotionAsset motion = editor.Document.Find(motionName) ?? throw new KeyNotFoundException($"모션 '{motionName}'이 없다.");

        switch (fix)
        {
            case MotionFix.PreviewModeToAbsolute:
                if (motion.PreviewMode == MotionChannelMode.Absolute)
                {
                    return false;
                }

                editor.SetPreviewMode(motionName, MotionChannelMode.Absolute);
                return true;

            case MotionFix.SplitByGroup:
            {
                if (controllers?.Rig(motion.Rig) is not { } table)
                {
                    return false;
                }

                string newName = MotionNames.Unique(editor.Document, motion.Name + "_place");
                return editor.SplitMotion(motionName, newName, c => MotionValidator.IsAbsoluteController(c, table), MotionChannelMode.Absolute) is not null;
            }

            case MotionFix.NameToValid:
            {
                string fixedName = MotionNames.Unique(editor.Document, MotionNames.Sanitize(motion.Name), except: motion.Name);

                if (fixedName == motion.Name)
                {
                    return false;
                }

                editor.RenameMotion(motionName, fixedName);
                return true;
            }

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
                        bool changed = ApplyToCurve(fix, keys, motion, neutral);

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

    private static bool ApplyToCurve(MotionFix fix, List<KeyAsset> keys, MotionAsset motion, float neutral)
    {
        KeyAsset first = keys[0];
        KeyAsset last = keys[^1];

        switch (fix)
        {
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

            case MotionFix.KeyAtDuration:
            {
                if (Math.Abs(last.T - motion.Duration) <= MotionRules.Tolerance)
                {
                    return false;
                }

                List<KeyAsset> withKey = KeyOps.WithKeyOnCurve(keys, motion.Duration);
                withKey.RemoveAll(k => k.T > motion.Duration + MotionRules.Tolerance);
                keys.Clear();
                keys.AddRange(withKey.OrderBy(k => k.T));
                return true;
            }

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
