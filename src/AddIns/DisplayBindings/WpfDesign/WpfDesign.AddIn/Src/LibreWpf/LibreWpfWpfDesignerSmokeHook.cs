// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

#if LIBREWPF
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Xml;

using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;
using ICSharpCode.WpfDesign.Designer.OutlineView;
using ICSharpCode.WpfDesign.Designer.PropertyGrid;

namespace ICSharpCode.WpfDesign.AddIn.LibreWpf
{
	public sealed class LibreWpfWpfDesignerSmokeHook : ILibreWpfSmokeHook
	{
		public string EnvironmentVariableName {
			get { return "LIBREWPF_SHARPDEVELOP_WPF_DESIGNER_SMOKE"; }
		}

		public async Task RunAsync(string mode)
		{
			string filePath = Path.GetFullPath(mode);
			if (!File.Exists(filePath) || !string.Equals(Path.GetExtension(filePath), ".xaml", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("Set LIBREWPF_SHARPDEVELOP_WPF_DESIGNER_SMOKE to an existing XAML document.");

			await WaitForProjectAsync();
			IViewContent primary = null;
			try {
				primary = SD.FileService.OpenFile(FileName.Create(filePath), true);
				if (primary == null)
					throw new InvalidOperationException("The XAML document did not create a primary view.");

				WpfViewContent designer = null;
				for (int attempt = 0; attempt < 100; attempt++) {
					SD.DisplayBindingService.AttachSubWindows(primary, attempt > 0);
					designer = primary.SecondaryViewContents.OfType<WpfViewContent>().FirstOrDefault();
					if (designer != null)
						break;
					await Task.Delay(100);
				}
				if (designer == null)
					throw new InvalidOperationException("The registered WPF designer did not attach a secondary view.");

				if (designer.WorkbenchWindow != null) {
					designer.WorkbenchWindow.ActiveViewContent = designer;
					designer.WorkbenchWindow.SelectWindow();
				}
				designer.PrimaryFile.ForceInitializeView(designer);

				for (int attempt = 0; attempt < 100 && designer.DesignContext == null; attempt++)
					await Task.Delay(100);
				if (designer.HasLoadError || designer.DesignContext == null || designer.DesignContext.RootItem == null)
					throw new InvalidOperationException("The WPF designer failed to load the XAML design context.");

				var surface = designer.DesignSurface;
				surface.ApplyTemplate();
				surface.UpdateLayout();
				await Task.Delay(250);
				var selection = await SelectStableDesignItemAsync(designer);
				var primarySelection = selection.PrimarySelection;
				bool selectionReady = primarySelection != null
					&& selection.SelectedItems.Contains(primarySelection);
				bool propertyGridReady = designer.PropertyContainer.PropertyGridReplacementContent is PropertyGridView;
				bool rootViewReady = designer.DesignContext.RootItem.View is FrameworkElement;
				bool presented = PresentationSource.FromVisual(surface) != null;
				bool editReady;
				bool undoReady;
				bool redoReady;
				bool saveReady;
				VerifyEditUndoRedoAndSave(designer, out editReady, out undoReady, out redoReady, out saveReady);
				var outlineResult = await VerifyOutlineSelectionAsync(designer);
				if (!selectionReady || !propertyGridReady || !rootViewReady || !presented
					|| !editReady || !undoReady || !redoReady || !saveReady
					|| !outlineResult.SelectionReady || !outlineResult.PropertyGridReady
					|| !outlineResult.EditReady || !outlineResult.UndoReady
					|| !outlineResult.RedoReady || !outlineResult.SaveReady
					|| !outlineResult.RestoreReady)
					throw new InvalidOperationException(
						"The WPF designer did not reach its selected, editable, and presented state."
						+ " selected=" + selectionReady
						+ " selectionCount=" + selection.SelectionCount
						+ " primary=" + (primarySelection == null ? "<null>" : primarySelection.ComponentType.FullName)
						+ " propertyGrid=" + propertyGridReady
						+ " rootView=" + rootViewReady
						+ " presented=" + presented
						+ " edit=" + editReady
						+ " undo=" + undoReady
						+ " redo=" + redoReady
						+ " save=" + saveReady
						+ " outlineSelection=" + outlineResult.SelectionReady
						+ " outlinePrimary=" + outlineResult.PrimaryTypeName
						+ " outlinePropertyGrid=" + outlineResult.PropertyGridReady
						+ " outlineEdit=" + outlineResult.EditReady
						+ " outlineUndo=" + outlineResult.UndoReady
						+ " outlineRedo=" + outlineResult.RedoReady
						+ " outlineSave=" + outlineResult.SaveReady
						+ " outlineRestore=" + outlineResult.RestoreReady);

				WriteResult(
					"Success",
					"file=" + Path.GetFileName(filePath)
					+ " root=" + designer.DesignContext.RootItem.ComponentType.FullName
					+ " selected=" + primarySelection.ComponentType.FullName
					+ " propertyGrid=True presented=True edit=True undo=True redo=True save=True"
					+ " outlineSelection=True outlinePrimary=" + outlineResult.PrimaryTypeName
					+ " outlinePropertyGrid=True outlineEdit=True outlineUndo=True outlineRedo=True"
					+ " outlineSave=True outlineRestore=True");
			} finally {
				if (primary != null && primary.WorkbenchWindow != null)
					primary.WorkbenchWindow.CloseWindow(true);
			}
		}

		static void VerifyEditUndoRedoAndSave(
			WpfViewContent designer,
			out bool editReady,
			out bool undoReady,
			out bool redoReady,
			out bool saveReady)
		{
			const string editedTitle = "LibreWPF designer smoke edit";
			var titleProperty = designer.DesignContext.RootItem.Properties.GetProperty("Title");
			object originalTitle = titleProperty.ValueOnInstance;
			titleProperty.SetValue(editedTitle);
			editReady = Equals(titleProperty.ValueOnInstance, editedTitle) && designer.DesignSurface.CanUndo();

			designer.DesignSurface.Undo();
			undoReady = Equals(titleProperty.ValueOnInstance, originalTitle) && designer.DesignSurface.CanRedo();
			designer.DesignSurface.Redo();
			redoReady = Equals(titleProperty.ValueOnInstance, editedTitle);

			var output = new StringBuilder();
			using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true })) {
				designer.DesignSurface.SaveDesigner(writer);
			}
			saveReady = output.ToString().Contains("Title=\"" + editedTitle + "\"");

			designer.DesignSurface.Undo();
			undoReady = undoReady && Equals(titleProperty.ValueOnInstance, originalTitle);
		}

		static async Task<ICSharpCode.WpfDesign.ISelectionService> SelectStableDesignItemAsync(WpfViewContent designer)
		{
			for (int attempt = 0; attempt < 20; attempt++) {
				var context = designer.DesignContext;
				if (context == null || context.RootItem == null) {
					await Task.Delay(100);
					continue;
				}

				var rootItem = context.RootItem;
				var selectionTarget = rootItem.ContentProperty == null
					? rootItem
					: rootItem.ContentProperty.Value
					  ?? rootItem.ContentProperty.CollectionElements.FirstOrDefault()
					  ?? rootItem;
				var selection = context.Services.Selection;
				selection.SetSelectedComponents(
					new[] { selectionTarget },
					ICSharpCode.WpfDesign.SelectionTypes.Primary);
				await Task.Delay(100);

				if (ReferenceEquals(context, designer.DesignContext)
					&& selection.PrimarySelection != null
					&& selection.SelectedItems.Contains(selection.PrimarySelection))
					return selection;
			}

			return designer.DesignContext.Services.Selection;
		}

		static async Task<OutlineSmokeResult> VerifyOutlineSelectionAsync(WpfViewContent designer)
		{
			var selection = designer.DesignContext.Services.Selection;
			var originalItems = selection.SelectedItems.ToArray();
			var originalPrimary = selection.PrimarySelection;
			var propertyGridView = designer.PropertyContainer.PropertyGridReplacementContent as PropertyGridView;
			var outlineRoot = designer.Outline.Root;
			if (outlineRoot == null)
				throw new InvalidOperationException("The WPF designer outline did not publish a root node.");

			var outlineTarget = outlineRoot.Children.FirstOrDefault();
			if (outlineTarget == null)
				throw new InvalidOperationException("The WPF designer outline did not publish a selectable child node.");

			var result = new OutlineSmokeResult {
				PrimaryTypeName = outlineTarget.DesignItem.ComponentType.FullName
			};
			try {
				selection.SetSelectedComponents(null);
				outlineTarget.IsSelected = true;
				for (int attempt = 0; attempt < 50; attempt++) {
					result.SelectionReady = ReferenceEquals(selection.PrimarySelection, outlineTarget.DesignItem)
						&& selection.SelectedItems.Count == 1
						&& selection.SelectedItems.Contains(outlineTarget.DesignItem)
						&& outlineTarget.IsSelected;
					result.PropertyGridReady = propertyGridView != null
						&& ReferenceEquals(propertyGridView.PropertyGrid.SingleItem, outlineTarget.DesignItem)
						&& propertyGridView.PropertyGrid.SelectedItems != null
						&& propertyGridView.PropertyGrid.SelectedItems.Contains(outlineTarget.DesignItem);
					if (result.SelectionReady && result.PropertyGridReady)
						break;
					await Task.Delay(50);
				}

				VerifyOutlineEditUndoRedoAndSave(designer, outlineTarget.DesignItem, result);
			} finally {
				selection.SetSelectedComponents(originalItems, ICSharpCode.WpfDesign.SelectionTypes.Replace);
				if (originalPrimary != null) {
					selection.SetSelectedComponents(
						new[] { originalPrimary },
						ICSharpCode.WpfDesign.SelectionTypes.Primary);
				}
			}

			for (int attempt = 0; attempt < 50; attempt++) {
				bool selectionRestored = ReferenceEquals(selection.PrimarySelection, originalPrimary)
					&& selection.SelectedItems.Count == originalItems.Length
					&& originalItems.All(selection.SelectedItems.Contains)
					&& !outlineTarget.IsSelected;
				bool propertyGridRestored = propertyGridView != null
					&& ReferenceEquals(propertyGridView.PropertyGrid.SingleItem, originalPrimary);
				result.RestoreReady = selectionRestored && propertyGridRestored;
				if (result.RestoreReady)
					break;
				await Task.Delay(50);
			}

			return result;
		}

		static void VerifyOutlineEditUndoRedoAndSave(
			WpfViewContent designer,
			ICSharpCode.WpfDesign.DesignItem outlineItem,
			OutlineSmokeResult result)
		{
			const string editedTag = "LibreWPF outline smoke edit";
			var tagProperty = outlineItem.Properties.GetProperty("Tag");
			object originalTag = tagProperty.ValueOnInstance;
			tagProperty.SetValue(editedTag);
			result.EditReady = Equals(tagProperty.ValueOnInstance, editedTag)
				&& designer.DesignSurface.CanUndo();

			designer.DesignSurface.Undo();
			result.UndoReady = Equals(tagProperty.ValueOnInstance, originalTag)
				&& designer.DesignSurface.CanRedo();
			designer.DesignSurface.Redo();
			result.RedoReady = Equals(tagProperty.ValueOnInstance, editedTag);

			var output = new StringBuilder();
			using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true })) {
				designer.DesignSurface.SaveDesigner(writer);
			}
			result.SaveReady = output.ToString().Contains(editedTag);

			designer.DesignSurface.Undo();
			result.UndoReady = result.UndoReady && Equals(tagProperty.ValueOnInstance, originalTag);
		}

		sealed class OutlineSmokeResult
		{
			public string PrimaryTypeName;
			public bool SelectionReady;
			public bool PropertyGridReady;
			public bool EditReady;
			public bool UndoReady;
			public bool RedoReady;
			public bool SaveReady;
			public bool RestoreReady;
		}

		static async Task WaitForProjectAsync()
		{
			for (int attempt = 0; attempt < 100; attempt++) {
				IProject project = SD.ProjectService.CurrentProject;
				ISolution solution = SD.ProjectService.CurrentSolution;
				if (project == null && solution != null) {
					project = solution.StartupProject ?? solution.Projects.FirstOrDefault();
					if (project != null)
						SD.ProjectService.CurrentProject = project;
				}
				if (project != null && !SD.ParserService.LoadSolutionProjectsThread.IsRunning)
					return;
				await Task.Delay(100);
			}
			throw new InvalidOperationException("The WPF designer smoke did not receive a loaded project.");
		}

		static void WriteResult(string result, string details)
		{
			string message = "LibreWPF WPF designer smoke result=" + result + " " + details;
			Console.WriteLine(message);
			SD.StatusBar.SetMessage(message);
		}
	}
}
#endif
