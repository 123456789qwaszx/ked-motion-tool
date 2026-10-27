using Avalonia;
using Avalonia.Headless;
using Mt.App;

namespace Mt.App.Tests;

/// <summary>
/// 화면 없는 UI 검증의 실행 자리 (VnTool <c>HeadlessUi</c>와 같은 꼴).
///
/// <b>왜 Avalonia.Headless.XUnit을 쓰지 않는가</b> — 그 어댑터(12.1.1)는 xunit v3를 끌고 오는데
/// 이 저장소는 xunit 2.9.2다. 한 어셈블리에 두 xunit이 들어오면 <c>FactAttribute</c>가 중복돼
/// 테스트 프로젝트 전체가 컴파일되지 않는다. 어댑터 없이 세션을 직접 몰면 <c>[Fact]</c> 그대로
/// 쓸 수 있다.
///
/// 세션은 프로세스당 하나다. Avalonia는 UI 스레드가 하나뿐이라 테스트마다 새로 만들 수 없다.
/// </summary>
internal static class HeadlessUi
{
    private static readonly Lazy<HeadlessUnitTestSession> SessionHandle =
        new(() => HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder)));

    /// <summary>
    /// 지금 이 스레드가 <see cref="Run"/> 안인가. <c>Dispatcher.UIThread.CheckAccess()</c>는
    /// 세션 밖(xUnit 스레드)에서 물으면 참을 돌려주므로 믿을 수 없다 — 직접 표시해 둔다.
    /// </summary>
    [ThreadStatic]
    private static bool _inside;

    public static bool OnUiThread => _inside;

    /// <summary>UI 스레드에서 본문을 돌리고 끝날 때까지 기다린다.</summary>
    public static void Run(Action body) =>
        SessionHandle.Value.Dispatch(
            () =>
            {
                _inside = true;

                try
                {
                    body();
                }
                finally
                {
                    _inside = false;
                }
            },
            CancellationToken.None).GetAwaiter().GetResult();
}

/// <summary>
/// 헤드리스 앱 진입점. 실제 <see cref="App"/>을 그대로 쓴다 — 테마·리소스가 진짜와 같아야
/// "그려진다"가 의미를 갖는다. <see cref="App.OnFrameworkInitializationCompleted"/>는 데스크톱
/// 수명주기일 때만 MainWindow를 만들므로 헤드리스에서는 주 창이 뜨지 않는다.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
