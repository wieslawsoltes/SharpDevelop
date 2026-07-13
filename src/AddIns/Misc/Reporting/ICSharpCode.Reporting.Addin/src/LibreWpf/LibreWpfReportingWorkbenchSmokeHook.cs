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

#if LIBREWPF
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Media;

using ICSharpCode.Core;
using ICSharpCode.Reporting.Addin.DesignableItems;
using ICSharpCode.Reporting.Addin.Designer;
using ICSharpCode.Reporting.Addin.DesignerBinding;
using ICSharpCode.Reporting.Addin.Views;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Workbench;

using FormsControl = System.Windows.Forms.Control;
using FormsPanel = System.Windows.Forms.Panel;

namespace ICSharpCode.Reporting.Addin.LibreWpf
{
	public sealed class LibreWpfReportingWorkbenchSmokeHook : ILibreWpfSmokeHook
	{
		const string ExpectedInitialName = "DependencyReport";
		const string ExpectedReloadName = "DependencyReport-LibreWPF-Reloaded";
		const int ExpectedSectionCount = 5;
		const int ExpectedItemCount = 12;

		public string EnvironmentVariableName {
			get { return "LIBREWPF_SHARPDEVELOP_REPORTING_SMOKE"; }
		}

		public async Task RunAsync(string mode)
		{
			string stage = "ResolveFixture";
			string temporaryDirectory = null;
			DesignerView view = null;

			try
			{
				string sourcePath = ResolveFixturePath(mode);
				byte[] originalBytes = File.ReadAllBytes(sourcePath);
				byte[] editedBytes = CreateReloadBytes(originalBytes);
				temporaryDirectory = Path.Combine(Path.GetTempPath(), "sharpdevelop-librewpf-reporting-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(temporaryDirectory);
				string workingPath = Path.Combine(temporaryDirectory, Path.GetFileName(sourcePath));
				File.WriteAllBytes(workingPath, originalBytes);

				stage = "ResolveBinding";
				FileName fileName = FileName.Create(workingPath);
				DisplayBindingDescriptor descriptor = FindReportingBinding(fileName);
				ReportDesignerBinding binding = descriptor == null ? null : descriptor.Binding as ReportDesignerBinding;
				if (binding == null) {
					throw new InvalidOperationException("The SharpDevelopReportsBinding display binding is not registered.");
				}

				stage = "OpenReport";
				view = SD.FileService.OpenFile(fileName, true) as DesignerView;
				if (view == null) {
					throw new InvalidOperationException("The Reporting display binding did not create DesignerView.");
				}
				OpenedFile file = view.PrimaryFile;
				IWorkbenchWindow originalWindow = view.WorkbenchWindow;
				if (originalWindow == null) {
					throw new InvalidOperationException("DesignerView was not attached to a workbench window.");
				}
				originalWindow.ActiveViewContent = view;
				file.ForceInitializeView(view);
				if (!ReferenceEquals(file.CurrentView, view)) {
					throw new InvalidOperationException("DesignerView is not the active Reporting file view.");
				}

				stage = "LoadInitialSurface";
				ReportState initial = await WaitForReportAsync(view, ExpectedInitialName);
				FormsPanel panel = view.Control as FormsPanel;
				if (panel == null) {
					throw new InvalidOperationException("DesignerView did not expose its WinForms panel.");
				}

				stage = "HostPanel";
				WindowsFormsHost host = await WaitForHostAsync(panel);
				bool hosted = host != null && ReferenceEquals(host.Child, panel);
				bool presentation = host != null && PresentationSource.FromVisual(host) != null;
				if (!hosted || !presentation) {
					throw new InvalidOperationException("The Reporting panel did not reach a presented WindowsFormsHost.");
				}

				stage = "SaveInitial";
				originalWindow.ActiveViewContent = view;
				file.ForceInitializeView(view);
				if (!ReferenceEquals(file.CurrentView, view)) {
					throw new InvalidOperationException("DesignerView is not the active Reporting file view.");
				}
				file.SaveToDisk();
				bool cleanSaveExact = ByteArraysEqual(File.ReadAllBytes(workingPath), originalBytes);
				if (!cleanSaveExact || file.IsDirty) {
					throw new InvalidOperationException("A clean Reporting save did not preserve the original bytes and clean state.");
				}

				stage = "PreviewInitialReport";
				bool initialPreview = await ExercisePreviewAsync(originalWindow, view);
				WindowsFormsHost hostAfterInitialPreview = await WaitForHostAsync(panel);
				if (!initialPreview || !ReferenceEquals(hostAfterInitialPreview, host)) {
					throw new InvalidOperationException("The initial Reporting preview did not return to the same hosted designer view.");
				}

				stage = "ReloadEditedReport";
				originalWindow.ActiveViewContent = view;
				file.ForceInitializeView(view);
				if (!ReferenceEquals(file.CurrentView, view)) {
					throw new InvalidOperationException("DesignerView stopped being the active Reporting file view before reload.");
				}
				FileChangeWatcher.DisableAllChangeWatchers();
				try
				{
					File.WriteAllBytes(workingPath, editedBytes);
					file.ReloadFromDisk();
				}
				finally
				{
					FileChangeWatcher.EnableAllChangeWatchers();
				}

				ReportState reloaded = await WaitForReportAsync(view, ExpectedReloadName);
				bool reloadSameView = ReferenceEquals(view, SD.FileService.GetOpenFile(fileName))
					&& ReferenceEquals(originalWindow, view.WorkbenchWindow);
				bool reloadSamePanel = ReferenceEquals(panel, view.Control)
					&& ReferenceEquals(host.Child, panel)
					&& PresentationSource.FromVisual(host) != null;
				if (!reloadSameView || !reloadSamePanel || ReferenceEquals(initial.Settings, reloaded.Settings) || file.IsDirty) {
					throw new InvalidOperationException("Reporting reload did not retain the clean workbench view and window.");
				}

				stage = "PreviewReloadedReport";
				bool reloadedPreview = await ExercisePreviewAsync(originalWindow, view);
				WindowsFormsHost hostAfterReloadPreview = await WaitForHostAsync(panel);
				if (!reloadedPreview || !ReferenceEquals(hostAfterReloadPreview, host)) {
					throw new InvalidOperationException("The reloaded Reporting preview did not return to the same hosted designer view.");
				}

				stage = "SaveReloadedReport";
				file.SaveToDisk();
				bool reloadCleanSaveExact = ByteArraysEqual(File.ReadAllBytes(workingPath), editedBytes);
				bool dirtyCleared = !file.IsDirty;
				if (!reloadCleanSaveExact || !dirtyCleared) {
					throw new InvalidOperationException("A clean save after reload did not preserve the edited bytes and clean state.");
				}

				stage = "CloseReport";
				bool closed = originalWindow != null && originalWindow.CloseWindow(true);
				for (int attempt = 0; attempt < 50 && !view.IsDisposed; attempt++) {
					await Task.Delay(50);
				}
				closed = closed && view.IsDisposed;
				if (!closed) {
					throw new InvalidOperationException("The Reporting workbench window did not close and dispose its view.");
				}

				stage = "Cleanup";
				Directory.Delete(temporaryDirectory, true);
				temporaryDirectory = null;

				WriteResult(
					"Success",
					"binding=SharpDevelopReportsBinding"
					+ " view=DesignerView"
					+ " hosted=" + hosted
					+ " presentation=" + presentation
					+ " initialName=" + initial.ReportName
					+ " sections=" + initial.SectionCount
					+ " items=" + initial.ItemCount
					+ " cleanSaveExact=" + cleanSaveExact
					+ " initialPreview=" + initialPreview
					+ " reloadSameView=" + reloadSameView
					+ " reloadName=" + reloaded.ReportName
					+ " reloadSections=" + reloaded.SectionCount
					+ " reloadItems=" + reloaded.ItemCount
					+ " reloadedPreview=" + reloadedPreview
					+ " reloadCleanSaveExact=" + reloadCleanSaveExact
					+ " dirtyCleared=" + dirtyCleared
					+ " closed=" + closed
					+ " cleanup=True");
			}
			catch (Exception ex)
			{
				WriteResult("Failed", "stage=" + stage + " error=" + SingleLine(ex.Message));
				throw;
			}
			finally
			{
				if (view != null && !view.IsDisposed && view.WorkbenchWindow != null) {
					view.WorkbenchWindow.CloseWindow(true);
				}
				if (temporaryDirectory != null) {
					try
					{
						Directory.Delete(temporaryDirectory, true);
					}
					catch (IOException)
					{
					}
					catch (UnauthorizedAccessException)
					{
					}
				}
			}
		}

		static string ResolveFixturePath(string mode)
		{
			if (string.IsNullOrWhiteSpace(mode)
			    || string.Equals(mode, "1", StringComparison.OrdinalIgnoreCase)
			    || string.Equals(mode, "default", StringComparison.OrdinalIgnoreCase))
			{
				throw new ArgumentException("Set LIBREWPF_SHARPDEVELOP_REPORTING_SMOKE to an existing .srd fixture path.");
			}

			string path = Path.GetFullPath(mode);
			if (!File.Exists(path)) {
				throw new FileNotFoundException("The requested Reporting smoke fixture does not exist.", path);
			}
			return path;
		}

		static byte[] CreateReloadBytes(byte[] originalBytes)
		{
			string original = Encoding.UTF8.GetString(originalBytes);
			string marker = "<ReportName>" + ExpectedInitialName + "</ReportName>";
			string replacement = "<ReportName>" + ExpectedReloadName + "</ReportName>";
			int markerIndex = original.IndexOf(marker, StringComparison.Ordinal);
			if (markerIndex < 0 || original.IndexOf(marker, markerIndex + marker.Length, StringComparison.Ordinal) >= 0) {
				throw new InvalidDataException("The Reporting fixture must contain exactly one expected ReportName element.");
			}
			return Encoding.UTF8.GetBytes(original.Substring(0, markerIndex) + replacement + original.Substring(markerIndex + marker.Length));
		}

		static DisplayBindingDescriptor FindReportingBinding(FileName fileName)
		{
			return SD.DisplayBindingService.GetCodonsPerFileName(fileName)
				.FirstOrDefault(descriptor => string.Equals(descriptor.Id, "SharpDevelopReportsBinding", StringComparison.Ordinal));
		}

		static async Task<ReportState> WaitForReportAsync(DesignerView view, string expectedName)
		{
			string lastReportName = "<none>";
			int lastSectionCount = -1;
			int lastItemCount = -1;
			int lastContainerSectionCount = -1;
			int lastRootParentCount = -1;
			bool lastPending = true;
			for (int attempt = 0; attempt < 100; attempt++) {
				var host = view.Host;
				var root = host == null ? null : host.RootComponent as RootReportModel;
				var settings = host == null ? null : host.Container.Components.OfType<ReportSettings>().FirstOrDefault();
				lastPending = view.IsDesignerLoadPending;
				if (host != null) {
					BaseSection[] containerSections = host.Container.Components.OfType<BaseSection>().ToArray();
					lastContainerSectionCount = containerSections.Length;
					lastRootParentCount = root == null ? -1 : containerSections.Count(section => ReferenceEquals(section.Parent, root));
				}
				if (root != null && settings != null) {
					BaseSection[] sections = root.Controls.OfType<BaseSection>().ToArray();
					int itemCount = sections.Sum(section => CountNestedItems(section.Controls));
					lastReportName = settings.ReportName;
					lastSectionCount = sections.Length;
					lastItemCount = itemCount;
					if (!lastPending
					    && string.Equals(settings.ReportName, expectedName, StringComparison.Ordinal)
					    && sections.Length == ExpectedSectionCount
					    && itemCount == ExpectedItemCount)
					{
						return new ReportState(settings, sections.Length, itemCount);
					}
				}
				await Task.Delay(100);
			}

			throw new InvalidOperationException(
				"The Reporting design surface did not reach the expected report state: " + expectedName
				+ "; lastName=" + lastReportName
				+ ", sections=" + lastSectionCount
				+ ", items=" + lastItemCount
				+ ", containerSections=" + lastContainerSectionCount
				+ ", rootParents=" + lastRootParentCount
				+ ", pending=" + lastPending + ".");
		}

		static int CountNestedItems(FormsControl.ControlCollection controls)
		{
			int count = 0;
			foreach (FormsControl control in controls) {
				count++;
				count += CountNestedItems(control.Controls);
			}
			return count;
		}

		static async Task<WindowsFormsHost> WaitForHostAsync(FormsPanel panel)
		{
			for (int attempt = 0; attempt < 100; attempt++) {
				WindowsFormsHost host = FindHost(SD.Workbench.MainWindow as DependencyObject, panel);
				if (host != null && PresentationSource.FromVisual(host) != null) {
					return host;
				}
				await Task.Delay(50);
			}
			return null;
		}

		static async Task<bool> ExercisePreviewAsync(IWorkbenchWindow window, DesignerView view)
		{
			WpfPreview preview = view.SecondaryViewContents.OfType<WpfPreview>().SingleOrDefault();
			if (preview == null) {
				return false;
			}

			window.ActiveViewContent = preview;
			Visual previewVisual = preview.Control as Visual;
			bool presented = false;
			for (int attempt = 0; attempt < 100; attempt++) {
				presented = ReferenceEquals(window.ActiveViewContent, preview)
					&& previewVisual != null
					&& PresentationSource.FromVisual(previewVisual) != null;
				if (presented) {
					break;
				}
				await Task.Delay(50);
			}

			window.ActiveViewContent = view;
			view.PrimaryFile.ForceInitializeView(view);
			return presented && ReferenceEquals(view.PrimaryFile.CurrentView, view);
		}

		static WindowsFormsHost FindHost(DependencyObject current, FormsPanel panel)
		{
			if (current == null) {
				return null;
			}

			var host = current as WindowsFormsHost;
			if (host != null && ReferenceEquals(host.Child, panel)) {
				return host;
			}

			int childCount = VisualTreeHelper.GetChildrenCount(current);
			for (int i = 0; i < childCount; i++) {
				WindowsFormsHost child = FindHost(VisualTreeHelper.GetChild(current, i), panel);
				if (child != null) {
					return child;
				}
			}
			return null;
		}

		static bool ByteArraysEqual(byte[] left, byte[] right)
		{
			return left.Length == right.Length && left.SequenceEqual(right);
		}

		static string SingleLine(string value)
		{
			return (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
		}

		static void WriteResult(string result, string details)
		{
			string message = "LibreWPF Reporting workbench smoke result=" + result + " " + details;
			Console.WriteLine(message);
			SD.StatusBar.SetMessage(message);
		}

		sealed class ReportState
		{
			public ReportState(ReportSettings settings, int sectionCount, int itemCount)
			{
				Settings = settings;
				SectionCount = sectionCount;
				ItemCount = itemCount;
			}

			public ReportSettings Settings { get; private set; }
			public string ReportName { get { return Settings.ReportName; } }
			public int SectionCount { get; private set; }
			public int ItemCount { get; private set; }
		}
	}
}
#endif
