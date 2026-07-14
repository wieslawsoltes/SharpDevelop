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
using System.IO;
using System.Linq;

using ICSharpCode.Core;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.Editor;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Dom;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;
using ICSharpCode.WpfDesign;
using ICSharpCode.WpfDesign.Designer.Xaml;

namespace ICSharpCode.WpfDesign.AddIn
{
	internal enum EventHandlerCreationFailure
	{
		None,
		InvalidEventProperty,
		ProjectUnavailable,
		CodeBehindUnavailable,
		SourceDocumentUnavailable,
		SourceParseFailed,
		DesignedClassUnavailable,
		PrimaryCodePartMismatch,
		EventDeclarationUnavailable,
		ComponentNameUnavailable,
		InvalidHandlerName,
		HandlerNameUnavailable,
		SourceInsertionFailed,
		InsertedHandlerVerificationFailed,
		XamlBindingFailed
	}

	sealed class SharpDevelopEventHandlerService : IEventHandlerService
	{
		readonly WpfViewContent viewContent;

		public SharpDevelopEventHandlerService(WpfViewContent viewContent)
		{
			if (viewContent == null)
				throw new ArgumentNullException("viewContent");
			this.viewContent = viewContent;
		}

		public void CreateEventHandler(DesignItemProperty eventProperty)
		{
			TryCreateEventHandler(eventProperty);
		}

		internal bool TryCreateEventHandler(DesignItemProperty eventProperty)
		{
			EventHandlerCreationFailure failure;
			return TryCreateEventHandler(eventProperty, out failure);
		}

		internal bool TryCreateEventHandler(
			DesignItemProperty eventProperty,
			out EventHandlerCreationFailure failure)
		{
			failure = EventHandlerCreationFailure.None;
			EventHandlerPlan plan;
			if (!TryCreatePlan(eventProperty, out plan, out failure))
				return false;

			if (plan.CompatibleMethod != null) {
				if (!TryCommitXamlBinding(plan)) {
					failure = EventHandlerCreationFailure.XamlBindingFailed;
					return false;
				}
				JumpToMethod(plan.CompatibleMethod);
				return true;
			}

			bool sourceCommitted = false;
			IMethod insertedMethod = null;
			using (var sourceTransaction = new SourceDocumentTransaction(
				plan.Editor.Document,
				() => plan.Editor.Caret.Offset,
				offset => plan.Editor.Caret.Offset = offset,
				plan.OpenedFile != null ? (Func<bool>)(() => plan.OpenedFile.IsDirty) : null,
				plan.OpenedFile != null ? (Action<bool>)(value => plan.OpenedFile.IsDirty = value) : null)) {
				try {
					failure = EventHandlerCreationFailure.SourceInsertionFailed;
					plan.Project.LanguageBinding.CodeGenerator.InsertEventHandler(
						plan.DesignedClass,
						plan.HandlerName,
						plan.EventDefinition,
						true);

					EventHandlerPlan currentPlan;
					if (!TryRefreshPlan(plan, out currentPlan)
					    || currentPlan.CompatibleMethod == null) {
						failure = EventHandlerCreationFailure.InsertedHandlerVerificationFailed;
						throw new InvalidOperationException(
							"The language binding did not create the requested event-handler signature in the current code-behind document.");
					}

					if (!TryCommitXamlBinding(currentPlan)) {
						failure = EventHandlerCreationFailure.XamlBindingFailed;
						throw new InvalidOperationException("The event hookup could not be committed to the XAML model.");
					}

					insertedMethod = currentPlan.CompatibleMethod;
					sourceTransaction.Commit();
					sourceCommitted = true;
					failure = EventHandlerCreationFailure.None;
				} catch (Exception ex) {
					LoggingService.Error("The WPF designer could not create the event handler without changing an unrelated source or XAML document.", ex);
				}
			}

			if (!sourceCommitted) {
				SynchronizeCodeBehind(plan);
				return false;
			}

			JumpToMethod(insertedMethod);
			return true;
		}

		bool TryCreatePlan(
			DesignItemProperty eventProperty,
			out EventHandlerPlan plan,
			out EventHandlerCreationFailure failure)
		{
			plan = null;
			failure = EventHandlerCreationFailure.InvalidEventProperty;
			if (eventProperty == null || !eventProperty.IsEvent || eventProperty.DesignItem == null)
				return false;

			IProject project = FindProjectContainingFile();
			if (project == null) {
				failure = EventHandlerCreationFailure.ProjectUnavailable;
				return false;
			}

			FileName codeBehindFile;
			if (!TryGetPrimaryCodeBehindFile(project, viewContent.PrimaryFileName, out codeBehindFile)) {
				failure = EventHandlerCreationFailure.CodeBehindUnavailable;
				return false;
			}

			IViewContent sourceView;
			ITextEditor editor;
			OpenedFile openedFile;
			try {
				sourceView = SD.FileService.OpenFile(codeBehindFile, false);
				editor = sourceView != null ? sourceView.GetService<ITextEditor>() : null;
				openedFile = SD.FileService.GetOpenedFile(codeBehindFile);
			} catch (Exception ex) {
				LoggingService.Error("The WPF designer could not open the current code-behind document.", ex);
				failure = EventHandlerCreationFailure.SourceDocumentUnavailable;
				return false;
			}
			if (editor == null || editor.Document == null
			    || !FileUtility.IsEqualFileName(editor.FileName, codeBehindFile)) {
				failure = EventHandlerCreationFailure.SourceDocumentUnavailable;
				return false;
			}

			try {
				SD.ParserService.Parse(codeBehindFile, editor.Document, project);
			} catch (Exception ex) {
				LoggingService.Error("The WPF designer could not parse the current code-behind document.", ex);
				failure = EventHandlerCreationFailure.SourceParseFailed;
				return false;
			}

			ICompilation compilation = SD.ParserService.GetCompilation(project);
			ITypeDefinition designedClass = GetDesignedClass(compilation);
			IUnresolvedTypeDefinition targetPart;
			if (designedClass == null) {
				failure = EventHandlerCreationFailure.DesignedClassUnavailable;
				return false;
			}
			if (!TrySelectPrimaryCodePart(designedClass, codeBehindFile, out targetPart)) {
				failure = EventHandlerCreationFailure.PrimaryCodePartMismatch;
				return false;
			}

			IEvent eventDefinition = FindEventDeclaration(
				compilation,
				eventProperty.DeclaringType,
				eventProperty.Name);
			IMethod invokeMethod = eventDefinition != null
				? eventDefinition.ReturnType.GetDelegateInvokeMethod()
				: null;
			if (invokeMethod == null) {
				failure = EventHandlerCreationFailure.EventDeclarationUnavailable;
				return false;
			}

			string currentHandlerName = eventProperty.ValueOnInstance as string;
			string componentName = eventProperty.DesignItem.Name;
			bool setComponentName = string.IsNullOrWhiteSpace(currentHandlerName)
				&& string.IsNullOrWhiteSpace(componentName);
			if (setComponentName)
				componentName = GetAvailableDesignItemName(eventProperty.DesignItem);
			if (string.IsNullOrWhiteSpace(currentHandlerName)
			    && string.IsNullOrWhiteSpace(componentName)) {
				failure = EventHandlerCreationFailure.ComponentNameUnavailable;
				return false;
			}

			string baseHandlerName = string.IsNullOrWhiteSpace(currentHandlerName)
				? componentName + "_" + eventProperty.Name
				: currentHandlerName.Trim();
			if (!IsPortableIdentifier(baseHandlerName)) {
				failure = EventHandlerCreationFailure.InvalidHandlerName;
				return false;
			}

			string handlerName = GetAvailableHandlerName(
				designedClass,
				invokeMethod,
				baseHandlerName,
				codeBehindFile);
			if (string.IsNullOrEmpty(handlerName)) {
				failure = EventHandlerCreationFailure.HandlerNameUnavailable;
				return false;
			}

			plan = new EventHandlerPlan {
				EventProperty = eventProperty,
				Project = project,
				DesignedClass = designedClass,
				EventDefinition = eventDefinition,
				CompatibleMethod = FindCompatibleMethod(
					designedClass,
					handlerName,
					invokeMethod,
					codeBehindFile),
				CodeBehindFile = codeBehindFile,
				Editor = editor,
				OpenedFile = openedFile,
				HandlerName = handlerName,
				ComponentName = componentName,
				SetComponentName = setComponentName,
				SetEventHandler = !string.Equals(currentHandlerName, handlerName, StringComparison.Ordinal)
			};
			failure = EventHandlerCreationFailure.None;
			return true;
		}

		bool TryRefreshPlan(EventHandlerPlan previous, out EventHandlerPlan current)
		{
			current = null;
			try {
				SD.ParserService.Parse(previous.CodeBehindFile, previous.Editor.Document, previous.Project);
				ICompilation compilation = SD.ParserService.GetCompilation(previous.Project);
				ITypeDefinition designedClass = GetDesignedClass(compilation);
				IUnresolvedTypeDefinition targetPart;
				if (designedClass == null
				    || !TrySelectPrimaryCodePart(designedClass, previous.CodeBehindFile, out targetPart))
					return false;

				IEvent eventDefinition = FindEventDeclaration(
					compilation,
					previous.EventProperty.DeclaringType,
					previous.EventProperty.Name);
				IMethod invokeMethod = eventDefinition != null
					? eventDefinition.ReturnType.GetDelegateInvokeMethod()
					: null;
				IMethod compatibleMethod = FindCompatibleMethod(
					designedClass,
					previous.HandlerName,
					invokeMethod,
					previous.CodeBehindFile);
				if (invokeMethod == null || compatibleMethod == null)
					return false;

				current = previous.CloneForRefresh(designedClass, compatibleMethod);
				return true;
			} catch (Exception ex) {
				LoggingService.Error("The WPF designer could not verify the newly inserted event handler.", ex);
				return false;
			}
		}

		bool TryCommitXamlBinding(EventHandlerPlan plan)
		{
			if (!plan.SetComponentName && !plan.SetEventHandler)
				return true;

			try {
				using (ChangeGroup group = plan.EventProperty.DesignItem.OpenGroup("Create event handler")) {
					if (plan.SetComponentName)
						plan.EventProperty.DesignItem.Name = plan.ComponentName;
					if (plan.SetEventHandler)
						plan.EventProperty.SetValue(plan.HandlerName);

					if ((plan.SetComponentName
					     && !string.Equals(plan.EventProperty.DesignItem.Name, plan.ComponentName, StringComparison.Ordinal))
					    || !string.Equals(plan.EventProperty.ValueOnInstance as string, plan.HandlerName, StringComparison.Ordinal)) {
						return false;
					}
					group.Commit();
				}
				return true;
			} catch (Exception ex) {
				LoggingService.Error("The WPF designer rolled back an event hookup that could not be committed atomically.", ex);
				return false;
			}
		}

		void SynchronizeCodeBehind(EventHandlerPlan plan)
		{
			try {
				SD.ParserService.Parse(plan.CodeBehindFile, plan.Editor.Document, plan.Project);
			} catch (Exception ex) {
				LoggingService.Error("The WPF designer could not refresh the restored code-behind document.", ex);
			}
		}

		IProject FindProjectContainingFile()
		{
			return SD.ProjectService.FindProjectContainingFile(viewContent.PrimaryFileName);
		}

		ITypeDefinition GetDesignedClass(ICompilation compilation)
		{
			if (compilation == null)
				return null;
			var xamlContext = viewContent.DesignContext as XamlDesignContext;
			string className = xamlContext != null ? xamlContext.ClassName : null;
			if (string.IsNullOrWhiteSpace(className))
				return null;
			return compilation.FindType(new FullTypeName(className)).GetDefinition();
		}

		internal bool TryGetPrimaryCodeBehindFile(out FileName codeBehindFile)
		{
			return TryGetPrimaryCodeBehindFile(
				FindProjectContainingFile(),
				viewContent.PrimaryFileName,
				out codeBehindFile);
		}

		internal static bool TryGetPrimaryCodeBehindFile(
			IProject project,
			FileName xamlFile,
			out FileName codeBehindFile)
		{
			codeBehindFile = null;
			if (project == null || xamlFile == null)
				return false;

			FileName exactCompanion = FileName.Create(xamlFile + ".cs");
			FileProjectItem exactItem = project.FindFile(exactCompanion);
			if (exactItem != null) {
				codeBehindFile = exactItem.FileName;
				return true;
			}

			string xamlName = Path.GetFileName(xamlFile);
			FileProjectItem[] dependentItems = project.Items
				.OfType<FileProjectItem>()
				.Where(item => string.Equals(
					item.DependentUpon,
					xamlName,
					StringComparison.OrdinalIgnoreCase))
				.Where(item => string.Equals(
					Path.GetExtension(item.FileName),
					".cs",
					StringComparison.OrdinalIgnoreCase))
				.ToArray();
			if (dependentItems.Length != 1)
				return false;

			codeBehindFile = dependentItems[0].FileName;
			return true;
		}

		internal static bool TrySelectPrimaryCodePart(
			ITypeDefinition designedClass,
			FileName codeBehindFile,
			out IUnresolvedTypeDefinition targetPart)
		{
			targetPart = null;
			if (designedClass == null || codeBehindFile == null)
				return false;

			IUnresolvedTypeDefinition[] exactParts = designedClass.Parts
				.Where(part => part != null
					&& !part.Region.IsEmpty
					&& FileUtility.IsEqualFileName(part.Region.FileName, codeBehindFile))
				.ToArray();
			if (exactParts.Length != 1)
				return false;

			IUnresolvedTypeDefinition generatorPart = null;
			foreach (IUnresolvedTypeDefinition part in designedClass.Parts) {
				if (generatorPart == null || EntityModelContextUtils.IsBetterPart(part, generatorPart, ".cs"))
					generatorPart = part;
			}
			if (!IsSameTypePart(exactParts[0], generatorPart))
				return false;

			targetPart = exactParts[0];
			return true;
		}

		internal static bool IsSameTypePart(
			IUnresolvedTypeDefinition first,
			IUnresolvedTypeDefinition second)
		{
			return first != null && second != null
				&& FileUtility.IsEqualFileName(first.Region.FileName, second.Region.FileName)
				&& first.Region.BeginLine == second.Region.BeginLine
				&& first.Region.BeginColumn == second.Region.BeginColumn
				&& first.Region.EndLine == second.Region.EndLine
				&& first.Region.EndColumn == second.Region.EndColumn
				&& string.Equals(first.FullName, second.FullName, StringComparison.Ordinal);
		}

		internal static string GetAvailableHandlerName(
			ITypeDefinition designedClass,
			IMethod invokeMethod,
			string baseName,
			FileName codeBehindFile)
		{
			if (designedClass == null || invokeMethod == null
			    || string.IsNullOrWhiteSpace(baseName) || codeBehindFile == null)
				return null;

			string candidate = baseName;
			for (int suffix = 1; suffix < 10000; ++suffix) {
				IMethod compatible = FindCompatibleMethod(
					designedClass,
					candidate,
					invokeMethod,
					codeBehindFile);
				if (compatible != null)
					return candidate;

				bool nameInUse = designedClass.Members.Any(member =>
					string.Equals(member.Name, candidate, StringComparison.Ordinal));
				if (!nameInUse)
					return candidate;

				candidate = baseName + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
			}
			return null;
		}

		internal static IMethod FindCompatibleMethod(
			ITypeDefinition designedClass,
			string handlerName,
			IMethod invokeMethod,
			FileName codeBehindFile)
		{
			if (designedClass == null || invokeMethod == null
			    || string.IsNullOrEmpty(handlerName) || codeBehindFile == null)
				return null;
			return designedClass
				.GetMethods(
					method => string.Equals(method.Name, handlerName, StringComparison.Ordinal),
					GetMemberOptions.IgnoreInheritedMembers)
				.Where(method => IsCompatibleEventHandler(method, invokeMethod)
					&& !method.Region.IsEmpty
					&& FileUtility.IsEqualFileName(method.Region.FileName, codeBehindFile))
				.OrderBy(method => method.Region.BeginLine)
				.ThenBy(method => method.Region.BeginColumn)
				.FirstOrDefault();
		}

		internal static bool IsCompatibleEventHandler(IMethod candidate, IMethod invokeMethod)
		{
			if (candidate == null || invokeMethod == null
			    || candidate.TypeParameters.Count != 0
			    || candidate.Parameters.Count != invokeMethod.Parameters.Count
			    || !string.Equals(
				candidate.ReturnType.ReflectionName,
				invokeMethod.ReturnType.ReflectionName,
				StringComparison.Ordinal)) {
				return false;
			}

			for (int index = 0; index < invokeMethod.Parameters.Count; ++index) {
				IParameter candidateParameter = candidate.Parameters[index];
				IParameter invokeParameter = invokeMethod.Parameters[index];
				if (!string.Equals(
					candidateParameter.Type.ReflectionName,
					invokeParameter.Type.ReflectionName,
					StringComparison.Ordinal)
				    || candidateParameter.IsRef != invokeParameter.IsRef
				    || candidateParameter.IsOut != invokeParameter.IsOut) {
					return false;
				}
			}
			return true;
		}

		static IEvent FindEventDeclaration(ICompilation compilation, Type declaringType, string name)
		{
			if (compilation == null || declaringType == null || string.IsNullOrEmpty(name))
				return null;
			return compilation.FindType(declaringType)
				.GetEvents(eventDefinition => string.Equals(eventDefinition.Name, name, StringComparison.Ordinal))
				.FirstOrDefault();
		}

		static string GetAvailableDesignItemName(DesignItem item)
		{
			if (item == null || item.Context == null || item.Context.RootItem == null)
				return null;

			var names = new HashSet<string>(StringComparer.Ordinal);
			var visited = new HashSet<DesignItem>();
			CollectDesignItemNames(item.Context.RootItem, names, visited);
			string typeName = item.ComponentType != null ? item.ComponentType.Name : null;
			if (string.IsNullOrEmpty(typeName))
				return null;
			string baseName = char.ToLowerInvariant(typeName[0]) + typeName.Substring(1);
			for (int suffix = 1; suffix < 10000; ++suffix) {
				string candidate = baseName + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
				if (!names.Contains(candidate))
					return candidate;
			}
			return null;
		}

		static void CollectDesignItemNames(
			DesignItem item,
			HashSet<string> names,
			HashSet<DesignItem> visited)
		{
			if (item == null || !visited.Add(item))
				return;
			if (!string.IsNullOrEmpty(item.Name))
				names.Add(item.Name);

			foreach (DesignItemProperty property in item.AllSetProperties) {
				if (property.IsCollection) {
					foreach (DesignItem child in property.CollectionElements)
						CollectDesignItemNames(child, names, visited);
				} else {
					CollectDesignItemNames(property.Value, names, visited);
				}
			}
		}

		internal static bool IsPortableIdentifier(string value)
		{
			if (string.IsNullOrEmpty(value) || !IsIdentifierStart(value[0]))
				return false;
			for (int index = 1; index < value.Length; ++index) {
				if (!IsIdentifierPart(value[index]))
					return false;
			}
			return true;
		}

		static bool IsIdentifierStart(char value)
		{
			return value == '_' || char.IsLetter(value);
		}

		static bool IsIdentifierPart(char value)
		{
			return value == '_' || char.IsLetterOrDigit(value);
		}

		static void JumpToMethod(IMethod method)
		{
			if (method == null || method.Region.IsEmpty || string.IsNullOrEmpty(method.Region.FileName))
				return;
			DomRegion target = !method.BodyRegion.IsEmpty ? method.BodyRegion : method.Region;
			SD.FileService.JumpToFilePosition(
				FileName.Create(method.Region.FileName),
				target.BeginLine,
				target.BeginColumn);
		}

		public DesignItemProperty GetDefaultEvent(DesignItem item)
		{
			if (item == null || item.Component == null)
				return null;
			var defaultEvent = TypeDescriptor.GetAttributes(item.Component)[typeof(DefaultEventAttribute)]
				as DefaultEventAttribute;
			if (defaultEvent == null)
				return null;
			EventDescriptor eventInfo = TypeDescriptor.GetEvents(item.Component)[defaultEvent.Name];
			if (eventInfo == null)
				return null;
			DesignItemProperty property = item.Properties.GetProperty(defaultEvent.Name);
			return property != null && property.IsEvent ? property : null;
		}

		sealed class EventHandlerPlan
		{
			public DesignItemProperty EventProperty;
			public IProject Project;
			public ITypeDefinition DesignedClass;
			public IEvent EventDefinition;
			public IMethod CompatibleMethod;
			public FileName CodeBehindFile;
			public ITextEditor Editor;
			public OpenedFile OpenedFile;
			public string HandlerName;
			public string ComponentName;
			public bool SetComponentName;
			public bool SetEventHandler;

			public EventHandlerPlan CloneForRefresh(
				ITypeDefinition designedClass,
				IMethod compatibleMethod)
			{
				return new EventHandlerPlan {
					EventProperty = EventProperty,
					Project = Project,
					DesignedClass = designedClass,
					EventDefinition = EventDefinition,
					CompatibleMethod = compatibleMethod,
					CodeBehindFile = CodeBehindFile,
					Editor = Editor,
					OpenedFile = OpenedFile,
					HandlerName = HandlerName,
					ComponentName = ComponentName,
					SetComponentName = SetComponentName,
					SetEventHandler = SetEventHandler
				};
			}
		}

		internal sealed class SourceDocumentTransaction : IDisposable
		{
			readonly IDocument document;
			readonly string originalText;
			readonly Action<int> setCaretOffset;
			readonly Action<bool> setDirty;
			readonly bool originalDirty;
			readonly int originalCaretOffset;
			bool committed;

			public SourceDocumentTransaction(
				IDocument document,
				Func<int> getCaretOffset = null,
				Action<int> setCaretOffset = null,
				Func<bool> getDirty = null,
				Action<bool> setDirty = null)
			{
				if (document == null)
					throw new ArgumentNullException("document");
				this.document = document;
				this.originalText = document.Text;
				this.setCaretOffset = setCaretOffset;
				this.setDirty = setDirty;
				this.originalDirty = getDirty != null && getDirty();
				this.originalCaretOffset = getCaretOffset != null ? getCaretOffset() : 0;
			}

			public void Commit()
			{
				committed = true;
			}

			public void Dispose()
			{
				if (committed)
					return;
				try {
					if (!string.Equals(document.Text, originalText, StringComparison.Ordinal))
						document.Text = originalText;
					if (setCaretOffset != null)
						setCaretOffset(Math.Max(0, Math.Min(originalCaretOffset, document.TextLength)));
					if (setDirty != null)
						setDirty(originalDirty);
				} catch (Exception ex) {
					LoggingService.Error("The WPF designer could not restore a failed event-handler source transaction.", ex);
				}
			}
		}
	}
}
