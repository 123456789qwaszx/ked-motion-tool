using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Presets;
using Mt.Authoring.Rig;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>프리셋 9종 — 컨트롤러 표 위에 지어 규칙·경고 없이 내보내진다.</summary>
public class MotionPresetsTests
{
    [Fact]
    public void 아홉_프리셋_전부_문제_없이_더해지고_내보내진다()
    {
        ControllerRig rig = BuiltinTuning.Controllers.Rig("character")!;
        var editor = new MotionEditor(new MotionDocument());

        Assert.Equal(new[] { "nod", "shake_head", "hop", "flinch", "tremble", "bow", "bounce", "breath", "turn" }, MotionPresets.All.Select(p => p.Id));

        foreach (MotionPreset preset in MotionPresets.All)
        {
            long revision = editor.Revision;
            Assert.Equal(preset.Id, MotionPresets.Add(editor, preset, "character", rig));
            Assert.Equal(revision + 1, editor.Revision); // 되돌리기 하나
            MotionAsset motion = editor.Document.Find(preset.Id)!;
            Assert.Equal(preset.Duration, motion.Duration);
            Assert.Equal(preset.End, motion.End);
            Assert.Equal(MotionChannelMode.Relative, motion.PreviewMode); // 전부 연기 묶음
            Assert.All(motion.Channels.SelectMany(c => new[] { c.X, c.Y, c.Z }).Where(c => c is not null), curve => Assert.Equal(preset.Duration, curve!.Keys[^1].T));
        }

        // 규칙(막음)도 경고(restore 끝 중립·absolute 컨트롤러)도 없다.
        Assert.Empty(MotionValidator.Validate(editor.Document, BuiltinTuning.RigSchemas.NodeIds, BuiltinTuning.Controllers));
        Assert.True(MotionExport.TryExport(editor.Document, BuiltinTuning.RigSchemas.NodeIds, out string json, out _));
        Assert.Contains("\"turn\"", json, StringComparison.Ordinal);

        // 끄덕은 몸(Track_Move) Y와 숙임(SwayPivot) Z. 돌아서기는 방향(ActingScale_X) X가 1 → -1(commit).
        MotionAsset nod = editor.Document.Find("nod")!;
        Assert.Equal(new[] { "CharacterPortrait_Track_Move", "CharacterPortrait_SwayPivot" }, nod.Channels.Select(c => c.Node));
        Assert.Equal(-18f, nod.FindChannel("CharacterPortrait_Track_Move", MotionChannelKind.Position)!.Y!.Keys[1].V);
        Assert.Equal(-1f, editor.Document.Find("turn")!.FindChannel("CharacterPortrait_ActingScale_X", MotionChannelKind.Scale)!.X!.Keys[^1].V);

        // 같은 프리셋을 또 더하면 nod_2. 되돌리기 한 번이면 통째로 없어진다.
        Assert.Equal("nod_2", MotionPresets.Add(editor, MotionPresets.Find("nod")!, "character", rig));
        editor.Undo();
        Assert.Null(editor.Document.Find("nod_2"));
        Assert.NotNull(editor.Document.Find("nod"));
    }

    [Fact]
    public void 표에_컨트롤러가_없으면_던지고_문서는_그대로다()
    {
        ControllerRig thin = ControllersFile.Parse("""
            { "schema": "controllers/1", "rigs": [ { "rigKind": "character", "groups": [ { "id": "acting", "mode": "relative" } ],
              "controllers": [ { "id": "body", "group": "acting", "node": "CharacterPortrait_Track_Move", "kind": "position", "axes": ["x", "y"] } ] } ] }
            """).Rig("character")!;
        var editor = new MotionEditor(new MotionDocument());
        long revision = editor.Revision;

        Assert.Equal("shake_head", MotionPresets.Add(editor, MotionPresets.Find("shake_head")!, "character", thin)); // 몸만 쓴다
        Assert.Throws<InvalidOperationException>(() => MotionPresets.Add(editor, MotionPresets.Find("bow")!, "character", thin));
        Assert.Null(editor.Document.Find("bow"));
        Assert.Equal(revision + 1, editor.Revision);
        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.Empty(editor.Document.Motions);
        Assert.False(editor.CanUndo);
    }
}
