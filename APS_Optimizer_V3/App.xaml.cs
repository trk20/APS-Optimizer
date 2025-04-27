using System.Diagnostics;
using Uno.Resizetizer;
using WinRT.Interop;

namespace APS_Optimizer_V3;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += App_UnhandledException;

        // TaskScheduler.UnobservedTaskException is for background Task exceptions
        // Note: This might not catch all async void exceptions reliably.
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        // AppDomain.CurrentDomain.UnhandledException is for other non-UI thread exceptions
        // Note: This might terminate the app immediately on WinUI 3 depending on config.
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    public Window? MainWindow { get; private set; }
    public static IntPtr WindowHandle { get; private set; }
    protected IHost? Host { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var builder = this.CreateBuilder(args)
            .Configure(host => host
#if DEBUG
                // Switch to Development environment when running in DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>()
                        .Section<AppConfig>()
                )
                .ConfigureServices((context, services) =>
                {
                    // none atm
                })
            );
        MainWindow = builder.Window;

#if DEBUG
        MainWindow.UseStudio();
#endif
        MainWindow.SetWindowIcon();

        Host = builder.Build();

        if (MainWindow.Content is not Frame rootFrame)
        {
            rootFrame = new Frame();
            MainWindow.Content = rootFrame;
        }

        if (rootFrame.Content == null)
        {
            rootFrame.Navigate(typeof(MainPage), args.Arguments);
        }

        WindowHandle = WindowNative.GetWindowHandle(MainWindow);

        MainWindow.Activate();
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Catches exceptions from Tasks that faulted and whose exceptions were never observed.
        // This often happens with fire-and-forget async void methods or tasks.
        Debug.WriteLine($"!!!!!!!!!! TaskScheduler_UnobservedTaskException Caught: {e.Exception}");
        // Marking as observed prevents the process from terminating by default.
        e.SetObserved();

        // Try to show the error dialog (best effort on UI thread)
        var dispatcherQueue = MainWindow?.DispatcherQueue;
        if (dispatcherQueue != null)
        {
            dispatcherQueue.TryEnqueue(async () =>
            {
                // AggregateException often wraps the real exception
                var exceptionToShow = e.Exception?.InnerException ?? e.Exception;
                await ShowErrorDialogAsync("Unobserved Task Exception", exceptionToShow);
            });
        }
        else { Debug.WriteLine("Cannot show error dialog: MainWindow or DispatcherQueue not available."); }
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        // Catches exceptions on non-UI threads that aren't caught elsewhere.
        // IMPORTANT: If e.IsTerminating is true, the app WILL likely terminate
        // regardless of what you do here, but logging/showing the dialog is still useful.
        Debug.WriteLine($"!!!!!!!!!! CurrentDomain_UnhandledException Caught (IsTerminating={e.IsTerminating}): {e.ExceptionObject}");

        // Try to show the error dialog (best effort, might not display if terminating)
        var dispatcherQueue = MainWindow?.DispatcherQueue;
        if (dispatcherQueue != null)
        {
            dispatcherQueue.TryEnqueue(async () =>
            {
                await ShowErrorDialogAsync("Unhandled Background Exception", e.ExceptionObject as Exception);
            });
        }
        else { Debug.WriteLine("Cannot show error dialog: MainWindow or DispatcherQueue not available."); }

        // If terminating, maybe try a quicker, blocking message box if possible? (Generally difficult/risky)
        // Or log to a file immediately.
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // This catches exceptions on the UI thread that weren't handled.
        Debug.WriteLine($"!!!!!!!!!! App_UnhandledException Caught: {e.Exception}");
        e.Handled = true; // Prevent the application from crashing immediately

        // Show the error dialog (needs to run on UI thread)
        // Use MainWindow if available, otherwise can't show dialog easily.
        var dispatcherQueue = MainWindow?.DispatcherQueue;
        if (dispatcherQueue != null)
        {
            dispatcherQueue.TryEnqueue(async () =>
            {
                await ShowErrorDialogAsync("Unhandled UI Exception", e.Exception);
            });
        }
        else
        {
            // Fallback if dispatcher isn't available (e.g., error very early)
            Debug.WriteLine("Cannot show error dialog: MainWindow or DispatcherQueue not available.");
        }
    }

    private async Task ShowErrorDialogAsync(string title, Exception? ex)
    {
        // Ensure we have XamlRoot to show the dialog
        var xamlRoot = MainWindow?.Content?.XamlRoot;
        if (xamlRoot == null)
        {
            Debug.WriteLine($"Cannot show error dialog '{title}': XamlRoot is null.");
            return;
        }
        var dialog = new ContentDialog
        {
            Title = title,
            // Format exception message for display
            Content = $"An unexpected error occurred:\n\n{ex?.GetType().Name}\n{ex?.Message}\n{ex?.Source}\n\n{ex?.StackTrace}", // Show stack trace for debugging
            CloseButtonText = "OK",
            XamlRoot = xamlRoot
        };

        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception dialogEx)
        {
            // If showing the dialog itself fails
            Debug.WriteLine($"Failed to show error dialog: {dialogEx}");
        }
    }


}
