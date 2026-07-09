// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;

using AvalonDock;
using ICSharpCode.Core;
using ICSharpCode.Core.Presentation;
#if LIBREWPF
using ICSharpCode.AvalonEdit.AddIn;
using ICSharpCode.FormsDesigner;
#endif
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Parser;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Startup;
using ICSharpCode.SharpDevelop.Templates;
using ICSharpCode.SharpDevelop.WinForms;

namespace ICSharpCode.SharpDevelop.Workbench
{
	/// <summary>
	/// Workbench implementation using WPF and AvalonDock.
	/// </summary>
	sealed partial class WpfWorkbench : FullScreenEnabledWindow, IWorkbench, System.Windows.Forms.IWin32Window
	{
		const string mainMenuPath    = "/SharpDevelop/Workbench/MainMenu";
		const string viewContentPath = "/SharpDevelop/Workbench/Pads";

		public event EventHandler ActiveWorkbenchWindowChanged;
		public event EventHandler ActiveViewContentChanged;
		public event EventHandler ActiveContentChanged;
		public event EventHandler<ViewContentEventArgs> ViewOpened;

		internal void OnViewOpened(ViewContentEventArgs e)
		{
			if (ViewOpened != null) {
				ViewOpened(this, e);
			}
		}

		public event EventHandler<ViewContentEventArgs> ViewClosed;

		internal void OnViewClosed(ViewContentEventArgs e)
		{
			if (ViewClosed != null) {
				ViewClosed(this, e);
			}
		}

		public System.Windows.Forms.IWin32Window MainWin32Window { get { return this; } }
		public Window MainWindow { get { return this; } }

		IntPtr System.Windows.Forms.IWin32Window.Handle {
			get {
				var wnd = System.Windows.PresentationSource.FromVisual(this) as System.Windows.Interop.IWin32Window;
				if (wnd != null)
					return wnd.Handle;
				else
					return IntPtr.Zero;
			}
		}

		List<PadDescriptor> padDescriptorCollection = new List<PadDescriptor>();
		SDStatusBar statusBar = new SDStatusBar();
		ToolBar[] toolBars;

		public WpfWorkbench()
		{
			SD.Services.AddService(typeof(IStatusBarService), new StatusBarService(statusBar));
			InitializeComponent();
			InitFocusTrackingEvents();
		}

		protected override void OnSourceInitialized(EventArgs e)
		{
			base.OnSourceInitialized(e);
			HwndSource.FromHwnd(this.MainWin32Window.Handle).AddHook(SingleInstanceHelper.WndProc);
			// validate after PresentationSource is initialized
			Rect bounds = new Rect(Left, Top, Width, Height);
			bounds = FormLocationHelper.Validate(bounds.TransformToDevice(this).ToSystemDrawing()).ToWpf().TransformFromDevice(this);
			SetBounds(bounds);
			// Set WindowState after PresentationSource is initialized, because now bounds and location are properly set.
			this.WindowState = lastNonMinimizedWindowState;
		}

		void SetBounds(Rect bounds)
		{
			this.Left = bounds.Left;
			this.Top = bounds.Top;
			this.Width = bounds.Width;
			this.Height = bounds.Height;
		}

		public void Initialize()
		{
			UpdateFlowDirection();

			var padDescriptors = AddInTree.BuildItems<PadDescriptor>(viewContentPath, this, false);
			((SharpDevelopServiceContainer)SD.Services).AddFallbackProvider(new PadServiceProvider(padDescriptors));
			foreach (PadDescriptor content in padDescriptors) {
				ShowPad(content);
			}

			mainMenu.ItemsSource = MenuService.CreateMenuItems(this, this, mainMenuPath, activationMethod: "MainMenu", immediatelyExpandMenuBuildersForShortcuts: true);

			toolBars = ToolBarService.CreateToolBars(this, this, "/SharpDevelop/Workbench/ToolBar");
			foreach (ToolBar tb in toolBars) {
				DockPanel.SetDock(tb, Dock.Top);
				dockPanel.Children.Insert(1, tb);
			}
			DockPanel.SetDock(statusBar, Dock.Bottom);
			dockPanel.Children.Insert(dockPanel.Children.Count - 2, statusBar);

			Core.WinForms.MenuService.ExecuteCommand = ExecuteCommand;
			Core.WinForms.MenuService.CanExecuteCommand = CanExecuteCommand;
			UpdateMenu();

			AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnRequestNavigate));
			Project.ProjectService.CurrentProjectChanged += SetProjectTitle;

			SharpDevelop.FileService.FileRemoved += CheckRemovedOrReplacedFile;
			SharpDevelop.FileService.FileReplaced += CheckRemovedOrReplacedFile;
			SharpDevelop.FileService.FileRenamed += CheckRenamedFile;

			SharpDevelop.FileService.FileRemoved += ((RecentOpen)SD.FileService.RecentOpen).FileRemoved;
			SharpDevelop.FileService.FileRenamed += ((RecentOpen)SD.FileService.RecentOpen).FileRenamed;

			requerySuggestedEventHandler = new EventHandler(CommandManager_RequerySuggested);
			CommandManager.RequerySuggested += requerySuggestedEventHandler;
			SD.ResourceService.LanguageChanged += OnLanguageChanged;

			SD.StatusBar.SetMessage("${res:MainWindow.StatusBar.ReadyMessage}");
#if LIBREWPF
			ScheduleLibreWpfSmokeHooks();
#endif
		}

		void ExecuteCommand(ICommand command, object caller)
		{
			ServiceSingleton.GetRequiredService<IAnalyticsMonitor>()
				.TrackFeature(command.GetType().FullName, "Menu");
			var routedCommand = command as RoutedCommand;
			if (routedCommand != null) {
				var target = FocusManager.GetFocusedElement(this);
				if (routedCommand.CanExecute(caller, target))
					routedCommand.Execute(caller, target);
			} else {
				if (command.CanExecute(caller))
					command.Execute(caller);
			}
		}

		bool CanExecuteCommand(ICommand command, object caller)
		{
			var routedCommand = command as RoutedCommand;
			if (routedCommand != null) {
				var target = FocusManager.GetFocusedElement(this);
				return routedCommand.CanExecute(caller, target);
			} else {
				return command.CanExecute(caller);
			}
		}

		// keep a reference to the event handler to prevent it from being garbage collected
		// (CommandManager.RequerySuggested only keeps weak references to the event handlers)
		EventHandler requerySuggestedEventHandler;

		void CommandManager_RequerySuggested(object sender, EventArgs e)
		{
			UpdateMenu();
		}

		void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
		{
			e.Handled = true;
			if (e.Uri.Scheme == "mailto") {
				try {
					Process.Start(e.Uri.ToString());
				} catch {
					// catch exceptions - e.g. incorrectly installed mail client
				}
			} else {
				SharpDevelop.FileService.OpenFile(e.Uri.ToString());
			}
		}

		void SetProjectTitle(object sender, Project.ProjectEventArgs e)
		{
			if (e.Project != null) {
				Title = e.Project.Name + " - " + ResourceService.GetString("MainWindow.DialogName");
			} else {
				Title = ResourceService.GetString("MainWindow.DialogName");
			}
		}

		void CheckRemovedOrReplacedFile(object sender, FileEventArgs e)
		{
			foreach (OpenedFile file in SD.FileService.OpenedFiles) {
				if (FileUtility.IsBaseDirectory(e.FileName, file.FileName)) {
					foreach (IViewContent content in file.RegisteredViewContents.ToArray()) {
						// content.WorkbenchWindow can be null if multiple view contents
						// were in the same WorkbenchWindow and both should be closed
						// (e.g. Windows Forms Designer, Subversion History View)
						if (content.WorkbenchWindow != null) {
							content.WorkbenchWindow.CloseWindow(true);
						}
					}
				}
			}
			Editor.PermanentAnchorService.FileDeleted(e);
		}

		void CheckRenamedFile(object sender, FileRenameEventArgs e)
		{
			if (e.IsDirectory) {
				foreach (OpenedFile file in SD.FileService.OpenedFiles) {
					if (file.FileName != null && FileUtility.IsBaseDirectory(e.SourceFile, file.FileName)) {
						file.FileName = new FileName(FileUtility.RenameBaseDirectory(file.FileName, e.SourceFile, e.TargetFile));
					}
				}
			} else {
				OpenedFile file = SD.FileService.GetOpenedFile(e.SourceFile);
				if (file != null) {
					file.FileName = new FileName(e.TargetFile);
				}
			}
			Editor.PermanentAnchorService.FileRenamed(e);
		}

		void UpdateMenu()
		{
			MenuService.UpdateStatus(mainMenu.ItemsSource);
			foreach (ToolBar tb in toolBars) {
				ToolBarService.UpdateStatus(tb.ItemsSource);
			}
		}

#if LIBREWPF
		StackPanel libreWpfPopupSmokeHost;
		ContextMenu libreWpfSmokeContextMenu;
		ContextMenu libreWpfSmokeToolBarDropDownMenu;
		ComboBox libreWpfSmokeComboBox;
		DropDownButton libreWpfSmokeToolBarDropDownButton;
		bool libreWpfSmokeContextMenuOpen;
		bool libreWpfSmokeToolBarDropDownOpen;
		readonly List<DispatcherTimer> libreWpfSmokeTimers = new List<DispatcherTimer>();
		Task libreWpfWinFormsContextMenuSmokeTask = Task.CompletedTask;
		Task libreWpfAvalonDockSmokeTask = Task.CompletedTask;

		void ScheduleLibreWpfSmokeHooks()
		{
			string popupMode = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE");
			if (!string.IsNullOrEmpty(popupMode)) {
				Dispatcher.BeginInvoke(new Action(delegate {
					RunLibreWpfFullPopupSmoke(popupMode);
				}), DispatcherPriority.ApplicationIdle);
			}

			string buildSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_BUILD_SMOKE");
			if (!string.IsNullOrEmpty(buildSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					await RunLibreWpfBuildSmoke(buildSmoke);
				}), DispatcherPriority.ApplicationIdle);
			}

			string resxSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_RESX_SMOKE");
			if (!string.IsNullOrEmpty(resxSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					await RunLibreWpfResXSmoke(resxSmoke);
				}), DispatcherPriority.ApplicationIdle);
			}

			string propertyPadSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE");
			if (!string.IsNullOrEmpty(propertyPadSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					await RunLibreWpfPropertyPadSmoke(propertyPadSmoke);
				}), DispatcherPriority.ApplicationIdle);
			}

			string avalonDockSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_AVALONDOCK_SMOKE");
			if (!string.IsNullOrEmpty(avalonDockSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					libreWpfAvalonDockSmokeTask = RunLibreWpfAvalonDockSmoke(avalonDockSmoke);
					await libreWpfAvalonDockSmokeTask;
				}), DispatcherPriority.ApplicationIdle);
			}

			string winFormsContextMenuSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE");
			if (!string.IsNullOrEmpty(winFormsContextMenuSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					libreWpfWinFormsContextMenuSmokeTask = RunLibreWpfWinFormsContextMenuSmoke(winFormsContextMenuSmoke);
					await libreWpfWinFormsContextMenuSmokeTask;
				}), DispatcherPriority.ApplicationIdle);
			}

			string editorCompletionSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_EDITOR_COMPLETION_SMOKE");
			if (!string.IsNullOrEmpty(editorCompletionSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					await RunLibreWpfEditorCompletionSmoke(editorCompletionSmoke);
				}), DispatcherPriority.ApplicationIdle);
			}

			string saveSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_SAVE_SMOKE");
			if (!string.IsNullOrEmpty(saveSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					await RunLibreWpfSaveSmoke(saveSmoke, false);
				}), DispatcherPriority.ApplicationIdle);
			}

			string saveAllSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_SAVE_ALL_SMOKE");
			if (!string.IsNullOrEmpty(saveAllSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					await RunLibreWpfSaveSmoke(saveAllSmoke, true);
				}), DispatcherPriority.ApplicationIdle);
			}

			string reloadSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_RELOAD_SMOKE");
			if (!string.IsNullOrEmpty(reloadSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					await RunLibreWpfReloadSmoke(reloadSmoke);
				}), DispatcherPriority.ApplicationIdle);
			}

			string newFileSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_NEW_FILE_SMOKE");
			if (!string.IsNullOrEmpty(newFileSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					await RunLibreWpfNewFileSmoke(newFileSmoke);
				}), DispatcherPriority.ApplicationIdle);
			}

			string templateSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_TEMPLATE_SMOKE");
			if (!string.IsNullOrEmpty(templateSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					await RunLibreWpfTemplateSmoke(templateSmoke);
				}), DispatcherPriority.ApplicationIdle);
			}

			string editorNavigationSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_EDITOR_NAVIGATION_SMOKE");
			if (!string.IsNullOrEmpty(editorNavigationSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					await RunLibreWpfEditorNavigationSmoke(editorNavigationSmoke);
				}), DispatcherPriority.ApplicationIdle);
			}

			string closeAllSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_CLOSE_ALL_SMOKE");
			if (!string.IsNullOrEmpty(closeAllSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					await RunLibreWpfCloseAllSmoke(closeAllSmoke);
				}), DispatcherPriority.ApplicationIdle);
			}

			string closeReopenSolutionSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_CLOSE_REOPEN_SOLUTION_SMOKE");
			if (!string.IsNullOrEmpty(closeReopenSolutionSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					await RunLibreWpfCloseReopenSolutionSmoke(closeReopenSolutionSmoke);
				}), DispatcherPriority.ApplicationIdle);
			}

			string formsDesignerSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_FORMS_DESIGNER_SMOKE");
			if (!string.IsNullOrEmpty(formsDesignerSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					await RunLibreWpfFormsDesignerSmoke(formsDesignerSmoke);
				}), DispatcherPriority.ApplicationIdle);
			}

			string shutdownPersistenceSmoke = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_SHUTDOWN_PERSISTENCE_SMOKE");
			if (!string.IsNullOrEmpty(shutdownPersistenceSmoke)) {
				Dispatcher.BeginInvoke(new Action(async delegate {
					await RunLibreWpfShutdownPersistenceSmoke(shutdownPersistenceSmoke);
				}), DispatcherPriority.ApplicationIdle);
			}

			ScheduleLibreWpfAddInSmokeHooks();

			string exitAfter = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS");
			int exitAfterMs;
			if (int.TryParse(exitAfter, out exitAfterMs) && exitAfterMs > 0) {
				DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(exitAfterMs) };
				timer.Tick += delegate {
					timer.Stop();
					libreWpfSmokeTimers.Remove(timer);
					if (Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_TRACE_OPEN") == "1") {
						Console.WriteLine("LibreWPF smoke exit timer closing workbench after " + exitAfterMs + " ms");
					}
					Close();
				};
				libreWpfSmokeTimers.Add(timer);
				timer.Start();
			}
			}

			void ScheduleLibreWpfAddInSmokeHooks()
			{
				try {
					foreach (ILibreWpfSmokeHook hook in AddInTree.BuildItems<ILibreWpfSmokeHook>("/SharpDevelop/LibreWpf/SmokeHooks", this, false)) {
						string mode = Environment.GetEnvironmentVariable(hook.EnvironmentVariableName);
						if (string.IsNullOrEmpty(mode)) {
							continue;
						}

						Dispatcher.BeginInvoke(new Action(async delegate {
							await RunLibreWpfAddInSmokeHook(hook, mode);
						}), DispatcherPriority.ApplicationIdle);
					}
				} catch (Exception ex) {
					Console.WriteLine("LibreWPF add-in smoke hook discovery failed: " + ex);
					SD.StatusBar.SetMessage("LibreWPF add-in smoke hook discovery failed: " + ex.Message);
				}
			}

			async Task RunLibreWpfAddInSmokeHook(ILibreWpfSmokeHook hook, string mode)
			{
				try {
					await hook.RunAsync(mode);
				} catch (Exception ex) {
					Console.WriteLine("LibreWPF add-in smoke hook failed: " + hook.GetType().FullName + ": " + ex);
					SD.StatusBar.SetMessage("LibreWPF add-in smoke hook failed: " + ex.Message);
				}
			}

			async Task RunLibreWpfFormsDesignerSmoke(string mode)
			{
				try {
					await WaitForLibreWpfProjectLoadAsync();

					FileName fileName = GetLibreWpfFormsDesignerSmokeFile(mode);
					if (fileName == null) {
						Console.WriteLine("LibreWPF FormsDesigner smoke unavailable: no designer-backed C# file found.");
						SD.StatusBar.SetMessage("LibreWPF FormsDesigner smoke unavailable: no designer-backed C# file found.");
						return;
					}

					IViewContent viewContent = SD.FileService.OpenFile(fileName, true);
					if (viewContent == null) {
						Console.WriteLine("LibreWPF FormsDesigner smoke failed: unable to open " + fileName);
						SD.StatusBar.SetMessage("LibreWPF FormsDesigner smoke failed: unable to open " + fileName);
						return;
					}

					IViewContent designerContent = null;
					for (int attempt = 0; attempt < 100; attempt++) {
						SD.DisplayBindingService.AttachSubWindows(viewContent, attempt > 0);
						designerContent = viewContent.SecondaryViewContents
							.FirstOrDefault(content => string.Equals(content.GetType().FullName, "ICSharpCode.FormsDesigner.FormsDesignerViewContent", StringComparison.Ordinal));
						if (designerContent != null)
							break;

						await Task.Delay(200);
					}

					if (designerContent == null) {
						string message = "LibreWPF FormsDesigner smoke result=NotAttached file=" + Path.GetFileName(fileName.ToString())
							+ " secondary=" + viewContent.SecondaryViewContents.Count;
						Console.WriteLine(message);
						SD.StatusBar.SetMessage(message);
						return;
					}

					SelectLibreWpfViewContent(designerContent);
					if (designerContent.PrimaryFile != null) {
						designerContent.PrimaryFile.ForceInitializeView(designerContent);
						if (Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_TRACE_OPEN") == "1") {
							AbstractViewContentHandlingLoadErrors loadErrorContent = designerContent as AbstractViewContentHandlingLoadErrors;
							Console.WriteLine("LibreWPF FormsDesigner smoke initialized primary="
								+ designerContent.PrimaryFile.FileName
								+ " registered=" + designerContent.PrimaryFile.RegisteredViewContents.Contains(designerContent)
								+ " current=" + (designerContent.PrimaryFile.CurrentView == designerContent)
								+ " hasLoadError=" + (loadErrorContent != null && loadErrorContent.HasLoadError));
						}
					}
					PropertyContainer designerProperties = null;
					for (int attempt = 0; attempt < 100; attempt++) {
						IHasPropertyContainer propertyContent = designerContent as IHasPropertyContainer;
						if (propertyContent == null) {
							propertyContent = designerContent.GetService<IHasPropertyContainer>();
						}

						designerProperties = propertyContent != null ? propertyContent.PropertyContainer : null;
						if (designerProperties != null && designerProperties.Host != null)
							break;

						await Task.Delay(200);
					}

					if (designerProperties == null || designerProperties.Host == null) {
						string message = "LibreWPF FormsDesigner smoke result=Attached surface=NotLoaded file=" + Path.GetFileName(fileName.ToString())
							+ " secondary=" + viewContent.SecondaryViewContents.Count
							+ " designer=" + designerContent.GetType().FullName
							+ " loadError=" + GetLibreWpfLoadErrorSummary(designerContent);
						Console.WriteLine(message);
						SD.StatusBar.SetMessage(message);
						return;
					}

					object rootComponent = designerProperties.Host.RootComponent;
					int componentCount = designerProperties.Host.Container != null && designerProperties.Host.Container.Components != null
						? designerProperties.Host.Container.Components.Count
						: 0;
					int selectableCount = designerProperties.SelectableObjects != null ? designerProperties.SelectableObjects.Count : 0;
					string success = "LibreWPF FormsDesigner smoke result=Attached file=" + Path.GetFileName(fileName.ToString())
						+ " secondary=" + viewContent.SecondaryViewContents.Count
						+ " designer=" + designerContent.GetType().FullName
						+ " surface=Loaded"
						+ " root=" + (rootComponent == null ? "<null>" : rootComponent.GetType().FullName)
						+ " components=" + componentCount
						+ " selectable=" + selectableCount;
					Console.WriteLine(success);
					SD.StatusBar.SetMessage(success);

					await RunLibreWpfFormsDesignerMutationSmoke(designerContent as FormsDesignerViewContent, designerProperties, rootComponent);
				} catch (Exception ex) {
					Console.WriteLine("LibreWPF FormsDesigner smoke failed: " + ex);
					SD.StatusBar.SetMessage("LibreWPF FormsDesigner smoke failed: " + ex.Message);
				}
			}

			async Task RunLibreWpfFormsDesignerMutationSmoke(FormsDesignerViewContent designerContent, PropertyContainer designerProperties, object rootComponent)
			{
				try {
					IComponent mutationTarget = designerProperties.Host.Container.Components
						.Cast<IComponent>()
						.FirstOrDefault(component => !ReferenceEquals(component, rootComponent)
							&& TypeDescriptor.GetProperties(component)["Text"] != null
							&& !TypeDescriptor.GetProperties(component)["Text"].IsReadOnly);

					if (mutationTarget == null) {
						string unavailable = "LibreWPF FormsDesigner mutation smoke result=Unavailable reason=NoTextComponent";
						Console.WriteLine(unavailable);
						SD.StatusBar.SetMessage(unavailable);
						return;
					}

					PropertyDescriptor textProperty = TypeDescriptor.GetProperties(mutationTarget)["Text"];
					object oldValue = textProperty.GetValue(mutationTarget);
					string testValue = "LibreWPF designer smoke";
					bool selectedByService = false;
					bool selectedByContainer = false;
					bool selectedByGrid = false;
					bool valueVisible = false;
					bool flushPersisted = false;
					int rowCount = 0;

					try {
						string originalDesignerCode = designerContent != null ? designerContent.DesignerCodeFileContent : null;
						bool originalDesignerDirty = designerContent != null && designerContent.DesignerCodeFile != null && designerContent.DesignerCodeFile.IsDirty;
						System.ComponentModel.Design.ISelectionService selectionService =
							designerProperties.Host.GetService(typeof(System.ComponentModel.Design.ISelectionService)) as System.ComponentModel.Design.ISelectionService;
						if (selectionService != null) {
							selectionService.SetSelectedComponents(
								new object[] { mutationTarget },
								System.ComponentModel.Design.SelectionTypes.Replace);
							selectedByService = selectionService.GetComponentSelected(mutationTarget);
						}

						designerProperties.SelectedObject = mutationTarget;
						PropertyPad.UpdateSelectedObjectIfActive(designerProperties);

						textProperty.SetValue(mutationTarget, testValue);

						PadDescriptor propertyPad = SD.Workbench.GetPad(typeof(PropertyPad));
						if (propertyPad != null) {
							propertyPad.BringPadToFront();
							await Task.Delay(100);
						}

						System.Windows.Forms.PropertyGrid grid = PropertyPad.Grid;
						if (grid != null) {
							if (!ReferenceEquals(grid.SelectedObject, mutationTarget)) {
								grid.SelectedObject = mutationTarget;
							}
							grid.Refresh();
							rowCount = grid.DisplayRows.Count;
							selectedByGrid = ReferenceEquals(grid.SelectedObject, mutationTarget);
							valueVisible = grid.DisplayRows.Any(row => !row.IsCategory
								&& string.Equals(row.Label, textProperty.DisplayName, StringComparison.OrdinalIgnoreCase)
								&& string.Equals(row.ValueText, testValue, StringComparison.Ordinal));
						}

						selectedByContainer = ReferenceEquals(designerProperties.SelectedObject, mutationTarget);

						if (designerContent != null && originalDesignerCode != null) {
							try {
								designerContent.MergeFormChanges();
								flushPersisted = designerContent.DesignerCodeFileContent != null
									&& designerContent.DesignerCodeFileContent.Contains(testValue);
							} finally {
								designerContent.DesignerCodeFileContent = originalDesignerCode;
								if (designerContent.DesignerCodeFile != null)
									designerContent.DesignerCodeFile.IsDirty = originalDesignerDirty;
							}
						}
					} finally {
						textProperty.SetValue(mutationTarget, oldValue);
					}

					string message = "LibreWPF FormsDesigner mutation smoke result="
						+ (selectedByService && selectedByContainer && selectedByGrid && valueVisible && flushPersisted ? "Success" : "Partial")
						+ " component=" + mutationTarget.GetType().FullName
						+ " name=" + (mutationTarget.Site != null ? mutationTarget.Site.Name : string.Empty)
						+ " selectedByService=" + selectedByService
						+ " selectedByContainer=" + selectedByContainer
						+ " selectedByGrid=" + selectedByGrid
						+ " valueVisible=" + valueVisible
						+ " flushPersisted=" + flushPersisted
						+ " rows=" + rowCount;
					Console.WriteLine(message);
					SD.StatusBar.SetMessage(message);
				} catch (Exception ex) {
					Console.WriteLine("LibreWPF FormsDesigner mutation smoke failed: " + ex);
					SD.StatusBar.SetMessage("LibreWPF FormsDesigner mutation smoke failed: " + ex.Message);
				}
			}

			static string GetLibreWpfLoadErrorSummary(IViewContent content)
			{
				AbstractViewContentHandlingLoadErrors loadErrorContent = content as AbstractViewContentHandlingLoadErrors;
				if (loadErrorContent == null || !loadErrorContent.HasLoadError)
					return "None";

				ContentPresenter presenter = content.Control as ContentPresenter;
				TextBox textBox = presenter != null ? presenter.Content as TextBox : null;
				string text = textBox != null ? textBox.Text : null;
				if (string.IsNullOrWhiteSpace(text))
					return "Unavailable";

				text = text.Replace(Environment.NewLine, " ").Replace("\r", " ").Replace("\n", " ").Trim();
				const int maxLength = 700;
				return text.Length > maxLength ? text.Substring(0, maxLength) + "..." : text;
			}

			FileName GetLibreWpfFormsDesignerSmokeFile(string mode)
			{
				CompilableProject project = GetLibreWpfSmokeProject();
				string requested = NormalizeLibreWpfFormsDesignerSmokePath(mode, project);
				if (!string.IsNullOrEmpty(requested) && File.Exists(requested))
					return FileName.Create(requested);

				if (project == null)
					return null;

				foreach (FileProjectItem item in project.Items.OfType<FileProjectItem>()) {
					string itemFileName = item.FileName;
					if (!IsLibreWpfFormsDesignerSmokeCandidate(itemFileName))
						continue;
					return FileName.Create(itemFileName);
				}

				return null;
			}

			static string NormalizeLibreWpfFormsDesignerSmokePath(string mode, CompilableProject project)
			{
				if (string.IsNullOrWhiteSpace(mode))
					return null;
				string value = mode.Trim();
				if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
				    || string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase)
				    || string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase))
					return null;
				if (Path.IsPathRooted(value))
					return value;
				if (project != null) {
					string projectRelative = Path.Combine(project.Directory, value);
					if (File.Exists(projectRelative))
						return projectRelative;
				}
				return Path.GetFullPath(value);
			}

			static bool IsLibreWpfFormsDesignerSmokeCandidate(string fileName)
			{
				if (string.IsNullOrEmpty(fileName)
				    || !File.Exists(fileName)
				    || !string.Equals(Path.GetExtension(fileName), ".cs", StringComparison.OrdinalIgnoreCase)
				    || fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
					return false;

				string designerFileName = Path.Combine(
					Path.GetDirectoryName(fileName),
					Path.GetFileNameWithoutExtension(fileName) + ".Designer.cs");
				if (!File.Exists(designerFileName))
					return false;

				try {
					string text = File.ReadAllText(fileName);
					return text.IndexOf("InitializeComponent", StringComparison.Ordinal) >= 0
						&& (text.IndexOf("System.Windows.Forms", StringComparison.Ordinal) >= 0
						    || text.IndexOf(": Form", StringComparison.Ordinal) >= 0
						    || text.IndexOf(": UserControl", StringComparison.Ordinal) >= 0);
				} catch (IOException) {
					return false;
				} catch (UnauthorizedAccessException) {
					return false;
				}
			}

			async Task RunLibreWpfBuildSmoke(string mode)
			{
			try {
				if (!await WaitForLibreWpfBuildSmokeTarget(mode)) {
					Console.WriteLine("LibreWPF build smoke unavailable: no project or solution is open.");
					SD.StatusBar.SetMessage("LibreWPF build smoke unavailable: no project or solution is open.");
					return;
				}

				BuildOptions options = new BuildOptions(BuildTarget.Build) {
					BuildOutputVerbosity = BuildOutputVerbosity.Diagnostic,
					BuildDetection = BuildDetection.RegularBuild
				};

				BuildResults results;
				if (string.Equals(mode, "Project", StringComparison.OrdinalIgnoreCase) && SD.ProjectService.CurrentProject != null) {
					Console.WriteLine("LibreWPF build smoke starting project build: " + SD.ProjectService.CurrentProject.Name);
					results = await SD.BuildService.BuildAsync(SD.ProjectService.CurrentProject, options);
				} else if (SD.ProjectService.CurrentSolution != null) {
					Console.WriteLine("LibreWPF build smoke starting solution build: " + SD.ProjectService.CurrentSolution.FileName);
					results = await SD.BuildService.BuildAsync(SD.ProjectService.CurrentSolution, options);
				} else {
					Console.WriteLine("LibreWPF build smoke unavailable: no project or solution is open.");
					SD.StatusBar.SetMessage("LibreWPF build smoke unavailable: no project or solution is open.");
					return;
				}

				string message = "LibreWPF build smoke result=" + results.Result
					+ " errors=" + results.ErrorCount
					+ " warnings=" + results.WarningCount;
				Console.WriteLine(message);
				foreach (BuildError error in results.Errors.Take(10)) {
					Console.WriteLine("LibreWPF build smoke diagnostic: "
						+ error.FileName + "(" + error.Line + "," + error.Column + "): "
						+ (error.IsWarning ? "warning " : "error ")
						+ error.ErrorCode + ": " + error.ErrorText);
				}
				SD.StatusBar.SetMessage(message);
			} catch (Exception ex) {
				Console.WriteLine("LibreWPF build smoke failed: " + ex);
				SD.StatusBar.SetMessage("LibreWPF build smoke failed: " + ex.Message);
			}
		}

		async Task<bool> WaitForLibreWpfBuildSmokeTarget(string mode)
		{
			for (int attempt = 0; attempt < 100; attempt++) {
				if (string.Equals(mode, "Project", StringComparison.OrdinalIgnoreCase)) {
					if (SD.ProjectService.CurrentProject != null)
						return true;
				} else if (SD.ProjectService.CurrentSolution != null) {
					return true;
				}

				await Task.Delay(200);
			}

			return false;
		}

		async Task RunLibreWpfResXSmoke(string mode)
		{
			try {
				CompilableProject project = null;
				for (int attempt = 0; attempt < 100; attempt++) {
					project = GetLibreWpfSmokeProject();
					if (project != null)
						break;
					await Task.Delay(200);
				}

				if (project == null) {
					Console.WriteLine("LibreWPF ResX smoke unavailable: no project is open.");
					SD.StatusBar.SetMessage("LibreWPF ResX smoke unavailable: no project is open.");
					return;
				}

				var resxFiles = project.Items.OfType<FileProjectItem>()
					.Where(item => ".resx".Equals(Path.GetExtension(item.FileName), StringComparison.OrdinalIgnoreCase))
					.Select(item => item.FileName.ToString())
					.Where(File.Exists)
					.ToList();
				var originalBytes = resxFiles.ToDictionary(fileName => fileName, File.ReadAllBytes);

				try {
					ResXConverter.UpdateResourceFiles(project);
				} finally {
					foreach (var entry in originalBytes) {
						File.WriteAllBytes(entry.Key, entry.Value);
					}
				}

				string message = "LibreWPF ResX smoke result=Success files=" + resxFiles.Count;
				Console.WriteLine(message);
				SD.StatusBar.SetMessage(message);
			} catch (Exception ex) {
				Console.WriteLine("LibreWPF ResX smoke failed: " + ex);
				SD.StatusBar.SetMessage("LibreWPF ResX smoke failed: " + ex.Message);
			}
		}

		CompilableProject GetLibreWpfSmokeProject()
		{
			CompilableProject currentProject = SD.ProjectService.CurrentProject as CompilableProject;
			if (currentProject != null)
				return currentProject;
			ISolution solution = SD.ProjectService.CurrentSolution;
			return solution != null ? solution.Projects.OfType<CompilableProject>().FirstOrDefault() : null;
		}

		async Task RunLibreWpfPropertyPadSmoke(string mode)
		{
			try {
				object target = null;
				for (int attempt = 0; attempt < 100; attempt++) {
					target = (object)GetLibreWpfSmokeProject() ?? SD.ProjectService.CurrentSolution;
					if (target != null)
						break;
					await Task.Delay(200);
				}

				if (target == null) {
					Console.WriteLine("LibreWPF property pad smoke unavailable: no project or solution is open.");
					SD.StatusBar.SetMessage("LibreWPF property pad smoke unavailable: no project or solution is open.");
					return;
				}

				PadDescriptor projectBrowserPad = SD.Workbench.GetPad(typeof(ProjectBrowserPad));
				if (projectBrowserPad != null) {
					projectBrowserPad.BringPadToFront();
					await Task.Delay(100);
					ProjectBrowserPad.Instance.PropertyContainer.SelectableObjects = new object[] { target };
					ProjectBrowserPad.Instance.PropertyContainer.SelectedObject = target;
				}

				PadDescriptor propertyPad = SD.Workbench.GetPad(typeof(PropertyPad));
				if (propertyPad != null) {
					propertyPad.BringPadToFront();
					await Task.Delay(100);
				}

				System.Windows.Forms.PropertyGrid grid = PropertyPad.Grid;
				if (grid == null) {
					Console.WriteLine("LibreWPF property pad smoke failed: property grid unavailable.");
					SD.StatusBar.SetMessage("LibreWPF property pad smoke failed: property grid unavailable.");
					return;
				}

				if (grid.SelectedObject == null && grid.SelectedObjects == null) {
					grid.SelectedObject = target;
				}
				grid.Refresh();

				int rowCount = grid.DisplayRows.Count;
				string selectedType = grid.SelectedObject != null ? grid.SelectedObject.GetType().Name : "(none)";
				string message = "LibreWPF property pad smoke result=Success selected=" + selectedType + " rows=" + rowCount;
				Console.WriteLine(message);
				SD.StatusBar.SetMessage(message);
			} catch (Exception ex) {
				Console.WriteLine("LibreWPF property pad smoke failed: " + ex);
				SD.StatusBar.SetMessage("LibreWPF property pad smoke failed: " + ex.Message);
			}
		}

		async Task RunLibreWpfWinFormsContextMenuSmoke(string mode)
		{
			try {
				await RunLibreWpfPropertyPadSmoke(mode);
				await Task.Delay(200);

				System.Windows.Forms.PropertyGrid grid = PropertyPad.Grid;
				if (grid == null || grid.ContextMenuStrip == null) {
					Console.WriteLine("LibreWPF WinForms context menu smoke failed: property grid context menu unavailable.");
					SD.StatusBar.SetMessage("LibreWPF WinForms context menu smoke failed: property grid context menu unavailable.");
						return;
					}

					await CloseLibreWpfSmokePopupsAsync();

					grid.ContextMenuStrip.Opened += delegate {
						Console.WriteLine("LibreWPF WinForms context menu Opened event");
					};
				grid.ContextMenuStrip.Closed += delegate {
					Console.WriteLine("LibreWPF WinForms context menu Closed event");
				};
				grid.ContextMenuStrip.Show(grid, new System.Drawing.Point(24, 24));

				string message = "LibreWPF WinForms context menu smoke result="
					+ (grid.ContextMenuStrip.Visible ? "Opened" : "NotVisible")
					+ " items=" + grid.ContextMenuStrip.Items.Count;
				Console.WriteLine(message);
				SD.StatusBar.SetMessage(message);
				await Task.Delay(200);
				grid.ContextMenuStrip.Close();
				await Task.Delay(100);
			} catch (Exception ex) {
				Console.WriteLine("LibreWPF WinForms context menu smoke failed: " + ex);
				SD.StatusBar.SetMessage("LibreWPF WinForms context menu smoke failed: " + ex.Message);
			}
		}

		async Task RunLibreWpfAvalonDockSmoke(string mode)
		{
			try {
				await WaitForLibreWpfProjectLoadAsync();
				await Task.Delay(3600);
				await CloseLibreWpfSmokePopupsAsync();

				AvalonDockLayout layout = workbenchLayout as AvalonDockLayout;
				if (layout == null || layout.DockingManager == null) {
					Console.WriteLine("LibreWPF AvalonDock smoke unavailable: workbench layout is not AvalonDock.");
					SD.StatusBar.SetMessage("LibreWPF AvalonDock smoke unavailable: workbench layout is not AvalonDock.");
					return;
				}

				PadDescriptor descriptor = GetLibreWpfAvalonDockSmokePad(mode);
				if (descriptor == null) {
					Console.WriteLine("LibreWPF AvalonDock smoke unavailable: no matching pad found.");
					SD.StatusBar.SetMessage("LibreWPF AvalonDock smoke unavailable: no matching pad found.");
					return;
				}

				descriptor.BringPadToFront();
				await Task.Delay(250);

				AvalonPadContent pad;
				if (!layout.TryGetPadContent(descriptor, out pad)) {
					layout.ShowPad(descriptor);
					await Task.Delay(250);
					if (!layout.TryGetPadContent(descriptor, out pad)) {
						Console.WriteLine("LibreWPF AvalonDock smoke failed: pad content was not created.");
						SD.StatusBar.SetMessage("LibreWPF AvalonDock smoke failed: pad content was not created.");
						return;
					}
				}

				layout.DockingManager.ApplyTemplate();
				layout.DockingManager.UpdateLayout();
				pad.LoadPadContentIfRequired();
				pad.UpdateLayout();

				int floatingBefore = layout.DockingManager.FloatingWindows.Length;
				pad.ShowAsFloatingWindow(layout.DockingManager, true);
				await Task.Delay(700);
				layout.DockingManager.UpdateLayout();
				DockableContentState floatingState = pad.State;
				FloatingWindow floatingWindow = layout.DockingManager.FloatingWindows.FirstOrDefault(window => window.IsVisible);
				bool floatingVisible = floatingWindow != null && PresentationSource.FromVisual(floatingWindow) != null;
				int floatingAfter = layout.DockingManager.FloatingWindows.Length;
				bool floatingContextMenuOpened = false;
				int floatingContextMenuItems = 0;
				bool floatingModeToggled = false;
				bool dockableModeRestored = false;
				DockableFloatingWindow dockableFloatingWindow = floatingWindow as DockableFloatingWindow;
				if (dockableFloatingWindow != null) {
					dockableFloatingWindow.ApplyTemplate();
					dockableFloatingWindow.UpdateLayout();
					ContextMenu floatingContextMenu;
					floatingContextMenuOpened = dockableFloatingWindow.TryOpenContextMenuForPortableHost(new Point(24, 24), out floatingContextMenu);
					if (floatingContextMenu != null) {
						floatingContextMenuItems = floatingContextMenu.Items.Count;
						await Task.Delay(250);
						floatingContextMenu.IsOpen = false;
					}

					dockableFloatingWindow.IsDockableWindow = false;
					await Task.Delay(200);
					floatingModeToggled = !dockableFloatingWindow.IsDockableWindow && pad.State == DockableContentState.FloatingWindow;
					dockableFloatingWindow.IsDockableWindow = true;
					await Task.Delay(200);
					dockableModeRestored = dockableFloatingWindow.IsDockableWindow && pad.State == DockableContentState.DockableWindow;
				}

				pad.Show(layout.DockingManager);
				await Task.Delay(500);
				layout.DockingManager.UpdateLayout();
				DockableContentState redockedState = pad.State;
				bool dockedOptionsMenuOpened = false;
				int dockedOptionsMenuItems = 0;
				DockablePane dockedPane = pad.ContainerPane as DockablePane;
				if (dockedPane != null) {
					dockedPane.ApplyTemplate();
					dockedPane.UpdateLayout();
					ContextMenu dockedOptionsMenu;
					dockedOptionsMenuOpened = dockedPane.TryOpenOptionsMenuForPortableHost(out dockedOptionsMenu);
					if (dockedOptionsMenu != null) {
						dockedOptionsMenuItems = dockedOptionsMenu.Items.Count;
						await Task.Delay(250);
						dockedOptionsMenu.IsOpen = false;
					}
				}

				bool autoHideRequested = false;
				bool flyoutVisible = false;
				bool flyoutShowResult = false;
				bool flyoutCreated = false;
				bool flyoutHasPresentationSource = false;
				int flyoutWindowCount = 0;
				DockableContentState autoHideState = pad.State;
				bool ownerActiveBeforeFlyout = IsActive;
				bool ownerActivateResult = false;
				bool ownerActiveAfterActivate = IsActive;
				if (pad.State == DockableContentState.Docked) {
					autoHideRequested = true;
					ownerActivateResult = Activate();
					Focus();
					await Task.Delay(300);
					ownerActiveAfterActivate = IsActive;
					pad.ToggleAutoHide();
					await Task.Delay(600);
					autoHideState = pad.State;
					ownerActivateResult = Activate() || ownerActivateResult;
					Focus();
					await Task.Delay(300);
					ownerActiveAfterActivate = IsActive;
					FlyoutPaneWindow flyoutWindow;
					flyoutShowResult = pad.TryShowAutoHideFlyoutForPortableHost(out flyoutWindow);
					if (!flyoutShowResult)
						pad.Activate();
					await Task.Delay(250);
					flyoutWindowCount = Application.Current.Windows
						.OfType<FlyoutPaneWindow>()
						.Count();
					if (flyoutWindow == null)
						flyoutWindow = Application.Current.Windows
							.OfType<FlyoutPaneWindow>()
							.FirstOrDefault();
					flyoutCreated = flyoutWindow != null;
					flyoutHasPresentationSource = flyoutWindow != null && PresentationSource.FromVisual(flyoutWindow) != null;
					flyoutVisible = flyoutWindow != null
						&& flyoutWindow.IsVisible
						&& flyoutHasPresentationSource;
					pad.Show(layout.DockingManager);
					await Task.Delay(400);
				}

				DockableContentState finalState = pad.State;
				bool success = floatingState == DockableContentState.DockableWindow
					&& floatingVisible
					&& floatingAfter > floatingBefore
					&& floatingContextMenuOpened
					&& floatingContextMenuItems > 0
					&& floatingModeToggled
					&& dockableModeRestored
					&& redockedState == DockableContentState.Docked
					&& dockedOptionsMenuOpened
					&& dockedOptionsMenuItems > 0
					&& autoHideRequested
					&& autoHideState == DockableContentState.AutoHide
					&& flyoutShowResult
					&& flyoutCreated
					&& flyoutVisible
					&& finalState == DockableContentState.Docked;
				string message = "LibreWPF AvalonDock smoke result=" + (success ? "Success" : "Partial")
					+ " pad=" + descriptor.Class
					+ " floatingState=" + floatingState
					+ " floatingVisible=" + floatingVisible
					+ " floatingWindows=" + floatingBefore + "->" + floatingAfter
					+ " floatingContextMenuOpened=" + floatingContextMenuOpened
					+ " floatingContextMenuItems=" + floatingContextMenuItems
					+ " floatingModeToggled=" + floatingModeToggled
					+ " dockableModeRestored=" + dockableModeRestored
					+ " redockedState=" + redockedState
					+ " dockedOptionsMenuOpened=" + dockedOptionsMenuOpened
					+ " dockedOptionsMenuItems=" + dockedOptionsMenuItems
					+ " ownerActiveBeforeFlyout=" + ownerActiveBeforeFlyout
					+ " ownerActivateResult=" + ownerActivateResult
					+ " ownerActiveAfterActivate=" + ownerActiveAfterActivate
					+ " autoHideState=" + autoHideState
					+ " flyoutShowResult=" + flyoutShowResult
					+ " flyoutCreated=" + flyoutCreated
					+ " flyoutHasPresentationSource=" + flyoutHasPresentationSource
					+ " flyoutWindowCount=" + flyoutWindowCount
					+ " flyoutVisible=" + flyoutVisible
					+ " finalState=" + finalState;
				Console.WriteLine(message);
				SD.StatusBar.SetMessage(message);
			} catch (Exception ex) {
				Console.WriteLine("LibreWPF AvalonDock smoke failed: " + ex);
				SD.StatusBar.SetMessage("LibreWPF AvalonDock smoke failed: " + ex.Message);
			}
		}

		PadDescriptor GetLibreWpfAvalonDockSmokePad(string mode)
		{
			if (IsLibreWpfPopupSmokeMode(mode, "PropertyPad"))
				return GetPad(typeof(PropertyPad));
			if (IsLibreWpfPopupSmokeMode(mode, "ProjectBrowser"))
				return GetPad(typeof(ProjectBrowserPad));

			return GetPad(typeof(ProjectBrowserPad)) ?? GetPad(typeof(PropertyPad));
		}

		async Task RunLibreWpfEditorCompletionSmoke(string mode)
		{
			try {
				await libreWpfWinFormsContextMenuSmokeTask;
				await libreWpfAvalonDockSmokeTask;
				await Task.Delay(2200);
				CodeEditor editor = null;
				for (int attempt = 0; attempt < 100; attempt++) {
					editor = GetLibreWpfCompletionCodeEditor(mode);
					if (editor != null &&
					    editor.Document != null &&
					    editor.FileName != null &&
					    !string.IsNullOrEmpty(editor.Document.Text) &&
					    IsLibreWpfCodeEditorPresentationReady(editor))
						break;
					editor = null;
					await Task.Delay(100);
				}

				if (editor == null || editor.Document == null || editor.FileName == null) {
					Console.WriteLine("LibreWPF editor completion smoke unavailable: no active code editor.");
					SD.StatusBar.SetMessage("LibreWPF editor completion smoke unavailable: no active code editor.");
					return;
				}

				await WaitForLibreWpfProjectLoadAsync();
				CloseLibreWpfSmokePopups();
				if (editor.PrimaryTextEditor.ActiveCompletionWindow != null)
					editor.PrimaryTextEditor.ActiveCompletionWindow.Close();

				int offset;
				string marker;
				int markerIndex;
				if (!TryGetLibreWpfCompletionCaretOffset(editor.Document.Text, mode, out offset, out marker, out markerIndex)) {
					Console.WriteLine("LibreWPF editor completion smoke unavailable: no completion marker in " + editor.FileName);
					SD.StatusBar.SetMessage("LibreWPF editor completion smoke unavailable: no completion marker.");
					return;
				}

				editor.PrimaryTextEditor.Focus();
				editor.PrimaryTextEditor.TextArea.Focus();
				editor.PrimaryTextEditor.TextArea.Caret.Offset = offset;
				editor.PrimaryTextEditor.TextArea.Caret.BringCaretToView();
				await Task.Delay(100);

				int bindingCount = CodeEditor.CodeCompletionBindings.Count;
				bool handled = false;
				foreach (var binding in CodeEditor.CodeCompletionBindings) {
					try {
						if (binding.CtrlSpace(editor.ActiveTextEditorAdapter)) {
							handled = true;
							break;
						}
					} catch (Exception ex) {
						Console.WriteLine("LibreWPF editor completion binding failed: "
							+ binding.GetType().FullName + ": " + ex);
					}
				}

				await Task.Delay(700);
				var window = editor.PrimaryTextEditor.ActiveCompletionWindow;
				int itemCount = window != null ? window.CompletionList.CompletionData.Count : 0;
				string selectedText = "(none)";
				if (window != null && window.CompletionList.SelectedItem != null)
					selectedText = window.CompletionList.SelectedItem.Text;

				string result = window != null && itemCount > 0 ? "Opened" : handled ? "NoWindow" : "NotHandled";
				string message = "LibreWPF editor completion smoke result=" + result
					+ " bindings=" + bindingCount
					+ " items=" + itemCount
					+ " visible=" + (window != null && window.IsVisible)
					+ " selected=" + selectedText
					+ " marker=" + marker
					+ " file=" + Path.GetFileName(editor.FileName.ToString());
				Console.WriteLine(message);
				SD.StatusBar.SetMessage(message);
			} catch (Exception ex) {
				Console.WriteLine("LibreWPF editor completion smoke failed: " + ex);
				SD.StatusBar.SetMessage("LibreWPF editor completion smoke failed: " + ex.Message);
			}
		}

		async Task RunLibreWpfSaveSmoke(string mode, bool saveAll)
		{
			try {
				await WaitForLibreWpfProjectLoadAsync();

				IViewContent content = null;
				CodeEditor editor = null;
				for (int attempt = 0; attempt < 100; attempt++) {
					content = GetLibreWpfSaveSmokeViewContent(mode);
					editor = GetLibreWpfCodeEditor(content);
					if (content != null
					    && editor != null
					    && editor.Document != null
					    && content.PrimaryFile != null
					    && content.PrimaryFile.FileName != null
					    && File.Exists(content.PrimaryFile.FileName)
					    && IsLibreWpfCodeEditorPresentationReady(editor)) {
						break;
					}
					content = null;
					editor = null;
					await Task.Delay(100);
				}

				if (content == null || editor == null || content.PrimaryFile == null || content.PrimaryFile.FileName == null) {
					Console.WriteLine("LibreWPF save smoke unavailable: no file-backed code editor is open.");
					SD.StatusBar.SetMessage("LibreWPF save smoke unavailable: no file-backed code editor is open.");
					return;
				}

				OpenedFile file = content.PrimaryFile;
				string fileName = file.FileName.ToString();
				byte[] originalBytes = File.ReadAllBytes(fileName);
				string originalText = editor.Document.Text;
				bool originalDirty = file.IsDirty;
				bool originalSafeSaving = SD.FileService.SaveUsingTemporaryFile;
				string marker = Environment.NewLine + "// LibreWPF save smoke marker";
				bool markedDirty = false;
				bool saveClearedDirty = false;
				bool diskContainsMarker = false;
				bool diskRestored = false;

				try {
					SD.FileService.SaveUsingTemporaryFile = true;
					SelectLibreWpfViewContent(content);
					editor.Document.Insert(editor.Document.TextLength, marker);
					file.MakeDirty();
					await Task.Delay(100);
					markedDirty = file.IsDirty && content.IsDirty;

					if (saveAll) {
						ICSharpCode.SharpDevelop.Commands.SaveAllFiles.SaveAll();
					} else {
						ICSharpCode.SharpDevelop.Commands.SaveFile.Save(content);
					}
					await Task.Delay(100);

					saveClearedDirty = !file.IsDirty && !content.IsDirty;
					diskContainsMarker = File.ReadAllText(fileName).Contains(marker);
				} finally {
					try {
						if (editor.Document != null) {
							editor.Document.Text = originalText;
							file.MakeDirty();
							file.SaveToDisk();
						}
					} catch (Exception ex) {
						Console.WriteLine("LibreWPF save smoke restore through save path failed: " + ex);
						File.WriteAllBytes(fileName, originalBytes);
					} finally {
						SD.FileService.SaveUsingTemporaryFile = originalSafeSaving;
						if (editor.Document != null) {
							editor.Document.Text = originalText;
							if (!originalDirty) {
								editor.Document.UndoStack.MarkAsOriginalFile();
							}
						}
						file.IsDirty = originalDirty;
						diskRestored = originalBytes.SequenceEqual(File.ReadAllBytes(fileName));
					}
				}

				string message = "LibreWPF save smoke result="
					+ (markedDirty && saveClearedDirty && diskContainsMarker && diskRestored ? "Success" : "Partial")
					+ " command=" + (saveAll ? "SaveAll" : "Save")
					+ " file=" + Path.GetFileName(fileName)
					+ " markedDirty=" + markedDirty
					+ " saveClearedDirty=" + saveClearedDirty
					+ " diskContainsMarker=" + diskContainsMarker
					+ " diskRestored=" + diskRestored
					+ " safeSaving=True";
				Console.WriteLine(message);
				SD.StatusBar.SetMessage(message);
			} catch (Exception ex) {
				Console.WriteLine("LibreWPF save smoke failed: " + ex);
				SD.StatusBar.SetMessage("LibreWPF save smoke failed: " + ex.Message);
			}
		}

		async Task RunLibreWpfReloadSmoke(string mode)
		{
			try {
				await WaitForLibreWpfProjectLoadAsync();

				IViewContent content = null;
				CodeEditor editor = null;
				for (int attempt = 0; attempt < 100; attempt++) {
					content = GetLibreWpfSaveSmokeViewContent(mode);
					editor = GetLibreWpfCodeEditor(content);
					if (content != null
					    && editor != null
					    && editor.Document != null
					    && content.PrimaryFile != null
					    && content.PrimaryFile.FileName != null
					    && File.Exists(content.PrimaryFile.FileName)
					    && IsLibreWpfCodeEditorPresentationReady(editor)) {
						break;
					}
					content = null;
					editor = null;
					await Task.Delay(100);
				}

				if (content == null || editor == null || content.PrimaryFile == null || content.PrimaryFile.FileName == null) {
					Console.WriteLine("LibreWPF reload smoke unavailable: no file-backed code editor is open.");
					SD.StatusBar.SetMessage("LibreWPF reload smoke unavailable: no file-backed code editor is open.");
					return;
				}

				OpenedFile file = content.PrimaryFile;
				string fileName = file.FileName.ToString();
				byte[] originalBytes = File.ReadAllBytes(fileName);
				string originalText = editor.Document.Text;
				bool originalDirty = file.IsDirty;
				string marker = Environment.NewLine + "// LibreWPF reload smoke marker";
				bool diskChanged = false;
				bool commandReloaded = false;
				bool dirtyCleared = false;
				bool diskRestored = false;
				bool editorRestored = false;

				try {
					SelectLibreWpfViewContent(content);
					string diskText = File.ReadAllText(fileName);
					File.WriteAllText(fileName, diskText + marker);
					diskChanged = File.ReadAllText(fileName).Contains(marker);
					await Task.Delay(650);

					new ICSharpCode.SharpDevelop.Commands.ReloadFile().Run();
					await Task.Delay(200);

					commandReloaded = editor.Document.Text.Contains(marker);
					dirtyCleared = !file.IsDirty && !content.IsDirty;
				} finally {
					File.WriteAllBytes(fileName, originalBytes);
					try {
						file.ReloadFromDisk();
						editorRestored = editor.Document != null && editor.Document.Text == originalText;
					} catch (Exception ex) {
						Console.WriteLine("LibreWPF reload smoke restore reload failed: " + ex);
						if (editor.Document != null) {
							editor.Document.Text = originalText;
							editorRestored = true;
						}
					} finally {
						if (editor.Document != null && !originalDirty) {
							editor.Document.UndoStack.MarkAsOriginalFile();
						}
						file.IsDirty = originalDirty;
						diskRestored = originalBytes.SequenceEqual(File.ReadAllBytes(fileName));
					}
				}

				string message = "LibreWPF reload smoke result="
					+ (diskChanged && commandReloaded && dirtyCleared && diskRestored && editorRestored ? "Success" : "Partial")
					+ " command=Reload"
					+ " file=" + Path.GetFileName(fileName)
					+ " diskChanged=" + diskChanged
					+ " commandReloaded=" + commandReloaded
					+ " dirtyCleared=" + dirtyCleared
					+ " diskRestored=" + diskRestored
					+ " editorRestored=" + editorRestored;
				Console.WriteLine(message);
				SD.StatusBar.SetMessage(message);
			} catch (Exception ex) {
				Console.WriteLine("LibreWPF reload smoke failed: " + ex);
				SD.StatusBar.SetMessage("LibreWPF reload smoke failed: " + ex.Message);
			}
		}

		async Task RunLibreWpfNewFileSmoke(string mode)
		{
			string directory = null;
			string savePath = null;
			IViewContent content = null;
			OpenedFile file = null;
			try {
				await WaitForLibreWpfProjectLoadAsync();

				string defaultName = NormalizeLibreWpfNewFileSmokeName(mode);
				string marker = "LibreWPF new file smoke marker " + Guid.NewGuid().ToString("N");
				string sourceText = "using System;" + Environment.NewLine
					+ Environment.NewLine
					+ "public sealed class LibreWpfNewFileSmoke" + Environment.NewLine
					+ "{" + Environment.NewLine
					+ "\tpublic string Marker { get { return \"" + marker + "\"; } }" + Environment.NewLine
					+ "}" + Environment.NewLine;

				content = SD.FileService.NewFile(defaultName, sourceText);
				await Task.Delay(300);

				CodeEditor editor = GetLibreWpfCodeEditor(content);
				file = content != null ? content.PrimaryFile : null;
				bool createdView = content != null
					&& editor != null
					&& editor.Document != null
					&& file != null
					&& IsLibreWpfCodeEditorPresentationReady(editor);
				bool untitledBeforeSave = file != null && file.IsUntitled;
				bool editorContainsMarker = editor != null && editor.Document != null && editor.Document.Text.Contains(marker);

				directory = Path.Combine(Path.GetTempPath(), "librewpf-sharpdevelop-smoke-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(directory);
				savePath = Path.Combine(directory, defaultName);

				if (file != null) {
					file.SaveToDisk(FileName.Create(savePath));
				}
				await Task.Delay(150);

				bool fileNameUpdated = file != null
					&& file.FileName != null
					&& string.Equals(Path.GetFullPath(file.FileName.ToString()), Path.GetFullPath(savePath), StringComparison.Ordinal);
				bool savedToDisk = File.Exists(savePath);
				bool diskContainsMarker = savedToDisk && File.ReadAllText(savePath).Contains(marker);
				bool dirtyCleared = file != null && content != null && !file.IsUntitled && !file.IsDirty && !content.IsDirty;
				bool openedFileRekeyed = file != null && SD.FileService.GetOpenedFile(FileName.Create(savePath)) == file;

				bool closed = false;
				if (content != null && content.WorkbenchWindow != null) {
					content.WorkbenchWindow.CloseWindow(true);
					await Task.Delay(200);
					closed = !SD.Workbench.ViewContentCollection.Contains(content)
						&& SD.FileService.GetOpenedFile(FileName.Create(savePath)) == null;
				}

				if (savePath != null && File.Exists(savePath)) {
					File.Delete(savePath);
				}
				if (directory != null && Directory.Exists(directory)) {
					Directory.Delete(directory, true);
				}
				bool cleanup = savePath != null && !File.Exists(savePath) && directory != null && !Directory.Exists(directory);

				string message = "LibreWPF new-file smoke result="
					+ (createdView && untitledBeforeSave && editorContainsMarker && fileNameUpdated && savedToDisk && diskContainsMarker && dirtyCleared && openedFileRekeyed && closed && cleanup ? "Success" : "Partial")
					+ " file=" + defaultName
					+ " createdView=" + createdView
					+ " untitledBeforeSave=" + untitledBeforeSave
					+ " editorContainsMarker=" + editorContainsMarker
					+ " fileNameUpdated=" + fileNameUpdated
					+ " savedToDisk=" + savedToDisk
					+ " diskContainsMarker=" + diskContainsMarker
					+ " dirtyCleared=" + dirtyCleared
					+ " openedFileRekeyed=" + openedFileRekeyed
					+ " closed=" + closed
					+ " cleanup=" + cleanup;
				Console.WriteLine(message);
				SD.StatusBar.SetMessage(message);
			} catch (Exception ex) {
				Console.WriteLine("LibreWPF new-file smoke failed: " + ex);
				SD.StatusBar.SetMessage("LibreWPF new-file smoke failed: " + ex.Message);
				try {
					if (content != null && content.WorkbenchWindow != null) {
						content.WorkbenchWindow.CloseWindow(true);
					}
					if (savePath != null && File.Exists(savePath)) {
						File.Delete(savePath);
					}
					if (directory != null && Directory.Exists(directory)) {
						Directory.Delete(directory, true);
					}
				} catch (Exception cleanupException) {
					Console.WriteLine("LibreWPF new-file smoke cleanup failed: " + cleanupException);
				}
			}
		}

		async Task RunLibreWpfTemplateSmoke(string mode)
		{
			string directory = null;
			string filePath = null;
			try {
				await WaitForLibreWpfProjectLoadAsync();

				SD.Templates.UpdateTemplates();
				IReadOnlyList<TemplateCategory> rootCategories = SD.Templates.TemplateCategories;
				List<TemplateCategory> categories = EnumerateLibreWpfTemplateCategories(rootCategories).ToList();
				List<FileTemplate> fileTemplates = categories
					.SelectMany(category => category.Templates.OfType<FileTemplate>())
					.ToList();
				List<ProjectTemplate> projectTemplates = categories
					.SelectMany(category => category.Templates.OfType<ProjectTemplate>())
					.ToList();

				directory = Path.Combine(Path.GetTempPath(), "librewpf-sharpdevelop-template-smoke-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(directory);
				DirectoryName templateDirectory = DirectoryName.Create(directory);
				FileTemplate template = GetLibreWpfTemplateSmokeFileTemplate(mode, fileTemplates, templateDirectory);

				string suggestedName = template != null ? template.SuggestFileName(templateDirectory) : null;
				if (string.IsNullOrWhiteSpace(suggestedName)) {
					suggestedName = "LibreWpfTemplateSmoke.txt";
				}
				suggestedName = Path.GetFileName(suggestedName);
				if (string.IsNullOrWhiteSpace(suggestedName)) {
					suggestedName = "LibreWpfTemplateSmoke.txt";
				}
				filePath = Path.Combine(directory, suggestedName);

				FileTemplateResult result = null;
				bool createdDiskFile = false;
				bool createdOpenedFile = false;
				bool resultContainsDiskFile = false;
				bool openedFileRegistered = false;
				bool createdOpenFilesClosed = false;
				bool cleanup = false;
				List<FileName> createdOpenFileNames = new List<FileName>();

				if (template != null) {
					FileTemplateOptions options = new FileTemplateOptions {
						ClassName = CreateLibreWpfTemplateSmokeIdentifier(Path.GetFileNameWithoutExtension(filePath)),
						CustomizationObject = template.CreateCustomizationObject(),
						FileName = FileName.Create(filePath),
						IsUntitled = false,
						Namespace = "LibreWpf.TemplateSmoke",
						Project = null
					};

					result = template.Create(options);
					if (result != null) {
						template.RunActions(result);
					}
					createdDiskFile = File.Exists(filePath);
					resultContainsDiskFile = result != null
						&& result.NewFiles.Any(file => string.Equals(Path.GetFullPath(file.ToString()), Path.GetFullPath(filePath), StringComparison.Ordinal));
					createdOpenedFile = result != null && result.NewOpenedFiles.Count > 0;
					if (result != null) {
						foreach (OpenedFile openedFile in result.NewOpenedFiles.ToArray()) {
							if (openedFile == null || openedFile.FileName == null)
								continue;

							createdOpenFileNames.Add(openedFile.FileName);
							if (SD.FileService.GetOpenedFile(openedFile.FileName) == openedFile) {
								openedFileRegistered = true;
							}
							foreach (IViewContent viewContent in openedFile.RegisteredViewContents.ToArray()) {
								if (viewContent.WorkbenchWindow != null) {
									viewContent.WorkbenchWindow.CloseWindow(true);
								} else {
									viewContent.Dispose();
								}
							}
							openedFile.CloseIfAllViewsClosed();
						}
					}
				}

				if (filePath != null && File.Exists(filePath)) {
					File.Delete(filePath);
				}
				if (directory != null && Directory.Exists(directory)) {
					Directory.Delete(directory, true);
				}
				cleanup = filePath != null && !File.Exists(filePath) && directory != null && !Directory.Exists(directory);
				createdOpenFilesClosed = createdOpenFileNames.All(fileName => SD.FileService.GetOpenedFile(fileName) == null);

				bool loadedTemplates = categories.Count > 0 && fileTemplates.Count > 0 && projectTemplates.Count > 0;
				bool createdTemplateOutput = (createdDiskFile && resultContainsDiskFile) || createdOpenedFile;
				string message = "LibreWPF template smoke result="
					+ (loadedTemplates && template != null && result != null && createdTemplateOutput && openedFileRegistered && createdOpenFilesClosed && cleanup ? "Success" : "Partial")
					+ " categories=" + categories.Count
					+ " fileTemplates=" + fileTemplates.Count
					+ " projectTemplates=" + projectTemplates.Count
					+ " selectedFileTemplate=" + (template != null ? template.DisplayName : "<none>")
					+ " requestedFile=" + (filePath ?? "<none>")
					+ " createdDiskFile=" + createdDiskFile
					+ " resultContainsDiskFile=" + resultContainsDiskFile
					+ " createdOpenedFile=" + createdOpenedFile
					+ " openedFileRegistered=" + openedFileRegistered
					+ " createdOpenFilesClosed=" + createdOpenFilesClosed
					+ " cleanup=" + cleanup;
				Console.WriteLine(message);
				SD.StatusBar.SetMessage(message);
			} catch (Exception ex) {
				Console.WriteLine("LibreWPF template smoke failed: " + ex);
				SD.StatusBar.SetMessage("LibreWPF template smoke failed: " + ex.Message);
				try {
					if (filePath != null && File.Exists(filePath)) {
						File.Delete(filePath);
					}
					if (directory != null && Directory.Exists(directory)) {
						Directory.Delete(directory, true);
					}
				} catch (Exception cleanupEx) {
					Console.WriteLine("LibreWPF template smoke cleanup failed: " + cleanupEx);
				}
			}
		}

		async Task RunLibreWpfEditorNavigationSmoke(string mode)
		{
			try {
				await WaitForLibreWpfProjectLoadAsync();

				IViewContent content = null;
				CodeEditor editor = null;
				for (int attempt = 0; attempt < 100; attempt++) {
					content = GetLibreWpfSaveSmokeViewContent(mode);
					editor = GetLibreWpfCodeEditor(content);
					if (content != null
					    && editor != null
					    && editor.Document != null
					    && content.PrimaryFile != null
					    && content.PrimaryFile.FileName != null
					    && File.Exists(content.PrimaryFile.FileName)
					    && IsLibreWpfCodeEditorPresentationReady(editor)) {
						break;
					}
					content = null;
					editor = null;
					await Task.Delay(100);
				}

				if (content == null || editor == null || editor.Document == null || content.PrimaryFile == null || content.PrimaryFile.FileName == null) {
					Console.WriteLine("LibreWPF editor navigation smoke unavailable: no file-backed code editor is open.");
					SD.StatusBar.SetMessage("LibreWPF editor navigation smoke unavailable: no file-backed code editor is open.");
					return;
				}

				string marker = GetLibreWpfEditorNavigationMarker(mode);
				int markerOffset = editor.Document.Text.IndexOf(marker, StringComparison.Ordinal);
				if (markerOffset < 0) {
					marker = "class ";
					markerOffset = editor.Document.Text.IndexOf(marker, StringComparison.Ordinal);
				}
				if (markerOffset < 0) {
					marker = "using ";
					markerOffset = editor.Document.Text.IndexOf(marker, StringComparison.Ordinal);
				}
				if (markerOffset < 0) {
					Console.WriteLine("LibreWPF editor navigation smoke unavailable: no navigation marker in " + content.PrimaryFile.FileName);
					SD.StatusBar.SetMessage("LibreWPF editor navigation smoke unavailable: no navigation marker.");
					return;
				}

				var location = editor.Document.GetLocation(markerOffset);
				string fileName = content.PrimaryFile.FileName.ToString();
				IViewContent jumpedContent = SD.FileService.JumpToFilePosition(content.PrimaryFile.FileName, location.Line, location.Column);
				await Task.Delay(350);

				CodeEditor jumpedEditor = GetLibreWpfCodeEditor(jumpedContent) ?? GetLibreWpfActiveCodeEditor();
				bool active = jumpedContent != null
					&& jumpedContent.WorkbenchWindow != null
					&& jumpedContent.WorkbenchWindow.ActiveViewContent == jumpedContent;
				bool sameFile = jumpedContent != null
					&& jumpedContent.PrimaryFile != null
					&& jumpedContent.PrimaryFile.FileName != null
					&& string.Equals(
						Path.GetFullPath(jumpedContent.PrimaryFile.FileName.ToString()),
						Path.GetFullPath(fileName),
						StringComparison.Ordinal);
				int caretLine = jumpedEditor != null && jumpedEditor.PrimaryTextEditor != null ? jumpedEditor.PrimaryTextEditor.TextArea.Caret.Line : -1;
				int caretColumn = jumpedEditor != null && jumpedEditor.PrimaryTextEditor != null ? jumpedEditor.PrimaryTextEditor.TextArea.Caret.Column : -1;
				int caretOffset = jumpedEditor != null && jumpedEditor.PrimaryTextEditor != null ? jumpedEditor.PrimaryTextEditor.TextArea.Caret.Offset : -1;
				bool caretMatched = caretLine == location.Line && caretColumn == location.Column;
				bool offsetMatched = Math.Abs(caretOffset - markerOffset) <= 1;
				bool visible = jumpedEditor != null && IsLibreWpfCodeEditorPresentationReady(jumpedEditor);

				string message = "LibreWPF editor navigation smoke result="
					+ (active && sameFile && caretMatched && offsetMatched && visible ? "Success" : "Partial")
					+ " file=" + Path.GetFileName(fileName)
					+ " marker=" + marker
					+ " requestedLine=" + location.Line
					+ " requestedColumn=" + location.Column
					+ " caretLine=" + caretLine
					+ " caretColumn=" + caretColumn
					+ " caretOffset=" + caretOffset
					+ " expectedOffset=" + markerOffset
					+ " active=" + active
					+ " sameFile=" + sameFile
					+ " visible=" + visible;
				Console.WriteLine(message);
				SD.StatusBar.SetMessage(message);
			} catch (Exception ex) {
				Console.WriteLine("LibreWPF editor navigation smoke failed: " + ex);
				SD.StatusBar.SetMessage("LibreWPF editor navigation smoke failed: " + ex.Message);
			}
		}

		async Task RunLibreWpfCloseAllSmoke(string mode)
		{
			try {
				await WaitForLibreWpfProjectLoadAsync();

				List<FileName> fileNames = GetLibreWpfCloseAllSmokeFileNames(mode).ToList();
				List<IViewContent> openedContents = new List<IViewContent>();
				foreach (FileName fileName in fileNames) {
					IViewContent content = SD.FileService.OpenFile(fileName, true);
					CodeEditor editor = GetLibreWpfCodeEditor(content);
					for (int attempt = 0; attempt < 50 && (editor == null || editor.Document == null || !IsLibreWpfCodeEditorPresentationReady(editor)); attempt++) {
						await Task.Delay(100);
						editor = GetLibreWpfCodeEditor(content);
					}
					if (content != null && editor != null && editor.Document != null)
						openedContents.Add(content);
				}

				if (openedContents.Count == 0) {
					Console.WriteLine("LibreWPF close-all smoke unavailable: no file-backed code editors were opened.");
					SD.StatusBar.SetMessage("LibreWPF close-all smoke unavailable: no file-backed code editors were opened.");
					return;
				}

				int beforeViews = SD.Workbench.ViewContentCollection.Count;
				int beforeTrackedOpenFiles = fileNames.Count(fileName => SD.FileService.GetOpenedFile(fileName) != null);
				new ICSharpCode.SharpDevelop.Commands.CloseAllWindows().Run();
				await Task.Delay(500);

				int afterViews = SD.Workbench.ViewContentCollection.Count;
				int afterTrackedOpenFiles = fileNames.Count(fileName => SD.FileService.GetOpenedFile(fileName) != null);
				int remainingOpenedContents = openedContents.Count(content => SD.Workbench.ViewContentCollection.Contains(content));
				bool allRequestedClosed = remainingOpenedContents == 0 && afterTrackedOpenFiles == 0;

				string message = "LibreWPF close-all smoke result="
					+ (allRequestedClosed ? "Success" : "Partial")
					+ " requestedFiles=" + fileNames.Count
					+ " openedEditors=" + openedContents.Count
					+ " beforeViews=" + beforeViews
					+ " afterViews=" + afterViews
					+ " beforeTrackedOpenFiles=" + beforeTrackedOpenFiles
					+ " afterTrackedOpenFiles=" + afterTrackedOpenFiles
					+ " remainingOpenedContents=" + remainingOpenedContents;
				Console.WriteLine(message);
				SD.StatusBar.SetMessage(message);
			} catch (Exception ex) {
				Console.WriteLine("LibreWPF close-all smoke failed: " + ex);
				SD.StatusBar.SetMessage("LibreWPF close-all smoke failed: " + ex.Message);
			}
		}

		async Task RunLibreWpfCloseReopenSolutionSmoke(string mode)
		{
			try {
				await WaitForLibreWpfProjectLoadAsync();

				ISolution solution = SD.ProjectService.CurrentSolution;
				if (solution == null || solution.FileName == null || !File.Exists(solution.FileName)) {
					Console.WriteLine("LibreWPF close/reopen solution smoke unavailable: no file-backed solution is open.");
					SD.StatusBar.SetMessage("LibreWPF close/reopen solution smoke unavailable: no file-backed solution is open.");
					return;
				}

				FileName solutionFileName = solution.FileName;
				int beforeProjects = solution.Projects.Count();
				List<FileName> fileNames = GetLibreWpfCloseAllSmokeFileNames(mode).ToList();
				List<IViewContent> openedContents = new List<IViewContent>();
				foreach (FileName fileName in fileNames) {
					IViewContent content = SD.FileService.OpenFile(fileName, true);
					CodeEditor editor = GetLibreWpfCodeEditor(content);
					for (int attempt = 0; attempt < 50 && (editor == null || editor.Document == null || !IsLibreWpfCodeEditorPresentationReady(editor)); attempt++) {
						await Task.Delay(100);
						editor = GetLibreWpfCodeEditor(content);
					}
					if (content != null && editor != null && editor.Document != null)
						openedContents.Add(content);
				}

				int beforeViews = SD.Workbench.ViewContentCollection.Count;
				int beforeTrackedOpenFiles = fileNames.Count(fileName => SD.FileService.GetOpenedFile(fileName) != null);

				new ICSharpCode.SharpDevelop.Project.Commands.CloseSolution().Run();
				await Task.Delay(700);

				bool closeClearedSolution = SD.ProjectService.CurrentSolution == null;
				int viewsAfterClose = SD.Workbench.ViewContentCollection.Count;
				int trackedOpenFilesAfterClose = fileNames.Count(fileName => SD.FileService.GetOpenedFile(fileName) != null);
				int remainingOpenedContentsAfterClose = openedContents.Count(content => SD.Workbench.ViewContentCollection.Contains(content));

				bool reopened = SD.ProjectService.OpenSolutionOrProject(solutionFileName);
				await WaitForLibreWpfProjectLoadAsync();
				await Task.Delay(500);

				ISolution reopenedSolution = SD.ProjectService.CurrentSolution;
				bool reopenedCurrentSolution = reopenedSolution != null
					&& reopenedSolution.FileName != null
					&& string.Equals(
						Path.GetFullPath(reopenedSolution.FileName.ToString()),
						Path.GetFullPath(solutionFileName.ToString()),
						StringComparison.Ordinal);
				int afterProjects = reopenedSolution != null ? reopenedSolution.Projects.Count() : 0;
				bool projectCountRestored = afterProjects == beforeProjects;

				string message = "LibreWPF close/reopen solution smoke result="
					+ (closeClearedSolution
					    && viewsAfterClose == 0
					    && trackedOpenFilesAfterClose == 0
					    && remainingOpenedContentsAfterClose == 0
					    && reopened
					    && reopenedCurrentSolution
					    && projectCountRestored ? "Success" : "Partial")
					+ " solution=" + Path.GetFileName(solutionFileName.ToString())
					+ " beforeProjects=" + beforeProjects
					+ " afterProjects=" + afterProjects
					+ " beforeViews=" + beforeViews
					+ " viewsAfterClose=" + viewsAfterClose
					+ " beforeTrackedOpenFiles=" + beforeTrackedOpenFiles
					+ " trackedOpenFilesAfterClose=" + trackedOpenFilesAfterClose
					+ " remainingOpenedContentsAfterClose=" + remainingOpenedContentsAfterClose
					+ " closeClearedSolution=" + closeClearedSolution
					+ " reopened=" + reopened
					+ " reopenedCurrentSolution=" + reopenedCurrentSolution;
				Console.WriteLine(message);
				SD.StatusBar.SetMessage(message);
			} catch (Exception ex) {
				Console.WriteLine("LibreWPF close/reopen solution smoke failed: " + ex);
				SD.StatusBar.SetMessage("LibreWPF close/reopen solution smoke failed: " + ex.Message);
			}
		}

		async Task RunLibreWpfShutdownPersistenceSmoke(string mode)
		{
			try {
				await WaitForLibreWpfProjectLoadAsync();

				string marker = NormalizeLibreWpfShutdownPersistenceMarker(mode);
				Properties nested = new Properties();
				nested.Set("Marker", marker);
				nested.Set("PreparedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
				nested.Set("HadSolution", SD.ProjectService.CurrentSolution != null);
				nested.Set("ViewCount", SD.Workbench.ViewContentCollection.Count);
				nested.Set("ConfigDirectory", SD.PropertyService.ConfigDirectory.ToString());
				SD.PropertyService.Set("LibreWpf.ShutdownPersistenceSmoke.Marker", marker);
				SD.PropertyService.SetNestedProperties("LibreWpfShutdownPersistenceSmoke", nested);

				string message = "LibreWPF shutdown persistence smoke prepared marker=" + marker
					+ " config=" + SD.PropertyService.ConfigDirectory;
				Console.WriteLine(message);
				SD.StatusBar.SetMessage(message);
			} catch (Exception ex) {
				Console.WriteLine("LibreWPF shutdown persistence smoke failed: " + ex);
				SD.StatusBar.SetMessage("LibreWPF shutdown persistence smoke failed: " + ex.Message);
			}
		}

		IViewContent GetLibreWpfSaveSmokeViewContent(string mode)
		{
			string requested = NormalizeLibreWpfSaveSmokePath(mode);
			if (!string.IsNullOrEmpty(requested) && File.Exists(requested)) {
				return SD.FileService.OpenFile(FileName.Create(requested), true);
			}

			foreach (IViewContent content in GetLibreWpfCandidateViewContents()) {
				CodeEditor editor = GetLibreWpfCodeEditor(content);
				if (editor != null
				    && editor.Document != null
				    && content.PrimaryFile != null
				    && content.PrimaryFile.FileName != null
				    && File.Exists(content.PrimaryFile.FileName)) {
					return content;
				}
			}

			return null;
		}

		string NormalizeLibreWpfSaveSmokePath(string mode)
		{
			if (string.IsNullOrWhiteSpace(mode))
				return null;
			string value = mode.Trim();
			if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
			    || string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase)
			    || string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase))
				return null;
			if (Path.IsPathRooted(value))
				return value;

			CompilableProject project = GetLibreWpfSmokeProject();
			if (project != null) {
				string projectRelative = Path.Combine(project.Directory, value);
				if (File.Exists(projectRelative))
					return projectRelative;
			}

			return Path.GetFullPath(value);
		}

		static string NormalizeLibreWpfNewFileSmokeName(string mode)
		{
			if (string.IsNullOrWhiteSpace(mode))
				return "LibreWpfNewFileSmoke.cs";
			string value = mode.Trim();
			if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
			    || string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase)
			    || string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase))
				return "LibreWpfNewFileSmoke.cs";
			string fileName = Path.GetFileName(value);
			if (string.IsNullOrWhiteSpace(fileName))
				return "LibreWpfNewFileSmoke.cs";
			return fileName;
		}

		static IEnumerable<TemplateCategory> EnumerateLibreWpfTemplateCategories(IEnumerable<TemplateCategory> categories)
		{
			if (categories == null)
				yield break;

			foreach (TemplateCategory category in categories) {
				if (category == null)
					continue;

				yield return category;
				foreach (TemplateCategory child in EnumerateLibreWpfTemplateCategories(category.Subcategories)) {
					yield return child;
				}
			}
		}

		static FileTemplate GetLibreWpfTemplateSmokeFileTemplate(string mode, IList<FileTemplate> templates, DirectoryName basePath)
		{
			if (templates == null || templates.Count == 0)
				return null;

			string requested = NormalizeLibreWpfTemplateSmokeName(mode);
			if (requested != null) {
				FileTemplate requestedTemplate = templates.FirstOrDefault(template =>
					string.Equals(template.Name, requested, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(template.DisplayName, requested, StringComparison.OrdinalIgnoreCase));
				if (requestedTemplate != null)
					return requestedTemplate;
			}

			FileTemplate textTemplate = templates.FirstOrDefault(template => IsLibreWpfTemplateSmokeDefaultCandidate(template, basePath, ".txt"));
			if (textTemplate != null)
				return textTemplate;

			return templates.FirstOrDefault(template => IsLibreWpfTemplateSmokeDefaultCandidate(template, basePath, null)) ?? templates[0];
		}

		static bool IsLibreWpfTemplateSmokeDefaultCandidate(FileTemplate template, DirectoryName basePath, string extension)
		{
			if (template == null || !template.IsVisible(null))
				return false;

			string suggestedName;
			try {
				suggestedName = template.SuggestFileName(basePath);
			} catch {
				return false;
			}

			if (string.IsNullOrWhiteSpace(suggestedName))
				return false;
			if (suggestedName.IndexOf(Path.DirectorySeparatorChar) >= 0 || suggestedName.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
				return false;
			if (extension != null && !string.Equals(Path.GetExtension(suggestedName), extension, StringComparison.OrdinalIgnoreCase))
				return false;

			return true;
		}

		static string NormalizeLibreWpfTemplateSmokeName(string mode)
		{
			if (string.IsNullOrWhiteSpace(mode))
				return null;
			string value = mode.Trim();
			if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
			    || string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase)
			    || string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase))
				return null;
			return value;
		}

		static string CreateLibreWpfTemplateSmokeIdentifier(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return "LibreWpfTemplateSmoke";

			StringBuilder builder = new StringBuilder(value.Length);
			for (int i = 0; i < value.Length; i++) {
				char ch = value[i];
				if (i == 0) {
					builder.Append(char.IsLetter(ch) || ch == '_' ? ch : '_');
				} else {
					builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
				}
			}

			return builder.Length > 0 ? builder.ToString() : "LibreWpfTemplateSmoke";
		}

		static string GetLibreWpfEditorNavigationMarker(string mode)
		{
			if (string.IsNullOrWhiteSpace(mode))
				return "LineCounterBrowser";
			string value = mode.Trim();
			if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
			    || string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase)
			    || string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase))
				return "LineCounterBrowser";

			foreach (string token in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)) {
				string marker = token.Trim();
				if (marker.Length > 0 && !marker.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
					return marker;
			}

			return "LineCounterBrowser";
		}

		IEnumerable<FileName> GetLibreWpfCloseAllSmokeFileNames(string mode)
		{
			HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
			foreach (string token in GetLibreWpfCloseAllSmokePathTokens(mode)) {
				string path = NormalizeLibreWpfSaveSmokePath(token);
				if (!string.IsNullOrEmpty(path) && File.Exists(path))
					paths.Add(Path.GetFullPath(path));
			}

			if (paths.Count == 0) {
				string lineCounterBrowser = NormalizeLibreWpfSaveSmokePath("Src/LineCounterBrowser.cs");
				if (!string.IsNullOrEmpty(lineCounterBrowser) && File.Exists(lineCounterBrowser))
					paths.Add(Path.GetFullPath(lineCounterBrowser));

				string extensibility = NormalizeLibreWpfSaveSmokePath("Src/Extensibility.cs");
				if (!string.IsNullOrEmpty(extensibility) && File.Exists(extensibility))
					paths.Add(Path.GetFullPath(extensibility));
			}

			foreach (string path in paths)
				yield return FileName.Create(path);
		}

		static IEnumerable<string> GetLibreWpfCloseAllSmokePathTokens(string mode)
		{
			if (string.IsNullOrWhiteSpace(mode)
			    || string.Equals(mode.Trim(), "1", StringComparison.OrdinalIgnoreCase)
			    || string.Equals(mode.Trim(), "Auto", StringComparison.OrdinalIgnoreCase)
			    || string.Equals(mode.Trim(), "Default", StringComparison.OrdinalIgnoreCase))
				yield break;

			foreach (string token in mode.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)) {
				string value = token.Trim();
				if (value.Length > 0)
					yield return value;
			}
		}

		static string NormalizeLibreWpfShutdownPersistenceMarker(string mode)
		{
			if (string.IsNullOrWhiteSpace(mode)
			    || string.Equals(mode.Trim(), "1", StringComparison.OrdinalIgnoreCase)
			    || string.Equals(mode.Trim(), "Auto", StringComparison.OrdinalIgnoreCase)
			    || string.Equals(mode.Trim(), "Default", StringComparison.OrdinalIgnoreCase))
				return "LibreWpfShutdownPersistenceSmoke";

			return mode.Trim();
		}

		static async Task WaitForLibreWpfProjectLoadAsync()
		{
			for (int attempt = 0; attempt < 150; attempt++) {
				if (!SD.ParserService.LoadSolutionProjectsThread.IsRunning)
					return;
				await Task.Delay(100);
			}

			if (Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_TRACE_OPEN") == "1") {
				Console.WriteLine("LibreWPF editor completion smoke continuing while project load is still running.");
			}
		}

		CodeEditor GetLibreWpfCompletionCodeEditor(string mode)
		{
			CodeEditor fallback = GetLibreWpfActiveCodeEditor();
			CodeEditor bestEditor = null;
			IViewContent bestContent = null;
			int bestMarkerIndex = int.MaxValue;

			foreach (IViewContent content in GetLibreWpfCandidateViewContents()) {
				CodeEditor editor = GetLibreWpfCodeEditor(content);
				if (editor.Document == null)
					continue;

				int offset;
				string marker;
				int markerIndex;
				if (!TryGetLibreWpfCompletionCaretOffset(editor.Document.Text, mode, out offset, out marker, out markerIndex))
					continue;

				if (markerIndex < bestMarkerIndex) {
					bestMarkerIndex = markerIndex;
					bestEditor = editor;
					bestContent = content;
				}
			}

			SelectLibreWpfViewContent(bestContent);
			return bestEditor ?? fallback;
		}

		IEnumerable<IViewContent> GetLibreWpfCandidateViewContents()
		{
			HashSet<IViewContent> contents = new HashSet<IViewContent>();
			AddLibreWpfViewContent(contents, ActiveWorkbenchWindow != null ? ActiveWorkbenchWindow.ActiveViewContent : null);
			AddLibreWpfViewContent(contents, ActiveViewContent);
			foreach (IViewContent content in ViewContentCollection) {
				AddLibreWpfViewContent(contents, content);
			}
			return contents;
		}

		static void AddLibreWpfViewContent(HashSet<IViewContent> contents, IViewContent content)
		{
			if (content != null && GetLibreWpfCodeEditor(content) != null)
				contents.Add(content);
		}

		CodeEditor GetLibreWpfActiveCodeEditor()
		{
			IWorkbenchWindow window = ActiveWorkbenchWindow;
			if (window != null) {
				CodeEditor editor = GetLibreWpfCodeEditor(window.ActiveViewContent);
				if (editor != null)
					return editor;
			}

			CodeEditor activeEditor = GetLibreWpfCodeEditor(ActiveViewContent);
			if (activeEditor != null)
				return activeEditor;

			foreach (IViewContent content in ViewContentCollection) {
				CodeEditor editor = GetLibreWpfCodeEditor(content);
				if (editor != null)
					return editor;
			}

			return null;
		}

		static CodeEditor GetLibreWpfCodeEditor(IViewContent content)
		{
			return content != null ? content.Control as CodeEditor : null;
		}

		static void SelectLibreWpfViewContent(IViewContent content)
		{
			if (content == null || content.WorkbenchWindow == null)
				return;

			content.WorkbenchWindow.ActiveViewContent = content;
			content.WorkbenchWindow.SelectWindow();
		}

		static bool IsLibreWpfCodeEditorPresentationReady(CodeEditor editor)
		{
			if (editor == null || editor.PrimaryTextEditor == null || editor.PrimaryTextEditor.TextArea == null)
				return false;

			editor.PrimaryTextEditor.ApplyTemplate();
			editor.PrimaryTextEditor.UpdateLayout();
			editor.PrimaryTextEditor.TextArea.ApplyTemplate();
			editor.PrimaryTextEditor.TextArea.UpdateLayout();

			return editor.PrimaryTextEditor.TextArea.TextView != null &&
				PresentationSource.FromVisual(editor.PrimaryTextEditor.TextArea.TextView) != null;
		}

		static bool TryGetLibreWpfCompletionCaretOffset(string text, string mode, out int offset, out string marker, out int markerIndex)
		{
			string[] markers = GetLibreWpfCompletionMarkers(mode);

			for (int i = 0; i < markers.Length; i++) {
				int index = text.IndexOf(markers[i], StringComparison.Ordinal);
				if (index >= 0) {
					offset = index + markers[i].Length;
					marker = markers[i];
					markerIndex = i;
					return true;
				}
			}

			int usingIndex = text.IndexOf("using ", StringComparison.Ordinal);
			if (usingIndex >= 0) {
				offset = usingIndex;
				marker = "using";
				markerIndex = markers.Length;
				return true;
			}

			offset = text.Length > 0 ? 0 : -1;
			marker = "(start)";
			markerIndex = markers.Length + 1;
			return offset >= 0;
		}

		static string[] GetLibreWpfCompletionMarkers(string mode)
		{
			string[] defaultMarkers = {
				"LineCounterBrowser.",
				"m_fileIconMappings.",
				"Console.",
				"Environment.",
				"Path.",
				"LoggingService.",
				"startup."
			};

			if (string.IsNullOrWhiteSpace(mode))
				return defaultMarkers;

			List<string> markers = new List<string>();
			foreach (string token in mode.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)) {
				string normalized = token.Trim().TrimEnd('.');
				if (normalized.Length == 0)
					continue;

				if (string.Equals(normalized, "Source", StringComparison.OrdinalIgnoreCase)) {
					markers.Add("LineCounterBrowser.");
				} else if (string.Equals(normalized, "Field", StringComparison.OrdinalIgnoreCase)) {
					markers.Add("m_fileIconMappings.");
				} else if (string.Equals(normalized, "Framework", StringComparison.OrdinalIgnoreCase)) {
					markers.Add("extensions.");
					markers.Add("Console.");
					markers.Add("Environment.");
					markers.Add("Path.");
				} else if (string.Equals(normalized, "Console", StringComparison.OrdinalIgnoreCase)) {
					markers.Add("Console.");
				} else if (string.Equals(normalized, "Environment", StringComparison.OrdinalIgnoreCase)) {
					markers.Add("Environment.");
				} else if (string.Equals(normalized, "Path", StringComparison.OrdinalIgnoreCase)) {
					markers.Add("Path.");
				} else {
					markers.Add(token.EndsWith(".", StringComparison.Ordinal) ? token : token + ".");
				}
			}

			if (markers.Count == 0)
				return defaultMarkers;

			markers.AddRange(defaultMarkers);
			return markers.Distinct(StringComparer.Ordinal).ToArray();
		}

		void RunLibreWpfFullPopupSmoke(string mode)
		{
			if (IsLibreWpfPopupSmokeMode(mode, "All")) {
				OpenLibreWpfMenuPopupSmoke();
				ScheduleLibreWpfPopupSmokeStep(800, delegate {
					CloseLibreWpfSmokePopups();
					OpenLibreWpfContextMenuPopupSmoke();
				});
				ScheduleLibreWpfPopupSmokeStep(1600, delegate {
					CloseLibreWpfSmokePopups();
					OpenLibreWpfComboBoxPopupSmoke();
				});
				ScheduleLibreWpfPopupSmokeStep(2400, delegate {
					CloseLibreWpfSmokePopups();
					OpenLibreWpfToolBarDropDownPopupSmoke();
				});
				return;
			}

			if (IsLibreWpfPopupSmokeMode(mode, "Menu"))
				OpenLibreWpfMenuPopupSmoke();
			if (IsLibreWpfPopupSmokeMode(mode, "ContextMenu"))
				OpenLibreWpfContextMenuPopupSmoke();
			if (IsLibreWpfPopupSmokeMode(mode, "ComboBox"))
				OpenLibreWpfComboBoxPopupSmoke();
			if (IsLibreWpfPopupSmokeMode(mode, "ToolBarDropDown"))
				OpenLibreWpfToolBarDropDownPopupSmoke();
		}

		static bool IsLibreWpfPopupSmokeMode(string mode, string value)
		{
			return mode.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
				.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
		}

		void ScheduleLibreWpfPopupSmokeStep(int delayMilliseconds, Action action)
		{
			DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMilliseconds) };
			timer.Tick += delegate {
				timer.Stop();
				libreWpfSmokeTimers.Remove(timer);
				try {
					if (Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_TRACE_OPEN") == "1") {
						Console.WriteLine("LibreWPF popup smoke step running after " + delayMilliseconds + " ms");
					}
					action();
				} catch (Exception ex) {
					Console.WriteLine("LibreWPF popup smoke step failed: " + ex);
					SD.StatusBar.SetMessage("LibreWPF popup smoke step failed: " + ex.Message);
				}
			};
			libreWpfSmokeTimers.Add(timer);
			timer.Start();
		}

		void OpenLibreWpfMenuPopupSmoke()
		{
			mainMenu.UpdateLayout();
			MenuItem item = mainMenu.Items.OfType<MenuItem>().FirstOrDefault();
			if (item == null) {
				item = mainMenu.ItemContainerGenerator.ContainerFromIndex(0) as MenuItem;
			}
			if (item != null) {
				item.IsSubmenuOpen = true;
				SD.StatusBar.SetMessage("LibreWPF full workbench menu popup opened");
				Console.WriteLine("LibreWPF full workbench menu popup opened");
			} else {
				SD.StatusBar.SetMessage("LibreWPF full workbench menu popup unavailable");
				Console.WriteLine("LibreWPF full workbench menu popup unavailable");
			}
		}

		void OpenLibreWpfContextMenuPopupSmoke()
		{
			OpenLibreWpfContextMenuPopupSmoke(0);
		}

		void OpenLibreWpfContextMenuPopupSmoke(int attempt)
		{
			EnsureLibreWpfPopupSmokeHost();
			libreWpfPopupSmokeHost.UpdateLayout();
			if (PresentationSource.FromVisual(libreWpfPopupSmokeHost) == null) {
				if (attempt < 40) {
					ScheduleLibreWpfPopupSmokeStep(100, delegate {
						OpenLibreWpfContextMenuPopupSmoke(attempt + 1);
					});
				} else {
					SD.StatusBar.SetMessage("LibreWPF full workbench context menu popup unavailable: target not connected");
					Console.WriteLine("LibreWPF full workbench context menu popup unavailable: target not connected");
				}
				return;
			}

			object owner = SD.ProjectService.CurrentSolution ?? (object)this;
			System.Collections.IList items = MenuService.CreateMenuItems(
				mainContent,
				owner,
				"/SharpDevelop/Pads/ProjectBrowser/ContextMenu/SolutionNode",
				"ContextMenu");
			if (items.Count == 0) {
				items = new object[] {
					new MenuItem { Header = "LibreWPF context popup smoke" },
					new MenuItem { Header = "SharpDevelop context menu" },
					new MenuItem { Header = "PopupRoot validation" }
				};
			}
			libreWpfSmokeContextMenu = new ContextMenu {
				ItemsSource = items,
				Placement = PlacementMode.RelativePoint,
				HorizontalOffset = 32,
				VerticalOffset = 72
			};
			libreWpfSmokeContextMenu.PlacementTarget = libreWpfPopupSmokeHost;
			libreWpfSmokeContextMenu.Opened += delegate {
				libreWpfSmokeContextMenuOpen = true;
				Console.WriteLine("LibreWPF full workbench context menu Opened event");
			};
			libreWpfSmokeContextMenu.Closed += delegate {
				libreWpfSmokeContextMenuOpen = false;
				Console.WriteLine("LibreWPF full workbench context menu Closed event");
			};
			libreWpfPopupSmokeHost.ContextMenu = libreWpfSmokeContextMenu;
			libreWpfSmokeContextMenu.IsOpen = true;
			SD.StatusBar.SetMessage("LibreWPF full workbench context menu popup opened");
			Console.WriteLine("LibreWPF full workbench context menu popup opened items=" + items.Count);
		}

		void OpenLibreWpfComboBoxPopupSmoke()
		{
			EnsureLibreWpfPopupSmokeHost();
			if (libreWpfSmokeComboBox == null) {
				libreWpfSmokeComboBox = new ComboBox {
					Width = 180,
					MinHeight = 24,
					Margin = new Thickness(4, 2, 4, 2)
				};
				libreWpfSmokeComboBox.Items.Add("LibreWPF popup smoke");
				libreWpfSmokeComboBox.Items.Add("SharpDevelop ComboBox");
				libreWpfSmokeComboBox.Items.Add("PopupRoot validation");
				libreWpfSmokeComboBox.SelectedIndex = 0;
				libreWpfPopupSmokeHost.Children.Add(libreWpfSmokeComboBox);
			}
			libreWpfSmokeComboBox.Focus();
			libreWpfSmokeComboBox.IsDropDownOpen = true;
			SD.StatusBar.SetMessage("LibreWPF full workbench ComboBox popup opened");
			Console.WriteLine("LibreWPF full workbench ComboBox popup opened");
		}

		void OpenLibreWpfToolBarDropDownPopupSmoke()
		{
			EnsureLibreWpfPopupSmokeHost();
			if (libreWpfSmokeToolBarDropDownButton == null) {
				ToolBar toolBar = new ToolBar {
					Margin = new Thickness(4, 2, 4, 2)
				};
				libreWpfSmokeToolBarDropDownMenu = new ContextMenu {
					Placement = PlacementMode.Bottom
				};
				libreWpfSmokeToolBarDropDownMenu.Items.Add(new MenuItem { Header = "LibreWPF toolbar dropdown smoke" });
				libreWpfSmokeToolBarDropDownMenu.Items.Add(new MenuItem { Header = "SharpDevelop DropDownButton" });
				libreWpfSmokeToolBarDropDownMenu.Items.Add(new MenuItem { Header = "PopupRoot validation" });
				libreWpfSmokeToolBarDropDownMenu.Opened += delegate {
					libreWpfSmokeToolBarDropDownOpen = true;
					Console.WriteLine("LibreWPF full workbench toolbar dropdown Opened event");
				};
				libreWpfSmokeToolBarDropDownMenu.Closed += delegate {
					libreWpfSmokeToolBarDropDownOpen = false;
					Console.WriteLine("LibreWPF full workbench toolbar dropdown Closed event");
				};
				libreWpfSmokeToolBarDropDownButton = new DropDownButton {
					Content = "LibreWPF toolbar",
					MinWidth = 160,
					Margin = new Thickness(2)
				};
				libreWpfSmokeToolBarDropDownButton.DropDownMenu = libreWpfSmokeToolBarDropDownMenu;
				toolBar.Items.Add(libreWpfSmokeToolBarDropDownButton);
				libreWpfPopupSmokeHost.Children.Add(toolBar);
			}
			libreWpfSmokeToolBarDropDownMenu.PlacementTarget = libreWpfSmokeToolBarDropDownButton;
			libreWpfSmokeToolBarDropDownButton.Focus();
			libreWpfSmokeToolBarDropDownMenu.IsOpen = true;
			SD.StatusBar.SetMessage("LibreWPF full workbench toolbar dropdown popup opened");
			Console.WriteLine("LibreWPF full workbench toolbar dropdown popup opened items=" + libreWpfSmokeToolBarDropDownMenu.Items.Count);
		}

		void EnsureLibreWpfPopupSmokeHost()
		{
			if (libreWpfPopupSmokeHost != null)
				return;

			libreWpfPopupSmokeHost = new StackPanel {
				Orientation = Orientation.Horizontal,
				Background = Brushes.Transparent
			};
			DockPanel.SetDock(libreWpfPopupSmokeHost, Dock.Top);
			dockPanel.Children.Insert(Math.Min(dockPanel.Children.Count - 1, 2), libreWpfPopupSmokeHost);
		}

		void CloseLibreWpfSmokePopups()
		{
			foreach (MenuItem item in mainMenu.Items.OfType<MenuItem>()) {
				item.IsSubmenuOpen = false;
			}
			if (libreWpfSmokeContextMenu != null)
				libreWpfSmokeContextMenu.IsOpen = false;
			if (libreWpfSmokeToolBarDropDownMenu != null)
				libreWpfSmokeToolBarDropDownMenu.IsOpen = false;
			if (libreWpfSmokeComboBox != null)
				libreWpfSmokeComboBox.IsDropDownOpen = false;
		}

		async Task CloseLibreWpfSmokePopupsAsync()
		{
			CloseLibreWpfSmokePopups();
			for (int attempt = 0; attempt < 10; attempt++) {
				if ((libreWpfSmokeContextMenu == null || !libreWpfSmokeContextMenu.IsOpen)
				    && (libreWpfSmokeToolBarDropDownMenu == null || !libreWpfSmokeToolBarDropDownMenu.IsOpen)
				    && !libreWpfSmokeContextMenuOpen
				    && !libreWpfSmokeToolBarDropDownOpen
				    && (libreWpfSmokeComboBox == null || !libreWpfSmokeComboBox.IsDropDownOpen)
				    && mainMenu.Items.OfType<MenuItem>().All(item => !item.IsSubmenuOpen)) {
					return;
				}
				await Task.Delay(100);
			}
		}
	#endif

		void OnLanguageChanged(object sender, EventArgs e)
		{
			MenuService.UpdateText(mainMenu.ItemsSource);
			UpdateFlowDirection();
		}

		void UpdateFlowDirection()
		{
			UILanguage language = UILanguageService.GetLanguage(ResourceService.Language);
			Core.WinForms.RightToLeftConverter.IsRightToLeft = language.IsRightToLeft;
			this.FlowDirection = language.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
			App.Current.Resources[GlobalStyles.FlowDirectionKey] = this.FlowDirection;
		}

		public ICollection<IViewContent> ViewContentCollection {
			get {
				SD.MainThread.VerifyAccess();
				return WorkbenchWindowCollection.SelectMany(w => w.ViewContents).ToList().AsReadOnly();
			}
		}

		public ICollection<IViewContent> PrimaryViewContents {
			get {
				SD.MainThread.VerifyAccess();
				return (from window in WorkbenchWindowCollection
					where window.ViewContents.Count > 0
					select window.ViewContents[0]
				).ToList().AsReadOnly();
			}
		}

		public IList<IWorkbenchWindow> WorkbenchWindowCollection {
			get {
				SD.MainThread.VerifyAccess();
				if (workbenchLayout != null)
					return workbenchLayout.WorkbenchWindows;
				else
					return new IWorkbenchWindow[0];
			}
		}

		public IList<PadDescriptor> PadContentCollection {
			get {
				SD.MainThread.VerifyAccess();
				return padDescriptorCollection.AsReadOnly();
			}
		}

		IWorkbenchWindow activeWorkbenchWindow;

		public IWorkbenchWindow ActiveWorkbenchWindow {
			get {
				SD.MainThread.VerifyAccess();
				return activeWorkbenchWindow;
			}
			private set {
				if (activeWorkbenchWindow != value) {
					if (activeWorkbenchWindow != null) {
						activeWorkbenchWindow.ActiveViewContentChanged -= WorkbenchWindowActiveViewContentChanged;
					}

					activeWorkbenchWindow = value;

					if (value != null) {
						value.ActiveViewContentChanged += WorkbenchWindowActiveViewContentChanged;
					}

					if (ActiveWorkbenchWindowChanged != null) {
						ActiveWorkbenchWindowChanged(this, EventArgs.Empty);
					}
					WorkbenchWindowActiveViewContentChanged(null, null);
				}
			}
		}

		void WorkbenchWindowActiveViewContentChanged(object sender, EventArgs e)
		{
			if (workbenchLayout != null) {
				// update ActiveViewContent
				IWorkbenchWindow window = this.ActiveWorkbenchWindow;
				if (window != null)
					this.ActiveViewContent = window.ActiveViewContent;
				else
					this.ActiveViewContent = null;

				// update ActiveContent
				this.ActiveContent = workbenchLayout.ActiveContent;
			}
		}

		bool activeWindowWasChanged;

		void OnActiveWindowChanged(object sender, EventArgs e)
		{
			if (activeWindowWasChanged)
				return;
			activeWindowWasChanged = true;
			Dispatcher.BeginInvoke(new Action(
				delegate {
					activeWindowWasChanged = false;
					if (workbenchLayout != null) {
						this.ActiveContent = workbenchLayout.ActiveContent;
						this.ActiveWorkbenchWindow = workbenchLayout.ActiveWorkbenchWindow;
					} else {
						this.ActiveContent = null;
						this.ActiveWorkbenchWindow = null;
					}
				}));
		}

		IViewContent activeViewContent;

		public IViewContent ActiveViewContent {
			get {
				SD.MainThread.VerifyAccess();
				return activeViewContent;
			}
			private set {
				if (activeViewContent != value) {
					activeViewContent = value;

					if (ActiveViewContentChanged != null) {
						ActiveViewContentChanged(this, EventArgs.Empty);
					}
				}
			}
		}

		IServiceProvider activeContent;

		public IServiceProvider ActiveContent {
			get {
				SD.MainThread.VerifyAccess();
				return activeContent;
			}
			private set {
				if (activeContent != value) {
					activeContent = value;

					if (ActiveContentChanged != null) {
						ActiveContentChanged(this, EventArgs.Empty);
					}
				}
			}
		}

		IWorkbenchLayout workbenchLayout;

		public IWorkbenchLayout WorkbenchLayout {
			get {
				return workbenchLayout;
			}
			set {
				SD.MainThread.VerifyAccess();

				if (workbenchLayout != null) {
					workbenchLayout.ActiveContentChanged -= OnActiveWindowChanged;
					workbenchLayout.Detach();
				}
				if (value != null) {
					value.Attach(this);
					value.ActiveContentChanged += OnActiveWindowChanged;
				}
				workbenchLayout = value;
				OnActiveWindowChanged(null, null);
			}
		}

		public bool IsActiveWindow {
			get {
				return IsActive;
			}
		}

		public void ShowView(IViewContent content)
		{
			ShowView(content, true);
		}

		public void ShowView(IViewContent content, bool switchToOpenedView)
		{
			SD.MainThread.VerifyAccess();
			if (content == null)
				throw new ArgumentNullException("content");
			if (ViewContentCollection.Contains(content))
				throw new ArgumentException("ViewContent was already shown");
			System.Diagnostics.Debug.Assert(WorkbenchLayout != null);

			LoadViewContentMemento(content);

			WorkbenchLayout.ShowView(content, switchToOpenedView);
#if LIBREWPF
			if (Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_TRACE_OPEN") == "1") {
				Console.WriteLine("LibreWPF WpfWorkbench.ShowView content=" + content.GetType().FullName + " windows=" + WorkbenchWindowCollection.Count + " active=" + ActiveWorkbenchWindow);
			}
#endif
		}

		public void ShowPad(PadDescriptor content)
		{
			SD.MainThread.VerifyAccess();
			if (content == null)
				throw new ArgumentNullException("content");
			if (padDescriptorCollection.Contains(content))
				throw new ArgumentException("Pad is already loaded");

			padDescriptorCollection.Add(content);

			if (WorkbenchLayout != null) {
				WorkbenchLayout.ShowPad(content);
			}
		}

		public PadDescriptor GetPad(Type type)
		{
			SD.MainThread.VerifyAccess();
			if (type == null)
				throw new ArgumentNullException("type");
			foreach (PadDescriptor pad in PadContentCollection) {
				if (pad.Class == type.FullName) {
					return pad;
				}
			}
			return null;
		}

		public void CloseAllViews()
		{
			SD.MainThread.VerifyAccess();
			foreach (IWorkbenchWindow window in this.WorkbenchWindowCollection.ToArray()) {
				window.CloseWindow(false);
			}
		}

		public bool CloseAllSolutionViews(bool force)
		{
			bool result = true;
			foreach (IWorkbenchWindow window in this.WorkbenchWindowCollection.ToArray()) {
				if (window.ActiveViewContent != null && window.ActiveViewContent.CloseWithSolution)
					result &= window.CloseWindow(force);
			}
			return result;
		}

		#region ViewContent Memento Handling
		FileName viewContentMementosFileName;

		FileName ViewContentMementosFileName {
			get {
				if (viewContentMementosFileName == null) {
					viewContentMementosFileName = SD.PropertyService.ConfigDirectory.CombineFile("LastViewStates.xml");
				}
				return viewContentMementosFileName;
			}
		}

		Properties LoadOrCreateViewContentMementos()
		{
			try {
				return Properties.Load(this.ViewContentMementosFileName) ?? new Properties();
			} catch (Exception ex) {
				LoggingService.Warn("Error while loading the view content memento file. Discarding any saved view states.", ex);
				return new Properties();
			}
		}

		static string GetMementoKeyName(IViewContent viewContent)
		{
			return String.Concat(viewContent.GetType().FullName.GetHashCode().ToString("x", CultureInfo.InvariantCulture), ":", FileUtility.NormalizePath(viewContent.PrimaryFileName).ToUpperInvariant());
		}

		public static bool LoadDocumentProperties {
			get { return SD.PropertyService.Get("SharpDevelop.LoadDocumentProperties", true); }
			set { SD.PropertyService.Set("SharpDevelop.LoadDocumentProperties", value); }
		}

		/// <summary>
		/// Stores the memento for the view content.
		/// Such mementos are automatically loaded in ShowView().
		/// </summary>
		public void StoreMemento(IViewContent viewContent)
		{
			IMementoCapable mementoCapable = viewContent.GetService<IMementoCapable>();
			if (mementoCapable != null && LoadDocumentProperties) {
				if (viewContent.PrimaryFileName == null)
					return;

				string key = GetMementoKeyName(viewContent);
				LoggingService.Debug("Saving memento of '" + viewContent.ToString() + "' to key '" + key + "'");

				Properties memento = mementoCapable.CreateMemento();
				Properties p = this.LoadOrCreateViewContentMementos();
				p.SetNestedProperties(key, memento);
				FileUtility.ObservedSave(new NamedFileOperationDelegate(p.Save), this.ViewContentMementosFileName, FileErrorPolicy.Inform);
			}
		}

		void LoadViewContentMemento(IViewContent viewContent)
		{
			IMementoCapable mementoCapable = viewContent.GetService<IMementoCapable>();
			if (mementoCapable != null && LoadDocumentProperties) {
				if (viewContent.PrimaryFileName == null)
					return;

				try {
					string key = GetMementoKeyName(viewContent);
					LoggingService.Debug("Trying to restore memento of '" + viewContent.ToString() + "' from key '" + key + "'");

					mementoCapable.SetMemento(this.LoadOrCreateViewContentMementos().NestedProperties(key));
				} catch (Exception e) {
					MessageService.ShowException(e, "Can't get/set memento");
				}
			}
		}
		#endregion

		System.Windows.WindowState lastNonMinimizedWindowState = System.Windows.WindowState.Normal;
		Rect restoreBoundsBeforeClosing;

		protected override void OnStateChanged(EventArgs e)
		{
			base.OnStateChanged(e);
			if (this.WindowState != System.Windows.WindowState.Minimized)
				lastNonMinimizedWindowState = this.WindowState;
		}

		public Properties CreateMemento()
		{
			Properties prop = new Properties();
			prop.Set("WindowState", lastNonMinimizedWindowState);
			var bounds = this.RestoreBounds;
			if (bounds.IsEmpty) bounds = restoreBoundsBeforeClosing;
			if (!bounds.IsEmpty) {
				prop.Set("Bounds", bounds);
			}
			return prop;
		}

		public void SetMemento(Properties memento)
		{
			Rect bounds = memento.Get("Bounds", new Rect(10, 10, 750, 550));
			// bounds are validated after PresentationSource is initialized (see OnSourceInitialized)
			lastNonMinimizedWindowState = memento.Get("WindowState", System.Windows.WindowState.Maximized);
			SetBounds(bounds);
		}

		protected override void OnClosing(CancelEventArgs e)
		{
#if LIBREWPF
			if (Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_TRACE_OPEN") == "1") {
				Console.WriteLine("LibreWPF WpfWorkbench.OnClosing entered cancel=" + e.Cancel);
			}
#endif
			base.OnClosing(e);
			if (!e.Cancel) {
				// see IShutdownService.Shutdown() for a description of the shutdown procedure

				var shutdownService = (ShutdownService)SD.ShutdownService;
				if (shutdownService.CurrentReasonPreventingShutdown != null) {
					MessageService.ShowMessage(shutdownService.CurrentReasonPreventingShutdown);
					e.Cancel = true;
					return;
				}

				if (!SD.ProjectService.CloseSolution()) {
					e.Cancel = true;
					return;
				}

				((ParserService)SD.ParserService).StopParserThread();
				((WpfWorkbench)SD.Workbench).WorkbenchLayout.StoreConfiguration();
				restoreBoundsBeforeClosing = this.RestoreBounds;

				this.WorkbenchLayout = null;

				shutdownService.SignalShutdownToken();
				foreach (PadDescriptor padDescriptor in this.PadContentCollection) {
					padDescriptor.Dispose();
				}
			}
		}

		protected override void OnDragEnter(DragEventArgs e)
		{
			try {
				base.OnDragEnter(e);
				if (!e.Handled) {
					e.Effects = GetEffect(e.Data);
					e.Handled = true;
				}
			} catch (Exception ex) {
				MessageService.ShowException(ex);
			}
		}

		protected override void OnDragOver(DragEventArgs e)
		{
			try {
				base.OnDragOver(e);
				if (!e.Handled) {
					e.Effects = GetEffect(e.Data);
					e.Handled = true;
				}
			} catch (Exception ex) {
				MessageService.ShowException(ex);
			}
		}

		DragDropEffects GetEffect(IDataObject data)
		{
			try {
				if (data != null && data.GetDataPresent(DataFormats.FileDrop)) {
					string[] files = (string[])data.GetData(DataFormats.FileDrop);
					if (files != null) {
						foreach (string file in files) {
							if (File.Exists(file)) {
								return DragDropEffects.Link;
							}
						}
					}
				}
			} catch (COMException) {
				// Ignore errors getting the data (e.g. happens when dragging attachments out of Thunderbird)
			}
			return DragDropEffects.None;
		}

		protected override void OnDrop(DragEventArgs e)
		{
			try {
				base.OnDrop(e);
				if (!e.Handled && e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) {
					e.Handled = true;
					string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
					if (files == null)
						return;
					// Handle opening the files outside the drop event, so that the drag source doesn't think
					// the operation is still in progress while we're showing a "file cannot be opened" error message.
					Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action<string[]>(HandleDrop), files);
				}
			} catch (Exception ex) {
				MessageService.ShowException(ex);
			}
		}

		void HandleDrop(string[] files)
		{
			foreach (string file in files) {
				if (File.Exists(file)) {
					var fileName = FileName.Create(file);
					if (SD.ProjectService.IsSolutionOrProjectFile(fileName)) {
						SD.ProjectService.OpenSolutionOrProject(fileName);
					} else {
						SD.FileService.OpenFile(fileName);
					}
				}
			}
		}

		void InitFocusTrackingEvents()
		{
			#if DEBUG
			this.PreviewLostKeyboardFocus += new KeyboardFocusChangedEventHandler(WpfWorkbench_PreviewLostKeyboardFocus);
			this.PreviewGotKeyboardFocus += new KeyboardFocusChangedEventHandler(WpfWorkbench_PreviewGotKeyboardFocus);
			#endif
		}

		[Conditional("DEBUG")]
		internal static void FocusDebug(string format, params object[] args)
		{
			#if DEBUG
			if (enableFocusDebugOutput)
			LoggingService.DebugFormatted(format, args);
			#endif
		}

		#if DEBUG
		static bool enableFocusDebugOutput;

		void WpfWorkbench_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
		{
		FocusDebug("GotKeyboardFocus: oldFocus={0}, newFocus={1}", e.OldFocus, e.NewFocus);
		}

		void WpfWorkbench_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
		{
		FocusDebug("LostKeyboardFocus: oldFocus={0}, newFocus={1}", e.OldFocus, e.NewFocus);
		}

		protected override void OnPreviewKeyDown(KeyEventArgs e)
		{
		base.OnPreviewKeyDown(e);
		if (!e.Handled && e.Key == Key.D && e.KeyboardDevice.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) {
		enableFocusDebugOutput = !enableFocusDebugOutput;

		StringWriter output = new StringWriter();
		output.WriteLine("Keyboard.FocusedElement = " + GetElementName(Keyboard.FocusedElement));
		output.WriteLine("ActiveContent = " + GetElementName(this.ActiveContent));
		output.WriteLine("ActiveViewContent = " + GetElementName(this.ActiveViewContent));
		output.WriteLine("ActiveWorkbenchWindow = " + GetElementName(this.ActiveWorkbenchWindow));
		((AvalonDockLayout)workbenchLayout).WriteState(output);
		LoggingService.Debug(output.ToString());
		e.Handled = true;
		}
		if (!e.Handled && e.Key == Key.F && e.KeyboardDevice.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) {
		if (TextOptions.GetTextFormattingMode(this) == TextFormattingMode.Display)
		TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
		else
		TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
		SD.StatusBar.SetMessage("TextFormattingMode=" + TextOptions.GetTextFormattingMode(this));
		}
		if (!e.Handled && e.Key == Key.R && e.KeyboardDevice.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) {
		switch (TextOptions.GetTextRenderingMode(this)) {
		case TextRenderingMode.Auto:
		case TextRenderingMode.ClearType:
		TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
		break;
		case TextRenderingMode.Grayscale:
		TextOptions.SetTextRenderingMode(this, TextRenderingMode.Aliased);
		break;
		default:
		TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
		break;
		}
		SD.StatusBar.SetMessage("TextRenderingMode=" + TextOptions.GetTextRenderingMode(this));
		}
		if (!e.Handled && e.Key == Key.G && e.KeyboardDevice.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) {
		GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
		SD.StatusBar.SetMessage("Total memory = " + (GC.GetTotalMemory(true) / 1024 / 1024f).ToString("f1") + " MB");
		}
		}
		#endif

		internal static string GetElementName(object element)
		{
			if (element == null)
				return "<null>";
			else
				return element.GetType().FullName + ": " + element.ToString();
		}

		public string CurrentLayoutConfiguration {
			get {
				return LayoutConfiguration.CurrentLayoutName;
			}
			set {
				LayoutConfiguration.CurrentLayoutName = value;
			}
		}

		public void ActivatePad(PadDescriptor content)
		{
			if (workbenchLayout != null)
				workbenchLayout.ActivatePad(content);
		}
	}
}
