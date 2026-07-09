using System;
using System.Windows;

namespace ICSharpCode.SharpDevelop.Startup;

public static class LibreWpfSharpDevelopMain
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };

        TryMergeResourceDictionary(app, "pack://application:,,,/ICSharpCode.Core.Presentation;component/themes/generic.xaml");
        TryMergeResourceDictionary(app, "pack://application:,,,/ICSharpCode.TreeView;component/Themes/Generic.xaml");
        TryMergeResourceDictionary(app, "pack://application:,,,/AvalonDock;component/Themes/generic.xaml");
        TryMergeResourceDictionary(app, "pack://application:,,,/ICSharpCode.AvalonEdit;component/themes/generic.xaml");

        var window = new Workbench.LibreWpfWorkbenchShell(args);
        app.Run(window);
        return 0;
    }

    private static void TryMergeResourceDictionary(Application app, string source)
    {
        try
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(source, UriKind.Absolute)
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SharpDevelop LibreWPF resource load failed: " + source);
            Console.Error.WriteLine(ex);
        }
    }
}
