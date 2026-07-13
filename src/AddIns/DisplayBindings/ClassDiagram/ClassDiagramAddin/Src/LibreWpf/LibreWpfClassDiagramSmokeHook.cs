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

#if LIBREWPF
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Xml;

using ClassDiagram;
using ICSharpCode.Core;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;

namespace ClassDiagramAddin.LibreWpf
{
	public sealed class LibreWpfClassDiagramSmokeHook : ILibreWpfSmokeHook
	{
		public string EnvironmentVariableName {
			get { return "LIBREWPF_SHARPDEVELOP_CLASS_DIAGRAM_SMOKE"; }
		}

		public async Task RunAsync(string mode)
		{
			string temporaryDirectory = null;
			IViewContent content = null;
			try {
				IProject project = await WaitForProjectAsync();
				ICompilation compilation = SD.ParserService.GetCompilation(project);
				ClassDiagramTypeSnapshot[] types = ClassDiagramTypeSnapshotFactory
					.CreateTopLevelSnapshots(compilation)
					.Take(8)
					.ToArray();
				if (types.Length == 0)
					throw new InvalidOperationException("The loaded project did not expose any top-level type snapshots.");

				temporaryDirectory = Path.Combine(
					Path.GetTempPath(),
					"sharpdevelop-librewpf-classdiagram-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(temporaryDirectory);
				string diagramPath = Path.Combine(temporaryDirectory, "LibreWpfClassDiagram.cd");
				CreateFixture(types).Save(diagramPath);

				FileName fileName = FileName.Create(diagramPath);
				DisplayBindingDescriptor descriptor = FindClassDiagramBinding(fileName);
				ClassDiagramDisplayBinding binding = descriptor == null
					? null
					: descriptor.Binding as ClassDiagramDisplayBinding;
				if (binding == null)
					throw new InvalidOperationException("The ClassDiagram display binding is not registered.");

				content = SD.FileService.OpenFileWith(fileName, binding, true);
				ClassDiagramViewContent view = content as ClassDiagramViewContent;
				if (view == null)
					throw new InvalidOperationException("The ClassDiagram binding did not create ClassDiagramViewContent.");

				ClassCanvas canvas = view.Control as ClassCanvas;
				if (canvas == null)
					throw new InvalidOperationException("ClassDiagramViewContent did not expose ClassCanvas.");
				if (canvas.GetCanvasItems().Length != types.Length)
					throw new InvalidOperationException("The ClassDiagram type snapshot count changed during load.");

				WindowsFormsHost host = await WaitForHostAsync(canvas);
				if (host == null || !ReferenceEquals(host.Child, canvas))
					throw new InvalidOperationException("ClassCanvas was not attached to WindowsFormsHost.");

				IPortableWinFormsPaintSource paintSource = canvas;
				long paintVersionBefore = paintSource.PortablePaintVersion;
				long invalidationsBefore = host.PortableChildInvalidationDispatchCount;
				canvas.PerformLayout();
				canvas.AutoArrange();
				canvas.Invalidate(true);
				await Task.Delay(150);
				long paintVersionAfter = paintSource.PortablePaintVersion;
				long invalidationsAfter = host.PortableChildInvalidationDispatchCount;

				string imagePath = Path.Combine(temporaryDirectory, "LibreWpfClassDiagram.png");
				canvas.SaveToImage(imagePath);
				int imageWidth;
				int imageHeight;
				using (Image image = Image.FromFile(imagePath)) {
					imageWidth = image.Width;
					imageHeight = image.Height;
				}
				if (imageWidth <= 0 || imageHeight <= 0 || new FileInfo(imagePath).Length <= 0)
					throw new InvalidOperationException("ClassCanvas did not produce a readable bitmap export.");

				canvas.Zoom = 1.25f;
				view.PrimaryFile.SaveToDisk();
				if (view.PrimaryFile.IsDirty)
					throw new InvalidOperationException("ClassDiagram save did not clear the dirty state.");
				var reloadedDocument = new XmlDocument();
				reloadedDocument.Load(diagramPath);
				canvas.LoadFromXml(reloadedDocument, ClassDiagramTypeSnapshotFactory.CreateCatalog(compilation));
				if (canvas.GetCanvasItems().Length != types.Length || Math.Abs(canvas.Zoom - 1.25f) > 0.001f)
					throw new InvalidOperationException("ClassDiagram save/reload did not preserve types and zoom.");

				bool painted = paintSource.SupportsPortablePainting && paintVersionAfter > paintVersionBefore;
				bool invalidated = invalidationsAfter > invalidationsBefore;
				bool presented = PresentationSource.FromVisual(host) != null;
				if (!painted || !invalidated || !presented)
					throw new InvalidOperationException(
						"ClassDiagram did not reach the portable GPU host path."
						+ " painted=" + painted
						+ " invalidated=" + invalidated
						+ " presented=" + presented);

				WriteResult(
					"Success",
					"types=" + types.Length
					+ " paintVersion=" + paintVersionAfter
					+ " invalidations=" + invalidationsAfter
					+ " bitmap=" + imageWidth + "x" + imageHeight
					+ " hosted=True presented=True saveReload=True cleanup=True");
			} finally {
				if (content != null && content.WorkbenchWindow != null)
					content.WorkbenchWindow.CloseWindow(true);
				if (temporaryDirectory != null) {
					try {
						Directory.Delete(temporaryDirectory, true);
					} catch (IOException) {
					} catch (UnauthorizedAccessException) {
					}
				}
			}
		}

		static async Task<IProject> WaitForProjectAsync()
		{
			for (int attempt = 0; attempt < 100; attempt++) {
				IProject project = SD.ProjectService.CurrentProject;
				if (project != null && SD.ParserService.GetCompilation(project).MainAssembly.TopLevelTypeDefinitions.Any())
					return project;
				await Task.Delay(100);
			}
			throw new InvalidOperationException("The ClassDiagram smoke did not receive a parsed current project.");
		}

		static XmlDocument CreateFixture(ClassDiagramTypeSnapshot[] types)
		{
			using (var canvas = new ClassCanvas()) {
				foreach (ClassDiagramTypeSnapshot type in types) {
					ClassCanvasItem item = ClassCanvas.CreateItemFromType(type);
					if (item != null)
						canvas.AddCanvasItem(item);
				}
				canvas.AutoArrange();
				return canvas.WriteToXml();
			}
		}

		static DisplayBindingDescriptor FindClassDiagramBinding(FileName fileName)
		{
			return SD.DisplayBindingService.GetCodonsPerFileName(fileName)
				.FirstOrDefault(descriptor => string.Equals(descriptor.Id, "ClassDiagram", StringComparison.Ordinal));
		}

		static async Task<WindowsFormsHost> WaitForHostAsync(ClassCanvas canvas)
		{
			for (int attempt = 0; attempt < 100; attempt++) {
				WindowsFormsHost host = FindHost(SD.Workbench.MainWindow, canvas);
				if (host != null)
					return host;
				await Task.Delay(50);
			}
			return null;
		}

		static WindowsFormsHost FindHost(DependencyObject current, ClassCanvas canvas)
		{
			WindowsFormsHost host = current as WindowsFormsHost;
			if (host != null && ReferenceEquals(host.Child, canvas))
				return host;
			int childCount = VisualTreeHelper.GetChildrenCount(current);
			for (int i = 0; i < childCount; i++) {
				WindowsFormsHost child = FindHost(VisualTreeHelper.GetChild(current, i), canvas);
				if (child != null)
					return child;
			}
			return null;
		}

		static void WriteResult(string result, string details)
		{
			string message = "LibreWPF ClassDiagram smoke result=" + result + " " + details;
			Console.WriteLine(message);
			SD.StatusBar.SetMessage(message);
		}
	}
}
#endif
