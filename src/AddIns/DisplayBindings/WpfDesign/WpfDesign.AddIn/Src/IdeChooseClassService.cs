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
using System.Reflection;

using ICSharpCode.Core;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Designer;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.WpfDesign.Designer.Services;

namespace ICSharpCode.WpfDesign.AddIn
{
	public class IdeChooseClassService : ChooseClassServiceBase
	{
		static readonly Assembly[] emptyAssemblies = new Assembly[0];

		readonly IProject project;
		readonly TypeResolutionService typeResolutionService;

		public IdeChooseClassService()
			: this(ProjectService.CurrentProject, new TypeResolutionService())
		{
		}

		internal IdeChooseClassService(IProject project, TypeResolutionService typeResolutionService)
		{
			if (typeResolutionService == null)
				throw new ArgumentNullException("typeResolutionService");
			this.project = project;
			this.typeResolutionService = typeResolutionService;
		}

		public override IEnumerable<Assembly> GetAssemblies()
		{
			if (project == null)
				return emptyAssemblies;

			ICompilation compilation = TryGetCompilation(project);
			if (compilation == null)
				return emptyAssemblies;

			Assembly projectAssembly = TryLoadAssembly(
				() => typeResolutionService.LoadAssembly(project),
				project.AssemblyName);
			var referencedAssemblies = new List<Assembly>(compilation.ReferencedAssemblies.Count);
			var references = new List<IAssembly>(compilation.ReferencedAssemblies.Count);
			foreach (IAssembly reference in compilation.ReferencedAssemblies) {
				if (reference != null)
					references.Add(reference);
			}
			references.Sort(CompareReferences);

			foreach (IAssembly reference in references) {
				Assembly assembly = TryLoadAssembly(
					() => typeResolutionService.LoadAssembly(reference),
					reference.FullAssemblyName);
				if (assembly != null)
					referencedAssemblies.Add(assembly);
			}

			return SelectAssemblies(projectAssembly, referencedAssemblies);
		}

		ICompilation TryGetCompilation(IProject selectedProject)
		{
			try {
				return SD.ParserService.GetCompilation(selectedProject);
			} catch (Exception ex) {
				LogLoadFailure(selectedProject.AssemblyName, ex);
				return null;
			}
		}

		internal static Assembly TryLoadAssembly(Func<Assembly> loadAssembly, string identity)
		{
			if (loadAssembly == null)
				return null;

			try {
				return loadAssembly();
			} catch (Exception ex) {
				LogLoadFailure(identity, ex);
				return null;
			}
		}

		internal static Assembly[] SelectAssemblies(
			Assembly projectAssembly,
			IEnumerable<Assembly> referencedAssemblies)
		{
			var result = new List<Assembly>();
			var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			TryAddAssembly(result, identities, projectAssembly);

			if (referencedAssemblies != null) {
				var references = new List<Assembly>();
				foreach (Assembly assembly in referencedAssemblies) {
					if (IsRuntimeAssembly(assembly))
						references.Add(assembly);
				}
				references.Sort(CompareAssemblies);
				foreach (Assembly assembly in references)
					TryAddAssembly(result, identities, assembly);
			}

			return result.ToArray();
		}

		static bool TryAddAssembly(
			ICollection<Assembly> assemblies,
			ISet<string> identities,
			Assembly assembly)
		{
			if (!IsRuntimeAssembly(assembly))
				return false;

			string identity = GetAssemblyIdentity(assembly);
			if (string.IsNullOrEmpty(identity) || !identities.Add(identity))
				return false;

			assemblies.Add(assembly);
			return true;
		}

		static bool IsRuntimeAssembly(Assembly assembly)
		{
			if (assembly == null)
				return false;
			try {
				return !assembly.IsDynamic;
			} catch (Exception ex) {
				LogLoadFailure(null, ex);
				return false;
			}
		}

		static int CompareReferences(IAssembly left, IAssembly right)
		{
			int nameResult = StringComparer.OrdinalIgnoreCase.Compare(
				left.AssemblyName,
				right.AssemblyName);
			if (nameResult != 0)
				return nameResult;
			return StringComparer.Ordinal.Compare(left.FullAssemblyName, right.FullAssemblyName);
		}

		static int CompareAssemblies(Assembly left, Assembly right)
		{
			return StringComparer.Ordinal.Compare(
				GetAssemblyIdentity(left),
				GetAssemblyIdentity(right));
		}

		static string GetAssemblyIdentity(Assembly assembly)
		{
			try {
				return assembly.FullName ?? assembly.GetName().Name;
			} catch (Exception ex) {
				LogLoadFailure(null, ex);
				return null;
			}
		}

		static void LogLoadFailure(string identity, Exception exception)
		{
			try {
				LoggingService.Warn(
					"The WPF designer could not load a class-selection assembly"
					+ (string.IsNullOrEmpty(identity) ? "." : " '" + identity + "'."),
					exception);
			} catch {
				// Assembly discovery must fail closed even before IDE logging is initialized.
			}
		}
	}
}
