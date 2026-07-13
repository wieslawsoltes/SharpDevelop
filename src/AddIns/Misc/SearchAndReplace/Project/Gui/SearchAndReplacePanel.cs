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
using System.Drawing;
using System.Windows.Forms;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Search;
using ICSharpCode.Core;
using ICSharpCode.Core.WinForms;
using ICSharpCode.NRefactory.Editor;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Editor.Search;
using ICSharpCode.SharpDevelop.Gui;

namespace SearchAndReplace
{
	class SearchAndReplacePanel : UserControl
	{
		SearchAndReplaceMode searchAndReplaceMode;
		ComboBox findComboBox;
		ComboBox replaceComboBox;
		ComboBox lookInComboBox;
		ComboBox fileTypesComboBox;
		ComboBox useComboBox;
		CheckBox matchCaseCheckBox;
		CheckBox matchWholeWordCheckBox;
		CheckBox includeSubFolderCheckBox;
		Label lookAtTypesLabel;
		Button findNextButton;
		Button lookInBrowseButton;
		Button findAllButton;
		Button bookmarkAllButton;
		Button replaceButton;
		Button replaceAllButton;

		static readonly string[] SearchTargetResourceKeys = {
			"${res:Dialog.NewProject.SearchReplace.LookIn.CurrentDocument}",
			"${res:Dialog.NewProject.SearchReplace.LookIn.CurrentSelection}",
			"${res:Dialog.NewProject.SearchReplace.LookIn.AllOpenDocuments}",
			"${res:Dialog.NewProject.SearchReplace.LookIn.WholeProject}",
			"${res:Dialog.NewProject.SearchReplace.LookIn.WholeSolution}"
		};
		
		public SearchAndReplaceMode SearchAndReplaceMode {
			get {
				return searchAndReplaceMode;
			}
			set {
				searchAndReplaceMode = value;
				SuspendLayout();
				Controls.Clear();
				CreateTypedLayout(searchAndReplaceMode);

				if (searchAndReplaceMode == SearchAndReplaceMode.Search) {
					bookmarkAllButton.Click += BookmarkAllButtonClicked;
					findAllButton.Click += FindAllButtonClicked;
				} else {
					replaceButton.Click += ReplaceButtonClicked;
					replaceAllButton.Click += ReplaceAllButtonClicked;
				}
				findComboBox.TextChanged += FindPatternChanged;
				findNextButton.Click += FindNextButtonClicked;
				lookInBrowseButton.Click += LookInBrowseButtonClicked;
				ParentForm.AcceptButton = searchAndReplaceMode == SearchAndReplaceMode.Search
					? findNextButton
					: replaceButton;
				SetOptions();
				EnableButtons(HasFindPattern);
				RightToLeftConverter.ReConvertRecursive(this);
				ResumeLayout(false);
			}
		}
		
		public SearchAndReplacePanel()
		{
		}

		#if LIBREWPF
		internal bool IsPortableLayoutReady {
			get {
				int expectedControlCount = searchAndReplaceMode == SearchAndReplaceMode.Search ? 15 : 17;
				return Controls.Count == expectedControlCount
					&& findComboBox != null
					&& lookInComboBox != null
					&& fileTypesComboBox != null
					&& useComboBox != null
					&& findNextButton != null
					&& (searchAndReplaceMode == SearchAndReplaceMode.Search
						? findAllButton != null && bookmarkAllButton != null
						: replaceComboBox != null && replaceButton != null && replaceAllButton != null);
			}
		}

		internal string FindText {
			get { return findComboBox == null ? string.Empty : findComboBox.Text; }
		}
		#endif

		void CreateTypedLayout(SearchAndReplaceMode mode)
		{
			bool replace = mode == SearchAndReplaceMode.Replace;
			ClientSize = new Size(432, replace ? 360 : 312);

			AddPortableLabel("findWhatLabel", "${res:Dialog.NewProject.SearchReplace.FindWhat}", 8, 8, 416, 23, 0);
			findComboBox = AddPortableComboBox("findComboBox", 8, 32, 416, 21, 1, AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right);

			int offset = 0;
			if (replace) {
				AddPortableLabel("replaceWithLabel", "${res:Dialog.NewProject.SearchReplace.ReplaceWith}", 8, 56, 416, 23, 2);
				replaceComboBox = AddPortableComboBox("replaceComboBox", 8, 80, 416, 21, 3, AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right);
				offset = 48;
			}

			AddPortableLabel("searchInLabel", "${res:Dialog.NewProject.SearchReplace.SearchIn}", 8, 56 + offset, 416, 23, 2 + (replace ? 2 : 0));
			lookInComboBox = AddPortableComboBox("lookInComboBox", 8, 80 + offset, replace ? 379 : 384, 21, 3 + (replace ? 2 : 0), AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right);
			lookInBrowseButton = AddPortableButton("lookInBrowseButton", "...", replace ? 393 : 395, 80 + offset, replace ? 31 : 29, 21, 4 + (replace ? 2 : 0), AnchorStyles.Top | AnchorStyles.Right);
			includeSubFolderCheckBox = AddPortableCheckBox("includeSubFolderCheckBox", "${res:Dialog.NewProject.SearchReplace.IncludeSubFolders}", 24, 104 + offset, 400, 24, 5 + (replace ? 2 : 0));
			lookAtTypesLabel = AddPortableLabel("lookAtTypesLabel", "${res:Dialog.NewProject.SearchReplace.LookAtFileTypes}", 8, 128 + offset, 416, 23, 6 + (replace ? 2 : 0));
			fileTypesComboBox = AddPortableComboBox("fileTypesComboBox", 8, 152 + offset, 416, 21, 7 + (replace ? 2 : 0), AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right);
			matchCaseCheckBox = AddPortableCheckBox("matchCaseCheckBox", "${res:Dialog.NewProject.SearchReplace.MatchCase}", 8, 176 + offset, 416, 24, 8 + (replace ? 2 : 0));
			matchWholeWordCheckBox = AddPortableCheckBox("matchWholeWordCheckBox", "${res:Dialog.NewProject.SearchReplace.MatchWholeWord}", 8, 200 + offset, 416, 24, 9 + (replace ? 2 : 0));
			AddPortableLabel("useMethodLabel", "${res:Dialog.NewProject.SearchReplace.UseMethodLabel}", 8, 224 + offset, 416, 23, 10 + (replace ? 2 : 0));
			useComboBox = AddPortableComboBox("useComboBox", 8, 248 + offset, 416, 21, 11 + (replace ? 2 : 0), AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right);
			useComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

			int buttonY = replace ? 328 : 280;
			if (replace) {
				findNextButton = AddPortableButton("findNextButton", "${res:Dialog.NewProject.SearchReplace.FindNextButton}", 124, buttonY, 96, 23, 14, AnchorStyles.Top | AnchorStyles.Right);
				replaceButton = AddPortableButton("replaceButton", "${res:Dialog.NewProject.SearchReplace.ReplaceButton}", 226, buttonY, 96, 23, 15, AnchorStyles.Top | AnchorStyles.Right);
				replaceAllButton = AddPortableButton("replaceAllButton", "${res:Dialog.NewProject.SearchReplace.ReplaceAllButton}", 328, buttonY, 96, 23, 16, AnchorStyles.Top | AnchorStyles.Right);
			} else {
				findAllButton = AddPortableButton("findAllButton", "${res:Dialog.NewProject.SearchReplace.FindAll}", 124, buttonY, 96, 23, 14, AnchorStyles.Top | AnchorStyles.Right);
				findNextButton = AddPortableButton("findNextButton", "${res:Dialog.NewProject.SearchReplace.FindButton}", 226, buttonY, 96, 23, 12, AnchorStyles.Top | AnchorStyles.Right);
				bookmarkAllButton = AddPortableButton("bookmarkAllButton", "${res:Dialog.NewProject.SearchReplace.MarkAllButton}", 328, buttonY, 96, 23, 13, AnchorStyles.Top | AnchorStyles.Right);
			}
		}

		Label AddPortableLabel(string name, string text, int x, int y, int width, int height, int tabIndex)
		{
			var control = new Label {
				Name = name,
				Text = StringParser.Parse(text),
				Location = new Point(x, y),
				Size = new Size(width, height),
				TabIndex = tabIndex,
				Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
				TextAlign = ContentAlignment.BottomLeft
			};
			Controls.Add(control);
			return control;
		}

		ComboBox AddPortableComboBox(string name, int x, int y, int width, int height, int tabIndex, AnchorStyles anchor)
		{
			var control = new ComboBox {
				Name = name,
				Location = new Point(x, y),
				Size = new Size(width, height),
				TabIndex = tabIndex,
				Anchor = anchor
			};
			Controls.Add(control);
			return control;
		}

		CheckBox AddPortableCheckBox(string name, string text, int x, int y, int width, int height, int tabIndex)
		{
			var control = new CheckBox {
				Name = name,
				Text = StringParser.Parse(text),
				Location = new Point(x, y),
				Size = new Size(width, height),
				TabIndex = tabIndex,
				Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
			};
			Controls.Add(control);
			return control;
		}

		Button AddPortableButton(string name, string text, int x, int y, int width, int height, int tabIndex, AnchorStyles anchor)
		{
			var control = new Button {
				Name = name,
				Text = StringParser.Parse(text),
				Location = new Point(x, y),
				Size = new Size(width, height),
				TabIndex = tabIndex,
				Anchor = anchor
			};
			Controls.Add(control);
			return control;
		}
		
		public SearchTarget SearchTarget {
			get {
				return (SearchTarget)lookInComboBox.SelectedIndex;
			}
			set {
				lookInComboBox.SelectedIndex = (int)value;
			}
		}
		
		void LookInBrowseButtonClicked(object sender, EventArgs e)
		{
			ComboBox lookinComboBox = lookInComboBox;
			string path = SD.FileService.BrowseForFolder("${res:Dialog.NewProject.SearchReplace.LookIn.SelectDirectory}", lookinComboBox.Text);
			if (path != null) {
				lookinComboBox.SelectedIndex = customDirectoryIndex;
				lookinComboBox.Text = path;
			}
		}
		
		SearchResultMatch lastMatch;
		
		void FindNextButtonClicked(object sender, EventArgs e)
		{
			try {
				WritebackOptions();
				var location = new SearchLocation(SearchOptions.SearchTarget, SearchOptions.LookIn, SearchOptions.LookInFiletypes, SearchOptions.IncludeSubdirectories, SearchOptions.SearchTarget == SearchTarget.CurrentSelection ? SearchManager.GetActiveSelection(true) : null);
				var strategy = SearchStrategyFactory.Create(SearchOptions.FindPattern, !SearchOptions.MatchCase, SearchOptions.MatchWholeWord, SearchOptions.SearchMode);
				lastMatch = SearchManager.FindNext(strategy, location);
				SearchManager.SelectResult(lastMatch);
				Focus();
			} catch (SearchPatternException ex) {
				MessageService.ShowError(ex.Message);
			}
		}
		
		void FindAllButtonClicked(object sender, EventArgs e)
		{
			WritebackOptions();
			var location = new SearchLocation(SearchOptions.SearchTarget, SearchOptions.LookIn, SearchOptions.LookInFiletypes, SearchOptions.IncludeSubdirectories, SearchOptions.SearchTarget == SearchTarget.CurrentSelection ? SearchManager.GetActiveSelection(false) : null);
			ISearchStrategy strategy;
			try {
				strategy = SearchStrategyFactory.Create(SearchOptions.FindPattern, !SearchOptions.MatchCase, SearchOptions.MatchWholeWord, SearchOptions.SearchMode);
			} catch (SearchPatternException ex) {
				MessageService.ShowError(ex.Message);
				return;
			}
			// No using block for the monitor; it is disposed when the asynchronous search finishes
			var monitor = SD.StatusBar.CreateProgressMonitor();
			monitor.TaskName = StringParser.Parse("${res:AddIns.SearchReplace.SearchProgressTitle}");
			var results = SearchManager.FindAllParallel(strategy, location, monitor);
			SearchManager.ShowSearchResults(SearchOptions.FindPattern, results);
		}
		
		void BookmarkAllButtonClicked(object sender, EventArgs e)
		{
			WritebackOptions();
			var location = new SearchLocation(SearchOptions.SearchTarget, SearchOptions.LookIn, SearchOptions.LookInFiletypes, SearchOptions.IncludeSubdirectories, SearchOptions.SearchTarget == SearchTarget.CurrentSelection ? SearchManager.GetActiveSelection(false) : null);
			ISearchStrategy strategy;
			try {
				strategy = SearchStrategyFactory.Create(SearchOptions.FindPattern, !SearchOptions.MatchCase, SearchOptions.MatchWholeWord, SearchOptions.SearchMode);
			} catch (SearchPatternException ex) {
				MessageService.ShowError(ex.Message);
				return;
			}
			// No using block for the monitor; it is disposed when the asynchronous search finishes
			var monitor = SD.StatusBar.CreateProgressMonitor();
			monitor.TaskName = StringParser.Parse("${res:AddIns.SearchReplace.SearchProgressTitle}");
			var results = SearchManager.FindAllParallel(strategy, location, monitor);
			SearchManager.MarkAll(results);
		}
		
		void ReplaceAllButtonClicked(object sender, EventArgs e)
		{
			WritebackOptions();
			int count = -1;
			try {
				AsynchronousWaitDialog.RunInCancellableWaitDialog(
					StringParser.Parse("${res:AddIns.SearchReplace.SearchProgressTitle}"), null,
					monitor => {
						var location = new SearchLocation(SearchOptions.SearchTarget, SearchOptions.LookIn, SearchOptions.LookInFiletypes, SearchOptions.IncludeSubdirectories, SearchOptions.SearchTarget == SearchTarget.CurrentSelection ? SearchManager.GetActiveSelection(true) : null);
						var strategy = SearchStrategyFactory.Create(SearchOptions.FindPattern, !SearchOptions.MatchCase, SearchOptions.MatchWholeWord, SearchOptions.SearchMode);
						var results = SearchManager.FindAll(strategy, location, monitor);
						count = SearchManager.ReplaceAll(results, SearchOptions.ReplacePattern, monitor.CancellationToken);
					});
				if (count != -1)
					SearchManager.ShowReplaceDoneMessage(count);
			} catch (SearchPatternException ex) {
				MessageService.ShowError(ex.Message);
			}
		}
		
		void ReplaceButtonClicked(object sender, EventArgs e)
		{
			try {
				WritebackOptions();
				if (SearchManager.IsResultSelected(lastMatch))
					SearchManager.Replace(lastMatch, SearchOptions.ReplacePattern);
				var location = new SearchLocation(SearchOptions.SearchTarget, SearchOptions.LookIn, SearchOptions.LookInFiletypes, SearchOptions.IncludeSubdirectories, SearchOptions.SearchTarget == SearchTarget.CurrentSelection ? SearchManager.GetActiveSelection(true) : null);
				var strategy = SearchStrategyFactory.Create(SearchOptions.FindPattern, !SearchOptions.MatchCase, SearchOptions.MatchWholeWord, SearchOptions.SearchMode);
				lastMatch = SearchManager.FindNext(strategy, location);
				SearchManager.SelectResult(lastMatch);
				Focus();
			} catch (SearchPatternException ex) {
				MessageService.ShowError(ex.Message);
			}
		}
		
		void WritebackOptions()
		{
			SearchOptions.FindPattern = findComboBox.Text;
			
			if (searchAndReplaceMode == SearchAndReplaceMode.Replace) {
				SearchOptions.ReplacePattern = replaceComboBox.Text;
			}
			
			if (lookInComboBox.DropDownStyle == ComboBoxStyle.DropDown) {
				SearchOptions.LookIn = lookInComboBox.Text;
			}
			SearchOptions.LookInFiletypes = fileTypesComboBox.Text;
			SearchOptions.MatchCase = matchCaseCheckBox.Checked;
			SearchOptions.MatchWholeWord = matchWholeWordCheckBox.Checked;
			SearchOptions.IncludeSubdirectories = includeSubFolderCheckBox.Checked;
			
			SearchOptions.SearchMode = (SearchMode)useComboBox.SelectedIndex;
			if (lookInComboBox.DropDownStyle == ComboBoxStyle.DropDown) {
				SearchOptions.SearchTarget = SearchTarget.Directory;
			} else {
				SearchOptions.SearchTarget = (SearchTarget)lookInComboBox.SelectedIndex;
			}
		}
		
		const int customDirectoryIndex = 5;
		
		void SetOptions()
		{
			findComboBox.Text = SearchOptions.FindPattern;
			findComboBox.Items.Clear();
			foreach (string findPattern in SearchOptions.FindPatterns) {
				findComboBox.Items.Add(findPattern);
			}
			
			if (searchAndReplaceMode == SearchAndReplaceMode.Replace) {
				replaceComboBox.Text = SearchOptions.ReplacePattern;
				replaceComboBox.Items.Clear();
				foreach (string replacePattern in SearchOptions.ReplacePatterns) {
					replaceComboBox.Items.Add(replacePattern);
				}
			}
			
			lookInComboBox.Text = SearchOptions.LookIn;
			for (int index = 0; index < SearchTargetResourceKeys.Length; index++) {
				lookInComboBox.Items.Add(StringParser.Parse(SearchTargetResourceKeys[index]));
			}
			lookInComboBox.Items.Add(SearchOptions.LookIn);
			lookInComboBox.SelectedIndexChanged += new EventHandler(LookInSelectedIndexChanged);
			
			if (IsMultipleLineSelection(SearchManager.GetActiveTextEditor())) {
				SearchTarget = SearchTarget.CurrentSelection;
			} else {
				if (SearchOptions.SearchTarget == SearchTarget.CurrentSelection) {
					SearchOptions.SearchTarget = SearchTarget.CurrentDocument;
				}
				SearchTarget = SearchOptions.SearchTarget;
			}
			
			fileTypesComboBox.Text = SearchOptions.LookInFiletypes;
			matchCaseCheckBox.Checked = SearchOptions.MatchCase;
			matchWholeWordCheckBox.Checked = SearchOptions.MatchWholeWord;
			includeSubFolderCheckBox.Checked = SearchOptions.IncludeSubdirectories;
			
			useComboBox.Items.Clear();
			useComboBox.Items.Add(StringParser.Parse("${res:Dialog.NewProject.SearchReplace.SearchStrategy.Standard}"));
			useComboBox.Items.Add(StringParser.Parse("${res:Dialog.NewProject.SearchReplace.SearchStrategy.RegexSearch}"));
			useComboBox.Items.Add(StringParser.Parse("${res:Dialog.NewProject.SearchReplace.SearchStrategy.WildcardSearch}"));
			switch (SearchOptions.SearchMode) {
				case SearchMode.RegEx:
					useComboBox.SelectedIndex = 1;
					break;
				case SearchMode.Wildcard:
					useComboBox.SelectedIndex = 2;
					break;
				default:
					useComboBox.SelectedIndex = 0;
					break;
			}
		}
		
		void LookInSelectedIndexChanged(object sender, EventArgs e)
		{
			if (lookInComboBox.SelectedIndex == customDirectoryIndex) {
				lookInComboBox.DropDownStyle = ComboBoxStyle.DropDown;
				includeSubFolderCheckBox.Enabled = true;
				fileTypesComboBox.Enabled = true;
				lookAtTypesLabel.Enabled = true;
			} else {
				lookInComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
				includeSubFolderCheckBox.Enabled = false;
				fileTypesComboBox.Enabled = false;
				lookAtTypesLabel.Enabled = false;
			}
		}
		
		/// <summary>
		/// Checks whether the selection spans two or more lines.
		/// </summary>
		static bool IsMultipleLineSelection(ITextEditor editor)
		{
			if (editor == null)
				return false;
			else
				return editor.SelectedText.IndexOf('\n') != -1;
		}
		
		/// <summary>
		/// Returns the first ISelection object from the currently active text editor
		/// </summary>
		static ISegment GetCurrentTextSelection()
		{
			ITextEditor textArea = SearchManager.GetActiveTextEditor();
			if (textArea != null) {
				return new TextSegment { StartOffset = textArea.SelectionStart, Length = textArea.SelectionLength };
			}
			return null;
		}
		
		/// <summary>
		/// Enables the various find, bookmark and replace buttons
		/// depending on whether any find string has been entered. The buttons
		/// are disabled otherwise.
		/// </summary>
		void EnableButtons(bool enabled)
		{
			if (searchAndReplaceMode == SearchAndReplaceMode.Replace) {
				replaceButton.Enabled = enabled;
				replaceAllButton.Enabled = enabled;
			} else {
				bookmarkAllButton.Enabled = enabled;
				findAllButton.Enabled = enabled;
			}
			findNextButton.Enabled = enabled;
		}
		
		/// <summary>
		/// Returns true if the string entered in the find or replace text box
		/// is not an empty string.
		/// </summary>
		bool HasFindPattern {
			get {
				return findComboBox.Text.Length != 0;
			}
		}
		
		/// <summary>
		/// Updates the enabled/disabled state of the search and replace buttons
		/// after the search or replace text has changed.
		/// </summary>
		void FindPatternChanged(object source, EventArgs e)
		{
			EnableButtons(HasFindPattern);
		}
	}
}
