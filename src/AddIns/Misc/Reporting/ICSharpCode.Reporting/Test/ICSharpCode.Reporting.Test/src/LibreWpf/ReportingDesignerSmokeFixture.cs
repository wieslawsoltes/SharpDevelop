#if LIBREWPF
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ICSharpCode.Reporting.Addin.DesignableItems;
using ICSharpCode.Reporting.Addin.DesignerBinding;
using ICSharpCode.Reporting.Addin.LibreWpf;
using ICSharpCode.Reporting.Addin.Views;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Workbench;
using NUnit.Framework;

namespace ICSharpCode.Reporting.Test.LibreWpf
{
	[TestFixture]
	public sealed class ReportingDesignerSmokeFixture
	{
		[Test]
		public void SourceBuiltDesignerLoadsFiltersAddsSectionsAndPaints()
		{
			LibreWpfReportingDesignerSmokeResult result = LibreWpfReportingDesignerSmoke.Run();

			Assert.That(result.ComponentCount, Is.EqualTo(3));
			Assert.That(result.RootChildCount, Is.EqualTo(1));
			Assert.That(result.VisiblePropertyCount, Is.GreaterThan(0));
			Assert.That(result.RootSizeFiltered, Is.True);
			Assert.That(result.PaintedPixelCount, Is.GreaterThan(0));
		}

		[Test]
		public void ReportLoaderFlushesAndReloadsOwnedContentWithoutRecreatingServices()
		{
			var generator = new TestDesignerGenerator {
				ReportFileContent = CreateReportXml("Saved", "utf-8")
			};
			ReportDesignerLoader loader;
			using (var initialStream = new MemoryStream(EncodeReport("Before", new UTF8Encoding(false)))) {
				loader = new ReportDesignerLoader(generator, initialStream);
			}

			using (var services = new ServiceContainer())
			using (var surface = new DesignSurface(services)) {
				surface.BeginLoad(loader);
				AssertLoaded(surface);

				var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost));
				var initialSettings = (ReportSettings)host.Container.Components["ReportSettings"];
				Assert.That(initialSettings.ReportName, Is.EqualTo("Before"));

				object serializationService = surface.GetService(typeof(ComponentSerializationService));
				Assert.That(serializationService, Is.Not.Null);

				host.CreateComponent(typeof(Component), "DirtyMarker");
				surface.Flush();
				Assert.That(generator.MergeCount, Is.EqualTo(1));
				Assert.That(ReadReportName(loader.SerializeModel()), Is.EqualTo("Saved"));

				var selection = (ISelectionService)surface.GetService(typeof(ISelectionService));
				selection.SetSelectedComponents(new object[] { initialSettings }, SelectionTypes.Replace);

				byte[] reloadedContent = EncodeReport("After", Encoding.Unicode);
				using (var reloadStream = new MemoryStream(reloadedContent)) {
					Assert.That(loader.ReloadFrom(reloadStream), Is.True);
				}

				generator.ReportFileContent = CreateReportXml("OldSurfaceFlush", "utf-8");
				host.CreateComponent(typeof(Component), "PendingDirtyMarker");
				surface.Flush();
				Assert.That(generator.MergeCount, Is.EqualTo(2));

				Application.RaiseIdle(EventArgs.Empty);

				AssertLoaded(surface);
				Assert.That(generator.MergeCount, Is.EqualTo(2), "The pending disk document was replaced by the old surface flush.");
				Assert.That(surface.GetService(typeof(ComponentSerializationService)), Is.SameAs(serializationService));

				var reloadedSettings = (ReportSettings)host.Container.Components["ReportSettings"];
				Assert.That(reloadedSettings, Is.Not.SameAs(initialSettings));
				Assert.That(reloadedSettings.ReportName, Is.EqualTo("After"));
				Assert.That(selection.PrimarySelection, Is.SameAs(reloadedSettings));
				Assert.That(ReadReportName(loader.SerializeModel()), Is.EqualTo("After"));
				using (var savedStream = new MemoryStream()) {
					loader.WriteReportContent(savedStream);
					CollectionAssert.AreEqual(reloadedContent, savedStream.ToArray());
				}
				Assert.That(Application.UseWaitCursor, Is.False);
			}
		}

		[Test]
		public void FailedReportLoadRestoresTheApplicationWaitCursor()
		{
			var generator = new TestDesignerGenerator();
			bool previousUseWaitCursor = Application.UseWaitCursor;
			Application.UseWaitCursor = true;
			try {
				using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("<invalid")))
				using (var surface = new DesignSurface()) {
					var loader = new ReportDesignerLoader(generator, stream);
					surface.BeginLoad(loader);
					Assert.That(surface.IsLoaded, Is.False);
					Assert.That(surface.LoadErrors.Count, Is.GreaterThan(0));
					Assert.That(Application.UseWaitCursor, Is.True);
				}
			} finally {
				Application.UseWaitCursor = previousUseWaitCursor;
			}
		}

		[Test]
		public void FailedAsynchronousReloadRestoresTheLastCommittedDocument()
		{
			var generator = new TestDesignerGenerator();
			byte[] initialContent = EncodeReport("Before", new UTF8Encoding(false));
			using (var initialStream = new MemoryStream(initialContent))
			using (var surface = new DesignSurface()) {
				var loader = new ReportDesignerLoader(generator, initialStream);
				int reloadFailures = 0;
				loader.ReloadFailed += delegate { reloadFailures++; };
				surface.BeginLoad(loader);
				AssertLoaded(surface);

				using (var invalidStream = new MemoryStream(Encoding.UTF8.GetBytes("<invalid"))) {
					Assert.That(loader.ReloadFrom(invalidStream), Is.True);
				}
				Application.RaiseIdle(EventArgs.Empty);

				Assert.That(surface.IsLoaded, Is.False);
				Assert.That(loader.RecoveringFailedReload, Is.True);
				Assert.That(reloadFailures, Is.EqualTo(1));
				using (var savedStream = new MemoryStream()) {
					loader.WriteReportContent(savedStream);
					CollectionAssert.AreEqual(initialContent, savedStream.ToArray());
				}

				Application.RaiseIdle(EventArgs.Empty);

				AssertLoaded(surface);
				Assert.That(loader.RecoveringFailedReload, Is.False);
				var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost));
				var settings = (ReportSettings)host.Container.Components["ReportSettings"];
				Assert.That(settings.ReportName, Is.EqualTo("Before"));
			}
		}

		static void AssertLoaded(DesignSurface surface)
		{
			Assert.That(surface.IsLoaded, Is.True,
				"Reporting design surface failed to load; errors=" + surface.LoadErrors.Count);
		}

		static string ReadReportName(System.Xml.XmlDocument document)
		{
			return document.SelectSingleNode("/ReportModel/ReportSettings/ReportSettings/ReportName").InnerText;
		}

		static byte[] EncodeReport(string reportName, Encoding encoding)
		{
			byte[] preamble = encoding.GetPreamble();
			byte[] content = encoding.GetBytes(CreateReportXml(reportName, encoding.WebName));
			var result = new byte[preamble.Length + content.Length];
			Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
			Buffer.BlockCopy(content, 0, result, preamble.Length, content.Length);
			return result;
		}

		static string CreateReportXml(string reportName, string encodingName)
		{
			return String.Format(
				System.Globalization.CultureInfo.InvariantCulture,
				"<?xml version=\"1.0\" encoding=\"{0}\"?>"
				+ "<ReportModel><ReportSettings><ReportSettings>"
				+ "<PageSize>640, 480</PageSize><ReportName>{1}</ReportName>"
				+ "</ReportSettings></ReportSettings><SectionCollection /></ReportModel>",
				encodingName,
				reportName);
		}

		sealed class TestDesignerGenerator : IDesignerGenerator
		{
			public int MergeCount { get; private set; }

			public string ReportFileContent { get; set; }

			public DesignerView ViewContent { get { return null; } }

			public CodeDomProvider CodeDomProvider { get { return null; } }

			public void Attach(DesignerView viewContent)
			{
			}

			public void Detach()
			{
			}

			public IEnumerable<OpenedFile> GetSourceFiles(out OpenedFile designerCodeFile)
			{
				designerCodeFile = null;
				return Array.Empty<OpenedFile>();
			}

			public void MergeFormChanges(CodeCompileUnit unit)
			{
				MergeCount++;
			}

			public bool InsertComponentEvent(
				IComponent component,
				EventDescriptor eventDescriptor,
				string eventMethodName,
				string body,
				out string file,
				out int position)
			{
				file = null;
				position = 0;
				return false;
			}
		}
	}
}
#endif
