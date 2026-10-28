using Mt.Authoring;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;
using Ked.Presentation.Core;

namespace Mt.App.Services;

/// <summary>
/// 열린 문서 하나의 살림 — 경로·편집기·리그 덤프·저장 여부. 화면은 이것을 통해서만 파일에 닿는다.
/// 파일 선택 창은 화면의 일이고, 여기는 경로를 받아 읽고 쓴다(헤드리스 테스트가 그대로 쓴다).
/// </summary>
public sealed class DocumentSession
{
    private long _savedRevision;

    public DocumentSession()
    {
        Editor = new MotionEditor(new MotionDocument());
        UseBuiltinTuning();
    }

    /// <summary>내장 기본값으로 — 덤프 파일이 없는 문서의 시작 상태.</summary>
    private void UseBuiltinTuning()
    {
        Controllers = BuiltinTuning.Controllers;
        Rig = BuiltinTuning.RigSchemas;
        RigPath = null;
        Depth = BuiltinTuning.Depth;
        Focus = BuiltinTuning.Focus;
        StageSize = BuiltinTuning.BaseResolution;
    }

    public MotionEditor Editor { get; private set; }

    public string? DocumentPath { get; private set; }

    /// <summary>리그 덤프 — 파일을 싣지 않았으면 내장 기본값(<see cref="BuiltinTuning"/>). null이 아니다.</summary>
    public RigSchemaFile? Rig { get; private set; }

    /// <summary>실은 덤프 파일의 경로. null이면 내장 리그다.</summary>
    public string? RigPath { get; private set; }

    public bool IsBuiltinRig => RigPath is null;

    /// <summary>덤프 옆 presets/depth.json. 없으면 레이어는 그리는 순서만 정한다.</summary>
    public DepthTuningFile? Depth { get; private set; }

    /// <summary>덤프 옆 presets/focus.json — 배역별 focus 오프셋. 없으면 base 오프셋 없이(0).</summary>
    public FocusTuningFile? Focus { get; private set; }

    /// <summary>컨트롤러 표 — 덤프 옆 <c>presets/controllers.json</c>, 없으면 내장본. 리그에 없는 노드를 가리키면 상태 줄에 알린다.</summary>
    public ControllersFile Controllers { get; private set; } = BuiltinTuning.Controllers;

    /// <summary>선택 리그의 컨트롤러 표. 표에 없는 리그는 null — 노드로 다룬다.</summary>
    public ControllerRig? ControllersOf(string? rigKind) => Controllers.Rig(rigKind);

    public string Status { get; private set; } = "";

    /// <summary>미리보기 초상 PNG의 절대 경로. 문서에는 문서 기준 상대 경로로 적힌다.</summary>
    /// <summary>오브젝트의 초상 파일(절대 경로). 문서에 없거나 파일이 없으면 null.</summary>
    public string? PortraitPathOf(SceneObject o)
    {
        ArgumentNullException.ThrowIfNull(o);

        if (string.IsNullOrEmpty(o.Portrait))
        {
            return null;
        }

        string resolved = ResolveFromDocument(o.Portrait);
        return File.Exists(resolved) ? resolved : null;
    }

    /// <summary>기준 해상도(무대 크기). base-resolution.json이 없으면 1920×1080.</summary>
    public Vec2 StageSize { get; private set; } = BaseResolutionFile.Default;

    public bool IsDirty => Editor.Revision != _savedRevision;

    /// <summary>문서·편집기가 통째로 갈렸다(새로/열기). 화면은 편집기를 다시 묶는다.</summary>
    public event EventHandler? DocumentReplaced;

    public event EventHandler? StatusChanged;

    /// <summary>리그 덤프·초상·기준 해상도가 바뀌었다. 뷰포트가 다시 싣는다.</summary>
    public event EventHandler? AssetsChanged;

    public IReadOnlyDictionary<string, ICollection<string>>? RigNodeIds => Rig?.NodeIds;

    public IReadOnlyList<MotionProblem> Problems => MotionValidator.Validate(Editor.Document, RigNodeIds);

    public void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void New()
    {
        Replace(new MotionDocument(), null);
        SetStatus("새 문서.");
    }

    public void Open(string path)
    {
        MotionDocument document = MotionDocumentJson.Read(File.ReadAllText(path));
        Replace(document, path);
        TryLoadRigFromDocument();
        TryLoadAssetsFromDocument();
        AssetsChanged?.Invoke(this, EventArgs.Empty);
        SetStatus($"열었다: {path}");
    }

    /// <summary>자동 저장 — 문서 옆 <c>*.autosave.motion-authoring.json</c>. 저장 여부(IsDirty)는 건드리지 않는다. 경로가 없으면 안 한다.</summary>
    public string? AutoSave()
    {
        if (DocumentPath is null || !IsDirty)
        {
            return null;
        }

        string path = AutoSavePathFor(DocumentPath);
        File.WriteAllText(path, MotionDocumentJson.Write(Editor.Document));
        SetStatus($"자동 저장: {Path.GetFileName(path)}");
        return path;
    }

    public static string AutoSavePathFor(string documentPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(documentPath)) ?? ".";
        string name = Path.GetFileName(documentPath);
        string stem = name.EndsWith(AuthoringFormat.AuthoringExtension, StringComparison.OrdinalIgnoreCase)
            ? name[..^AuthoringFormat.AuthoringExtension.Length]
            : Path.GetFileNameWithoutExtension(name);
        return Path.Combine(directory, stem + ".autosave" + AuthoringFormat.AuthoringExtension);
    }

    public void Save(string? path = null)
    {
        path ??= DocumentPath ?? throw new InvalidOperationException("저장할 경로가 없다 — 다른 이름으로 저장을 쓴다.");
        File.WriteAllText(path, MotionDocumentJson.Write(Editor.Document));
        DocumentPath = path;
        _savedRevision = Editor.Revision;
        SetStatus($"저장했다: {path}");
    }

    /// <summary>리그 덤프를 싣고 문서에 경로(문서 기준 상대)와 해시를 적는다.</summary>
    public void LoadRig(string path)
    {
        RigSchemaFile rig = RigSchemaFile.Load(path);
        Rig = rig;
        RigPath = path;
        Editor.SetRigSchemas(RelativeToDocument(path), rig.Sha256);

        // 덤프 옆의 base-resolution.json·presets/depth.json을 같이 싣는다 — ExportedTuning 폴더를 통째로 복사하는 관례. 옆에 없으면 내장 기본값.
        string? depthPath = DepthTuningFile.TryFindBeside(path);
        Depth = BuiltinTuning.Depth;

        if (depthPath is not null)
        {
            try
            {
                Depth = DepthTuningFile.Load(depthPath);
            }
            catch (Exception exception)
            {
                SetStatus($"depth.json을 읽지 못했다: {exception.Message} — 깊이는 그리는 순서만 정한다.");
            }
        }

        string? focusPath = FocusTuningFile.TryFindBeside(path);
        Focus = BuiltinTuning.Focus;

        if (focusPath is not null)
        {
            try
            {
                Focus = FocusTuningFile.Load(focusPath);
            }
            catch (Exception exception)
            {
                SetStatus($"focus.json을 읽지 못했다: {exception.Message} — 배역 오프셋 없이.");
            }
        }

        string? baseResolution = BaseResolutionFile.TryFindBeside(path);

        if (baseResolution is not null)
        {
            StageSize = BaseResolutionFile.Load(baseResolution);
            Editor.SetBaseResolution(RelativeToDocument(baseResolution));
        }

        string? controllersPath = LoadControllersBeside(path);

        AssetsChanged?.Invoke(this, EventArgs.Empty);
        SetStatus($"리그 덤프: {string.Join(", ", rig.RigKinds)} ({Path.GetFileName(path)})"
                  + (baseResolution is null ? " — base-resolution.json이 옆에 없어 1920×1080으로 둔다" : $" · 무대 {StageSize.X:0}×{StageSize.Y:0}")
                  + (depthPath is null ? " · depth.json 없음(내장 깊이 커브)" : " · 깊이 커브")
                  + (focusPath is null ? " · focus 없음(내장 표)" : " · focus 표")
                  + (controllersPath is null ? " · 컨트롤러 내장 표" : " · 컨트롤러 표"));
    }

    /// <summary>덤프 옆 컨트롤러 표를 싣는다(없으면 내장본). 리그에 없는 노드를 가리키면 상태 줄에 알리고 그대로 쓴다. 찾은 경로를 돌려준다.</summary>
    private string? LoadControllersBeside(string rigSchemasPath)
    {
        string? controllersPath = ControllersFile.TryFindBeside(rigSchemasPath);
        Controllers = BuiltinTuning.Controllers;

        if (controllersPath is not null)
        {
            try
            {
                Controllers = ControllersFile.Load(controllersPath);
            }
            catch (Exception exception)
            {
                SetStatus($"controllers.json을 읽지 못했다: {exception.Message} — 내장 컨트롤러 표로 둔다.");
                controllersPath = null;
            }
        }

        if (Rig is { } rig)
        {
            foreach (string rigKind in Controllers.RigKinds)
            {
                if (rig.NodeIds.TryGetValue(rigKind, out ICollection<string>? ids) && Controllers.TryGetRig(rigKind, out ControllerRig table))
                {
                    IReadOnlyList<string> problems = table.Validate(ids);

                    if (problems.Count > 0)
                    {
                        SetStatus($"컨트롤러 표가 리그와 어긋난다: {string.Join(" · ", problems)}");
                    }
                }
            }
        }

        return controllersPath;
    }

    /// <summary>오브젝트의 미리보기 초상. 문서에 문서 기준 상대 경로로 적힌다 — 산출물과는 무관하다.</summary>
    public void SetPortrait(string objectId, string? path)
    {
        if (path is not null && !File.Exists(path))
        {
            throw new FileNotFoundException("초상 파일이 없다.", path);
        }

        Editor.SetObjectPortrait(null, objectId, path is null ? null : RelativeToDocument(path));
        AssetsChanged?.Invoke(this, EventArgs.Empty);
        SetStatus(path is null ? $"{objectId}: 초상을 뺐다." : $"{objectId}: 초상 {Path.GetFileName(path)}");
    }

    /// <summary><c>motions/1</c>을 읽어 문서로 올린다. 이름이 겹치면 바꿔 넣는다.</summary>
    public IReadOnlyList<string> ImportMotions(string path)
    {
        if (!MotionsJson.TryRead(File.ReadAllText(path), out List<MotionClip> clips, out List<string> problems))
        {
            SetStatus($"가져오기 실패: {string.Join(" ", problems)}");
            return Array.Empty<string>();
        }

        IReadOnlyList<string> imported = Editor.ImportClips(clips, replace: true);
        SetStatus(problems.Count == 0
            ? $"가져왔다: {string.Join(", ", imported)}"
            : $"가져왔다: {string.Join(", ", imported)} — 건너뜀 {problems.Count}건: {string.Join(" ", problems)}");
        return imported;
    }

    /// <summary>문제가 0건일 때만 쓴다. 정규 바이트(UTF-8 BOM 없음·LF)로 — Unity 내보내기와 같은 바이트다.</summary>
    /// <param name="filter">내보낼 모션(null이면 전부). <paramref name="scopeLabel"/>은 상태 줄 글자.</param>
    public bool Export(string path, Func<MotionAsset, bool>? filter = null, string? scopeLabel = null)
    {
        if (!MotionExport.TryExport(Editor.Document, RigNodeIds, out string json, out IReadOnlyList<MotionProblem> problems, filter))
        {
            SetStatus($"내보내기 막힘 — 문제 {problems.Count}건을 먼저 고친다.");
            return false;
        }

        File.WriteAllBytes(path, new System.Text.UTF8Encoding(false).GetBytes(json));
        int count = Editor.Document.Motions.Count(m => filter is null || filter(m));
        SetStatus($"내보냈다: {path} — 모션 {count}개{(scopeLabel is null ? "" : $" ({scopeLabel})")}");
        return true;
    }

    private void Replace(MotionDocument document, string? path)
    {
        Editor = new MotionEditor(document);
        DocumentPath = path;
        _savedRevision = Editor.Revision;
        UseBuiltinTuning();
        DocumentReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>문서가 적어 둔 초상·기준 해상도를 찾아 싣는다. 없는 파일은 상태 줄에 남기고 넘어간다.</summary>
    private void TryLoadAssetsFromDocument()
    {
        MotionDocument document = Editor.Document;

        if (document.BaseResolution is { Path: { Length: > 0 } basePath })
        {
            string resolved = ResolveFromDocument(basePath);

            if (File.Exists(resolved))
            {
                StageSize = BaseResolutionFile.Load(resolved);
            }
            else
            {
                SetStatus($"기준 해상도 파일을 찾지 못했다: {resolved} — 1920×1080으로 둔다.");
            }
        }

        foreach (SceneAsset scene in document.Scenes)
        {
            foreach (SceneObject o in scene.Objects)
            {
                if (!string.IsNullOrEmpty(o.Portrait) && !File.Exists(ResolveFromDocument(o.Portrait)))
                {
                    SetStatus($"{o.Id}의 초상 파일을 찾지 못했다: {ResolveFromDocument(o.Portrait)} — 자리표시로 보인다.");
                }
            }
        }
    }

    /// <summary>문서가 적어 둔 리그 덤프를 찾아 싣는다. 해시가 다르면 알린다 — 조용히 맞는 척하지 않는다.</summary>
    private void TryLoadRigFromDocument()
    {
        RigSchemasReference? reference = Editor.Document.RigSchemas;

        if (reference is null || string.IsNullOrEmpty(reference.Path))
        {
            return;
        }

        string resolved = ResolveFromDocument(reference.Path);

        if (!File.Exists(resolved))
        {
            SetStatus($"리그 덤프를 찾지 못했다: {resolved} — 내장 리그({BuiltinTuning.Source})로 연다.");
            return;
        }

        RigSchemaFile rig = RigSchemaFile.Load(resolved);
        Rig = rig;
        RigPath = resolved;

        try
        {
            Depth = DepthTuningFile.TryFindBeside(resolved) is { } depthPath ? DepthTuningFile.Load(depthPath) : BuiltinTuning.Depth;
            Focus = FocusTuningFile.TryFindBeside(resolved) is { } focusPath ? FocusTuningFile.Load(focusPath) : BuiltinTuning.Focus;
            LoadControllersBeside(resolved);
        }
        catch (Exception exception)
        {
            SetStatus($"덤프 옆 프리셋을 읽지 못했다: {exception.Message} — 내장 프리셋으로 둔다.");
            Depth = BuiltinTuning.Depth;
            Focus = BuiltinTuning.Focus;
        }

        if (!string.IsNullOrEmpty(reference.Sha256) && !string.Equals(reference.Sha256, rig.Sha256, StringComparison.Ordinal))
        {
            IReadOnlyList<MotionProblem> problems = Problems;
            SetStatus($"리그가 바뀌었다 ({Path.GetFileName(resolved)}) — 모든 채널의 노드를 다시 검증했다: 문제 {problems.Count}건.");
        }
    }

    private string RelativeToDocument(string path)
    {
        string full = Path.GetFullPath(path);

        if (DocumentPath is null)
        {
            return full.Replace('\\', '/');
        }

        string documentDirectory = Path.GetDirectoryName(Path.GetFullPath(DocumentPath)) ?? ".";
        return Path.GetRelativePath(documentDirectory, full).Replace('\\', '/');
    }

    private string ResolveFromDocument(string path)
    {
        if (Path.IsPathRooted(path) || DocumentPath is null)
        {
            return Path.GetFullPath(path);
        }

        string documentDirectory = Path.GetDirectoryName(Path.GetFullPath(DocumentPath)) ?? ".";
        return Path.GetFullPath(Path.Combine(documentDirectory, path));
    }
}
