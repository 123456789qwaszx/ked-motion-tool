using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Presets;

/// <summary>프리셋의 한 성분 — 컨트롤러 성분에 (초, 값) 키. 값은 묶음의 mode대로(연기는 relative: 위치·회전 더하기, 배율 곱하기).</summary>
public sealed record PresetTrack(string ControllerId, Axis Axis, IReadOnlyList<(float T, float V)> Keys, TangentMode Tangent = TangentMode.Auto);

/// <summary>프리셋 모션 — 컨트롤러 표 위에 코드로 지은 저작본. 노드는 추가할 때 표가 푼다(리그·덤프가 달라도 같은 프리셋).</summary>
public sealed record MotionPreset(string Id, string Label, string Note, float Duration, MotionEnd End, IReadOnlyList<PresetTrack> Tracks)
{
    public string Name => $"{Label} ({Id})";
}

/// <summary>
/// 종이 인형극 대사 연기의 기본 9종(PLAN-controller §4) — 전부 연기(relative) 컨트롤러라 어느 자리·깊이에서도 같다.
/// restore 프리셋은 끝 키가 중립값(제자리), commit 프리셋(돌아서기)은 끝 값이 남는다. 모든 성분이 duration에서 끝난다.
/// </summary>
public static class MotionPresets
{
    public static IReadOnlyList<MotionPreset> All { get; } = new[]
    {
        new MotionPreset("nod", "끄덕", "몸을 살짝 내렸다 올린다 — 긍정·인사", 0.5f, MotionEnd.Restore, new[]
        {
            Track("body", Axis.Y, (0f, 0f), (0.2f, -18f), (0.5f, 0f)),
            Track("bow", Axis.Z, (0f, 0f), (0.2f, 4f), (0.5f, 0f)),
        }),
        new MotionPreset("shake_head", "젓기", "좌우로 저으며 잦아든다 — 부정", 0.6f, MotionEnd.Restore, new[]
        {
            Track("body", Axis.X, (0f, 0f), (0.1f, -12f), (0.25f, 12f), (0.4f, -8f), (0.5f, 4f), (0.6f, 0f)),
        }),
        new MotionPreset("hop", "깡충", "한 번 뛰었다 내려앉는다 — 기쁨·놀람", 0.5f, MotionEnd.Restore, new[]
        {
            Track("body", Axis.Y, (0f, 0f), (0.2f, 40f), (0.4f, 0f), (0.45f, -6f), (0.5f, 0f)),
            Track("squash", Axis.X, (0f, 1f), (0.2f, 0.95f), (0.4f, 1.04f), (0.5f, 1f)),
            Track("squash", Axis.Y, (0f, 1f), (0.2f, 1.06f), (0.4f, 0.96f), (0.5f, 1f)),
        }),
        new MotionPreset("flinch", "움찔", "뒤로 짧게 움츠렸다 돌아온다 — 놀람·겁", 0.4f, MotionEnd.Restore, new[]
        {
            Track("body", Axis.X, (0f, 0f), (0.08f, -10f), (0.4f, 0f)),
            Track("lean", Axis.Z, (0f, 0f), (0.08f, -4f), (0.4f, 0f)),
            Track("squash", Axis.Y, (0f, 1f), (0.08f, 0.96f), (0.4f, 1f)),
        }),
        new MotionPreset("tremble", "떨림", "잘게 좌우로 떤다 — 두려움·추위", 0.6f, MotionEnd.Restore, new[]
        {
            Track("shake", Axis.X, TangentMode.Linear, (0f, 0f), (0.05f, 3f), (0.1f, -3f), (0.15f, 3f), (0.2f, -3f), (0.25f, 3f), (0.3f, -3f), (0.35f, 3f), (0.4f, -3f), (0.45f, 3f), (0.5f, -3f), (0.55f, 2f), (0.6f, 0f)),
        }),
        new MotionPreset("bow", "숙임", "발을 축으로 숙였다 든다 — 인사·사과", 0.8f, MotionEnd.Restore, new[]
        {
            Track("bow", Axis.Z, (0f, 0f), (0.3f, 18f), (0.5f, 18f), (0.8f, 0f)),
        }),
        new MotionPreset("bounce", "바운스", "통통 두 번 — 들뜸", 0.6f, MotionEnd.Restore, new[]
        {
            Track("body", Axis.Y, (0f, 0f), (0.15f, 12f), (0.3f, 0f), (0.45f, 6f), (0.6f, 0f)),
            Track("squash", Axis.Y, (0f, 1f), (0.15f, 1.03f), (0.3f, 0.97f), (0.45f, 1.01f), (0.6f, 1f)),
        }),
        new MotionPreset("breath", "호흡", "천천히 부풀었다 가라앉는다 — 대기·루프", 2f, MotionEnd.Restore, new[]
        {
            Track("squash", Axis.Y, (0f, 1f), (1f, 1.015f), (2f, 1f)),
            Track("body", Axis.Y, (0f, 0f), (1f, 2f), (2f, 0f)),
        }),
        new MotionPreset("turn", "돌아서기", "방향을 뒤집는다(지금 방향 × -1) — commit이라 끝 값이 남는다", 0.3f, MotionEnd.Commit, new[]
        {
            Track("facing", Axis.X, (0f, 1f), (0.15f, 0.05f), (0.3f, -1f)),
        }),
    };

    public static MotionPreset? Find(string id) => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// 프리셋을 문서에 더한다 — 이름은 id(겹치면 id_2…), 리그는 준 것, 노드는 표가 푼다(주 노드). 되돌리기 하나.
    /// 표에 컨트롤러가 없거나 성분이 안 맞으면 던지고 문서는 그대로다.
    /// </summary>
    public static string Add(MotionEditor editor, MotionPreset preset, string rig, ControllerRig controllers)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(controllers);

        var resolved = new List<(Controller Controller, PresetTrack Track)>();

        foreach (PresetTrack track in preset.Tracks)
        {
            Controller c = controllers.Find(track.ControllerId)
                ?? throw new InvalidOperationException($"프리셋 '{preset.Id}': 리그 '{rig}'의 컨트롤러 표에 '{track.ControllerId}'가 없다 — presets/controllers.json");

            if (!c.Axes.Contains(track.Axis))
            {
                throw new InvalidOperationException($"프리셋 '{preset.Id}': 컨트롤러 '{c.Label}'에 {track.Axis} 성분이 없다");
            }

            resolved.Add((c, track));
        }

        string name = MotionNames.Unique(editor.Document, preset.Id);
        MotionChannelMode previewMode = resolved.Count > 0 ? resolved[0].Controller.Group.Mode : MotionChannelMode.Relative;

        editor.Batch(MotionChangeKind.Structure, () =>
        {
            editor.AddMotion(name, rig, preset.Duration, preset.End, previewMode);

            foreach ((Controller c, PresetTrack track) in resolved)
            {
                if (editor.Document.Find(name)!.FindChannel(c.Node, c.Kind) is null)
                {
                    editor.AddChannel(name, c.Node, c.Kind);
                }

                editor.SetKeys(name, c.Node, c.Kind, track.Axis, track.Keys.Select(k => new KeyAsset { T = k.T, V = k.V, Tangent = track.Tangent }));
            }
        });

        return name;
    }

    private static PresetTrack Track(string controller, Axis axis, params (float T, float V)[] keys) => new(controller, axis, keys);

    private static PresetTrack Track(string controller, Axis axis, TangentMode tangent, params (float T, float V)[] keys) => new(controller, axis, keys, tangent);
}
