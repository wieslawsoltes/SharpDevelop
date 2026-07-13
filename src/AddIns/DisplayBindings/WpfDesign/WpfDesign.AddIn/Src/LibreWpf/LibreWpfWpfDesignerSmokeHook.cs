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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;
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
				designer.DesignContext.Services.Selection.SetSelectedComponents(
					new[] { designer.DesignContext.RootItem });
				await Task.Delay(100);

				bool rootSelected = ReferenceEquals(
					designer.DesignContext.Services.Selection.PrimarySelection,
					designer.DesignContext.RootItem);
				bool propertyGridReady = designer.PropertyContainer.PropertyGridReplacementContent is PropertyGridView;
				bool rootViewReady = designer.DesignContext.RootItem.View is FrameworkElement;
				bool presented = PresentationSource.FromVisual(surface) != null;
				if (!rootSelected || !propertyGridReady || !rootViewReady || !presented)
					throw new InvalidOperationException(
						"The WPF designer did not reach its selected, property, and presented state."
						+ " selected=" + rootSelected
						+ " propertyGrid=" + propertyGridReady
						+ " rootView=" + rootViewReady
						+ " presented=" + presented);

				WriteResult(
					"Success",
					"file=" + Path.GetFileName(filePath)
					+ " root=" + designer.DesignContext.RootItem.ComponentType.FullName
					+ " selected=True propertyGrid=True presented=True");
			} finally {
				if (primary != null && primary.WorkbenchWindow != null)
					primary.WorkbenchWindow.CloseWindow(true);
			}
		}

		static async Task WaitForProjectAsync()
		{
			for (int attempt = 0; attempt < 100; attempt++) {
				if (SD.ProjectService.CurrentProject != null
				    && !SD.ParserService.LoadSolutionProjectsThread.IsRunning)
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
