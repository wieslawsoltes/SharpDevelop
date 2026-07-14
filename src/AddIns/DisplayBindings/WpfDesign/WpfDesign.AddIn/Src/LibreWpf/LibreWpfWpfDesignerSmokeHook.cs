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
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;

using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;
using ICSharpCode.WpfDesign;
using ICSharpCode.WpfDesign.Adorners;
using ICSharpCode.WpfDesign.Designer.Extensions;
using ICSharpCode.WpfDesign.Designer.OutlineView;
using ICSharpCode.WpfDesign.Designer.PropertyGrid;
using ICSharpCode.WpfDesign.Designer.Services;
using ProGPU.Wpf.Interop;

namespace ICSharpCode.WpfDesign.AddIn.LibreWpf
{
	public sealed class LibreWpfWpfDesignerSmokeHook : ILibreWpfSmokeHook
	{
		public string EnvironmentVariableName {
			get { return "LIBREWPF_SHARPDEVELOP_WPF_DESIGNER_SMOKE"; }
		}

		public async Task RunAsync(string mode)
		{
			WriteTrace("starting");
			string filePath = Path.GetFullPath(mode);
			if (!File.Exists(filePath) || !string.Equals(Path.GetExtension(filePath), ".xaml", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("Set LIBREWPF_SHARPDEVELOP_WPF_DESIGNER_SMOKE to an existing XAML document.");

			await WaitForProjectAsync();
			WriteTrace("project ready");
			IViewContent primary = null;
			try {
				WriteTrace("opening XAML document");
				primary = SD.FileService.OpenFile(FileName.Create(filePath), true);
				WriteTrace("opened XAML document");
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
				WriteTrace("designer attached");

				if (designer.WorkbenchWindow != null) {
					WriteTrace("activating designer");
					designer.WorkbenchWindow.ActiveViewContent = designer;
					designer.WorkbenchWindow.SelectWindow();
				}
				WriteTrace("initializing designer view");
				designer.PrimaryFile.ForceInitializeView(designer);
				WriteTrace("initialized designer view");

				for (int attempt = 0; attempt < 100 && designer.DesignContext == null; attempt++)
					await Task.Delay(100);
				if (designer.HasLoadError || designer.DesignContext == null || designer.DesignContext.RootItem == null)
					throw new InvalidOperationException("The WPF designer failed to load the XAML design context.");
				WriteTrace("design context ready");

				var surface = designer.DesignSurface;
				surface.ApplyTemplate();
				surface.UpdateLayout();
				await Task.Delay(250);
				WriteTrace("selecting stable design item");
				var selection = await SelectStableDesignItemAsync(designer);
				WriteTrace("selected stable design item");
				var primarySelection = selection.PrimarySelection;
				bool selectionReady = primarySelection != null
					&& selection.SelectedItems.Contains(primarySelection);
				bool propertyGridReady = designer.PropertyContainer.PropertyGridReplacementContent is PropertyGridView;
				bool rootViewReady = designer.DesignContext.RootItem.View is FrameworkElement;
				bool presented = PresentationSource.FromVisual(surface) != null;
				WriteTrace("verifying class selection");
				var classSelectionResult = VerifyClassSelection(designer, filePath);
				WriteTrace("verified class selection");
				bool editReady;
				bool undoReady;
				bool redoReady;
				bool saveReady;
				WriteTrace("verifying edit history");
				VerifyEditUndoRedoAndSave(designer, out editReady, out undoReady, out redoReady, out saveReady);
				WriteTrace("verified edit history");
				WriteTrace("verifying outline selection");
				var outlineResult = await VerifyOutlineSelectionAsync(designer);
				WriteTrace("verified outline selection");
				WriteTrace("verifying toolbox interactions");
				var toolboxResult = await VerifyToolboxInsertionAsync(designer, primarySelection);
				WriteTrace("verified toolbox interactions");
				if (!selectionReady || !propertyGridReady || !rootViewReady || !presented
					|| !classSelectionResult.ServiceReady
					|| !classSelectionResult.ProjectAssemblyFirst
					|| !classSelectionResult.Stable
					|| !classSelectionResult.DataContextClassReady
					|| !editReady || !undoReady || !redoReady || !saveReady
					|| !outlineResult.SelectionReady || !outlineResult.PropertyGridReady
					|| !outlineResult.EditReady || !outlineResult.UndoReady
					|| !outlineResult.RedoReady || !outlineResult.SaveReady
					|| !outlineResult.RestoreReady
					|| !toolboxResult.RenderedToolboxPresented
					|| !toolboxResult.RenderedToolboxInputReady
					|| !toolboxResult.RenderedToolboxHostFocusReady
					|| !toolboxResult.RenderedToolboxToolSelected
					|| !toolboxResult.RenderedToolboxDropped
					|| !toolboxResult.RenderedToolboxSelectionReady
					|| !toolboxResult.RenderedToolboxPropertyGridReady
					|| !toolboxResult.RenderedToolboxXamlReady
					|| !toolboxResult.RenderedToolboxToolResetReady
					|| !toolboxResult.RenderedToolboxUndoReady
					|| !toolboxResult.RenderedToolboxRedoReady
					|| !toolboxResult.RenderedToolboxRestoreReady
					|| !toolboxResult.ToolSelected || !toolboxResult.Inserted
					|| !toolboxResult.SelectionReady || !toolboxResult.PropertyGridReady
					|| !toolboxResult.XamlReady || !toolboxResult.UndoReady
					|| !toolboxResult.RedoReady || !toolboxResult.RestoreReady
					|| !toolboxResult.ToolResetReady
					|| !toolboxResult.EventServiceReady || !toolboxResult.EventFailClosedReady
					|| !toolboxResult.EventCreatedReady || !toolboxResult.EventXamlReady
					|| !toolboxResult.EventSourceReady || !toolboxResult.EventNavigationReady
					|| !toolboxResult.EventReusedReady || !toolboxResult.EventDuplicateFreeReady
					|| !toolboxResult.EventUndoReady || !toolboxResult.EventRedoReady
					|| !toolboxResult.EventReloadReady || !toolboxResult.EventRestoreReady
					|| !toolboxResult.PointerToolReady || !toolboxResult.PointerMissFailClosed
					|| !toolboxResult.PointerHitReady || !toolboxResult.PointerSelectionReady
					|| !toolboxResult.PointerPropertyGridReady
					|| !toolboxResult.PointerAdornerExtensionReady
					|| !toolboxResult.PointerAdornerPanelReady
					|| !toolboxResult.PointerMoveReady || !toolboxResult.PointerXamlReady
					|| !toolboxResult.PointerUndoReady || !toolboxResult.PointerRedoReady
					|| !toolboxResult.PointerRestoreReady
					|| !toolboxResult.ResizeFailClosedReady
					|| !toolboxResult.ResizeAppliedReady || !toolboxResult.ResizeXamlReady
					|| !toolboxResult.ResizeUndoReady || !toolboxResult.ResizeRedoReady
					|| !toolboxResult.ResizeCancelReady || !toolboxResult.ResizeRestoreReady)
					throw new InvalidOperationException(
						"The WPF designer did not reach its selected, editable, and presented state."
						+ " selected=" + selectionReady
						+ " selectionCount=" + selection.SelectionCount
						+ " primary=" + (primarySelection == null ? "<null>" : primarySelection.ComponentType.FullName)
						+ " propertyGrid=" + propertyGridReady
						+ " rootView=" + rootViewReady
						+ " presented=" + presented
						+ " classService=" + classSelectionResult.ServiceReady
						+ " classProjectFirst=" + classSelectionResult.ProjectAssemblyFirst
						+ " classStable=" + classSelectionResult.Stable
						+ " dataContextClass=" + classSelectionResult.DataContextClassReady
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
						+ " outlineRestore=" + outlineResult.RestoreReady
						+ " renderedToolboxPresented=" + toolboxResult.RenderedToolboxPresented
						+ " renderedToolboxInput=" + toolboxResult.RenderedToolboxInputReady
						+ " renderedToolboxHostFocus=" + toolboxResult.RenderedToolboxHostFocusReady
						+ " renderedToolboxToolSelected=" + toolboxResult.RenderedToolboxToolSelected
						+ " renderedToolboxDropped=" + toolboxResult.RenderedToolboxDropped
						+ " renderedToolboxSelection=" + toolboxResult.RenderedToolboxSelectionReady
						+ " renderedToolboxPropertyGrid=" + toolboxResult.RenderedToolboxPropertyGridReady
						+ " renderedToolboxXaml=" + toolboxResult.RenderedToolboxXamlReady
						+ " renderedToolboxToolReset=" + toolboxResult.RenderedToolboxToolResetReady
						+ " renderedToolboxUndo=" + toolboxResult.RenderedToolboxUndoReady
						+ " renderedToolboxRedo=" + toolboxResult.RenderedToolboxRedoReady
						+ " renderedToolboxRestore=" + toolboxResult.RenderedToolboxRestoreReady
						+ " toolboxToolSelected=" + toolboxResult.ToolSelected
						+ " toolboxInserted=" + toolboxResult.Inserted
						+ " toolboxPrimary=" + toolboxResult.PrimaryTypeName
						+ " toolboxSelection=" + toolboxResult.SelectionReady
						+ " toolboxPropertyGrid=" + toolboxResult.PropertyGridReady
						+ " toolboxXaml=" + toolboxResult.XamlReady
						+ " toolboxUndo=" + toolboxResult.UndoReady
						+ " toolboxRedo=" + toolboxResult.RedoReady
						+ " toolboxRestore=" + toolboxResult.RestoreReady
						+ " toolboxToolReset=" + toolboxResult.ToolResetReady
						+ " eventService=" + toolboxResult.EventServiceReady
						+ " eventFailClosed=" + toolboxResult.EventFailClosedReady
						+ " eventFailure=" + toolboxResult.EventFailure
						+ " eventCreated=" + toolboxResult.EventCreatedReady
						+ " eventXaml=" + toolboxResult.EventXamlReady
						+ " eventSource=" + toolboxResult.EventSourceReady
						+ " eventNavigation=" + toolboxResult.EventNavigationReady
						+ " eventReused=" + toolboxResult.EventReusedReady
						+ " eventDuplicateFree=" + toolboxResult.EventDuplicateFreeReady
						+ " eventUndo=" + toolboxResult.EventUndoReady
						+ " eventRedo=" + toolboxResult.EventRedoReady
						+ " eventReload=" + toolboxResult.EventReloadReady
						+ " eventRestore=" + toolboxResult.EventRestoreReady
						+ " pointerTool=" + toolboxResult.PointerToolReady
						+ " pointerMissFailClosed=" + toolboxResult.PointerMissFailClosed
						+ " pointerHit=" + toolboxResult.PointerHitReady
						+ " pointerSelection=" + toolboxResult.PointerSelectionReady
						+ " pointerPropertyGrid=" + toolboxResult.PointerPropertyGridReady
						+ " pointerAdornerExtension=" + toolboxResult.PointerAdornerExtensionReady
						+ " pointerAdornerPanel=" + toolboxResult.PointerAdornerPanelReady
						+ " pointerMove=" + toolboxResult.PointerMoveReady
						+ " pointerXaml=" + toolboxResult.PointerXamlReady
						+ " pointerUndo=" + toolboxResult.PointerUndoReady
						+ " pointerRedo=" + toolboxResult.PointerRedoReady
						+ " pointerRestore=" + toolboxResult.PointerRestoreReady
						+ " resizeFailClosed=" + toolboxResult.ResizeFailClosedReady
						+ " resizeApplied=" + toolboxResult.ResizeAppliedReady
						+ " resizeXaml=" + toolboxResult.ResizeXamlReady
						+ " resizeUndo=" + toolboxResult.ResizeUndoReady
						+ " resizeRedo=" + toolboxResult.ResizeRedoReady
						+ " resizeCancel=" + toolboxResult.ResizeCancelReady
						+ " resizeRestore=" + toolboxResult.ResizeRestoreReady);

				WriteResult(
					"Success",
					"file=" + Path.GetFileName(filePath)
					+ " root=" + designer.DesignContext.RootItem.ComponentType.FullName
					+ " selected=" + primarySelection.ComponentType.FullName
					+ " propertyGrid=True presented=True edit=True undo=True redo=True save=True"
					+ " classService=True classProjectFirst=True classStable=True dataContextClass=True"
					+ " outlineSelection=True outlinePrimary=" + outlineResult.PrimaryTypeName
					+ " outlinePropertyGrid=True outlineEdit=True outlineUndo=True outlineRedo=True"
					+ " outlineSave=True outlineRestore=True"
					+ " renderedToolboxPresented=True renderedToolboxInput=True"
					+ " renderedToolboxHostFocus=True renderedToolboxToolSelected=True"
					+ " renderedToolboxDropped=True renderedToolboxSelection=True"
					+ " renderedToolboxPropertyGrid=True renderedToolboxXaml=True"
					+ " renderedToolboxToolReset=True renderedToolboxUndo=True"
					+ " renderedToolboxRedo=True renderedToolboxRestore=True"
					+ " toolboxToolSelected=True toolboxInserted=True"
					+ " toolboxPrimary=" + toolboxResult.PrimaryTypeName
					+ " toolboxSelection=True toolboxPropertyGrid=True toolboxXaml=True"
					+ " toolboxUndo=True toolboxRedo=True toolboxRestore=True toolboxToolReset=True"
					+ " eventService=True eventFailClosed=True eventFailure=None eventCreated=True eventXaml=True"
					+ " eventSource=True eventNavigation=True eventReused=True eventDuplicateFree=True"
					+ " eventUndo=True eventRedo=True eventReload=True eventRestore=True"
					+ " pointerTool=True pointerMissFailClosed=True pointerHit=True"
					+ " pointerSelection=True pointerPropertyGrid=True"
					+ " pointerAdornerExtension=True pointerAdornerPanel=True"
					+ " pointerMove=True pointerXaml=True pointerUndo=True pointerRedo=True"
					+ " pointerRestore=True"
					+ " resizeFailClosed=True resizeApplied=True resizeXaml=True"
					+ " resizeUndo=True resizeRedo=True resizeCancel=True resizeRestore=True");
			} finally {
				if (primary != null && primary.WorkbenchWindow != null)
					primary.WorkbenchWindow.CloseWindow(true);
			}
		}

		static ClassSelectionSmokeResult VerifyClassSelection(
			WpfViewContent designer,
			string filePath)
		{
			var result = new ClassSelectionSmokeResult();
			var service = designer.DesignContext.Services.GetService<ChooseClassServiceBase>();
			result.ServiceReady = service is IdeChooseClassService;
			if (!result.ServiceReady)
				return result;

			var assemblies = service.GetAssemblies().Where(assembly => assembly != null).ToArray();
			var secondAssemblies = service.GetAssemblies().Where(assembly => assembly != null).ToArray();
			IProject project = SD.ProjectService.FindProjectContainingFile(FileName.Create(filePath));
			result.ProjectAssemblyFirst = project != null
				&& assemblies.Length > 0
				&& string.Equals(
					assemblies[0].GetName().Name,
					project.AssemblyName,
					StringComparison.OrdinalIgnoreCase);
			result.Stable = assemblies.SequenceEqual(secondAssemblies);
			if (!result.ProjectAssemblyFirst || !result.Stable)
				return result;

			var chooser = new ChooseClass(assemblies) { ShowSystemClasses = true };
			result.DataContextClassReady = chooser.Classes
				.Cast<object>()
				.OfType<Type>()
				.Any(type => string.Equals(
					type.FullName,
					"ICSharpCode.SharpSnippetCompiler.MainWindow",
					StringComparison.Ordinal));
			return result;
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

		static async Task<ToolboxSmokeResult> VerifyToolboxInsertionAsync(
			WpfViewContent designer,
			ICSharpCode.WpfDesign.DesignItem container)
		{
			if (container == null)
				throw new InvalidOperationException("The WPF designer toolbox smoke did not receive a container.");

			var context = designer.DesignContext;
			var selection = context.Services.Selection;
			var originalItems = selection.SelectedItems.ToArray();
			var originalPrimary = selection.PrimarySelection;
			var propertyGridView = designer.PropertyContainer.PropertyGridReplacementContent as PropertyGridView;
			string originalXaml = SaveDesignerToString(designer);
			int originalButtonCount = CountElementsByLocalName(originalXaml, "Button");
			var result = new ToolboxSmokeResult();
			ICSharpCode.WpfDesign.DesignItem createdItem = null;

			try {
				await VerifyRenderedToolboxDragAsync(designer, container, result);
				if (!result.RenderedToolboxPresented
					|| !result.RenderedToolboxInputReady
					|| !result.RenderedToolboxHostFocusReady
					|| !result.RenderedToolboxToolSelected
					|| !result.RenderedToolboxDropped
					|| !result.RenderedToolboxSelectionReady
					|| !result.RenderedToolboxPropertyGridReady
					|| !result.RenderedToolboxXamlReady
					|| !result.RenderedToolboxToolResetReady
					|| !result.RenderedToolboxUndoReady
					|| !result.RenderedToolboxRedoReady
					|| !result.RenderedToolboxRestoreReady)
					return result;

				CreateComponentTool selectedTool;
				result.ToolSelected = WpfToolbox.Instance.TrySelectComponentTool(
					typeof(System.Windows.Controls.Button),
					out selectedTool)
					&& selectedTool != null
					&& selectedTool.ComponentType == typeof(System.Windows.Controls.Button)
					&& ReferenceEquals(context.Services.Tool.CurrentTool, selectedTool);
				if (!result.ToolSelected)
					return result;

				result.Inserted = WpfToolbox.Instance.TryInsertSelectedComponent(
					container,
					new Rect(24, 32, 120, 30),
					out createdItem)
					&& createdItem != null
					&& createdItem.ComponentType == typeof(System.Windows.Controls.Button)
					&& ReferenceEquals(createdItem.Parent, container)
					&& ICSharpCode.WpfDesign.Designer.ModelTools.IsInDocument(createdItem);
				result.PrimaryTypeName = createdItem == null ? "<null>" : createdItem.ComponentType.FullName;
				result.ToolResetReady = ReferenceEquals(context.Services.Tool.CurrentTool, context.Services.Tool.PointerTool);
				if (!result.Inserted)
					return result;

				for (int attempt = 0; attempt < 50; attempt++) {
					result.SelectionReady = ReferenceEquals(selection.PrimarySelection, createdItem)
						&& selection.SelectedItems.Count == 1
						&& selection.SelectedItems.Contains(createdItem);
					result.PropertyGridReady = propertyGridView != null
						&& ReferenceEquals(propertyGridView.PropertyGrid.SingleItem, createdItem)
						&& propertyGridView.PropertyGrid.SelectedItems != null
						&& propertyGridView.PropertyGrid.SelectedItems.Contains(createdItem);
					if (result.SelectionReady && result.PropertyGridReady)
						break;
					await Task.Delay(50);
				}

				string insertedXaml = SaveDesignerToString(designer);
				result.XamlReady = !string.Equals(insertedXaml, originalXaml, StringComparison.Ordinal)
					&& CountElementsByLocalName(insertedXaml, "Button") == originalButtonCount + 1;
				if (result.XamlReady)
					await VerifyEventHandlerAsync(designer, createdItem, result);
				designer.DesignSurface.Undo();
				result.UndoReady = !ICSharpCode.WpfDesign.Designer.ModelTools.IsInDocument(createdItem)
					&& designer.DesignSurface.CanRedo()
					&& AreXamlDocumentsEquivalent(SaveDesignerToString(designer), originalXaml);

				designer.DesignSurface.Redo();
				result.RedoReady = ICSharpCode.WpfDesign.Designer.ModelTools.IsInDocument(createdItem)
					&& string.Equals(SaveDesignerToString(designer), insertedXaml, StringComparison.Ordinal);

				designer.DesignSurface.Undo();
				selection.SetSelectedComponents(originalItems, ICSharpCode.WpfDesign.SelectionTypes.Replace);
				if (originalPrimary != null) {
					selection.SetSelectedComponents(
						new[] { originalPrimary },
						ICSharpCode.WpfDesign.SelectionTypes.Primary);
				}
				for (int attempt = 0; attempt < 50; attempt++) {
					bool selectionRestored = ReferenceEquals(selection.PrimarySelection, originalPrimary)
						&& selection.SelectedItems.Count == originalItems.Length
						&& originalItems.All(selection.SelectedItems.Contains);
					bool propertyGridRestored = propertyGridView != null
						&& ReferenceEquals(propertyGridView.PropertyGrid.SingleItem, originalPrimary);
					result.RestoreReady = selectionRestored
						&& propertyGridRestored
						&& !ICSharpCode.WpfDesign.Designer.ModelTools.IsInDocument(createdItem)
						&& AreXamlDocumentsEquivalent(SaveDesignerToString(designer), originalXaml);
					if (result.RestoreReady)
						break;
					await Task.Delay(50);
				}
			} finally {
				context.Services.Tool.CurrentTool = context.Services.Tool.PointerTool;
			}

			return result;
		}

		static async Task VerifyRenderedToolboxDragAsync(
			WpfViewContent designer,
			ICSharpCode.WpfDesign.DesignItem preferredContainer,
			ToolboxSmokeResult result)
		{
			const int mouseMoveInputKind = 3;
			const int mouseDownInputKind = 4;
			const int mouseUpInputKind = 5;
			const int leftMouseButton = 1;

			var context = designer.DesignContext;
			var selection = context.Services.Selection;
			var undoService = context.Services.GetService<UndoService>();
			var propertyGridView = designer.PropertyContainer.PropertyGridReplacementContent as PropertyGridView;
			var originalItems = selection.SelectedItems.ToArray();
			var originalPrimary = selection.PrimarySelection;
			string originalXaml = SaveDesignerToString(designer);
			int originalButtonCount = CountElementsByLocalName(originalXaml, "Button");
			int originalUndoCount = undoService == null ? -1 : undoService.UndoActions.Count();
			ICSharpCode.WpfDesign.DesignItem droppedItem = null;
			Window inputWindow = null;
			IPortableWindowActivationServiceRegistrar inputActivationService = null;
			Point lastInputPoint = default(Point);
			bool inputButtonPressed = false;
			DispatcherOperation[] queuedInputOperations = null;
			WpfToolboxDragSource dragSource = default(WpfToolboxDragSource);
			int dragEnterCount = 0;
			int dragOverCount = 0;
			int dropCount = 0;
			DragEventHandler dragEnter = delegate { dragEnterCount++; };
			DragEventHandler dragOver = delegate(object sender, DragEventArgs e) {
				dragOverCount++;
				WriteTrace(
					"rendered toolbox drag-over data="
					+ (e.Data.GetData(typeof(CreateComponentTool)) == null ? "<null>" : e.Data.GetData(typeof(CreateComponentTool)).GetType().FullName)
					+ " sameTool=" + ReferenceEquals(e.Data.GetData(typeof(CreateComponentTool)), dragSource.Tool)
					+ " currentTool=" + context.Services.Tool.CurrentTool.GetType().FullName
					+ " active=" + ReferenceEquals(context.Services.Tool.CurrentTool, dragSource.Tool)
					+ " effects=" + e.Effects
					+ " handled=" + e.Handled);
			};
			DragEventHandler drop = delegate(object sender, DragEventArgs e) {
				dropCount++;
				WriteTrace("rendered toolbox drop effects=" + e.Effects + " handled=" + e.Handled);
			};

			try {
				PadDescriptor toolsPad = SD.Workbench.GetPad(typeof(ToolsPad));
				if (toolsPad == null || undoService == null)
					return;

				toolsPad.BringPadToFront();
				ContentPresenter presenter = null;
				WindowsFormsHost host = null;
				for (int attempt = 0; attempt < 50; attempt++) {
					var padContent = toolsPad.PadContent as ToolsPad;
					presenter = padContent == null ? null : padContent.Control as ContentPresenter;
					host = presenter == null ? null : presenter.Content as WindowsFormsHost;
					if (host != null) {
						host.ApplyTemplate();
						host.UpdateLayout();
					}

					var toolboxControl = WpfToolbox.Instance.ToolboxControl;
					result.RenderedToolboxPresented = presenter != null
						&& host != null
						&& host.IsLoaded
						&& host.IsVisible
						&& PresentationSource.FromVisual(host) != null
						&& ReferenceEquals(host.Child, toolboxControl)
						&& toolboxControl.Visible;
					result.RenderedToolboxInputReady = result.RenderedToolboxPresented
						&& WpfToolbox.Instance.TryLocateComponentDragSource(
							typeof(System.Windows.Controls.Button),
							out dragSource)
						&& dragSource.InputControl != null
						&& dragSource.Tool != null
						&& dragSource.Tool.ComponentType == typeof(System.Windows.Controls.Button)
						&& IsHostedControlDescendant(toolboxControl, dragSource.InputControl);
					if (result.RenderedToolboxInputReady)
						break;
					await Task.Delay(50);
				}
				if (!result.RenderedToolboxInputReady)
					return;

				Window window = Window.GetWindow(host);
				IPortableWindowActivationServiceRegistrar activationService;
				if (window == null
					|| !PortableWpfServiceRegistry.TryGetWindowActivationService(
						PortableWpfServiceKey.PresentationFramework,
						out activationService))
					return;
				inputWindow = window;
				inputActivationService = activationService;

				var designPanel = designer.DesignSurface.DesignPanel;
				designPanel.DragEnter += dragEnter;
				designPanel.DragOver += dragOver;
				designPanel.Drop += drop;
				designer.DesignSurface.ApplyTemplate();
				designer.DesignSurface.UpdateLayout();
				WriteTrace(
					"locating rendered toolbox drop point panel="
					+ designPanel.RenderSize.Width + "x" + designPanel.RenderSize.Height);
				Point destinationPoint;
				if (!TryFindRenderedToolboxDropPoint(
					designPanel,
					preferredContainer,
					out destinationPoint))
					return;
				WriteTrace("located rendered toolbox drop point=" + destinationPoint);

				var destinationPoints = new[] {
					destinationPoint,
					destinationPoint + new Vector(4, 3),
					destinationPoint + new Vector(8, 6),
					destinationPoint + new Vector(12, 9),
					destinationPoint + new Vector(16, 12)
				};
				Point[] destinationWindowPoints = destinationPoints
					.Select(point => ToWindowInputPoint(window, designPanel.PointToScreen(point)))
					.ToArray();
				var destinationHit = window.InputHitTest(destinationWindowPoints[0]) as DependencyObject;
				WriteTrace(
					"rendered toolbox destination window=" + destinationWindowPoints[0]
					+ " hit=" + (destinationHit == null ? "<null>" : destinationHit.GetType().FullName)
					+ " allowDrop=" + GetAllowDrop(destinationHit));

				var sourcePoint = dragSource.InputPoint;
				Point sourceWindowPoint = ToWindowInputPoint(
					window,
					ToWpfPoint(dragSource.InputControl.PointToScreen(sourcePoint)));

				bool sourceMoveReady = activationService.TryProcessInputEvent(
					window,
					CreatePointerInput(mouseMoveInputKind, sourceWindowPoint, button: 0));
				bool sourceDownReady = activationService.TryProcessInputEvent(
					window,
					CreatePointerInput(mouseDownInputKind, sourceWindowPoint, leftMouseButton));
				if (sourceDownReady) {
					inputButtonPressed = true;
					lastInputPoint = sourceWindowPoint;
				}
				result.RenderedToolboxHostFocusReady = sourceDownReady
					&& host.IsKeyboardFocusWithin;
				result.RenderedToolboxToolSelected = sourceDownReady
					&& ReferenceEquals(context.Services.Tool.CurrentTool, dragSource.Tool);
				if (!sourceMoveReady
					|| !result.RenderedToolboxHostFocusReady
					|| !result.RenderedToolboxToolSelected)
					return;

				bool[] destinationMovesReady = new bool[destinationWindowPoints.Length];
				bool destinationUpReady = false;
				Exception queuedInputFailure = null;
				bool dragStarted = false;
				queuedInputOperations = new DispatcherOperation[destinationWindowPoints.Length + 1];
				for (int index = 0; index < destinationWindowPoints.Length; index++) {
					int capturedIndex = index;
					queuedInputOperations[capturedIndex] = window.Dispatcher.BeginInvoke(
						DispatcherPriority.Input,
						new Action(delegate {
							try {
								lastInputPoint = destinationWindowPoints[capturedIndex];
								destinationMovesReady[capturedIndex] = activationService.TryProcessInputEvent(
									window,
									CreatePointerInput(
										mouseMoveInputKind,
										destinationWindowPoints[capturedIndex],
										leftMouseButton));
							} catch (Exception ex) {
								queuedInputFailure = queuedInputFailure ?? ex;
							}
						}));
				}
				queuedInputOperations[queuedInputOperations.Length - 1] = window.Dispatcher.BeginInvoke(
					DispatcherPriority.Input,
					new Action(delegate {
						try {
							lastInputPoint = destinationWindowPoints[destinationWindowPoints.Length - 1];
							destinationUpReady = activationService.TryProcessInputEvent(
								window,
								CreatePointerInput(
									mouseUpInputKind,
									destinationWindowPoints[destinationWindowPoints.Length - 1],
									leftMouseButton));
							if (destinationUpReady)
								inputButtonPressed = false;
						} catch (Exception ex) {
							queuedInputFailure = queuedInputFailure ?? ex;
						}
					}));
				WriteTrace("starting rendered toolbox drag");
				dragStarted = WpfToolbox.Instance.TryStartComponentDrag(dragSource);
				WriteTrace(
					"completed rendered toolbox drag"
					+ " enter=" + dragEnterCount
					+ " over=" + dragOverCount
					+ " drop=" + dropCount);
				if (queuedInputFailure != null)
					throw new InvalidOperationException(
						"The queued rendered toolbox pointer sequence failed.",
						queuedInputFailure);
				result.RenderedToolboxInputReady = dragStarted
					&& destinationMovesReady.All(ready => ready)
					&& destinationUpReady;
				if (!result.RenderedToolboxInputReady)
					return;

				for (int attempt = 0; attempt < 50; attempt++) {
					droppedItem = selection.PrimarySelection;
					string droppedXaml = SaveDesignerToString(designer);
					if (attempt == 0) {
						WriteTrace(
							"rendered toolbox post-drop primary="
							+ (droppedItem == null ? "<null>" : droppedItem.ComponentType.FullName)
							+ " selection=" + selection.SelectionCount
							+ " buttons=" + CountElementsByLocalName(droppedXaml, "Button")
							+ " tool=" + context.Services.Tool.CurrentTool.GetType().FullName);
					}
					result.RenderedToolboxDropped = droppedItem != null
						&& droppedItem.ComponentType == typeof(System.Windows.Controls.Button)
						&& ICSharpCode.WpfDesign.Designer.ModelTools.IsInDocument(droppedItem);
					result.RenderedToolboxSelectionReady = result.RenderedToolboxDropped
						&& selection.SelectionCount == 1
						&& selection.SelectedItems.Contains(droppedItem);
					result.RenderedToolboxPropertyGridReady = result.RenderedToolboxDropped
						&& propertyGridView != null
						&& ReferenceEquals(propertyGridView.PropertyGrid.SingleItem, droppedItem)
						&& propertyGridView.PropertyGrid.SelectedItems != null
						&& propertyGridView.PropertyGrid.SelectedItems.Contains(droppedItem);
					result.RenderedToolboxXamlReady = result.RenderedToolboxDropped
						&& !string.Equals(droppedXaml, originalXaml, StringComparison.Ordinal)
						&& CountElementsByLocalName(droppedXaml, "Button") == originalButtonCount + 1;
					result.RenderedToolboxToolResetReady = ReferenceEquals(
						context.Services.Tool.CurrentTool,
						context.Services.Tool.PointerTool);
					if (result.RenderedToolboxSelectionReady
						&& result.RenderedToolboxPropertyGridReady
						&& result.RenderedToolboxXamlReady
						&& result.RenderedToolboxToolResetReady)
						break;
					await Task.Delay(50);
				}
				if (!result.RenderedToolboxDropped
					|| !result.RenderedToolboxSelectionReady
					|| !result.RenderedToolboxPropertyGridReady
					|| !result.RenderedToolboxXamlReady
					|| !result.RenderedToolboxToolResetReady)
					return;

				string committedXaml = SaveDesignerToString(designer);
				await VerifyPointerManipulationAsync(designer, droppedItem, committedXaml, result);
				if (!result.PointerRestoreReady || !result.ResizeRestoreReady)
					return;

				designer.DesignSurface.Undo();
				result.RenderedToolboxUndoReady = undoService.UndoActions.Count() == originalUndoCount
					&& !ICSharpCode.WpfDesign.Designer.ModelTools.IsInDocument(droppedItem)
					&& designer.DesignSurface.CanRedo()
					&& AreXamlDocumentsEquivalent(SaveDesignerToString(designer), originalXaml);
				WriteTrace(
					"rendered toolbox undo ready=" + result.RenderedToolboxUndoReady
					+ " undoCount=" + undoService.UndoActions.Count() + "/" + originalUndoCount
					+ " inDocument=" + ICSharpCode.WpfDesign.Designer.ModelTools.IsInDocument(droppedItem)
					+ " canRedo=" + designer.DesignSurface.CanRedo()
					+ " xaml=" + AreXamlDocumentsEquivalent(SaveDesignerToString(designer), originalXaml));

				designer.DesignSurface.Redo();
				result.RenderedToolboxRedoReady = undoService.UndoActions.Count() == originalUndoCount + 1
					&& ICSharpCode.WpfDesign.Designer.ModelTools.IsInDocument(droppedItem)
					&& string.Equals(SaveDesignerToString(designer), committedXaml, StringComparison.Ordinal);

				designer.DesignSurface.Undo();
				selection.SetSelectedComponents(originalItems, ICSharpCode.WpfDesign.SelectionTypes.Replace);
				if (originalPrimary != null) {
					selection.SetSelectedComponents(
						new[] { originalPrimary },
						ICSharpCode.WpfDesign.SelectionTypes.Primary);
				}
				for (int attempt = 0; attempt < 50; attempt++) {
					bool selectionRestored = ReferenceEquals(selection.PrimarySelection, originalPrimary)
						&& selection.SelectedItems.Count == originalItems.Length
						&& originalItems.All(selection.SelectedItems.Contains);
					bool propertyGridRestored = propertyGridView != null
						&& ReferenceEquals(propertyGridView.PropertyGrid.SingleItem, originalPrimary);
					result.RenderedToolboxRestoreReady = result.RenderedToolboxUndoReady
						&& result.RenderedToolboxRedoReady
						&& selectionRestored
						&& propertyGridRestored
						&& !ICSharpCode.WpfDesign.Designer.ModelTools.IsInDocument(droppedItem)
						&& undoService.UndoActions.Count() == originalUndoCount
						&& AreXamlDocumentsEquivalent(SaveDesignerToString(designer), originalXaml);
					if (result.RenderedToolboxRestoreReady)
						break;
					await Task.Delay(50);
				}
			} finally {
				var designPanel = designer.DesignSurface.DesignPanel;
				designPanel.DragEnter -= dragEnter;
				designPanel.DragOver -= dragOver;
				designPanel.Drop -= drop;
				if (queuedInputOperations != null) {
					foreach (DispatcherOperation operation in queuedInputOperations) {
						if (operation != null)
							operation.Abort();
					}
				}
				if (inputButtonPressed && inputWindow != null && inputActivationService != null) {
					try {
						inputActivationService.TryProcessInputEvent(
							inputWindow,
							CreatePointerInput(mouseUpInputKind, lastInputPoint, leftMouseButton));
					} catch {
						// Preserve the primary rendered-toolbox failure while still attempting input cleanup.
					}
				}
				context.Services.Tool.CurrentTool = context.Services.Tool.PointerTool;
				if (undoService != null
					&& originalUndoCount >= 0
					&& undoService.UndoActions.Count() > originalUndoCount)
					designer.DesignSurface.Undo();
				selection.SetSelectedComponents(originalItems, ICSharpCode.WpfDesign.SelectionTypes.Replace);
				if (originalPrimary != null) {
					selection.SetSelectedComponents(
						new[] { originalPrimary },
						ICSharpCode.WpfDesign.SelectionTypes.Primary);
				}
				if (designer.WorkbenchWindow != null) {
					designer.WorkbenchWindow.ActiveViewContent = designer;
					designer.WorkbenchWindow.SelectWindow();
				}
			}
		}

		static bool TryFindRenderedToolboxDropPoint(
			IDesignPanel designPanel,
			ICSharpCode.WpfDesign.DesignItem preferredContainer,
			out Point dropPoint)
		{
			const int columnCount = 8;
			const int rowCount = 8;
			const double leadingInset = 16;
			const double trailingInset = 24;

			dropPoint = default(Point);
			if (designPanel == null || designPanel.Context == null)
				return false;

			var panel = designPanel as FrameworkElement;
			if (panel == null || panel.ActualWidth < 32 || panel.ActualHeight < 32)
				return false;

			Point fallbackPoint = default(Point);
			bool hasFallback = false;
			double availableWidth = panel.ActualWidth - leadingInset - trailingInset;
			double availableHeight = panel.ActualHeight - leadingInset - trailingInset;
			for (int row = 0; row < rowCount; row++) {
				double y = leadingInset + availableHeight * row / (rowCount - 1);
				for (int column = 0; column < columnCount; column++) {
					double x = leadingInset + availableWidth * column / (columnCount - 1);
					var firstPoint = new Point(x, y);
					var secondPoint = firstPoint + new Vector(4, 3);
					var thirdPoint = firstPoint + new Vector(8, 6);
					var firstHit = designPanel.HitTest(
						firstPoint,
						false,
						true,
						ICSharpCode.WpfDesign.HitTestType.Default).ModelHit;
					if (firstHit == null || firstHit.GetBehavior<IPlacementBehavior>() == null)
						continue;

					var secondHit = designPanel.HitTest(
						secondPoint,
						false,
						true,
						ICSharpCode.WpfDesign.HitTestType.Default).ModelHit;
					var thirdHit = designPanel.HitTest(
						thirdPoint,
						false,
						true,
						ICSharpCode.WpfDesign.HitTestType.Default).ModelHit;
					if (!ReferenceEquals(firstHit, secondHit) || !ReferenceEquals(firstHit, thirdHit))
						continue;

					if (ReferenceEquals(firstHit, preferredContainer)) {
						dropPoint = firstPoint;
						return true;
					}
					if (!hasFallback) {
						fallbackPoint = firstPoint;
						hasFallback = true;
					}
				}
			}

			dropPoint = fallbackPoint;
			return hasFallback;
		}

		static bool IsHostedControlDescendant(
			System.Windows.Forms.Control root,
			System.Windows.Forms.Control candidate)
		{
			for (var current = candidate; current != null; current = current.Parent) {
				if (ReferenceEquals(current, root))
					return true;
			}
			return false;
		}

		static string GetAllowDrop(DependencyObject candidate)
		{
			for (var current = candidate; current != null; current = VisualTreeHelper.GetParent(current)) {
				var element = current as UIElement;
				if (element != null && element.AllowDrop)
					return current.GetType().FullName;
			}
			return "<none>";
		}

		static Point ToWpfPoint(System.Drawing.Point point)
		{
			return new Point(point.X, point.Y);
		}

		static Point ToWindowInputPoint(Window window, Point screenPoint)
		{
			return window.PointFromScreen(screenPoint);
		}

		static PortableWindowInputEvent CreatePointerInput(
			int kind,
			Point point,
			int button)
		{
			return new PortableWindowInputEvent(
				kind,
				x: point.X,
				y: point.Y,
				button: button);
		}

		static async Task VerifyEventHandlerAsync(
			WpfViewContent designer,
			ICSharpCode.WpfDesign.DesignItem item,
			ToolboxSmokeResult result)
		{
			var context = designer.DesignContext;
			var eventService = context.Services.GetService<IEventHandlerService>() as SharpDevelopEventHandlerService;
			var undoService = context.Services.GetService<UndoService>();
			var eventProperty = item.Properties.GetProperty("Click");
			var nonEventProperty = item.Properties.GetProperty("Content");
			FileName codeBehindFile = null;
			result.EventServiceReady = eventService != null
				&& undoService != null
				&& eventProperty != null
				&& eventProperty.IsEvent
				&& nonEventProperty != null
				&& !nonEventProperty.IsEvent
				&& eventService.TryGetPrimaryCodeBehindFile(out codeBehindFile);
			if (!result.EventServiceReady)
				return;
			await Task.Yield();

			IViewContent existingSourceView = SD.FileService.GetOpenFile(codeBehindFile);
			IViewContent sourceView = SD.FileService.OpenFile(codeBehindFile, false);
			ITextEditor editor = sourceView != null ? sourceView.GetService<ITextEditor>() : null;
			OpenedFile openedFile = SD.FileService.GetOpenedFile(codeBehindFile);
			if (editor == null || editor.Document == null || openedFile == null)
				return;

			string originalSource = editor.Document.Text;
			int originalCaretOffset = editor.Caret.Offset;
			bool originalDirty = openedFile.IsDirty;
			string originalXaml = SaveDesignerToString(designer);
			string originalName = item.Name;
			bool originalEventIsSet = eventProperty.IsSet;
			string originalEventValue = eventProperty.ValueOnInstance as string;
			int originalUndoCount = undoService.UndoActions.Count();
			bool sourceRestored = false;
			bool sourceClosed = existingSourceView != null;

			try {
				if (designer.WorkbenchWindow != null) {
					designer.WorkbenchWindow.ActiveViewContent = designer;
					designer.WorkbenchWindow.SelectWindow();
				}

				result.EventFailClosedReady = !eventService.TryCreateEventHandler(nonEventProperty)
					&& string.Equals(editor.Document.Text, originalSource, StringComparison.Ordinal)
					&& editor.Caret.Offset == originalCaretOffset
					&& openedFile.IsDirty == originalDirty
					&& string.Equals(SaveDesignerToString(designer), originalXaml, StringComparison.Ordinal)
					&& string.Equals(item.Name, originalName, StringComparison.Ordinal)
					&& eventProperty.IsSet == originalEventIsSet
					&& string.Equals(eventProperty.ValueOnInstance as string, originalEventValue, StringComparison.Ordinal)
					&& undoService.UndoActions.Count() == originalUndoCount;
				if (!result.EventFailClosedReady)
					return;

				EventHandlerCreationFailure eventFailure;
				bool created = eventService.TryCreateEventHandler(eventProperty, out eventFailure);
				result.EventFailure = eventFailure;
				string handlerName = eventProperty.ValueOnInstance as string;
				string createdSource = editor.Document.Text;
				int firstMethodCount = CountMethodOccurrences(createdSource, handlerName);
				int handlerOffset = string.IsNullOrEmpty(handlerName)
					? -1
					: createdSource.IndexOf(handlerName + "(", StringComparison.Ordinal);
				int caretOffset = editor.Caret.Offset;
				result.EventCreatedReady = created
					&& !string.IsNullOrEmpty(handlerName)
					&& !string.IsNullOrEmpty(item.Name)
					&& firstMethodCount == 1;
				result.EventXamlReady = result.EventCreatedReady
					&& SaveDesignerToString(designer).Contains("Click=\"" + handlerName + "\"")
					&& string.Equals(item.Name + "_Click", handlerName, StringComparison.Ordinal);
				result.EventSourceReady = result.EventCreatedReady
					&& !string.Equals(createdSource, originalSource, StringComparison.Ordinal);
				result.EventNavigationReady = sourceView.WorkbenchWindow != null
					&& ReferenceEquals(sourceView.WorkbenchWindow.ActiveViewContent, sourceView)
					&& handlerOffset >= 0
					&& caretOffset >= handlerOffset
					&& caretOffset <= Math.Min(editor.Document.TextLength, handlerOffset + 800);
				if (!result.EventXamlReady || !result.EventSourceReady || !result.EventNavigationReady)
					return;

				IProject project = SD.ProjectService.FindProjectContainingFile(codeBehindFile);
				SD.ParserService.Parse(codeBehindFile, editor.Document, project);
				bool reused = eventService.TryCreateEventHandler(eventProperty);
				string reusedSource = editor.Document.Text;
				int secondMethodCount = CountMethodOccurrences(reusedSource, handlerName);
				int reusedCaretOffset = editor.Caret.Offset;
				result.EventReusedReady = reused
					&& string.Equals(reusedSource, createdSource, StringComparison.Ordinal)
					&& sourceView.WorkbenchWindow != null
					&& ReferenceEquals(sourceView.WorkbenchWindow.ActiveViewContent, sourceView)
					&& reusedCaretOffset >= handlerOffset
					&& reusedCaretOffset <= Math.Min(editor.Document.TextLength, handlerOffset + 800);
				result.EventDuplicateFreeReady = secondMethodCount == firstMethodCount && secondMethodCount == 1;
				result.EventReloadReady = result.EventReusedReady && result.EventDuplicateFreeReady;
				if (!result.EventReloadReady)
					return;

				designer.DesignSurface.Undo();
				result.EventUndoReady = undoService.UndoActions.Count() == originalUndoCount
					&& designer.DesignSurface.CanRedo()
					&& string.Equals(item.Name, originalName, StringComparison.Ordinal)
					&& eventProperty.IsSet == originalEventIsSet
					&& string.Equals(eventProperty.ValueOnInstance as string, originalEventValue, StringComparison.Ordinal);

				designer.DesignSurface.Redo();
				result.EventRedoReady = undoService.UndoActions.Count() == originalUndoCount + 1
					&& string.Equals(eventProperty.ValueOnInstance as string, handlerName, StringComparison.Ordinal)
					&& SaveDesignerToString(designer).Contains("Click=\"" + handlerName + "\"");

				designer.DesignSurface.Undo();
			} finally {
				if (undoService.UndoActions.Count() > originalUndoCount)
					designer.DesignSurface.Undo();
				if (!string.Equals(editor.Document.Text, originalSource, StringComparison.Ordinal))
					editor.Document.Text = originalSource;
				editor.Caret.Offset = Math.Max(
					0,
					Math.Min(originalCaretOffset, editor.Document.TextLength));
				openedFile.IsDirty = originalDirty;
				try {
					IProject project = SD.ProjectService.FindProjectContainingFile(codeBehindFile);
					SD.ParserService.Parse(codeBehindFile, editor.Document, project);
				} catch {
				}
				sourceRestored = string.Equals(editor.Document.Text, originalSource, StringComparison.Ordinal)
					&& editor.Caret.Offset == originalCaretOffset
					&& openedFile.IsDirty == originalDirty;

				if (designer.WorkbenchWindow != null) {
					designer.WorkbenchWindow.ActiveViewContent = designer;
					designer.WorkbenchWindow.SelectWindow();
				}
				if (existingSourceView == null && sourceView.WorkbenchWindow != null) {
					sourceView.WorkbenchWindow.CloseWindow(true);
					sourceClosed = SD.FileService.GetOpenFile(codeBehindFile) == null;
				}
				bool xamlRestored = string.Equals(SaveDesignerToString(designer), originalXaml, StringComparison.Ordinal)
					&& string.Equals(item.Name, originalName, StringComparison.Ordinal)
					&& eventProperty.IsSet == originalEventIsSet
					&& string.Equals(eventProperty.ValueOnInstance as string, originalEventValue, StringComparison.Ordinal)
					&& undoService.UndoActions.Count() == originalUndoCount;
				result.EventRestoreReady = result.EventUndoReady
					&& result.EventRedoReady
					&& xamlRestored
					&& sourceRestored
					&& sourceClosed;
			}
		}

		static async Task VerifyPointerManipulationAsync(
			WpfViewContent designer,
			ICSharpCode.WpfDesign.DesignItem item,
			string originalXaml,
			ToolboxSmokeResult result)
		{
			var context = designer.DesignContext;
			var selection = context.Services.Selection;
			var propertyGridView = designer.PropertyContainer.PropertyGridReplacementContent as PropertyGridView;
			var pointerTool = context.Services.Tool.PointerTool as ICSharpCode.WpfDesign.IPointerTool;
			var view = item.View as FrameworkElement;
			var undoService = context.Services.GetService<UndoService>();
			result.PointerToolReady = pointerTool != null
				&& ReferenceEquals(context.Services.Tool.CurrentTool, context.Services.Tool.PointerTool)
				&& view != null
				&& undoService != null;
			if (!result.PointerToolReady)
				return;

			Thickness originalMargin = view.Margin;
			HorizontalAlignment originalHorizontalAlignment = view.HorizontalAlignment;
			VerticalAlignment originalVerticalAlignment = view.VerticalAlignment;
			int originalGridRow = Grid.GetRow(view);
			int originalGridColumn = Grid.GetColumn(view);

			selection.SetSelectedComponents(null);
			int originalUndoCount = undoService.UndoActions.Count();
			result.PointerMissFailClosed = pointerTool.TryStartGesture(
				designer.DesignSurface.DesignPanel,
				new Point(double.NaN, 0),
				1,
				ICSharpCode.WpfDesign.SelectionTypes.Primary) == null
				&& pointerTool.TryStartGesture(
					designer.DesignSurface.DesignPanel,
					new Point(-8, -8),
					1,
					ICSharpCode.WpfDesign.SelectionTypes.Primary) == null
				&& selection.SelectionCount == 0
				&& undoService.UndoActions.Count() == originalUndoCount
				&& string.Equals(SaveDesignerToString(designer), originalXaml, StringComparison.Ordinal);
			if (!result.PointerMissFailClosed)
				return;

			Point pointerPosition = new Point();
			for (int attempt = 0; attempt < 50; attempt++) {
				designer.DesignSurface.ApplyTemplate();
				designer.DesignSurface.UpdateLayout();
				if (view.ActualWidth > 0 && view.ActualHeight > 0) {
					pointerPosition = view.TranslatePoint(
						new Point(view.ActualWidth / 2, view.ActualHeight / 2),
						designer.DesignSurface.DesignPanel);
					var hit = designer.DesignSurface.DesignPanel.HitTest(
						pointerPosition,
						false,
						true,
						ICSharpCode.WpfDesign.HitTestType.ElementSelection);
					result.PointerHitReady = ReferenceEquals(hit.ModelHit, item);
					if (attempt == 0) {
						WriteTrace(
							"pointer target size=" + view.ActualWidth + "x" + view.ActualHeight
							+ " point=" + pointerPosition
							+ " panel=" + designer.DesignSurface.DesignPanel.RenderSize
							+ " model=" + (hit.ModelHit == null ? "<null>" : hit.ModelHit.ComponentType.FullName)
							+ " visual=" + (hit.VisualHit == null ? "<null>" : hit.VisualHit.GetType().FullName));
					}
				}
				if (result.PointerHitReady)
					break;
				await Task.Delay(50);
			}
			if (!result.PointerHitReady)
				return;

			var gesture = pointerTool.TryStartGesture(
				designer.DesignSurface.DesignPanel,
				pointerPosition,
				1,
				ICSharpCode.WpfDesign.SelectionTypes.Primary);
			if (gesture == null || !ReferenceEquals(gesture.HitItem, item))
				return;

			for (int attempt = 0; attempt < 50; attempt++) {
				result.PointerSelectionReady = ReferenceEquals(selection.PrimarySelection, item)
					&& selection.SelectionCount == 1
					&& selection.SelectedItems.Contains(item);
				result.PointerPropertyGridReady = propertyGridView != null
					&& ReferenceEquals(propertyGridView.PropertyGrid.SingleItem, item)
					&& propertyGridView.PropertyGrid.SelectedItems != null
					&& propertyGridView.PropertyGrid.SelectedItems.Contains(item);

				var resizeExtension = item.Extensions.OfType<ResizeThumbExtension>().SingleOrDefault();
				var selectionAdorners = item.Extensions
					.OfType<SelectionAdornerProvider>()
					.SelectMany(provider => provider.Adorners)
					.ToArray();
				result.PointerAdornerExtensionReady = resizeExtension != null
					&& resizeExtension.Adorners.Count > 0
					&& selectionAdorners.Length > 0;
				result.PointerAdornerPanelReady = result.PointerAdornerExtensionReady
					&& selectionAdorners.All(designer.DesignSurface.DesignPanel.Adorners.Contains);
				if (result.PointerSelectionReady
					&& result.PointerPropertyGridReady
					&& result.PointerAdornerExtensionReady
					&& result.PointerAdornerPanelReady)
					break;
				await Task.Delay(50);
			}

			if (!result.PointerSelectionReady
				|| !result.PointerPropertyGridReady
				|| !result.PointerAdornerExtensionReady
				|| !result.PointerAdornerPanelReady) {
				gesture.Cancel();
				return;
			}

			// Seed XamlDom's stable placement-attribute order, then prove that the
			// reversible move preserved the typed placement values before measuring.
			bool baselineMoveReady = gesture.Move(pointerPosition + new Vector(16, 12))
				&& gesture.HasMoved;
			if (!baselineMoveReady) {
				gesture.Cancel();
				return;
			}

			gesture.Complete();
			bool baselineCommitReady = !gesture.IsActive
				&& undoService.UndoActions.Count() == originalUndoCount + 1;
			if (!baselineCommitReady)
				return;

			designer.DesignSurface.Undo();
			string stableXaml = SaveDesignerToString(designer);
			bool baselineRestoreReady = undoService.UndoActions.Count() == originalUndoCount
				&& designer.DesignSurface.CanRedo()
				&& originalMargin.Equals(view.Margin)
				&& originalHorizontalAlignment == view.HorizontalAlignment
				&& originalVerticalAlignment == view.VerticalAlignment
				&& originalGridRow == Grid.GetRow(view)
				&& originalGridColumn == Grid.GetColumn(view);
			if (!baselineRestoreReady)
				return;

			designer.DesignSurface.ApplyTemplate();
			designer.DesignSurface.UpdateLayout();
			pointerPosition = view.TranslatePoint(
				new Point(view.ActualWidth / 2, view.ActualHeight / 2),
				designer.DesignSurface.DesignPanel);
			gesture = pointerTool.TryStartGesture(
				designer.DesignSurface.DesignPanel,
				pointerPosition,
				1,
				ICSharpCode.WpfDesign.SelectionTypes.Primary);
			if (gesture == null || !ReferenceEquals(gesture.HitItem, item))
				return;

			result.PointerMoveReady = gesture.Move(pointerPosition + new Vector(16, 12))
				&& gesture.HasMoved;
			if (!result.PointerMoveReady) {
				gesture.Cancel();
				return;
			}

			gesture.Complete();
			string movedXaml = SaveDesignerToString(designer);
			result.PointerMoveReady = result.PointerMoveReady
				&& !gesture.IsActive
				&& undoService.UndoActions.Count() == originalUndoCount + 1;
			result.PointerXamlReady = !string.Equals(movedXaml, stableXaml, StringComparison.Ordinal);

			designer.DesignSurface.Undo();
			result.PointerUndoReady = undoService.UndoActions.Count() == originalUndoCount
				&& string.Equals(SaveDesignerToString(designer), stableXaml, StringComparison.Ordinal)
				&& designer.DesignSurface.CanRedo();

			designer.DesignSurface.Redo();
			result.PointerRedoReady = string.Equals(
				SaveDesignerToString(designer),
				movedXaml,
				StringComparison.Ordinal);

			designer.DesignSurface.Undo();
			for (int attempt = 0; attempt < 50; attempt++) {
				bool sourceRestored = undoService.UndoActions.Count() == originalUndoCount
					&& string.Equals(SaveDesignerToString(designer), stableXaml, StringComparison.Ordinal);
				bool selectionRestored = ReferenceEquals(selection.PrimarySelection, item)
					&& selection.SelectionCount == 1
					&& selection.SelectedItems.Contains(item);
				bool propertyGridRestored = propertyGridView != null
					&& ReferenceEquals(propertyGridView.PropertyGrid.SingleItem, item);
				result.PointerRestoreReady = sourceRestored
					&& selectionRestored
					&& propertyGridRestored;
				if (result.PointerRestoreReady)
					break;
				await Task.Delay(50);
			}

			if (result.PointerRestoreReady)
				await VerifyResizeManipulationAsync(designer, item, result);
		}

		static async Task VerifyResizeManipulationAsync(
			WpfViewContent designer,
			ICSharpCode.WpfDesign.DesignItem item,
			ToolboxSmokeResult result)
		{
			var context = designer.DesignContext;
			var undoService = context.Services.GetService<UndoService>();
			if (undoService == null)
				return;

			string originalXaml = SaveDesignerToString(designer);
			int originalUndoCount = undoService.UndoActions.Count();
			ResizeThumbExtension resizeExtension = null;
			for (int attempt = 0; attempt < 50; attempt++) {
				resizeExtension = item.Extensions.OfType<ResizeThumbExtension>().SingleOrDefault();
				if (resizeExtension != null && resizeExtension.Adorners.Count > 0)
					break;
				await Task.Delay(50);
			}
			if (resizeExtension == null)
				return;

			IResizeThumbGesture noOpGesture = null;
			try {
				bool invalidAlignmentFailedClosed = resizeExtension.TryStartGesture(
					ICSharpCode.WpfDesign.PlacementAlignment.Center) == null;
				noOpGesture = resizeExtension.TryStartGesture(
					ICSharpCode.WpfDesign.PlacementAlignment.BottomRight);
				if (noOpGesture == null)
					return;

				bool reentrantGestureFailedClosed = resizeExtension.TryStartGesture(
					ICSharpCode.WpfDesign.PlacementAlignment.Right) == null;
				bool nonFiniteDeltaFailedClosed = !noOpGesture.Update(
					new Vector(double.NaN, 1),
					false)
					&& !noOpGesture.HasResized;
				noOpGesture.Complete();
				result.ResizeFailClosedReady = invalidAlignmentFailedClosed
					&& reentrantGestureFailedClosed
					&& nonFiniteDeltaFailedClosed
					&& !noOpGesture.IsActive
					&& !resizeExtension.IsResizing
					&& undoService.UndoActions.Count() == originalUndoCount
					&& string.Equals(
						SaveDesignerToString(designer),
						originalXaml,
						StringComparison.Ordinal);
			} finally {
				if (noOpGesture != null && noOpGesture.IsActive)
					noOpGesture.Cancel();
			}
			if (!result.ResizeFailClosedReady)
				return;

			resizeExtension = item.Extensions.OfType<ResizeThumbExtension>().SingleOrDefault();
			if (resizeExtension == null)
				return;

			// Seed XamlDom's stable resize-attribute order before checking exact source.
			// This mirrors the pointer-move baseline above and leaves no undo unit.
			IResizeThumbGesture baselineGesture = null;
			bool baselineCommitted = false;
			try {
				baselineGesture = resizeExtension.TryStartGesture(
					ICSharpCode.WpfDesign.PlacementAlignment.BottomRight);
				if (baselineGesture == null
					|| !baselineGesture.Update(new Vector(1, 1), false)
					|| !baselineGesture.HasResized)
					return;

				baselineGesture.Complete();
				baselineCommitted = !baselineGesture.IsActive
					&& undoService.UndoActions.Count() == originalUndoCount + 1;
				if (!baselineCommitted)
					return;

				designer.DesignSurface.Undo();
				baselineCommitted = false;
				if (undoService.UndoActions.Count() != originalUndoCount
					|| !designer.DesignSurface.CanRedo())
					return;
				originalXaml = SaveDesignerToString(designer);
			} finally {
				if (baselineGesture != null && baselineGesture.IsActive)
					baselineGesture.Cancel();
				if (baselineCommitted
					&& undoService.UndoActions.Count() > originalUndoCount)
					designer.DesignSurface.Undo();
			}

			IResizeThumbGesture resizeGesture = null;
			bool resizeCommitted = false;
			try {
				resizeGesture = resizeExtension.TryStartGesture(
					ICSharpCode.WpfDesign.PlacementAlignment.BottomRight);
				if (resizeGesture == null)
					return;

				result.ResizeAppliedReady = resizeGesture.Update(new Vector(24, 18), false)
					&& resizeGesture.IsActive
					&& resizeGesture.HasResized;
				string resizedXaml = SaveDesignerToString(designer);
				result.ResizeXamlReady = result.ResizeAppliedReady
					&& !string.Equals(resizedXaml, originalXaml, StringComparison.Ordinal);
				if (!result.ResizeXamlReady)
					return;

				resizeGesture.Complete();
				resizeCommitted = !resizeGesture.IsActive
					&& !resizeExtension.IsResizing
					&& undoService.UndoActions.Count() == originalUndoCount + 1;
				if (!resizeCommitted)
					return;

				string committedXaml = SaveDesignerToString(designer);
				result.ResizeXamlReady = string.Equals(
					committedXaml,
					resizedXaml,
					StringComparison.Ordinal);
				designer.DesignSurface.Undo();
				result.ResizeUndoReady = undoService.UndoActions.Count() == originalUndoCount
					&& designer.DesignSurface.CanRedo()
					&& string.Equals(
						SaveDesignerToString(designer),
						originalXaml,
						StringComparison.Ordinal);

				designer.DesignSurface.Redo();
				result.ResizeRedoReady = undoService.UndoActions.Count() == originalUndoCount + 1
					&& string.Equals(
						SaveDesignerToString(designer),
						committedXaml,
						StringComparison.Ordinal);

				designer.DesignSurface.Undo();
				resizeCommitted = false;
			} finally {
				if (resizeGesture != null && resizeGesture.IsActive)
					resizeGesture.Cancel();
				if (resizeCommitted
					&& undoService.UndoActions.Count() > originalUndoCount)
					designer.DesignSurface.Undo();
			}
			if (!result.ResizeUndoReady || !result.ResizeRedoReady)
				return;

			resizeExtension = item.Extensions.OfType<ResizeThumbExtension>().SingleOrDefault();
			if (resizeExtension == null)
				return;

			IResizeThumbGesture canceledGesture = null;
			try {
				canceledGesture = resizeExtension.TryStartGesture(
					ICSharpCode.WpfDesign.PlacementAlignment.Left);
				if (canceledGesture == null)
					return;

				bool cancelMutationReady = canceledGesture.Update(new Vector(12, 0), false)
					&& canceledGesture.HasResized
					&& !string.Equals(
						SaveDesignerToString(designer),
						originalXaml,
						StringComparison.Ordinal);
				canceledGesture.Cancel();
				result.ResizeCancelReady = cancelMutationReady
					&& !canceledGesture.IsActive
					&& !resizeExtension.IsResizing
					&& undoService.UndoActions.Count() == originalUndoCount
					&& AreXamlDocumentsEquivalent(
						SaveDesignerToString(designer),
						originalXaml);
				WriteTrace(
					"resize cancel mutation=" + cancelMutationReady
					+ " active=" + canceledGesture.IsActive
					+ " resizing=" + resizeExtension.IsResizing
					+ " undoCount=" + undoService.UndoActions.Count() + "/" + originalUndoCount
					+ " xaml=" + AreXamlDocumentsEquivalent(
						SaveDesignerToString(designer),
						originalXaml));
			} finally {
				if (canceledGesture != null && canceledGesture.IsActive)
					canceledGesture.Cancel();
			}

			result.ResizeRestoreReady = result.ResizeCancelReady
				&& undoService.UndoActions.Count() == originalUndoCount
				&& AreXamlDocumentsEquivalent(
					SaveDesignerToString(designer),
					originalXaml);
		}

		static string SaveDesignerToString(WpfViewContent designer)
		{
			var output = new StringBuilder();
			using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true })) {
				designer.DesignSurface.SaveDesigner(writer);
			}
			return output.ToString();
		}

		static bool AreXamlDocumentsEquivalent(string first, string second)
		{
			XDocument firstDocument = XDocument.Parse(first);
			XDocument secondDocument = XDocument.Parse(second);
			NormalizeXamlDocument(firstDocument);
			NormalizeXamlDocument(secondDocument);
			return XNode.DeepEquals(firstDocument, secondDocument);
		}

		static void NormalizeXamlDocument(XDocument document)
		{
			foreach (XElement element in document.Descendants().ToArray()) {
				var attributes = element.Attributes()
					.OrderBy(attribute => attribute.IsNamespaceDeclaration ? 0 : 1)
					.ThenBy(attribute => attribute.Name.NamespaceName, StringComparer.Ordinal)
					.ThenBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal)
					.Select(attribute => new XAttribute(attribute))
					.ToArray();
				element.ReplaceAttributes(attributes);
				if (!element.Nodes().Any())
					element.Add(new XText(string.Empty));
			}
		}

		static int CountElementsByLocalName(string xaml, string localName)
		{
			var document = new XmlDocument();
			document.LoadXml(xaml);
			return document.SelectNodes("//*[local-name()='" + localName + "']").Count;
		}

		static int CountMethodOccurrences(string source, string handlerName)
		{
			if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(handlerName))
				return 0;
			int count = 0;
			int offset = 0;
			string token = handlerName + "(";
			while ((offset = source.IndexOf(token, offset, StringComparison.Ordinal)) >= 0) {
				count++;
				offset += token.Length;
			}
			return count;
		}

		sealed class ClassSelectionSmokeResult
		{
			public bool ServiceReady;
			public bool ProjectAssemblyFirst;
			public bool Stable;
			public bool DataContextClassReady;
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

		sealed class ToolboxSmokeResult
		{
			public string PrimaryTypeName;
			public bool RenderedToolboxPresented;
			public bool RenderedToolboxInputReady;
			public bool RenderedToolboxHostFocusReady;
			public bool RenderedToolboxToolSelected;
			public bool RenderedToolboxDropped;
			public bool RenderedToolboxSelectionReady;
			public bool RenderedToolboxPropertyGridReady;
			public bool RenderedToolboxXamlReady;
			public bool RenderedToolboxToolResetReady;
			public bool RenderedToolboxUndoReady;
			public bool RenderedToolboxRedoReady;
			public bool RenderedToolboxRestoreReady;
			public bool ToolSelected;
			public bool Inserted;
			public bool SelectionReady;
			public bool PropertyGridReady;
			public bool XamlReady;
			public bool UndoReady;
			public bool RedoReady;
			public bool RestoreReady;
			public bool ToolResetReady;
			public bool EventServiceReady;
			public bool EventFailClosedReady;
			public bool EventCreatedReady;
			public bool EventXamlReady;
			public bool EventSourceReady;
			public bool EventNavigationReady;
			public bool EventReusedReady;
			public bool EventDuplicateFreeReady;
			public bool EventUndoReady;
			public bool EventRedoReady;
			public bool EventReloadReady;
			public bool EventRestoreReady;
			public EventHandlerCreationFailure EventFailure;
			public bool PointerToolReady;
			public bool PointerMissFailClosed;
			public bool PointerHitReady;
			public bool PointerSelectionReady;
			public bool PointerPropertyGridReady;
			public bool PointerAdornerExtensionReady;
			public bool PointerAdornerPanelReady;
			public bool PointerMoveReady;
			public bool PointerXamlReady;
			public bool PointerUndoReady;
			public bool PointerRedoReady;
			public bool PointerRestoreReady;
			public bool ResizeFailClosedReady;
			public bool ResizeAppliedReady;
			public bool ResizeXamlReady;
			public bool ResizeUndoReady;
			public bool ResizeRedoReady;
			public bool ResizeCancelReady;
			public bool ResizeRestoreReady;
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

		static void WriteTrace(string state)
		{
			if (Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_TRACE_OPEN") == "1")
				Console.WriteLine("LibreWPF WPF designer smoke state=" + state);
		}
	}
}
#endif
