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
using System.Windows.Threading;
using ICSharpCode.Core;
using ICSharpCode.FormsDesigner.Gui.OptionPanels;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.Editor;
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
			string componentName = GetComponentName(component);
			string baseName = GetEventHandlerName(componentName, e.DisplayName);
			ITypeDefinition definition = loader.GetPrimaryTypeDefinition();
			HashSet<string> compatibleMethods = new HashSet<string>(
				GetCompatibleMethods(e).Cast<string>(),
				StringComparer.Ordinal);
			return GetAvailableMethodName(definition, compatibleMethods, baseName);
		}

		static string GetAvailableMethodName(
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
			string siteName = component.Site != null ? component.Site.Name : null;
			if (string.IsNullOrEmpty(siteName))
				siteName = component.GetType().Name;
			if (GeneralOptionsPanel.GenerateVisualStudioStyleEventHandlers)
				return siteName;
			return Char.ToUpper(siteName[0]) + siteName.Substring(1);
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
			ITypeDefinition definition = loader.GetPrimaryTypeDefinition();
			ArrayList compatibleMethods = new ArrayList();
			IEvent eventDefinition = FindEvent(e);
			IMethod invokeMethod = eventDefinition != null
				? eventDefinition.ReturnType.GetDelegateInvokeMethod()
				: null;
			if (invokeMethod == null)
				return compatibleMethods;
			
			foreach (IMethod method in definition.Methods) {
				if (method.Parameters.Count == invokeMethod.Parameters.Count
				    && string.Equals(method.ReturnType.ReflectionName, invokeMethod.ReturnType.ReflectionName, StringComparison.Ordinal)) {
					bool found = true;
					for (int i = 0; i < invokeMethod.Parameters.Count; ++i) {
						IParameter invokeParameter = invokeMethod.Parameters[i];
						IParameter p = method.Parameters[i];
						if (!string.Equals(p.Type.ReflectionName, invokeParameter.Type.ReflectionName, StringComparison.Ordinal)
						    || p.IsRef != invokeParameter.IsRef
						    || p.IsOut != invokeParameter.IsOut) {
							found = false;
							break;
						}
					}
					if (found) {
						compatibleMethods.Add(method.Name);
					}
				}
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
				// TODO : does not properly update events list in properties pad!
				IEvent evt = FindEvent(edesc);
				if (evt == null) return false;
				context.ShowSourceCode();
				Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)delegate { InsertEventHandlerInternal(methodName, evt); });
				return true;
			}
			return false;
		}
		
		void InsertEventHandlerInternal(string methodName, IEvent evt)
		{
			if (TryJumpToCurrentPrimaryMethod(methodName))
				return;

			CSharpCodeGenerator generator = new CSharpCodeGenerator();
			var primary = loader.GetPrimaryTypeDefinition();
			var evtHandler = primary.GetMethods(m => m.Name == methodName, GetMemberOptions.IgnoreInheritedMembers).FirstOrDefault();
			if (evtHandler == null) {
				var insertionType = GeneralOptionsPanel.InsertTodoComment ? InsertEventHandlerBodyKind.TodoComment : InsertEventHandlerBodyKind.Nothing;
				generator.InsertEventHandler(primary, methodName, evt, true, insertionType);
			} else {
				CSharpBinding.Parser.CSharpFullParseInformation parseInfo;
				var node = evtHandler.GetDeclaration(out parseInfo) as MethodDeclaration;
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

		bool TryJumpToCurrentPrimaryMethod(string methodName)
		{
			CSharpBinding.Parser.CSharpFullParseInformation parseInfo = context.GetPrimaryFileParseInformation();
			ITypeDefinition primary = loader.GetPrimaryTypeDefinition();
			TypeDeclaration primaryDeclaration = parseInfo.SyntaxTree
				.Descendants
				.OfType<TypeDeclaration>()
				.FirstOrDefault(type => string.Equals(type.Name, primary.Name, StringComparison.Ordinal));
			MethodDeclaration declaration = primaryDeclaration != null
				? primaryDeclaration.Members
					.OfType<MethodDeclaration>()
					.FirstOrDefault(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
				: null;
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
		
		IEvent FindEvent(EventDescriptor edesc)
		{
			var compilation = context.GetCompilation();
			var type = compilation.FindType(edesc.ComponentType);
			return type.GetEvents(evt => evt.Name == edesc.Name).FirstOrDefault();
		}
	}
}
