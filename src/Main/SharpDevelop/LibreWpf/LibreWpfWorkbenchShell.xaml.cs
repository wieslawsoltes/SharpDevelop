using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.NRefactory.Editor;
using ICSharpCode.TreeView;

namespace ICSharpCode.SharpDevelop.Workbench;

public partial class LibreWpfWorkbenchShell : Window
{
    private CompletionWindow? _completionWindow;

    public LibreWpfWorkbenchShell(string[] args)
    {
        InitializeComponent();
        ProjectTree.Root = CreateProjectTree();
        Editor.Text = CreateEditorText(args);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StatusText.Content = "Loaded";
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(RunPopupSmoke));
        ScheduleAutoExitIfRequested();
    }

    private void RunPopupSmoke()
    {
        try
        {
            string mode = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_POPUP_SMOKE") ?? string.Empty;
            if (string.Equals(mode, "Menu", StringComparison.OrdinalIgnoreCase))
            {
                FileMenu.IsSubmenuOpen = true;
                SetStatus("Menu popup opened");
            }
            else if (string.Equals(mode, "Context", StringComparison.OrdinalIgnoreCase))
            {
                ProjectContextMenu.PlacementTarget = ProjectTree;
                ProjectContextMenu.IsOpen = true;
                SetStatus("Project context popup opened");
            }
            else if (string.Equals(mode, "Combo", StringComparison.OrdinalIgnoreCase))
            {
                ConfigurationCombo.IsDropDownOpen = true;
                SetStatus("Configuration combo popup opened");
            }
            else if (string.Equals(mode, "CoreDropDown", StringComparison.OrdinalIgnoreCase))
            {
                BuildDropDownMenu.PlacementTarget = BuildDropDown;
                BuildDropDownMenu.IsOpen = true;
                SetStatus("Build dropdown popup opened");
            }
            else if (string.Equals(mode, "AvalonEditCompletion", StringComparison.OrdinalIgnoreCase))
            {
                OpenCompletionWindowWhenReady();
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.GetType().Name + ": " + ex.Message);
            Console.Error.WriteLine(ex);
        }
    }

    private void SetStatus(string message)
    {
        StatusText.Content = message;
        Console.WriteLine(message);
    }

    private void OpenProjectContextMenu(object sender, RoutedEventArgs e)
    {
        ProjectContextMenu.PlacementTarget = ProjectTree;
        ProjectContextMenu.IsOpen = true;
    }

    private void OpenConfigurationCombo(object sender, RoutedEventArgs e)
    {
        ConfigurationCombo.IsDropDownOpen = true;
    }

    private void OpenCompletionWindow(object sender, RoutedEventArgs e)
    {
        OpenCompletionWindowWhenReady();
    }

    private void OpenCompletionWindowWhenReady(int remainingAttempts = 20)
    {
        ProgramDocument.Activate();
        Editor.ApplyTemplate();
        Editor.TextArea.ApplyTemplate();
        Editor.TextArea.TextView.ApplyTemplate();
        Editor.UpdateLayout();

        if (PresentationSource.FromVisual(Editor.TextArea.TextView) == null)
        {
            if (remainingAttempts <= 0)
            {
                StatusText.Content = "AvalonEdit completion waiting for text view";
                return;
            }

            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() => OpenCompletionWindowWhenReady(remainingAttempts - 1)));
            return;
        }

        OpenCompletionWindow();
    }

    private void OpenCompletionWindow()
    {
        Editor.Focus();
        Editor.TextArea.Focus();
        int markerOffset = Editor.Text.IndexOf("Console", StringComparison.Ordinal);
        Editor.CaretOffset = markerOffset >= 0 ? markerOffset : Editor.Text.Length;
        Editor.TextArea.Caret.BringCaretToView();

        _completionWindow?.Close();
        _completionWindow = new CompletionWindow(Editor.TextArea)
        {
            CloseAutomatically = false,
            CloseWhenCaretAtBeginning = false
        };
        _completionWindow.Closed += (_, _) => _completionWindow = null;

        IList<ICompletionData> data = _completionWindow.CompletionList.CompletionData;
        data.Add(new ShellCompletionData("Console", "System.Console type"));
        data.Add(new ShellCompletionData("WriteLine", "Writes a line to the output stream."));
        data.Add(new ShellCompletionData("LibreWPF", "LibreWPF SDK package mode"));
        data.Add(new ShellCompletionData("ProGPU", "ProGPU renderer and windowing backend"));

        _completionWindow.Show();
        SetStatus("AvalonEdit completion opened");
    }

    private void ScheduleAutoExitIfRequested()
    {
        string? value = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS");
        if (!int.TryParse(value, out int delayMs) || delayMs <= 0)
        {
            return;
        }

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(delayMs)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close();
        };
        timer.Start();
    }

    private static SharpTreeNode CreateProjectTree()
    {
        var root = new ShellTreeNode("SharpDevelop");
        var src = new ShellTreeNode("src") { IsExpanded = true };
        var main = new ShellTreeNode("Main") { IsExpanded = true };
        main.Children.Add(new ShellTreeNode("SharpDevelop.LibreWpf"));
        main.Children.Add(new ShellTreeNode("ICSharpCode.Core"));
        main.Children.Add(new ShellTreeNode("ICSharpCode.Core.Presentation"));
        src.Children.Add(main);
        src.Children.Add(new ShellTreeNode("Libraries"));
        src.Children.Add(new ShellTreeNode("AddIns"));
        root.Children.Add(src);
        root.Children.Add(new ShellTreeNode("samples"));
        root.Children.Add(new ShellTreeNode("tools"));
        root.IsExpanded = true;
        return root;
    }

    private static string CreateEditorText(string[] args)
    {
        string joinedArgs = args.Length == 0 ? "<none>" : string.Join(", ", args);
        return $$"""
            using System;
            using ICSharpCode.AvalonEdit;
            using ICSharpCode.Core.Presentation;

            namespace ICSharpCode.SharpDevelop;

            public static class Program
            {
                public static void Main()
                {
                    Console.WriteLine("SharpDevelop LibreWPF shell");
                    Console.WriteLine("Arguments: {{joinedArgs}}");
                }
            }
            """;
    }

    private sealed class ShellTreeNode : SharpTreeNode
    {
        private readonly string _text;

        public ShellTreeNode(string text)
        {
            _text = text;
        }

        public override object Text => _text;
    }

    private sealed class ShellCompletionData : ICompletionData
    {
        public ShellCompletionData(string text, string description)
        {
            Text = text;
            Description = description;
        }

        public ImageSource Image => null!;
        public string Text { get; }
        public object Content => Text;
        public object Description { get; }
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }
}
