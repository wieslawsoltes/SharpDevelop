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
using System.Diagnostics;

using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.Core;
using ICSharpCode.NRefactory.Editor;
using ICSharpCode.SharpDevelop.Debugging;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Editor.Bookmarks;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Project;

namespace ICSharpCode.SharpDevelop.Services
{
	/// <summary>
	/// Cross-platform debugger service used by the LibreWPF host until a native managed debugger backend is ported.
	/// </summary>
	[CLSCompliant(false)]
	public sealed class LibreWpfPortableDebuggerService : BaseDebuggerService
	{
		public override bool IsDebugging {
			get { return false; }
		}

		public override bool IsProcessRunning {
			get { return false; }
		}

		public override bool BreakAtBeginning { get; set; }

		public override bool IsAttached {
			get { return false; }
		}

		public override bool CanDebug(IProject project)
		{
			return false;
		}

		public override bool Supports(DebuggerFeatures feature)
		{
			return feature == DebuggerFeatures.StartWithoutDebugging;
		}

		public override void Start(ProcessStartInfo processStartInfo)
		{
			throw new NotSupportedException("LibreWPF does not have a managed debugger backend yet.");
		}

		public override void StartWithoutDebugging(ProcessStartInfo processStartInfo)
		{
			if (processStartInfo == null)
				throw new ArgumentNullException("processStartInfo");

			Process.Start(processStartInfo);
		}

		public override void Stop()
		{
		}

		public override void Break()
		{
			throw new NotSupportedException("LibreWPF does not have a managed debugger backend yet.");
		}

		public override void Continue()
		{
			throw new NotSupportedException("LibreWPF does not have a managed debugger backend yet.");
		}

		public override void StepInto()
		{
			throw new NotSupportedException("LibreWPF does not have a managed debugger backend yet.");
		}

		public override void StepOver()
		{
			throw new NotSupportedException("LibreWPF does not have a managed debugger backend yet.");
		}

		public override void StepOut()
		{
			throw new NotSupportedException("LibreWPF does not have a managed debugger backend yet.");
		}

		public override void ShowAttachDialog()
		{
		}

		public override void Attach(Process process)
		{
		}

		public override void Detach()
		{
		}

		public override bool SetInstructionPointer(string filename, int line, int column, bool dryRun)
		{
			return false;
		}

		public override void ToggleBreakpointAt(ITextEditor editor, int lineNumber)
		{
			if (editor == null)
				throw new ArgumentNullException("editor");

			if (string.IsNullOrEmpty(editor.FileName))
				return;

			if (!SD.BookmarkManager.RemoveBookmarkAt(editor.FileName, lineNumber, b => b is LibreWpfPortableBreakpointBookmark)) {
				SD.BookmarkManager.AddMark(new LibreWpfPortableBreakpointBookmark(), editor.Document, lineNumber);
			}
		}

		public override void RemoveCurrentLineMarker()
		{
		}

		public override void HandleToolTipRequest(ToolTipRequestEventArgs e)
		{
		}
	}

	[CLSCompliant(false)]
	public sealed class LibreWpfPortableBreakpointBookmark : SDMarkerBookmark, IHaveStateEnabled
	{
		bool isEnabled = true;

		public bool IsEnabled {
			get { return isEnabled; }
			set {
				if (isEnabled != value) {
					isEnabled = value;
					Redraw();
				}
			}
		}

		public override IImage Image {
			get { return SD.ResourceService.GetImage("Bookmarks.Breakpoint") ?? BookmarkBase.DefaultBookmarkImage; }
		}

		protected override ITextMarker CreateMarker(ITextMarkerService markerService)
		{
			IDocumentLine line = Document.GetLineByNumber(LineNumber);
			ITextMarker marker = markerService.Create(line.Offset, line.Length);
			IHighlighter highlighter = Document.GetService(typeof(IHighlighter)) as IHighlighter;
			marker.BackgroundColor = BookmarkBase.BreakpointDefaultBackground;
			marker.ForegroundColor = BookmarkBase.BreakpointDefaultForeground;
			marker.MarkerColor = BookmarkBase.BreakpointDefaultBackground;
			marker.MarkerTypes = TextMarkerTypes.CircleInScrollBar;

			if (highlighter != null) {
				var color = highlighter.GetNamedColor(BookmarkBase.BreakpointMarkerName);
				if (color != null) {
					marker.BackgroundColor = color.Background.GetColor(null);
					marker.MarkerColor = color.Background.GetColor(null) ?? BookmarkBase.BreakpointDefaultForeground;
					marker.ForegroundColor = color.Foreground.GetColor(null);
				}
			}

			return marker;
		}
	}
}
#endif
