#if LIBREWPF
using ICSharpCode.Reporting.Addin.LibreWpf;
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
	}
}
#endif
