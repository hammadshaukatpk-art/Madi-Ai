using System;
using System.Windows;
namespace MadiAI;
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new MainWindow());
    }
}
