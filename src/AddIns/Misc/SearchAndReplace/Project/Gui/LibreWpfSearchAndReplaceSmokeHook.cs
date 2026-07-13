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

using ICSharpCode.AvalonEdit.Search;
using ICSharpCode.SharpDevelop;

namespace SearchAndReplace
{
	public sealed class LibreWpfSearchAndReplaceSmokeHook : ILibreWpfSmokeHook
	{
		public string EnvironmentVariableName {
			get { return "LIBREWPF_SHARPDEVELOP_SEARCH_AND_REPLACE_SMOKE"; }
		}

		public Task RunAsync(string mode)
		{
			SearchOptions.FindPattern = "LibreWPF-search-smoke";
			SearchOptions.ReplacePattern = "ProGPU-replace-smoke";
			SearchOptions.SearchTarget = SearchTarget.CurrentDocument;
			SearchOptions.SearchMode = SearchMode.Normal;

			SearchAndReplaceDialog.ShowSingleInstance(SearchAndReplaceMode.Search);
			SearchAndReplaceDialog dialog = SearchAndReplaceDialog.CurrentInstance;
			bool searchReady = dialog != null
				&& dialog.CurrentMode == SearchAndReplaceMode.Search
				&& dialog.IsPortableLayoutReady
				&& dialog.CurrentFindText == SearchOptions.FindPattern
				&& dialog.HasExpectedKeyboardShortcuts;

			bool replaceReady = false;
			bool closed = false;
			if (dialog != null) {
				dialog.SelectReplaceModeForSmoke();
				replaceReady = dialog.CurrentMode == SearchAndReplaceMode.Replace
					&& dialog.IsPortableLayoutReady
					&& dialog.CurrentFindText == SearchOptions.FindPattern;
				dialog.Close();
				closed = SearchAndReplaceDialog.CurrentInstance == null;
			}

			bool success = searchReady && replaceReady && closed;
			string result = success ? "Success" : "Failed";
			string message = "LibreWPF SearchAndReplace smoke result=" + result
				+ " mode=" + (string.IsNullOrEmpty(mode) ? "Default" : mode)
				+ " searchReady=" + searchReady
				+ " replaceReady=" + replaceReady
				+ " shortcuts=" + (dialog != null && dialog.HasExpectedKeyboardShortcuts)
				+ " closed=" + closed;
			Console.WriteLine(message);
			SD.StatusBar.SetMessage(message);

			if (!success) {
				throw new InvalidOperationException(message);
			}

			return Task.CompletedTask;
		}
	}
}
#endif
