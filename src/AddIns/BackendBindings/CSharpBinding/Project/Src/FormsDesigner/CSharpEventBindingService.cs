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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using ICSharpCode.Core;
using ICSharpCode.FormsDesigner.Gui.OptionPanels;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.Editor;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Refactoring;
using CSharpBinding.Refactoring;

namespace CSharpBinding.FormsDesigner
{
	public class CSharpEventBindingService : System.ComponentModel.Design.EventBindingService, ICSharpCode.FormsDesigner.IFormsDesignerEventBindingService
	{
		readonly CSharpDesignerLoader loader;
		readonly ICSharpDesignerLoaderContext context;
		readonly PendingEventHandlerRequests pendingRequests = new PendingEventHandlerRequests();
		
		public CSharpEventBindingService(ICSharpDesignerLoaderContext context, IServiceProvider provider, CSharpDesignerLoader loader) : base(provider)
		{
			this.loader = loader;
			if (context == null)
				throw new ArgumentNullException("context");
			if (loader == null)
				throw new ArgumentNullException("loader");
			this.context = context;
			this.loader = loader;
		}

		protected override string CreateUniqueMethodName(IComponent component, EventDescriptor e)
		{
			if (e == null)
				throw new ArgumentNullException("e");
			string componentName = GetComponentName(component);
			string baseName = GetEventHandlerName(componentName, e.DisplayName);
			ITypeDefinition definition;
			IUnresolvedTypeDefinition primaryPart;
			CSharpBinding.Parser.CSharpFullParseInformation parseInfo;
			ICompilation compilation;
			if (!loader.TryGetCurrentPrimaryType(out definition, out primaryPart, out parseInfo, out compilation))
				throw new InvalidOperationException("The current primary designer type is unavailable.");
			HashSet<string> compatibleMethods = new HashSet<string>(
				GetCompatibleMethods(e).Cast<string>(),
				StringComparer.Ordinal);
			return GetAvailableMethodName(definition, compatibleMethods, baseName);
		}

		internal static string GetAvailableMethodName(
			ITypeDefinition definition,
			HashSet<string> compatibleMethods,
			string baseName)
		{
			string candidate = baseName;
			for (int suffix = 1; ; suffix++) {
				bool nameInUse = definition.Members.Any(member =>
					string.Equals(member.Name, candidate, StringComparison.Ordinal));
				if (!nameInUse || compatibleMethods.Contains(candidate))
					return candidate;
				candidate = baseName + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
			}
		}
		
		string GetComponentName(IComponent component)
		{
			string siteName = GetSitedComponentName(component);
			if (GeneralOptionsPanel.GenerateVisualStudioStyleEventHandlers)
				return siteName;
			return Char.ToUpper(siteName[0]) + siteName.Substring(1);
		}

		internal static string GetSitedComponentName(IComponent component)
		{
			if (component == null)
				throw new ArgumentNullException("component");
			string siteName = component.Site != null ? component.Site.Name : null;
			if (string.IsNullOrWhiteSpace(siteName)) {
				throw new InvalidOperationException(
					"Event handlers can be generated only for components with a non-empty design-time site name.");
			}
			return siteName;
		}
		
		string GetEventHandlerName(string componentName, string eventName)
		{
			string eventHandlerNameFormat = GetEventHandlerNameFormat();
			return String.Format(eventHandlerNameFormat, componentName, eventName);
		}
		
		string GetEventHandlerNameFormat()
		{
			if (GeneralOptionsPanel.GenerateVisualStudioStyleEventHandlers) {
				return "{0}_{1}";
			}
			return "{0}{1}";
		}

		protected override ICollection GetCompatibleMethods(EventDescriptor e)
		{
			ArrayList compatibleMethods = new ArrayList();
			ITypeDefinition definition;
			IUnresolvedTypeDefinition primaryPart;
			CSharpBinding.Parser.CSharpFullParseInformation parseInfo;
			ICompilation compilation;
			if (!loader.TryGetCurrentPrimaryType(out definition, out primaryPart, out parseInfo, out compilation))
				return compatibleMethods;
			IEvent eventDefinition = FindEvent(e);
			IMethod invokeMethod = eventDefinition != null
				? eventDefinition.ReturnType.GetDelegateInvokeMethod()
				: null;
			if (invokeMethod == null)
				return compatibleMethods;
			
			foreach (IMethod method in definition.Methods) {
				if (IsCompatibleEventHandler(method, invokeMethod))
					compatibleMethods.Add(method.Name);
			}
			
			return compatibleMethods;
		}
		
		protected override bool ShowCode()
		{
			if (context != null) {
				context.ShowSourceCode();
				return true;
			}
			return false;
		}

		protected override bool ShowCode(int lineNumber)
		{
			if (context != null) {
				context.ShowSourceCode(lineNumber);
				return true;
			}
			return false;
		}

		protected override bool ShowCode(IComponent component, EventDescriptor edesc, string methodName)
		{
			// There were reports of an ArgumentNullException caused by edesc==null.
			// Looking at the .NET code calling this method, this can happen when there are two calls to ShowCode() before the Application.Idle
			// event gets raised. In that case, ShowCode() already was called for the second set of arguments, and we can safely ignore
			// the call with edesc==null.
			if (context != null && edesc != null) {
				if (string.IsNullOrEmpty(methodName))
					return false;
				// TODO : does not properly update events list in properties pad!
				IEvent evt = FindEvent(edesc);
				if (evt == null)
					return false;
				IMethod invokeMethod = evt.ReturnType.GetDelegateInvokeMethod();
				if (invokeMethod == null)
					return false;
				context.ShowSourceCode();
				if (!pendingRequests.TryBegin(methodName, invokeMethod))
					return true;
				try {
					Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)delegate {
						try {
							InsertEventHandlerInternal(methodName, evt, invokeMethod);
						} finally {
							pendingRequests.Complete(methodName, invokeMethod);
						}
					});
				} catch {
					pendingRequests.Complete(methodName, invokeMethod);
					throw;
				}
				return true;
			}
			return false;
		}
		
		void InsertEventHandlerInternal(string methodName, IEvent evt, IMethod invokeMethod)
		{
			ITypeDefinition primary;
			IUnresolvedTypeDefinition primaryPart;
			CSharpBinding.Parser.CSharpFullParseInformation parseInfo;
			ICompilation compilation;
			if (!loader.TryGetCurrentPrimaryType(out primary, out primaryPart, out parseInfo, out compilation))
				return;

			if (TryJumpToCurrentPrimaryMethod(methodName, invokeMethod, parseInfo, primaryPart, compilation))
				return;

			CSharpCodeGenerator generator = new CSharpCodeGenerator();
			var evtHandler = primary
				.GetMethods(m => m.Name == methodName, GetMemberOptions.IgnoreInheritedMembers)
				.FirstOrDefault(method => IsCompatibleEventHandler(method, invokeMethod));
			if (evtHandler == null) {
				var insertionType = GeneralOptionsPanel.InsertTodoComment ? InsertEventHandlerBodyKind.TodoComment : InsertEventHandlerBodyKind.Nothing;
				generator.InsertEventHandler(primary, primaryPart, methodName, evt, true, insertionType);
			} else {
				CSharpBinding.Parser.CSharpFullParseInformation declarationParseInfo;
				var node = evtHandler.GetDeclaration(out declarationParseInfo) as MethodDeclaration;
				var fileName = new FileName(evtHandler.Region.FileName);
				var fileContentFinder = new ParseableFileContentFinder();
				
				if (node != null && !node.Body.IsNull) {
					var location = node.Body.FirstChild.StartLocation;
					var firstStatement = node.Body.Children.OfType<Statement>().FirstOrDefault();
					
					if (firstStatement == null) {
						var fileContent = fileContentFinder.Create(fileName);
						var document = new ReadOnlyDocument(fileContent);
						var offset = document.GetOffset(new TextLocation(location.Line + 1, 1));
						var length = DocumentUtilities.GetWhitespaceAfter(fileContent, offset).Length;
						location = new TextLocation(location.Line + 1, length + 1);
					} else {
						location = firstStatement.StartLocation;
					}
					SD.FileService.JumpToFilePosition(fileName, location.Line, location.Column);
				}
			}
		}

		bool TryJumpToCurrentPrimaryMethod(
			string methodName,
			IMethod invokeMethod,
			CSharpBinding.Parser.CSharpFullParseInformation parseInfo,
			IUnresolvedTypeDefinition primaryPart,
			ICompilation compilation)
		{
			MethodDeclaration declaration = FindCompatiblePrimaryMethodDeclaration(
				parseInfo,
				primaryPart,
				compilation,
				methodName,
				invokeMethod);
			if (declaration == null)
				return false;

			TextLocation location = declaration.Body != null && !declaration.Body.IsNull
				? declaration.Body.StartLocation
				: declaration.StartLocation;
			SD.FileService.JumpToFilePosition(
				new FileName(parseInfo.UnresolvedFile.FileName),
				location.Line,
				location.Column);
			return true;
		}

		internal static MethodDeclaration FindCompatiblePrimaryMethodDeclaration(
			CSharpBinding.Parser.CSharpFullParseInformation parseInfo,
			IUnresolvedTypeDefinition primaryPart,
			ICompilation compilation,
			string methodName,
			IMethod invokeMethod)
		{
			if (parseInfo == null || parseInfo.UnresolvedFile == null || parseInfo.SyntaxTree == null
			    || primaryPart == null || compilation == null || invokeMethod == null
			    || string.IsNullOrEmpty(methodName)) {
				return null;
			}

			FileName parsedFileName = FileName.Create(parseInfo.UnresolvedFile.FileName);
			FileName primaryFileName = FileName.Create(primaryPart.Region.FileName);
			if (parsedFileName == null || primaryFileName == null || !parsedFileName.Equals(primaryFileName))
				return null;

			TypeDeclaration primaryDeclaration = parseInfo.SyntaxTree.GetNodeAt<TypeDeclaration>(primaryPart.Region.Begin);
			if (primaryDeclaration == null
			    || primaryDeclaration.EndLocation.Line != primaryPart.Region.EndLine
			    || primaryDeclaration.EndLocation.Column != primaryPart.Region.EndColumn
			    || !string.Equals(primaryDeclaration.Name, primaryPart.Name, StringComparison.Ordinal)) {
				return null;
			}

			var resolver = parseInfo.GetResolver(compilation);
			var primaryResolveResult = resolver.Resolve(primaryDeclaration) as TypeResolveResult;
			ITypeDefinition resolvedPrimary = primaryResolveResult != null
				? primaryResolveResult.Type.GetDefinition()
				: null;
			if (resolvedPrimary == null
			    || !string.Equals(resolvedPrimary.FullName, primaryPart.FullName, StringComparison.Ordinal)) {
				return null;
			}
			foreach (MethodDeclaration declaration in primaryDeclaration.Members.OfType<MethodDeclaration>()) {
				if (!string.Equals(declaration.Name, methodName, StringComparison.Ordinal))
					continue;
				var result = resolver.Resolve(declaration) as MemberResolveResult;
				var candidate = result != null ? result.Member as IMethod : null;
				if (IsCompatibleEventHandler(candidate, invokeMethod))
					return declaration;
			}
			return null;
		}

		internal static bool IsCompatibleEventHandler(IMethod candidate, IMethod invokeMethod)
		{
			if (candidate == null || invokeMethod == null
			    || candidate.TypeParameters.Count != 0
			    || candidate.Parameters.Count != invokeMethod.Parameters.Count
			    || !string.Equals(candidate.ReturnType.ReflectionName, invokeMethod.ReturnType.ReflectionName, StringComparison.Ordinal)) {
				return false;
			}

			for (int i = 0; i < invokeMethod.Parameters.Count; ++i) {
				IParameter candidateParameter = candidate.Parameters[i];
				IParameter invokeParameter = invokeMethod.Parameters[i];
				if (!string.Equals(candidateParameter.Type.ReflectionName, invokeParameter.Type.ReflectionName, StringComparison.Ordinal)
				    || candidateParameter.IsRef != invokeParameter.IsRef
				    || candidateParameter.IsOut != invokeParameter.IsOut) {
					return false;
				}
			}
			return true;
		}
		
		IEvent FindEvent(EventDescriptor edesc)
		{
			var compilation = context.GetCompilation();
			var type = compilation.FindType(edesc.ComponentType);
			return type.GetEvents(evt => evt.Name == edesc.Name).FirstOrDefault();
		}

		internal sealed class PendingEventHandlerRequests
		{
			readonly HashSet<string> requestKeys = new HashSet<string>(StringComparer.Ordinal);

			public bool TryBegin(string methodName, IMethod invokeMethod)
			{
				string key = CreateRequestKey(methodName, invokeMethod);
				if (key == null)
					return false;
				lock (requestKeys) {
					return requestKeys.Add(key);
				}
			}

			public void Complete(string methodName, IMethod invokeMethod)
			{
				string key = CreateRequestKey(methodName, invokeMethod);
				if (key == null)
					return;
				lock (requestKeys) {
					requestKeys.Remove(key);
				}
			}

			static string CreateRequestKey(string methodName, IMethod invokeMethod)
			{
				if (string.IsNullOrEmpty(methodName) || invokeMethod == null)
					return null;

				var builder = new StringBuilder(methodName.Length + 64);
				builder.Append(methodName);
				builder.Append('|');
				builder.Append(invokeMethod.ReturnType.ReflectionName);
				foreach (IParameter parameter in invokeMethod.Parameters) {
					builder.Append('|');
					builder.Append(parameter.IsOut ? 'o' : parameter.IsRef ? 'r' : 'v');
					builder.Append(':');
					builder.Append(parameter.Type.ReflectionName);
				}
				return builder.ToString();
			}
		}
	}
}
