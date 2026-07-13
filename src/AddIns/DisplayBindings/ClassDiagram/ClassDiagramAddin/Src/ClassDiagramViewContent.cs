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
using System.IO;
using System.Windows.Forms;
using System.Xml;

using ClassDiagram;
using ICSharpCode.Core.WinForms;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Parser;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;

namespace ClassDiagramAddin
{
	/// <summary>
	/// Hosts the portable WinForms ClassCanvas and feeds it immutable parser snapshots.
	/// </summary>
	public sealed class ClassDiagramViewContent : AbstractViewContent
	{
		readonly ClassCanvas canvas = new ClassCanvas();
		readonly ToolStrip toolstrip;
		IProject project;
		bool refreshingTypes;

		public ClassDiagramViewContent(OpenedFile file)
			: base(file)
		{
			TabPageText = "Class Diagram";
			canvas.LayoutChanged += HandleLayoutChange;
			SD.ParserService.ParseInformationUpdated += OnParseInformationUpdated;
			toolstrip = ToolbarService.CreateToolStrip(this, "/SharpDevelop/ViewContent/ClassDiagram/Toolbar");
			toolstrip.GripStyle = ToolStripGripStyle.Hidden;
			toolstrip.Stretch = true;
			canvas.Controls.Add(toolstrip);
			canvas.ContextMenuStrip = MenuService.CreateContextMenu(this, "/SharpDevelop/ViewContent/ClassDiagram/ContextMenu");
		}

		public override object Control {
			get { return canvas; }
		}

		internal ClassCanvas Canvas {
			get { return canvas; }
		}

		public override void Load(OpenedFile file, Stream stream)
		{
			if (stream == null)
				throw new ArgumentNullException("stream");

			var document = new XmlDocument();
			document.Load(stream);
			project = SD.ProjectService.CurrentProject;
			canvas.LoadFromXml(document, CreateCatalog(project));
		}

		public override void Save(OpenedFile file, Stream stream)
		{
			if (stream == null)
				throw new ArgumentNullException("stream");

			var settings = new XmlWriterSettings {
				Indent = true,
				Encoding = System.Text.Encoding.UTF8
			};
			using (XmlWriter writer = XmlWriter.Create(stream, settings)) {
				canvas.WriteToXml().WriteTo(writer);
			}
		}

		void OnParseInformationUpdated(object sender, ParseInformationEventArgs e)
		{
			if (e == null || project == null || !Object.ReferenceEquals(e.ParentProject, project))
				return;

			// Preserve the persisted positions/collapse state while replacing every live
			// type/member payload with a fresh immutable compilation snapshot.
			XmlDocument layout = canvas.WriteToXml();
			refreshingTypes = true;
			try {
				canvas.LoadFromXml(layout, CreateCatalog(project));
			} finally {
				refreshingTypes = false;
			}
		}

		static IClassDiagramTypeResolver CreateCatalog(IProject project)
		{
			if (project == null)
				return new ClassDiagramTypeCatalog(new ClassDiagramTypeSnapshot[0]);
			return ClassDiagramTypeSnapshotFactory.CreateCatalog(SD.ParserService.GetCompilation(project));
		}

		public override void Dispose()
		{
			SD.ParserService.ParseInformationUpdated -= OnParseInformationUpdated;
			canvas.LayoutChanged -= HandleLayoutChange;
			canvas.Dispose();
			base.Dispose();
		}

		void HandleLayoutChange(object sender, EventArgs args)
		{
			if (!refreshingTypes && PrimaryFile != null)
				PrimaryFile.MakeDirty();
		}
	}
}
