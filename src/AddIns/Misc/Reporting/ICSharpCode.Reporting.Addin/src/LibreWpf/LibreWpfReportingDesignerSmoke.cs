#if LIBREWPF
using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Drawing;
using System.Drawing.Design;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using ICSharpCode.Reporting.Addin.DesignableItems;
using ICSharpCode.Reporting.Addin.Designer;
using ICSharpCode.Reporting.Addin.Services;
using ReportingMenuCommandService = ICSharpCode.Reporting.Addin.Services.MenuCommandService;

namespace ICSharpCode.Reporting.Addin.LibreWpf
{
	public sealed class LibreWpfReportingDesignerSmokeResult
	{
		public int ComponentCount { get; internal set; }
		public int RootChildCount { get; internal set; }
		public int VisiblePropertyCount { get; internal set; }
		public int PaintedPixelCount { get; internal set; }
		public bool RootSizeFiltered { get; internal set; }
	}

	/// <summary>
	/// Runs the source-built Reporting design surface without SharpDevelop shell state.
	/// The hook exercises the same typed designer, service, property-filter, layout,
	/// and System.Drawing paths used when an .srd file opens in the workbench.
	/// </summary>
	public static class LibreWpfReportingDesignerSmoke
	{
		public static LibreWpfReportingDesignerSmokeResult Run()
		{
			using (var services = new ServiceContainer())
			using (var surface = new DesignSurface(services))
			using (var panel = new Panel { Size = new Size(640, 480) })
			{
				var toolbox = new ToolboxService();
				var menuCommands = new ReportingMenuCommandService(panel, surface);
				services.AddService(typeof(IToolboxService), toolbox);
				services.AddService(typeof(IMenuCommandService), menuCommands);
				services.AddService(typeof(ReportingMenuCommandService), menuCommands);

				surface.BeginLoad(new ReportingSmokeLoader());
				if (!surface.IsLoaded)
				{
					throw new InvalidOperationException(
						"Reporting design surface failed to load: "
						+ string.Join("; ", surface.LoadErrors.Cast<object>()));
				}

				var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost));
				var root = host.RootComponent as RootReportModel
					?? throw new InvalidOperationException("Reporting designer did not create its typed root model.");
				PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(root);
				bool rootSizeFiltered = properties[nameof(Control.Size)] == null;

				var section = (BaseSection)host.CreateComponent(typeof(BaseSection), "DetailSection");
				section.Size = new Size(320, 72);
				if (!ReferenceEquals(section.Parent, root))
					throw new InvalidOperationException("Reporting root designer did not attach the new section.");

				using (var bitmap = new Bitmap(360, 160))
				using (Graphics graphics = Graphics.FromImage(bitmap))
				{
					graphics.Clear(Color.Transparent);
					root.RaisePaint(new PaintEventArgs(graphics, new Rectangle(0, 0, bitmap.Width, bitmap.Height)));
					section.RaisePaint(new PaintEventArgs(graphics, section.Bounds));

					int paintedPixelCount = CountPaintedPixels(bitmap);

					if (paintedPixelCount == 0)
						throw new InvalidOperationException("Reporting designer produced no System.Drawing output.");

					return new LibreWpfReportingDesignerSmokeResult
					{
						ComponentCount = host.Container.Components.Count,
						RootChildCount = root.Controls.Count,
						VisiblePropertyCount = properties.Count,
						PaintedPixelCount = paintedPixelCount,
						RootSizeFiltered = rootSizeFiltered
					};
				}
			}
		}

		private static int CountPaintedPixels(Bitmap bitmap)
		{
			Rectangle bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
			BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			try
			{
				int stride = Math.Abs(data.Stride);
				var pixels = new byte[stride * bitmap.Height];
				Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
				int paintedPixelCount = 0;
				for (int y = 0; y < bitmap.Height; y++)
				{
					int rowOffset = y * stride;
					for (int x = 0; x < bitmap.Width; x++)
					{
						if (pixels[rowOffset + (x * 4) + 3] != 0)
							paintedPixelCount++;
					}
				}

				return paintedPixelCount;
			}
			finally
			{
				bitmap.UnlockBits(data);
			}
		}

		private sealed class ReportingSmokeLoader : BasicDesignerLoader
		{
			protected override void PerformLoad(IDesignerSerializationManager serializationManager)
			{
				var root = (RootReportModel)LoaderHost.CreateComponent(typeof(RootReportModel), "RootReportModel");
				var settings = (ReportSettings)LoaderHost.CreateComponent(typeof(ReportSettings), "ReportSettings");
				root.Size = settings.PageSize;
			}

			protected override void PerformFlush(IDesignerSerializationManager serializationManager)
			{
			}
		}
	}
}
#endif
