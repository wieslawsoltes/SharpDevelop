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
using System.Threading.Tasks;

using Hornung.ResourceToolkit.Refactoring;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;

namespace Hornung.ResourceToolkit.LibreWpf
{
	public sealed class LibreWpfResourceToolkitSmokeHook : ILibreWpfSmokeHook
	{
		public string EnvironmentVariableName {
			get { return "LIBREWPF_SHARPDEVELOP_RESOURCE_TOOLKIT_SMOKE"; }
		}

		public async Task RunAsync(string mode)
		{
			for (int attempt = 0; attempt < 100; attempt++) {
				if (ProjectService.OpenSolution != null) {
					break;
				}
				await Task.Delay(200);
			}

			if (ProjectService.OpenSolution == null) {
				WriteResult("Unavailable", "reason=NoSolution");
				return;
			}

			var files = ResourceRefactoringService.GetPossibleFiles(SearchScope.WholeSolution);
			var references = ResourceRefactoringService.FindAllReferences(null, SearchScope.WholeSolution);
			var missing = ResourceRefactoringService.FindReferencesToMissingKeys(null, SearchScope.WholeSolution);
			var unused = ResourceRefactoringService.FindUnusedKeys(null);

			WriteResult(
				"Success",
				"mode=" + (string.IsNullOrEmpty(mode) ? "Default" : mode)
				+ " files=" + files.Count
				+ " references=" + (references == null ? 0 : references.Count)
				+ " missing=" + (missing == null ? 0 : missing.Count)
				+ " unused=" + (unused == null ? 0 : unused.Count));
		}

		static void WriteResult(string result, string details)
		{
			string message = "LibreWPF ResourceToolkit smoke result=" + result + " " + details;
			Console.WriteLine(message);
			SD.StatusBar.SetMessage(message);
		}
	}
}
#endif
