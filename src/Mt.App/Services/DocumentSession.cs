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
    }

    public MotionEditor Editor { get; private set; }

    public string? DocumentPath { get; private set; }

    public RigSchemaFile? Rig { get; private set; }

    public string? RigPath { get; private set; }

    public string Status { get; private set; } = "";

    public bool IsDirty => Editor.Revision != _savedRevision;

    /// <summary>문서·편집기가 통째로 갈렸다(새로/열기). 화면은 편집기를 다시 묶는다.</summary>
    public event EventHandler? DocumentReplaced;

    public event EventHandler? StatusChanged;

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
        SetStatus($"열었다: {path}");
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
        SetStatus($"리그 덤프: {string.Join(", ", rig.RigKinds)} ({Path.GetFileName(path)})");
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
    public bool Export(string path)
    {
        if (!MotionExport.TryExport(Editor.Document, RigNodeIds, out string json, out IReadOnlyList<MotionProblem> problems))
        {
            SetStatus($"내보내기 막힘 — 문제 {problems.Count}건을 먼저 고친다.");
            return false;
        }

        File.WriteAllBytes(path, new System.Text.UTF8Encoding(false).GetBytes(json));
        SetStatus($"내보냈다: {path}");
        return true;
    }

    private void Replace(MotionDocument document, string? path)
    {
        Editor = new MotionEditor(document);
        DocumentPath = path;
        _savedRevision = Editor.Revision;
        Rig = null;
        RigPath = null;
        DocumentReplaced?.Invoke(this, EventArgs.Empty);
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
            SetStatus($"리그 덤프를 찾지 못했다: {resolved} — 덤프를 다시 지정한다.");
            return;
        }

        RigSchemaFile rig = RigSchemaFile.Load(resolved);
        Rig = rig;
        RigPath = resolved;

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
