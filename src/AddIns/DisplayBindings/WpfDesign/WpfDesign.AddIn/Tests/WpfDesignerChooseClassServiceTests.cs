using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using ICSharpCode.WpfDesign.Designer.Services;

namespace ICSharpCode.WpfDesign.AddIn.Tests
{
	public sealed class DesignerDataContextCandidate
	{
		public DesignerDataContextCandidate()
		{
		}
	}

	static class WpfDesignerChooseClassServiceTests
	{
		public static int Main()
		{
			try {
				ProjectAssemblyIsFirstAndReferencesAreDeterministic();
				AssemblyLoadFailuresAndDynamicAssembliesFailClosed();
				ProjectDataContextClassIsAvailableToTheChooser();
				Console.WriteLine("PASS: WPF designer typed project/reference class-selection contracts");
				return 0;
			} catch (Exception ex) {
				Console.Error.WriteLine("FAIL: " + ex);
				return 1;
			}
		}

		static void ProjectAssemblyIsFirstAndReferencesAreDeterministic()
		{
			Assembly projectAssembly = typeof(DesignerDataContextCandidate).Assembly;
			Assembly coreAssembly = typeof(object).Assembly;
			Assembly linqAssembly = typeof(Enumerable).Assembly;
			Assembly[] result = IdeChooseClassService.SelectAssemblies(
				projectAssembly,
				new[] {
					linqAssembly,
					projectAssembly,
					coreAssembly,
					linqAssembly,
					null
				});

			Assert(ReferenceEquals(result[0], projectAssembly),
			       "The project output assembly was not selected first.");
			Assert(result.Count(assembly => ReferenceEquals(assembly, projectAssembly)) == 1,
			       "The project assembly was duplicated through its references.");
			Assert(result.Distinct(new AssemblyIdentityComparer()).Count() == result.Length,
			       "A referenced assembly identity was returned more than once.");

			string[] actualReferenceIdentities = result
				.Skip(1)
				.Select(assembly => assembly.FullName)
				.ToArray();
			string[] expectedReferenceIdentities = actualReferenceIdentities
				.OrderBy(identity => identity, StringComparer.Ordinal)
				.ToArray();
			Assert(actualReferenceIdentities.SequenceEqual(expectedReferenceIdentities),
			       "Referenced runtime assemblies were not ordered deterministically.");
		}

		static void AssemblyLoadFailuresAndDynamicAssembliesFailClosed()
		{
			Assembly failed = IdeChooseClassService.TryLoadAssembly(
				() => throw new BadImageFormatException("invalid designer reference"),
				"Invalid.Reference");
			Assert(failed == null, "A throwing assembly candidate did not fail closed.");

			Assembly dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(
				new AssemblyName("Dynamic.Designer.Reference"),
				AssemblyBuilderAccess.Run);
			Assembly[] result = IdeChooseClassService.SelectAssemblies(
				typeof(DesignerDataContextCandidate).Assembly,
				new[] { dynamicAssembly });
			Assert(result.Length == 1,
			       "A dynamic runtime assembly was exposed to class selection.");
		}

		static void ProjectDataContextClassIsAvailableToTheChooser()
		{
			Assembly projectAssembly = typeof(DesignerDataContextCandidate).Assembly;
			Assembly[] assemblies = IdeChooseClassService.SelectAssemblies(
				projectAssembly,
				Array.Empty<Assembly>());
			var chooser = new ChooseClass(assemblies);
			Type[] classes = chooser.Classes.Cast<object>().OfType<Type>().ToArray();

			Assert(classes.Contains(typeof(DesignerDataContextCandidate)),
			       "The project's exported DataContext candidate is absent from the chooser.");
			Assert(classes.Count(type => type == typeof(DesignerDataContextCandidate)) == 1,
			       "The project's DataContext candidate was duplicated.");
		}

		static void Assert(bool condition, string message)
		{
			if (!condition)
				throw new InvalidOperationException(message);
		}

		sealed class AssemblyIdentityComparer : IEqualityComparer<Assembly>
		{
			public bool Equals(Assembly left, Assembly right)
			{
				if (ReferenceEquals(left, right))
					return true;
				if (left == null || right == null)
					return false;
				return string.Equals(left.FullName, right.FullName, StringComparison.OrdinalIgnoreCase);
			}

			public int GetHashCode(Assembly assembly)
			{
				return StringComparer.OrdinalIgnoreCase.GetHashCode(assembly.FullName);
			}
		}
	}
}
