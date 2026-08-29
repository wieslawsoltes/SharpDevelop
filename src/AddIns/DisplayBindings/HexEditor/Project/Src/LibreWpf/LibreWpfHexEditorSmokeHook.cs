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
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

using HexEditor.View;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Workbench;

namespace HexEditor.LibreWpf
{
	public sealed class LibreWpfHexEditorSmokeHook : ILibreWpfSmokeHook
	{
		static readonly byte[] FixtureBytes = Encoding.ASCII.GetBytes("LibreWPF HexEditor smoke");

		public string EnvironmentVariableName {
			get { return "LIBREWPF_SHARPDEVELOP_HEXEDITOR_SMOKE"; }
		}

		public async Task RunAsync(string mode)
		{
			string temporaryDirectory = null;
			string filePath = ResolveFixturePath(mode, out temporaryDirectory);
			IViewContent content = null;

			try {
				FileName fileName = FileName.Create(filePath);
				DisplayBindingDescriptor binding = FindHexEditorBinding(fileName);
				if (binding == null || binding.Binding == null) {
					throw new InvalidOperationException("The HexEditor display binding is not registered.");
				}

				content = SD.FileService.OpenFileWith(fileName, binding.Binding, true);
				HexEditView view = content as HexEditView;
				if (view == null) {
					throw new InvalidOperationException("The HexEditor display binding did not create HexEditView.");
				}

				HexEditContainer container = view.Control as HexEditContainer;
				if (container == null) {
					throw new InvalidOperationException("HexEditView did not expose HexEditContainer.");
				}

				WindowsFormsHost host = await WaitForHostAsync(container);
				if (host == null || !ReferenceEquals(host.Child, container)) {
					throw new InvalidOperationException("HexEditContainer was not attached to WindowsFormsHost.");
				}

				int invalidationCount = 0;
				container.Invalidated += delegate { invalidationCount++; };
#if !LIBREWPF_CANONICAL_WINFORMS
				IPortableWinFormsPaintSource paintSource = container;
				long paintVersionBefore = paintSource.PortablePaintVersion;
#endif
				container.PerformLayout();
				container.Invalidate();
				await Task.Delay(100);

#if !LIBREWPF_CANONICAL_WINFORMS
				long graphicsDispatchBeforeResize = host.PortableCreateGraphicsDispatchCount;
#endif
				System.Drawing.Size originalEditorSize = container.hexEditControl.Size;
				container.hexEditControl.Size = new System.Drawing.Size(
					Math.Max(1, originalEditorSize.Width - 1),
					Math.Max(1, originalEditorSize.Height - 1));
				await Task.Delay(100);
#if !LIBREWPF_CANONICAL_WINFORMS
				long graphicsDispatchAfterResize = host.PortableCreateGraphicsDispatchCount;
#endif
				container.hexEditControl.Size = originalEditorSize;
				await Task.Delay(100);

#if !LIBREWPF_CANONICAL_WINFORMS
				long graphicsDispatchBeforeRepaint = host.PortableCreateGraphicsDispatchCount;
#endif
				container.Invalidate();
				await Task.Delay(100);
				container.Invalidate();
				await Task.Delay(100);
#if !LIBREWPF_CANONICAL_WINFORMS
				long graphicsDispatchAfterRepaint = host.PortableCreateGraphicsDispatchCount;
				long paintVersionAfter = paintSource.PortablePaintVersion;
#endif

				container.SelectAll();
				string selectedText = container.Copy();
				if (!string.Equals(selectedText, Encoding.ASCII.GetString(FixtureBytes), StringComparison.Ordinal)) {
					throw new InvalidOperationException("HexEditor selection/copy did not preserve the fixture bytes.");
				}

				AssertSavedBytes(container, content.PrimaryFile, FixtureBytes, "initial save");
				container.Paste("GPU");
				AssertSavedBytes(container, content.PrimaryFile, Encoding.ASCII.GetBytes("GPU"), "edited save");
				container.Undo();
				AssertSavedBytes(container, content.PrimaryFile, FixtureBytes, "undo save");

#if LIBREWPF_CANONICAL_WINFORMS
				if (invalidationCount == 0) {
					throw new InvalidOperationException("HexEditor did not publish canonical WinForms invalidation.");
				}

				WriteResult(
					"Success",
					"bytes=" + FixtureBytes.Length
					+ " invalidations=" + invalidationCount
					+ " canonical=True saveUndo=True");
#else
				bool painted = paintSource.SupportsPortablePainting && paintVersionAfter > paintVersionBefore;
				bool hostedGraphics = host.PortableCreateGraphicsSurfaceCount >= 4;
				bool invalidationRouted = host.PortableChildInvalidationDispatchCount > 0;
				bool resizedGraphics = graphicsDispatchAfterResize > graphicsDispatchBeforeResize;
				bool repaintStable = graphicsDispatchAfterRepaint == graphicsDispatchBeforeRepaint;
				if (!painted || !hostedGraphics || !invalidationRouted || !resizedGraphics || !repaintStable) {
					throw new InvalidOperationException(
						"HexEditor host rendering did not reach the portable paint/create-graphics path."
						+ " painted=" + painted
						+ " surfaces=" + host.PortableCreateGraphicsSurfaceCount
						+ " invalidations=" + host.PortableChildInvalidationDispatchCount
						+ " resizedGraphics=" + resizedGraphics
						+ " repaintStable=" + repaintStable
						+ " dispatches=" + graphicsDispatchBeforeResize
						+ "/" + graphicsDispatchAfterResize
						+ "/" + graphicsDispatchBeforeRepaint
						+ "/" + graphicsDispatchAfterRepaint);
				}

				WriteResult(
					"Success",
					"bytes=" + FixtureBytes.Length
					+ " paintVersion=" + paintVersionAfter
					+ " surfaces=" + host.PortableCreateGraphicsSurfaceCount
					+ " invalidations=" + host.PortableChildInvalidationDispatchCount
					+ " dispatches=" + graphicsDispatchAfterRepaint
					+ " repaintStable=True");
#endif
			} finally {
				if (content != null && content.WorkbenchWindow != null) {
					content.WorkbenchWindow.CloseWindow(true);
				}

				if (temporaryDirectory != null) {
					try {
						Directory.Delete(temporaryDirectory, true);
					} catch (IOException) {
					} catch (UnauthorizedAccessException) {
					}
				}
			}
		}

		static string ResolveFixturePath(string mode, out string temporaryDirectory)
		{
			temporaryDirectory = null;
			if (!string.IsNullOrWhiteSpace(mode)
			    && !string.Equals(mode, "1", StringComparison.OrdinalIgnoreCase)
			    && !string.Equals(mode, "default", StringComparison.OrdinalIgnoreCase))
			{
				string requestedPath = Path.GetFullPath(mode);
				if (!File.Exists(requestedPath)) {
					throw new FileNotFoundException("The requested HexEditor smoke fixture does not exist.", requestedPath);
				}
				return requestedPath;
			}

			temporaryDirectory = Path.Combine(Path.GetTempPath(), "sharpdevelop-librewpf-hexeditor-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(temporaryDirectory);
			string fixturePath = Path.Combine(temporaryDirectory, "fixture.bin");
			File.WriteAllBytes(fixturePath, FixtureBytes);
			return fixturePath;
		}

		static DisplayBindingDescriptor FindHexEditorBinding(FileName fileName)
		{
			foreach (DisplayBindingDescriptor binding in SD.DisplayBindingService.GetCodonsPerFileName(fileName)) {
				if (string.Equals(binding.Id, "HexEditor", StringComparison.Ordinal)) {
					return binding;
				}
			}
			return null;
		}

		static async Task<WindowsFormsHost> WaitForHostAsync(HexEditContainer container)
		{
			for (int attempt = 0; attempt < 50; attempt++) {
				WindowsFormsHost host = FindHost(SD.Workbench.MainWindow, container);
				if (host != null) {
					return host;
				}
				await Task.Delay(50);
			}
			return null;
		}

		static WindowsFormsHost FindHost(DependencyObject current, HexEditContainer container)
		{
			WindowsFormsHost host = current as WindowsFormsHost;
			if (host != null && ReferenceEquals(host.Child, container)) {
				return host;
			}

			int childCount = VisualTreeHelper.GetChildrenCount(current);
			for (int i = 0; i < childCount; i++) {
				WindowsFormsHost childHost = FindHost(VisualTreeHelper.GetChild(current, i), container);
				if (childHost != null) {
					return childHost;
				}
			}
			return null;
		}

		static void AssertSavedBytes(HexEditContainer container, OpenedFile file, byte[] expected, string operation)
		{
			using (var stream = new MemoryStream()) {
				container.SaveFile(file, stream);
				byte[] actual = stream.ToArray();
				if (!ByteArraysEqual(actual, expected)) {
					throw new InvalidOperationException("HexEditor " + operation + " byte mismatch.");
				}
			}
		}

		static bool ByteArraysEqual(byte[] left, byte[] right)
		{
			if (left.Length != right.Length) {
				return false;
			}
			for (int i = 0; i < left.Length; i++) {
				if (left[i] != right[i]) {
					return false;
				}
			}
			return true;
		}

		static void WriteResult(string result, string details)
		{
			string message = "LibreWPF HexEditor smoke result=" + result + " " + details;
			Console.WriteLine(message);
			SD.StatusBar.SetMessage(message);
		}
	}
}
#endif
