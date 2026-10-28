using System.Text.Json;
using System.Text.Json.Serialization;
using Mt.Authoring.Model;

namespace Mt.Authoring.Settings;

/// <summary>
/// 툴 설정 — 문서가 아니라 이 컴퓨터의 것(마야 preferences). JSON 한 파일, 없으면 기본값.
/// 읽기 실패는 조용히 기본값으로 떨어지되 <see cref="LoadProblem"/>에 남긴다.
/// </summary>
public sealed class ToolSettings
{
    public const string FileName = "settings.json";
    public const double MinPanelWidth = 160;
    public const int MaxRecentFiles = 8;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public bool SnapEnabled { get; set; } = true;

    /// <summary>초. 기본 1/60.</summary>
    public double SnapStep { get; set; } = 1.0 / 60.0;

    public TangentMode DefaultTangent { get; set; } = TangentMode.Auto;

    public bool AutoKey { get; set; }

    /// <summary>1프레임 단위 재생(마야 Real-time 24fps 표시). 꺼지면 연속.</summary>
    public bool PlayWholeFrames { get; set; }

    /// <summary>하이어아키에 컨트롤러(자리·깊이·연기) 대신 덤프의 리그 노드 26단을 보인다. 리거용 — 기본은 끔.</summary>
    public bool ShowRigNodes { get; set; }

    /// <summary>창 배치 — 왼쪽(씬·모션) 패널 폭(px). 스플리터로 끌면 저장된다.</summary>
    public double LeftPanelWidth { get; set; } = 280;

    /// <summary>창 배치 — 오른쪽(인스펙터·채널 박스) 패널 폭(px).</summary>
    public double RightPanelWidth { get; set; } = 280;

    /// <summary>창 배치 — 뷰포트 : 그래프/도프 시트 높이 비율(별 단위).</summary>
    public double ViewportRowStar { get; set; } = 2;

    public double GraphRowStar { get; set; } = 3;

    /// <summary>시간 표시 — 마야처럼 프레임이 기본.</summary>
    public Graph.TimeDisplay TimeDisplay { get; set; } = Graph.TimeDisplay.Frames;

    /// <summary>분. 0이면 끈다.</summary>
    public int AutoSaveMinutes { get; set; } = 5;

    public List<string> RecentFiles { get; set; } = new();

    [JsonIgnore]
    public string? LoadProblem { get; private set; }

    public void Touch(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string full = Path.GetFullPath(path);
        RecentFiles.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, full);

        if (RecentFiles.Count > MaxRecentFiles)
        {
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        }
    }

    public static string DefaultPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ked-motion-tool", FileName);

    public static ToolSettings Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            return new ToolSettings();
        }

        try
        {
            ToolSettings settings = JsonSerializer.Deserialize<ToolSettings>(File.ReadAllText(path), Options) ?? new ToolSettings();
            settings.RecentFiles ??= new List<string>();
            settings.SnapStep = settings.SnapStep > 0 ? settings.SnapStep : 1.0 / 60.0;
            settings.AutoSaveMinutes = Math.Clamp(settings.AutoSaveMinutes, 0, 120);
            settings.LeftPanelWidth = Math.Clamp(settings.LeftPanelWidth, MinPanelWidth, 800);
            settings.RightPanelWidth = Math.Clamp(settings.RightPanelWidth, MinPanelWidth, 800);
            settings.ViewportRowStar = settings.ViewportRowStar > 0 ? settings.ViewportRowStar : 2;
            settings.GraphRowStar = settings.GraphRowStar > 0 ? settings.GraphRowStar : 3;
            return settings;
        }
        catch (JsonException exception)
        {
            return new ToolSettings { LoadProblem = $"설정을 읽지 못해 기본값으로 둔다: {exception.Message}" };
        }
    }

    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, Options) + "\n");
    }
}
