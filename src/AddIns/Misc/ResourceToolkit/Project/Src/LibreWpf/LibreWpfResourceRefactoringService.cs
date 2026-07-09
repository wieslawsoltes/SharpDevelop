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
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Hornung.ResourceToolkit.Resolver;
using Hornung.ResourceToolkit.ResourceFileContent;
using ICSharpCode.Core;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.Editor;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Editor.Search;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;

namespace Hornung.ResourceToolkit.Refactoring
{
	/// <summary>
	/// Provides ResourceToolkit refactoring support for the LibreWPF SharpDevelop build.
	/// </summary>
	public static class ResourceRefactoringService
	{
		sealed class ResourceReferenceMatch
		{
			public ResourceResolveResult ResolveResult { get; private set; }
			public SearchResultMatch SearchMatch { get; private set; }

			public ResourceReferenceMatch(ResourceResolveResult resolveResult, SearchResultMatch searchMatch)
			{
				this.ResolveResult = resolveResult;
				this.SearchMatch = searchMatch;
			}
		}

		public static List<SearchResultMatch> FindReferences(string resourceFileName, string key, IProgressMonitor monitor)
		{
			return FindResourceReferences(
				monitor,
				SearchScope.WholeSolution,
				result => result != null
					&& result.Key != null
					&& FileUtility.IsEqualFileName(resourceFileName, result.FileName)
					&& key.Equals(result.Key, StringComparison.OrdinalIgnoreCase))
				.Select(reference => reference.SearchMatch)
				.ToList();
		}

		public static List<SearchResultMatch> FindAllReferences(IProgressMonitor monitor, SearchScope scope)
		{
			return FindResourceReferences(monitor, scope, result => result != null)
				.Select(reference => reference.SearchMatch)
				.ToList();
		}

		public static List<SearchResultMatch> FindReferencesToMissingKeys(IProgressMonitor monitor, SearchScope scope)
		{
			return FindResourceReferences(monitor, scope, IsReferenceToMissingKey)
				.Select(reference => reference.SearchMatch)
				.ToList();
		}

		static List<ResourceReferenceMatch> FindResourceReferences(
			IProgressMonitor monitor,
			SearchScope scope,
			Func<ResourceResolveResult, bool> predicate)
		{
			if (predicate == null) {
				throw new ArgumentNullException("predicate");
			}

			ICollection<string> files = GetPossibleFiles(scope);
			List<ResourceReferenceMatch> references = new List<ResourceReferenceMatch>();

			if (monitor != null) {
				monitor.TaskName = StringParser.Parse("${res:SharpDevelop.Refactoring.FindingReferences}");
			}

			double workDone = 0;
			foreach (string fileName in files) {
				if (monitor != null) {
					monitor.Progress = files.Count == 0 ? 1 : workDone / files.Count;
				}
				workDone += 1;

				if (monitor != null && monitor.CancellationToken.IsCancellationRequested) {
					return null;
				}

				IDocument document = TryCreateDocument(fileName);
				if (document == null || document.TextLength == 0) {
					continue;
				}

				string fileContent = document.Text;
				int pos = -1;
				while ((pos = GetNextPossibleOffset(fileName, fileContent, pos)) >= 0) {
					TextLocation location = document.GetLocation(pos);
					ResourceResolveResult result = ResourceResolverService.Resolve(fileName, document, location.Line - 1, location.Column - 1, null);
					if (result == null || result.ResourceFileContent == null || !predicate(result)) {
						continue;
					}

					references.Add(new ResourceReferenceMatch(result, CreateSearchMatch(fileName, document, fileContent, pos, result)));
				}
			}

			LoggingService.Info("ResourceToolkit: LibreWPF resource reference search found " + references.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " matches.");
			return references;
		}

		static IDocument TryCreateDocument(string fileName)
		{
			try {
				ITextSource content = SD.FileService.GetFileContent(FileName.Create(fileName));
				return new ReadOnlyDocument(content, fileName);
			} catch (FileNotFoundException) {
				return null;
			} catch (IOException) {
				return null;
			} catch (UnauthorizedAccessException) {
				return null;
			}
		}

		static int GetNextPossibleOffset(string fileName, string fileContent, int previousOffset)
		{
			int result = -1;
			foreach (IResourceResolver resolver in ResourceResolverService.Resolvers) {
				if (!resolver.SupportsFile(fileName)) {
					continue;
				}

				foreach (string pattern in resolver.GetPossiblePatternsForFile(fileName)) {
					int candidate = fileContent.IndexOf(pattern, previousOffset + 1, StringComparison.OrdinalIgnoreCase);
					if (candidate >= 0 && (result < 0 || candidate < result)) {
						result = candidate;
					}
				}
			}

			return result;
		}

		static SearchResultMatch CreateSearchMatch(string fileName, IDocument document, string fileContent, int referenceOffset, ResourceResolveResult result)
		{
			int keyOffset = referenceOffset;
			int keyLength = 0;

			if (result.Key != null) {
				keyOffset = FindResolvedKeyOffset(fileContent, referenceOffset, result.Key);
				if (keyOffset < referenceOffset) {
					keyOffset = referenceOffset;
				}
				keyLength = result.Key.Length;
			}

			TextLocation start = document.GetLocation(keyOffset);
			TextLocation end = document.GetLocation(Math.Min(document.TextLength, keyOffset + Math.Max(1, keyLength)));
			return new SearchResultMatch(FileName.Create(fileName), start, end, keyOffset, keyLength, null, null);
		}

		static int FindResolvedKeyOffset(string fileContent, int referenceOffset, string key)
		{
			const string token = ICSharpCodeCoreResourceResolver.ResourceReferenceToken;
			int tokenEnd = referenceOffset + token.Length;
			if (referenceOffset >= 0
			    && tokenEnd <= fileContent.Length
			    && fileContent.IndexOf(token, referenceOffset, token.Length, StringComparison.OrdinalIgnoreCase) == referenceOffset) {
				int referenceEnd = fileContent.IndexOf('}', tokenEnd);
				if (referenceEnd >= tokenEnd) {
					int keyOffset = fileContent.IndexOf(key, tokenEnd, referenceEnd - tokenEnd, StringComparison.OrdinalIgnoreCase);
					if (keyOffset >= tokenEnd) {
						return keyOffset;
					}
				}
			}

			return fileContent.IndexOf(key, referenceOffset, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsReferenceToMissingKey(ResourceResolveResult result)
		{
			if (result == null || result.Key == null) {
				return false;
			}
			if (result.ResourceFileContent == null) {
				return true;
			}
			return !result.ResourceFileContent.ContainsKey(result.Key);
		}

		public static ICollection<ResourceItem> FindUnusedKeys(IProgressMonitor monitor)
		{
			List<ResourceReferenceMatch> references = FindResourceReferences(monitor, SearchScope.WholeSolution, result => result != null);
			if (references == null) {
				return null;
			}

			Dictionary<string, HashSet<string>> referencedKeys = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
			Dictionary<string, HashSet<string>> referencedPrefixes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (ResourceReferenceMatch reference in references) {
				ResourceResolveResult result = reference.ResolveResult;
				if (result.ResourceFileContent == null) {
					continue;
				}

				string fileName = result.FileName;
				if (String.IsNullOrEmpty(fileName)) {
					continue;
				}

				HashSet<string> keys;
				if (!referencedKeys.TryGetValue(fileName, out keys)) {
					keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					referencedKeys.Add(fileName, keys);
					referencedPrefixes.Add(fileName, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
				}

				if (result.Key != null) {
					keys.Add(result.Key);
				} else {
					ResourcePrefixResolveResult prefix = result as ResourcePrefixResolveResult;
					if (prefix != null && prefix.Prefix != null) {
						referencedPrefixes[fileName].Add(prefix.Prefix);
					}
				}
			}

			List<ResourceItem> unused = new List<ResourceItem>();
			foreach (string fileName in referencedKeys.Keys) {
				IResourceFileContent content = ResourceFileContentRegistry.GetResourceFileContent(fileName);
				if (content == null) {
					continue;
				}

				foreach (KeyValuePair<string, object> entry in content.Data) {
					if (!referencedKeys[fileName].Contains(entry.Key)
					    && !referencedPrefixes[fileName].Any(prefix => entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) {
						unused.Add(new ResourceItem(fileName, entry.Key));
					}
				}
			}

			return unused.AsReadOnly();
		}

		public static void Rename(ResourceResolveResult result)
		{
			if (result == null || result.Key == null || result.ResourceFileContent == null) {
				MessageService.ShowMessage("${res:SharpDevelop.Refactoring.CannotRenameElement}");
				return;
			}

			string newKey = MessageService.ShowInputBox("${res:SharpDevelop.Refactoring.Rename}", "${res:Hornung.ResourceToolkit.RenameResourceText}", result.Key);
			if (!String.IsNullOrEmpty(newKey) && !newKey.Equals(result.Key, StringComparison.Ordinal)) {
				using (AsynchronousWaitDialog monitor = AsynchronousWaitDialog.ShowWaitDialog("${res:SharpDevelop.Refactoring.Rename}")) {
					Rename(result, newKey, monitor);
				}
			}
		}

		public static void Rename(ResourceResolveResult result, string newKey, IProgressMonitor monitor)
		{
			if (result == null) {
				throw new ArgumentNullException("result");
			}
			if (newKey == null) {
				throw new ArgumentNullException("newKey");
			}
			if (result.ResourceFileContent == null || result.Key == null) {
				MessageService.ShowMessage("${res:SharpDevelop.Refactoring.CannotRenameElement}");
				return;
			}

			if (result.ResourceFileContent.ContainsKey(newKey)) {
				if (monitor != null) {
					monitor.ShowingDialog = true;
				}
				MessageService.ShowWarning("${res:Hornung.ResourceToolkit.EditStringResourceDialog.DuplicateKey}");
				if (monitor != null) {
					monitor.ShowingDialog = false;
				}
				return;
			}

			List<ResourceReferenceMatch> references = FindResourceReferences(
				monitor,
				SearchScope.WholeSolution,
				candidate => candidate != null
					&& candidate.Key != null
					&& FileUtility.IsEqualFileName(result.FileName, candidate.FileName)
					&& result.Key.Equals(candidate.Key, StringComparison.OrdinalIgnoreCase));

			if (references == null) {
				return;
			}

			try {
				if (result.ResourceFileContent.ContainsKey(result.Key)) {
					result.ResourceFileContent.RenameKey(result.Key, newKey);
				} else {
					ShowWarning(monitor, "${res:Hornung.ResourceToolkit.RenameKeyDefinitionNotFoundWarning}");
				}
			} catch (Exception ex) {
				ShowWarning(monitor, StringParser.Parse("${res:Hornung.ResourceToolkit.ErrorProcessingResourceFile}") + Environment.NewLine + ex.Message);
				return;
			}

			RenameReferences(references, newKey, monitor);
			RenameLocalizedDefinitions(result, newKey, monitor);
		}

		static void RenameReferences(IEnumerable<ResourceReferenceMatch> references, string newKey, IProgressMonitor monitor)
		{
			foreach (var fileGroup in references
			         .Select(reference => reference.SearchMatch)
			         .Where(match => match.Length > 0)
			         .GroupBy(match => match.FileName)
			         .ToList()) {
				if (monitor != null && monitor.CancellationToken.IsCancellationRequested) {
					return;
				}

				IViewContent viewContent = FileService.OpenFile(fileGroup.Key, false);
				ITextEditor editor = viewContent == null ? null : viewContent.GetService<ITextEditor>();
				if (editor == null) {
					continue;
				}

				int offsetDelta = 0;
				using (editor.Document.OpenUndoGroup()) {
					foreach (SearchResultMatch match in fileGroup.OrderBy(match => match.StartOffset)) {
						editor.Document.Replace(match.StartOffset + offsetDelta, match.Length, newKey);
						offsetDelta += newKey.Length - match.Length;
					}
				}
			}
		}

		static void RenameLocalizedDefinitions(ResourceResolveResult result, string newKey, IProgressMonitor monitor)
		{
			foreach (KeyValuePair<string, IResourceFileContent> entry in ResourceFileContentRegistry.GetLocalizedContents(result.FileName)) {
				try {
					if (entry.Value.ContainsKey(result.Key)) {
						entry.Value.RenameKey(result.Key, newKey);
					}
				} catch (Exception ex) {
					ShowWarning(monitor, StringParser.Parse("${res:Hornung.ResourceToolkit.ErrorProcessingResourceFile}") + Environment.NewLine + ex.Message);
				}
			}
		}

		static void ShowWarning(IProgressMonitor monitor, string message)
		{
			if (monitor != null) {
				monitor.ShowingDialog = true;
			}
			MessageService.ShowWarning(message);
			if (monitor != null) {
				monitor.ShowingDialog = false;
			}
		}

		public static ICollection<string> GetPossibleFiles(SearchScope scope)
		{
			List<string> files = new List<string>();

			switch (scope) {
				case SearchScope.WholeSolution:
					ISolution solution = ProjectService.OpenSolution;
					if (solution == null) {
						throw new InvalidOperationException("Cannot search in whole solution when no solution is open.");
					}
					foreach (IProject project in solution.Projects) {
						AddFilesFromProject(files, project);
					}
					break;

				case SearchScope.CurrentProject:
					IProject currentProject = ProjectService.CurrentProject;
					if (currentProject == null) {
						throw new InvalidOperationException("Cannot search in current project when no project is active.");
					}
					AddFilesFromProject(files, currentProject);
					break;

				case SearchScope.CurrentFile:
					IViewContent activeView = WorkbenchSingleton.Workbench.ActiveViewContent;
					if (activeView == null) {
						throw new InvalidOperationException("Cannot search in current file when no file is open.");
					}
					AddFilesFromViewContent(files, activeView);
					break;

				case SearchScope.OpenFiles:
					foreach (IViewContent viewContent in WorkbenchSingleton.Workbench.ViewContentCollection) {
						AddFilesFromViewContent(files, viewContent);
					}
					break;

				default:
					throw new ArgumentOutOfRangeException("scope");
			}

			return files.AsReadOnly();
		}

		static void AddFilesFromProject(IList<string> files, IProject project)
		{
			foreach (ProjectItem item in project.Items) {
				FileProjectItem fileItem = item as FileProjectItem;
				if (fileItem == null) {
					continue;
				}

				string fileName = fileItem.FileName;
				if (IsPossibleFile(fileName) && !ContainsFile(files, fileName)) {
					files.Add(fileName);
					ProjectFileDictionaryService.AddFile(fileName, project);
				}
			}
		}

		static void AddFilesFromViewContent(IList<string> files, IViewContent viewContent)
		{
			foreach (OpenedFile file in viewContent.Files) {
				string fileName = file.FileName;
				if (fileName != null && IsPossibleFile(fileName) && !ContainsFile(files, fileName)) {
					files.Add(fileName);
				}
			}
		}

		static bool ContainsFile(IEnumerable<string> files, string fileName)
		{
			return files.Any(existing => FileUtility.IsEqualFileName(existing, fileName));
		}

		public static bool IsPossibleFile(string name)
		{
			return !String.IsNullOrEmpty(name) && ResourceResolverService.Resolvers.Any(resolver => resolver.SupportsFile(name));
		}

		public static int FindStringLiteral(string fileName, string fileContent, string literal, int startOffset, out string code)
		{
			code = literal;
			return String.IsNullOrEmpty(literal) ? -1 : fileContent.IndexOf(literal, startOffset, StringComparison.OrdinalIgnoreCase);
		}
	}

	public enum SearchScope
	{
		WholeSolution,
		CurrentProject,
		CurrentFile,
		OpenFiles
	}
}
#endif
