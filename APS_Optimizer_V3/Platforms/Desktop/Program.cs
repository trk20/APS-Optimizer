using Uno.UI.Runtime.Skia;

namespace APS_Optimizer_V3;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var host = SkiaHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWindows()
            .Build();

        host.Run();
    }
}
