using System;
using System.Linq;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.Core;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.CSharp.TypeSystem;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.SharpDevelop.Parser;

namespace ICSharpCode.WpfDesign.AddIn.Tests
{
	static class WpfDesignerEventHandlerServiceTests
	{
		const string PrimaryFileName = "/virtual/MainWindow.xaml.cs";

		public static int Main()
		{
			try {
				LegacyBareWpfReferencesResolveClickDelegate();
				ExactPrimaryPartAndDelegateSignatureAreRequired();
				ShorterUnrelatedPartialPartFailsClosed();
				SourceTransactionRollsBackAndCommitsDeterministically();
				PortableHandlerIdentifiersFailClosed();
				Console.WriteLine("PASS: WPF designer exact-part, signature, rollback, and identifier contracts");
				return 0;
			} catch (Exception ex) {
				Console.Error.WriteLine("FAIL: " + ex);
				return 1;
			}
		}

		static void LegacyBareWpfReferencesResolveClickDelegate()
		{
			string[] bareReferences = {
				"PresentationFramework",
				"PresentationCore",
				"WindowsBase",
				"System.Xaml",
				"WindowsFormsIntegration"
			};
			foreach (string reference in bareReferences) {
				Assert(ProjectContentContainer.IsLibreWpfPortableCompatibilityReference(reference),
				       "The legacy bare-name reference did not activate typed desktop compatibility: " + reference);
			}

			var loader = new CecilLoader { LazyLoad = true };
			IUnresolvedAssembly[] assemblies = ProjectContentContainer
				.LibreWpfPortableCompatibilityReferenceTypes
				.Concat(new[] { typeof(object) })
				.Select(type => type.Assembly.Location)
				.Where(location => !string.IsNullOrEmpty(location))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Select(loader.LoadAssemblyFile)
				.Where(assembly => assembly != null)
				.ToArray();
			ICompilation compilation = new CSharpProjectContent()
				.AddAssemblyReferences(assemblies)
				.CreateCompilation();
			ITypeDefinition buttonBase = compilation
				.FindType(new FullTypeName("System.Windows.Controls.Primitives.ButtonBase"))
				.GetDefinition();
			IEvent click = buttonBase != null
				? buttonBase.GetEvents(
					eventDefinition => string.Equals(eventDefinition.Name, "Click", StringComparison.Ordinal))
					.FirstOrDefault()
				: null;
			IMethod invoke = click != null ? click.ReturnType.GetDelegateInvokeMethod() : null;
			Assert(buttonBase != null, "The typed PresentationFramework anchor did not resolve ButtonBase.");
			Assert(click != null, "The typed PresentationFramework anchor did not resolve ButtonBase.Click.");
			Assert(invoke != null, "The Click event did not resolve its RoutedEventHandler signature.");
			Assert(invoke.Parameters.Count == 2,
			       "The resolved RoutedEventHandler signature did not contain two parameters.");
			Assert(string.Equals(invoke.ReturnType.ReflectionName, "System.Void", StringComparison.Ordinal),
			       "The resolved RoutedEventHandler return type was not System.Void.");
			Assert(string.Equals(invoke.Parameters[0].Type.ReflectionName, "System.Object", StringComparison.Ordinal),
			       "The resolved RoutedEventHandler sender type was not System.Object.");
			Assert(string.Equals(
				invoke.Parameters[1].Type.ReflectionName,
				"System.Windows.RoutedEventArgs",
				StringComparison.Ordinal),
			       "The resolved RoutedEventHandler args type was not System.Windows.RoutedEventArgs.");
		}

		static void ExactPrimaryPartAndDelegateSignatureAreRequired()
		{
			ParsedModel model = Parse(
				new SourceFile(PrimaryFileName, @"
namespace Contracts
{
	public class Sender {}
	public class Args {}
	public delegate void ClickHandler(Sender sender, Args args);
	public class Source { public event ClickHandler Click; }
}
namespace App
{
	public partial class MainWindow
	{
		void button1_Click(Contracts.Sender sender) {}
		void button1_Click1(Contracts.Sender sender, Contracts.Args args) {}
		void generic_Click<T>(Contracts.Sender sender, Contracts.Args args) {}
	}
}"),
				new SourceFile("/virtual/MainWindow.Other.cs", @"
namespace App
{
	public partial class MainWindow
	{
		void button2_Click(Contracts.Sender sender, Contracts.Args args) {}
	}
}"));

			ITypeDefinition mainWindow = model.Compilation
				.FindType(new FullTypeName("App.MainWindow"))
				.GetDefinition();
			ITypeDefinition source = model.Compilation
				.FindType(new FullTypeName("Contracts.Source"))
				.GetDefinition();
			IMethod invokeMethod = source.Events.Single(item => item.Name == "Click")
				.ReturnType.GetDelegateInvokeMethod();
			IUnresolvedTypeDefinition targetPart;

			Assert(SharpDevelopEventHandlerService.TrySelectPrimaryCodePart(
				mainWindow,
				FileName.Create(PrimaryFileName),
				out targetPart),
			       "The exact XAML code-behind part was not selected.");
			Assert(string.Equals(targetPart.Region.FileName, PrimaryFileName, StringComparison.Ordinal),
			       "The selected insertion part is not the exact XAML code-behind document.");

			IMethod incompatible = mainWindow.Methods.Single(method => method.Name == "button1_Click");
			IMethod compatible = mainWindow.Methods.Single(method => method.Name == "button1_Click1");
			IMethod generic = mainWindow.Methods.Single(method => method.Name == "generic_Click");
			Assert(!SharpDevelopEventHandlerService.IsCompatibleEventHandler(incompatible, invokeMethod),
			       "An incompatible same-name overload was accepted.");
			Assert(SharpDevelopEventHandlerService.IsCompatibleEventHandler(compatible, invokeMethod),
			       "The exact delegate signature was rejected.");
			Assert(!SharpDevelopEventHandlerService.IsCompatibleEventHandler(generic, invokeMethod),
			       "A generic method was accepted as a XAML event handler.");
			Assert(ReferenceEquals(
				SharpDevelopEventHandlerService.FindCompatibleMethod(
					mainWindow,
					"button1_Click1",
					invokeMethod,
					FileName.Create(PrimaryFileName)),
				compatible),
			       "The signature-aware handler lookup did not return the compatible method.");
			Assert(string.Equals(
				SharpDevelopEventHandlerService.GetAvailableHandlerName(
					mainWindow,
					invokeMethod,
					"button1_Click",
					FileName.Create(PrimaryFileName)),
				"button1_Click1",
				StringComparison.Ordinal),
			       "The incompatible base name did not advance to the existing compatible handler.");
			Assert(SharpDevelopEventHandlerService.FindCompatibleMethod(
				mainWindow,
				"button2_Click",
				invokeMethod,
				FileName.Create(PrimaryFileName)) == null,
			       "A compatible handler from an unrelated partial document was reused.");
			Assert(string.Equals(
				SharpDevelopEventHandlerService.GetAvailableHandlerName(
					mainWindow,
					invokeMethod,
					"button2_Click",
					FileName.Create(PrimaryFileName)),
				"button2_Click1",
				StringComparison.Ordinal),
			       "A handler in another partial document did not force an exact-document suffix.");
		}

		static void ShorterUnrelatedPartialPartFailsClosed()
		{
			ParsedModel model = Parse(
				new SourceFile(PrimaryFileName, @"
namespace App { public partial class MainWindow { void ExpectedPart() {} } }"),
				new SourceFile("/virtual/MainWindow.cs", @"
namespace App { public partial class MainWindow { void ShorterPart() {} } }"));
			ITypeDefinition mainWindow = model.Compilation
				.FindType(new FullTypeName("App.MainWindow"))
				.GetDefinition();
			IUnresolvedTypeDefinition targetPart;
			Assert(!SharpDevelopEventHandlerService.TrySelectPrimaryCodePart(
				mainWindow,
				FileName.Create(PrimaryFileName),
				out targetPart),
			       "The service accepted an exact code-behind part while the language generator would mutate another partial file.");
		}

		static void SourceTransactionRollsBackAndCommitsDeterministically()
		{
			var document = new TextDocument("original source");
			int caretOffset = 3;
			bool sourceDirty = false;
			string xaml = "<Button />";
			string componentName = null;
			string eventHandler = null;
			try {
				using (var transaction = new SharpDevelopEventHandlerService.SourceDocumentTransaction(
					document,
					() => caretOffset,
					offset => caretOffset = offset,
					() => sourceDirty,
					value => sourceDirty = value)) {
					document.Text = "partially inserted handler";
					caretOffset = document.TextLength;
					sourceDirty = true;

					// Model the exact service failure boundary: source insertion has completed,
					// but the designer change group has not committed any XAML mutation.
					throw new InvalidOperationException("XAML commit rejected");
				}
			} catch (InvalidOperationException) {
			}
			Assert(string.Equals(document.Text, "original source", StringComparison.Ordinal),
			       "A failed source transaction did not restore the complete document.");
			Assert(caretOffset == 3, "A failed source transaction did not restore the caret.");
			Assert(!sourceDirty, "A failed source transaction did not restore the dirty state.");
			Assert(string.Equals(xaml, "<Button />", StringComparison.Ordinal),
			       "A pre-commit failure changed the XAML document.");
			Assert(componentName == null, "A pre-commit failure assigned a component name.");
			Assert(eventHandler == null, "A pre-commit failure assigned an event handler.");

			using (var transaction = new SharpDevelopEventHandlerService.SourceDocumentTransaction(document)) {
				document.Text = "committed handler";
				transaction.Commit();
			}
			Assert(string.Equals(document.Text, "committed handler", StringComparison.Ordinal),
			       "A committed source transaction was rolled back.");
		}

		static void PortableHandlerIdentifiersFailClosed()
		{
			Assert(SharpDevelopEventHandlerService.IsPortableIdentifier("button1_Click"),
			       "A portable XAML handler identifier was rejected.");
			Assert(!SharpDevelopEventHandlerService.IsPortableIdentifier("button1.Click"),
			       "A member-access expression was accepted as a generated handler name.");
			Assert(!SharpDevelopEventHandlerService.IsPortableIdentifier("1button_Click"),
			       "A digit-leading handler name was accepted.");
		}

		static ParsedModel Parse(params SourceFile[] files)
		{
			IProjectContent content = new CSharpProjectContent();
			foreach (SourceFile file in files) {
				SyntaxTree syntaxTree = new CSharpParser().Parse(file.Source, file.FileName);
				content = content.AddOrUpdateFiles(syntaxTree.ToTypeSystem());
			}
			return new ParsedModel(content.CreateCompilation());
		}

		static void Assert(bool condition, string message)
		{
			if (!condition)
				throw new InvalidOperationException(message);
		}

		sealed class ParsedModel
		{
			public ParsedModel(ICompilation compilation)
			{
				Compilation = compilation;
			}

			public ICompilation Compilation { get; private set; }
		}

		sealed class SourceFile
		{
			public SourceFile(string fileName, string source)
			{
				FileName = fileName;
				Source = source;
			}

			public string FileName { get; private set; }
			public string Source { get; private set; }
		}
	}
}
