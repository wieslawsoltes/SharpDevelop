using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ICSharpCode.TreeView;

namespace LibreWpfSharpDevelopSmoke;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Editor.Text = """
            using System;
            using ICSharpCode.AvalonEdit;

            namespace SharpDevelopSmoke;

            public static class Program
            {
                public static void Main()
                {
                    Console.WriteLine("LibreWPF SharpDevelop smoke");
                }
            }
            """;

        ProjectTree.Root = CreateProjectTree();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StatusText.Content = "Loaded";
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(RunPopupSmoke));
    }

    private void RunPopupSmoke()
    {
        try
        {
            string mode = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_POPUP_SMOKE") ?? "Menu";
            if (string.Equals(mode, "Context", StringComparison.OrdinalIgnoreCase))
            {
                ProjectContextMenu.PlacementTarget = ProjectTree;
                ProjectContextMenu.IsOpen = true;
                StatusText.Content = "Context popup opened";
            }
            else if (string.Equals(mode, "Combo", StringComparison.OrdinalIgnoreCase))
            {
                ModeCombo.IsDropDownOpen = true;
                StatusText.Content = "Combo popup opened";
            }
            else if (string.Equals(mode, "CoreDropDown", StringComparison.OrdinalIgnoreCase))
            {
                CoreDropDownMenu.PlacementTarget = CoreDropDown;
                CoreDropDownMenu.IsOpen = true;
                StatusText.Content = "Core dropdown popup opened";
            }
            else
            {
                FileMenu.IsSubmenuOpen = true;
                StatusText.Content = "Menu popup opened";
            }
        }
        catch (Exception ex)
        {
            StatusText.Content = ex.GetType().Name + ": " + ex.Message;
            Console.Error.WriteLine(ex);
        }
    }

    private void OpenTreeContextMenu(object sender, RoutedEventArgs e)
    {
        ProjectContextMenu.PlacementTarget = ProjectTree;
        ProjectContextMenu.IsOpen = true;
    }

    private void OpenComboDropDown(object sender, RoutedEventArgs e)
    {
        ModeCombo.IsDropDownOpen = true;
        PropertiesCombo.IsDropDownOpen = true;
    }

    private static SharpTreeNode CreateProjectTree()
    {
        var root = new SmokeTreeNode("SharpDevelop");
        var src = new SmokeTreeNode("src") { IsExpanded = true };
        src.Children.Add(new SmokeTreeNode("Main"));
        src.Children.Add(new SmokeTreeNode("Libraries"));
        src.Children.Add(new SmokeTreeNode("AddIns"));
        root.Children.Add(src);
        root.Children.Add(new SmokeTreeNode("samples"));
        root.Children.Add(new SmokeTreeNode("tools"));
        root.IsExpanded = true;
        return root;
    }

    private sealed class SmokeTreeNode : SharpTreeNode
    {
        private readonly string _text;

        public SmokeTreeNode(string text)
        {
            _text = text;
        }

        public override object Text => _text;
    }
}
