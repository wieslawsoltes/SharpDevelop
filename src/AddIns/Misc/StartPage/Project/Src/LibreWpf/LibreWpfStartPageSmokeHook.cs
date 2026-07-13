// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is furnished
// to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
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
using System.Threading.Tasks;

using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.StartPage.LibreWpf
{
	public sealed class LibreWpfStartPageSmokeHook : ILibreWpfSmokeHook
	{
		public string EnvironmentVariableName {
			get { return "LIBREWPF_SHARPDEVELOP_START_PAGE_SMOKE"; }
		}

		public async Task RunAsync(string mode)
		{
			if (SD.ProjectService.CurrentSolution != null) {
				throw new InvalidOperationException("The StartPage smoke requires an empty workbench.");
			}

			string temporaryDirectory = Path.Combine(
				Path.GetTempPath(),
				"sharpdevelop-librewpf-startpage-" + Guid.NewGuid().ToString("N"));
			string solutionPath = Path.Combine(temporaryDirectory, "StartPageSmoke.sln");
			FileName solutionFileName = FileName.Create(solutionPath);
			StartPageViewContent view = null;
			bool recentProjectAdded = false;
			bool solutionOpened = false;
			bool closedOnOpen = false;
			bool cleanup = false;

			Directory.CreateDirectory(temporaryDirectory);
			File.WriteAllText(
				solutionPath,
				"Microsoft Visual Studio Solution File, Format Version 12.00\r\n"
				+ "# Visual Studio 2013\r\n"
				+ "VisualStudioVersion = 12.0.21005.1\r\n"
				+ "MinimumVisualStudioVersion = 10.0.40219.1\r\n"
				+ "Global\r\n"
				+ "EndGlobal\r\n");

			try {
				SD.FileService.RecentOpen.AddRecentProject(solutionFileName);
				recentProjectAdded = true;

				new ShowStartPageCommand().Run();
				view = await WaitForStartPageAsync();
				if (view == null) {
					throw new InvalidOperationException("The StartPage add-in did not create its view content.");
				}

				StartPageControl control = view.Control as StartPageControl;
				if (control == null || control.RecentProjectsControl == null || control.ItemCount == 0) {
					throw new InvalidOperationException("The StartPage view did not load its recent-project control.");
				}

				await control.RecentProjectsControl.RefreshRecentProjectsAsync();
				if (control.RecentProjectsControl.RecentProjectCount == 0) {
					throw new InvalidOperationException("The recent-project list did not expose the smoke fixture.");
				}

				bool loaded = await WaitForLoadedAsync(control, control.RecentProjectsControl);
				if (!loaded) {
					throw new InvalidOperationException("The StartPage controls were not attached to the workbench visual tree.");
				}

				bool interactionRaised = control.RecentProjectsControl.TryActivateRecentProject(solutionPath);
				if (!interactionRaised) {
					throw new InvalidOperationException("The recent-project double-click interaction was not raised.");
				}

				solutionOpened = await WaitForSolutionAsync(solutionPath);
				if (!solutionOpened) {
					throw new InvalidOperationException("The recent-project interaction did not open the selected solution.");
				}

				closedOnOpen = await WaitForStartPageClosedAsync(view);
				if (!closedOnOpen) {
					throw new InvalidOperationException("The StartPage did not close after opening a solution.");
				}
			} finally {
				if (IsCurrentSolution(solutionPath)) {
					SD.ProjectService.CloseSolution(allowCancel: false);
				}
				if (recentProjectAdded) {
					SD.FileService.RecentOpen.RemoveRecentProject(solutionFileName);
				}
				if (view != null && view.WorkbenchWindow != null) {
					view.WorkbenchWindow.CloseWindow(true);
				}
				try {
					Directory.Delete(temporaryDirectory, true);
					cleanup = !Directory.Exists(temporaryDirectory);
				} catch (IOException) {
				} catch (UnauthorizedAccessException) {
				}
			}

			if (!cleanup) {
				throw new InvalidOperationException("The StartPage smoke fixture was not cleaned up.");
			}

			WriteResult(
				"Success",
				"mode=" + (string.IsNullOrEmpty(mode) ? "Default" : mode)
				+ " addin=True"
				+ " view=StartPageViewContent"
				+ " items=1+"
				+ " loaded=True"
				+ " interaction=True"
				+ " solutionOpened=" + solutionOpened
				+ " closedOnOpen=" + closedOnOpen
				+ " cleanup=True");
		}

		static async Task<StartPageViewContent> WaitForStartPageAsync()
		{
			for (int attempt = 0; attempt < 100; attempt++) {
				foreach (IViewContent content in SD.Workbench.ViewContentCollection) {
					StartPageViewContent startPage = content as StartPageViewContent;
					if (startPage != null) {
						return startPage;
					}
				}
				await Task.Delay(50);
			}
			return null;
		}

		static async Task<bool> WaitForLoadedAsync(
			StartPageControl control,
			RecentProjectsControl recentProjects)
		{
			for (int attempt = 0; attempt < 100; attempt++) {
				if (control.IsLoaded && recentProjects.IsLoaded) {
					return true;
				}
				await Task.Delay(50);
			}
			return false;
		}

		static async Task<bool> WaitForSolutionAsync(string solutionPath)
		{
			for (int attempt = 0; attempt < 100; attempt++) {
				if (IsCurrentSolution(solutionPath)) {
					return true;
				}
				await Task.Delay(50);
			}
			return false;
		}

		static bool IsCurrentSolution(string solutionPath)
		{
			ISolution solution = SD.ProjectService.CurrentSolution;
			return solution != null
				&& string.Equals(
					Path.GetFullPath(solution.FileName),
					Path.GetFullPath(solutionPath),
					StringComparison.Ordinal);
		}

		static async Task<bool> WaitForStartPageClosedAsync(StartPageViewContent expected)
		{
			for (int attempt = 0; attempt < 100; attempt++) {
				bool found = false;
				foreach (IViewContent content in SD.Workbench.ViewContentCollection) {
					if (ReferenceEquals(content, expected)) {
						found = true;
						break;
					}
				}
				if (!found) {
					return true;
				}
				await Task.Delay(50);
			}
			return false;
		}

		static void WriteResult(string result, string details)
		{
			string message = "LibreWPF StartPage smoke result=" + result + " " + details;
			Console.WriteLine(message);
			SD.StatusBar.SetMessage(message);
		}
	}
}
#endif
