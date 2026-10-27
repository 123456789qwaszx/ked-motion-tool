using Avalonia;

namespace Mt.App;

internal static class Program
{
    // AppMain 전에는 Avalonia·서드파티 API·SynchronizationContext에 기대는 코드를 쓰지 않는다 —
    // 아직 초기화되지 않았다.
    [STAThread]
    public static int Main(string[] args)
    {
        // 데스크톱 앱이라 콘솔이 없다. 여기서 잡지 않으면 시작 예외는 아무 흔적 없이 사라진다.
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception exception)
        {
            Report("시작", exception);
            return 1;
        }
    }

    // Avalonia 구성. 비주얼 디자이너도 이 메서드를 쓴다 — 지우지 않는다.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Report("실행", exception);
        }
    }

    /// <summary>
    /// OutputType이 WinExe라 Release에서 콘솔 창이 새로 뜨지 않는다. 다만 <c>dotnet run</c>이나
    /// 터미널에서 실행하면 부모의 stderr를 물려받으므로 그 자리에서 바로 원인이 보인다.
    /// </summary>
    private static void Report(string context, Exception exception)
    {
        try
        {
            Console.Error.WriteLine($"[ked-motion-tool] {context} 실패: {exception}");
        }
        catch (Exception)
        {
            // 콘솔이 없으면 할 수 있는 것이 없다.
        }
    }
}
