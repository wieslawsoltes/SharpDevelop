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

using System.IO;
using System.Xml;

using ClassDiagram;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Commands;
using ICSharpCode.SharpDevelop.Project;

namespace ClassDiagramAddin
{
	public sealed class ShowClassDiagramCommand : AbstractMenuCommand
	{
		public override void Run()
		{
			IProject project = SD.ProjectService.CurrentProject;
			if (project == null)
				return;

			FileName fileName = FileName.Create(Path.Combine(project.Directory, project.Name + ".cd"));
			XmlDocument document;
			using (var canvas = new ClassCanvas()) {
				foreach (ClassDiagramTypeSnapshot type in
				         ClassDiagramTypeSnapshotFactory.CreateTopLevelSnapshots(SD.ParserService.GetCompilation(project))) {
					ClassCanvasItem item = ClassCanvas.CreateItemFromType(type);
					if (item != null)
						canvas.AddCanvasItem(item);
				}

				canvas.AutoArrange();
				document = canvas.WriteToXml();
			}

			FileUtility.ObservedSave(
				newFileName => SaveAndOpenNewClassDiagram(project, newFileName, document),
				fileName,
				FileErrorPolicy.ProvideAlternative);
		}

		static void SaveAndOpenNewClassDiagram(IProject project, FileName fileName, XmlDocument document)
		{
			document.Save(fileName);
			var item = new FileProjectItem(project, ItemType.Content) {
				FileName = fileName
			};
			ProjectService.AddProjectItem(project, item);
			ProjectBrowserPad.RefreshViewAsync();
			project.Save();
			SD.FileService.OpenFile(fileName);
		}
	}
}
