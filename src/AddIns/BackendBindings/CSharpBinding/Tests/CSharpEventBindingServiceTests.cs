using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CSharpBinding.FormsDesigner;
using CSharpBinding.Parser;
using CSharpBinding.Refactoring;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.CSharp.TypeSystem;
using ICSharpCode.NRefactory.Editor;
using ICSharpCode.NRefactory.TypeSystem;

namespace CSharpBinding.Tests
{
	static class CSharpEventBindingServiceTests
	{
		const string PrimaryFileName = "/virtual/Forms/MainForm.cs";

		public static int Main()
		{
			try {
				DesignerReloadTracksDocumentVersion();
				ExactPrimaryDeclarationAndCompatibleOverloadAreSelected();
				LivePrimaryEditorDocumentPrecedesSourceStorage();
				CurrentDesignerBodyRegionRejectsStaleSemanticLocation();
				IncompatibleSameNameForcesUniqueSuffix();
				UnsitedComponentsFailClosed();
				DuplicatePendingRequestsAreCoalesced();
				Console.WriteLine("PASS: CSharpEventBindingService exact type, signature, site, and scheduling contracts");
				return 0;
			} catch (Exception ex) {
				Console.Error.WriteLine("FAIL: " + ex);
				return 1;
			}
		}

		static void DesignerReloadTracksDocumentVersion()
		{
			var versions = new TextSourceVersionProvider();
			ITextSourceVersion loadedVersion = versions.CurrentVersion;
			Assert(!CSharpDesignerLoader.IsDesignerDocumentVersionChanged(
				loadedVersion,
				loadedVersion),
			       "An unchanged designer document requested a reload.");

			versions.AppendChange(new TextChangeEventArgs(0, string.Empty, " "));
			ITextSourceVersion changedVersion = versions.CurrentVersion;
			Assert(CSharpDesignerLoader.IsDesignerDocumentVersionChanged(
				changedVersion,
				loadedVersion),
			       "A changed designer document did not request a reload.");
			Assert(CSharpDesignerLoader.IsDesignerDocumentVersionChanged(
				loadedVersion,
				null),
			       "A designer document without a loaded checkpoint did not request a reload.");
		}

		static void LivePrimaryEditorDocumentPrecedesSourceStorage()
		{
			var liveDocument = new ICSharpCode.NRefactory.Editor.ReadOnlyDocument("live editor");
			var sourceStorageDocument = new ICSharpCode.NRefactory.Editor.ReadOnlyDocument("stale source storage");
			Assert(ReferenceEquals(
				CSharpFormsDesignerLoaderContext.SelectCurrentPrimaryDocument(liveDocument, sourceStorageDocument),
				liveDocument),
			       "The designer loader context did not prefer the live primary editor document.");
			Assert(ReferenceEquals(
				CSharpFormsDesignerLoaderContext.SelectCurrentPrimaryDocument(null, sourceStorageDocument),
				sourceStorageDocument),
			       "The designer loader context did not retain its source-storage fallback.");
		}

		static void CurrentDesignerBodyRegionRejectsStaleSemanticLocation()
		{
			const string designerFileName = "/virtual/Forms/MainForm.Designer.cs";
			const string staleSource = @"
namespace Right.Namespace
{
	public partial class MainForm
	{



















		void InitializeComponent()
		{
		}
	}
}";
			const string currentSource = @"
namespace Wrong.Namespace
{
	public partial class MainForm
	{
		void InitializeComponent() {}
	}
}
namespace Right.Namespace
{
	public partial class MainForm
	{
		void InitializeComponent()
		{
		}
	}
}";

			ParsedModel staleModel = Parse(staleSource, designerFileName);
			ITypeDefinition staleForm = Resolve(
				staleModel.UnresolvedFile.TopLevelTypeDefinitions.Single(type => type.FullName == "Right.Namespace.MainForm"),
				staleModel.Compilation);
			IMethod staleInitialize = staleForm.Methods.Single(method => method.Name == "InitializeComponent");
			ParsedModel currentModel = Parse(currentSource, designerFileName);
			IUnresolvedTypeDefinition currentPart = currentModel.UnresolvedFile.TopLevelTypeDefinitions
				.Single(type => type.FullName == "Right.Namespace.MainForm");
			IUnresolvedMethod currentInitialize = currentPart.Members
				.OfType<IUnresolvedMethod>()
				.Single(method => method.Name == "InitializeComponent");

			DomRegion currentRegion = CSharpDesignerGenerator.FindCurrentInitializeComponentsBodyRegion(
				currentModel.ParseInformation,
				staleForm);
			Assert(!currentRegion.IsEmpty, "The current InitializeComponent body was not found.");
			Assert(currentRegion == currentInitialize.BodyRegion,
			       "The exact current type declaration was not selected from the live designer document.");
			Assert(currentRegion != staleInitialize.BodyRegion,
			       "The stale compilation method region was reused for the current designer document.");
			Assert(currentRegion.EndLine <= currentSource.Split('\n').Length,
			       "The selected method body lies outside the current designer document.");
		}

		static void ExactPrimaryDeclarationAndCompatibleOverloadAreSelected()
		{
			const string source = @"
namespace Contracts
{
	public class Sender {}
	public class Args {}
	public delegate void ClickHandler(Sender sender, Args args);
	public delegate void AlternateClickHandler(Sender sender);
	public class Source
	{
		public event ClickHandler Click;
		public event AlternateClickHandler AlternateClick;
	}
}

namespace Wrong.Namespace
{
	public partial class MainForm
	{
		void EventButtonClick(Contracts.Sender sender, Contracts.Args args) {}
	}
}

namespace Right.Namespace
{
	public partial class MainForm
	{
		#region Event handlers
		void EventButtonClick(Contracts.Sender sender) {}
		void EventButtonClick<T>(Contracts.Sender sender, Contracts.Args args) {}
		void EventButtonClick(Contracts.Sender sender, Contracts.Args args) {}
		#endregion
	}
}";

			ParsedModel model = Parse(source, PrimaryFileName);
			IUnresolvedTypeDefinition primaryPart = model.UnresolvedFile.TopLevelTypeDefinitions
				.Single(type => string.Equals(type.FullName, "Right.Namespace.MainForm", StringComparison.Ordinal));
			IUnresolvedTypeDefinition wrongNamespacePart = model.UnresolvedFile.TopLevelTypeDefinitions
				.Single(type => string.Equals(type.FullName, "Wrong.Namespace.MainForm", StringComparison.Ordinal));
			ITypeDefinition primary = Resolve(primaryPart, model.Compilation);
			Assert(CSharpCodeGenerator.IsMatchingTypePart(primary, primaryPart),
			       "The loader's exact primary type part was rejected.");
			Assert(!CSharpCodeGenerator.IsMatchingTypePart(primary, wrongNamespacePart),
			       "A same-named type part from the wrong namespace was accepted for insertion.");
			ParsedModel stalePrimaryModel = Parse(Environment.NewLine + source, PrimaryFileName);
			IUnresolvedTypeDefinition stalePrimaryPart = stalePrimaryModel.UnresolvedFile.TopLevelTypeDefinitions
				.Single(type => string.Equals(type.FullName, "Right.Namespace.MainForm", StringComparison.Ordinal));
			Assert(!CSharpCodeGenerator.IsMatchingTypePart(primary, stalePrimaryPart),
			       "A stale same-file primary type region was accepted for insertion.");
			ITypeDefinition sourceType = model.Compilation.FindType(new FullTypeName("Contracts.Source")).GetDefinition();
			IEvent clickEvent = sourceType.Events.Single(item => item.Name == "Click");
			IMethod invokeMethod = clickEvent.ReturnType.GetDelegateInvokeMethod();
			IMethod compatible = primary.Methods.Single(method =>
				method.Name == "EventButtonClick" && method.Parameters.Count == 2 && method.TypeParameters.Count == 0);
			IMethod incompatible = primary.Methods.Single(method =>
				method.Name == "EventButtonClick" && method.Parameters.Count == 1);
			IMethod generic = primary.Methods.Single(method =>
				method.Name == "EventButtonClick" && method.TypeParameters.Count == 1);

			Assert(CSharpEventBindingService.IsCompatibleEventHandler(compatible, invokeMethod),
			       "The exact event delegate signature was not accepted.");
			Assert(!CSharpEventBindingService.IsCompatibleEventHandler(incompatible, invokeMethod),
			       "An incompatible parameter count was accepted.");
			Assert(!CSharpEventBindingService.IsCompatibleEventHandler(generic, invokeMethod),
			       "A generic method that cannot bind as the event method group was accepted.");

			MethodDeclaration declaration = CSharpEventBindingService.FindCompatiblePrimaryMethodDeclaration(
				model.ParseInformation,
				primaryPart,
				model.Compilation,
				"EventButtonClick",
				invokeMethod);
			Assert(declaration != null, "No compatible declaration was found in the primary type part.");
			Assert(declaration.Parameters.Count == 2 && declaration.TypeParameters.Count == 0,
			       "The current-document lookup selected an incompatible same-name overload.");
			NamespaceDeclaration declarationNamespace = declaration.Ancestors
				.OfType<NamespaceDeclaration>()
				.FirstOrDefault();
			Assert(declarationNamespace != null
			       && string.Equals(declarationNamespace.Name, "Right.Namespace", StringComparison.Ordinal),
			       "The lookup selected the same-named type from the wrong namespace.");

			ParsedModel wrongFile = Parse(source, "/virtual/Forms/OtherForm.cs");
			Assert(CSharpEventBindingService.FindCompatiblePrimaryMethodDeclaration(
				wrongFile.ParseInformation,
				primaryPart,
				wrongFile.Compilation,
				"EventButtonClick",
				invokeMethod) == null,
			       "A declaration from a different primary file was accepted.");

			var compatibleNames = new HashSet<string>(StringComparer.Ordinal) { "EventButtonClick" };
			Assert(string.Equals(
				CSharpEventBindingService.GetAvailableMethodName(primary, compatibleNames, "EventButtonClick"),
				"EventButtonClick",
				StringComparison.Ordinal),
			       "A compatible existing handler was not reused.");
		}

		static void IncompatibleSameNameForcesUniqueSuffix()
		{
			const string source = @"
namespace Contracts
{
	public class Sender {}
	public class Args {}
}

namespace Right.Namespace
{
	public class MainForm
	{
		void EventButtonClick(Contracts.Sender sender) {}
	}
}";
			ParsedModel model = Parse(source, PrimaryFileName);
			ITypeDefinition primary = Resolve(
				model.UnresolvedFile.TopLevelTypeDefinitions.Single(type => type.FullName == "Right.Namespace.MainForm"),
				model.Compilation);
			string name = CSharpEventBindingService.GetAvailableMethodName(
				primary,
				new HashSet<string>(StringComparer.Ordinal),
				"EventButtonClick");
			Assert(string.Equals(name, "EventButtonClick1", StringComparison.Ordinal),
			       "An incompatible same-name method was reused instead of allocating a unique name.");
		}

		static void UnsitedComponentsFailClosed()
		{
			using (var component = new Component()) {
				AssertThrows<InvalidOperationException>(
					() => CSharpEventBindingService.GetSitedComponentName(component),
					"An unsited component fell back to its runtime type name.");
			}

			using (var container = new Container())
			using (var component = new Component()) {
				container.Add(component, "eventButton");
				Assert(string.Equals(
					CSharpEventBindingService.GetSitedComponentName(component),
					"eventButton",
					StringComparison.Ordinal),
				       "The design-time site name was not preserved.");
			}
		}

		static void DuplicatePendingRequestsAreCoalesced()
		{
			const string source = @"
namespace Contracts
{
	public class Sender {}
	public class Args {}
	public delegate void ClickHandler(Sender sender, Args args);
	public delegate void AlternateClickHandler(Sender sender);
	public class Source
	{
		public event ClickHandler Click;
		public event AlternateClickHandler AlternateClick;
	}
}";
			ParsedModel model = Parse(source, PrimaryFileName);
			ITypeDefinition sourceType = model.Compilation.FindType(new FullTypeName("Contracts.Source")).GetDefinition();
			IMethod clickInvoke = sourceType.Events.Single(item => item.Name == "Click")
				.ReturnType.GetDelegateInvokeMethod();
			IMethod alternateInvoke = sourceType.Events.Single(item => item.Name == "AlternateClick")
				.ReturnType.GetDelegateInvokeMethod();
			var requests = new CSharpEventBindingService.PendingEventHandlerRequests();
			Assert(requests.TryBegin("EventButtonClick", clickInvoke), "The first ShowCode request was rejected.");
			Assert(!requests.TryBegin("EventButtonClick", clickInvoke), "A duplicate pending ShowCode request was accepted.");
			Assert(requests.TryBegin("EventButtonClick", alternateInvoke),
			       "A same-name handler with a different delegate signature was incorrectly coalesced.");
			Assert(requests.TryBegin("OtherEvent", clickInvoke), "An independent handler request was incorrectly coalesced.");
			requests.Complete("EventButtonClick", clickInvoke);
			Assert(requests.TryBegin("EventButtonClick", clickInvoke), "A completed handler request could not be scheduled again.");
		}

		static ParsedModel Parse(string source, string fileName)
		{
			SyntaxTree syntaxTree = new CSharpParser().Parse(source, fileName);
			CSharpUnresolvedFile unresolvedFile = syntaxTree.ToTypeSystem();
			ICompilation compilation = new CSharpProjectContent()
				.AddOrUpdateFiles(unresolvedFile)
				.CreateCompilation();
			return new ParsedModel(
				unresolvedFile,
				compilation,
				new CSharpFullParseInformation(unresolvedFile, null, syntaxTree));
		}

		static ITypeDefinition Resolve(IUnresolvedTypeDefinition type, ICompilation compilation)
		{
			return type.Resolve(new SimpleTypeResolveContext(compilation.MainAssembly)).GetDefinition();
		}

		static void Assert(bool condition, string message)
		{
			if (!condition)
				throw new InvalidOperationException(message);
		}

		static void AssertThrows<T>(Action action, string message) where T : Exception
		{
			try {
				action();
			} catch (T) {
				return;
			}
			throw new InvalidOperationException(message);
		}

		sealed class ParsedModel
		{
			public ParsedModel(
				CSharpUnresolvedFile unresolvedFile,
				ICompilation compilation,
				CSharpFullParseInformation parseInformation)
			{
				UnresolvedFile = unresolvedFile;
				Compilation = compilation;
				ParseInformation = parseInformation;
			}

			public CSharpUnresolvedFile UnresolvedFile { get; private set; }
			public ICompilation Compilation { get; private set; }
			public CSharpFullParseInformation ParseInformation { get; private set; }
		}
	}
}
