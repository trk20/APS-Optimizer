using System.Reflection;
using Uno.UI.Runtime.Skia;

namespace APS_Optimizer_V3;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            AppDomain.CurrentDomain.SetData("APP_CONTEXT_BASE_DIRECTORY",
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

            var host = SkiaHostBuilder.Create()
                .App(() => new App())
                .UseX11()
                .UseLinuxFrameBuffer()
                .UseMacOS()
                .UseWindows()
                .Build();

            host.Run();
        }
        catch (Exception ex)
        {
            File.WriteAllText("error.log", ex.ToString());
            throw;
        }
    }
}
