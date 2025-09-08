using System;

namespace APS_Optimizer_V3_Linux;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var host = new Uno.UI.Runtime.Skia.Gtk.GtkHost(() => new APS_Optimizer_V3.App());
            host.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("GTK host failed: " + ex);
        }
    }
}
