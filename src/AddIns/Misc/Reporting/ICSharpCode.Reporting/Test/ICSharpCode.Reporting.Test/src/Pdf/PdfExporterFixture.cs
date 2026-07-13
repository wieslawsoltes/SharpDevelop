// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
// of the Software, and to permit persons to whom the Software is furnished to do so.

using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;

using ICSharpCode.Reporting.BaseClasses;
using ICSharpCode.Reporting.PageBuilder.ExportColumns;
using ICSharpCode.Reporting.Pdf;
using NUnit.Framework;

namespace ICSharpCode.Reporting.Test.Pdf
{
	[TestFixture]
	public class PdfExporterFixture
	{
		[Test]
		public void ImagePageExportsToPortablePdfStream()
		{
			using (var bitmap = new Bitmap(2, 2)) {
				bitmap.SetPixel(0, 0, Color.Red);
				bitmap.SetPixel(1, 0, Color.Green);
				bitmap.SetPixel(0, 1, Color.Blue);
				bitmap.SetPixel(1, 1, Color.White);

				var pageInfo = new PageInfo { ReportName = "LibreWPF Reporting" };
				var page = new ExportPage(pageInfo, new Size(200, 200));
				page.ExportedItems.Add(new ExportImage {
					Image = bitmap,
					Location = new Point(10, 10),
					Size = new Size(100, 100),
					Parent = page,
					ScaleImageToSize = true
				});

				using (var stream = new MemoryStream()) {
					new PdfExporter(new Collection<ExportPage> { page }).Run(stream);
					byte[] pdf = stream.ToArray();
					Assert.That(pdf.Length, Is.GreaterThan(128));
					Assert.That((char)pdf[0], Is.EqualTo('%'));
					Assert.That((char)pdf[1], Is.EqualTo('P'));
					Assert.That((char)pdf[2], Is.EqualTo('D'));
					Assert.That((char)pdf[3], Is.EqualTo('F'));
				}
			}
		}
	}
}
