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
using System.Drawing;
#if LIBREWPF
using System.Drawing.Imaging;
using System.IO;
#else
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
#endif
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ResourceEditor
{
	/// <summary>
	/// Bitmap conversion extensions for WinForms -> WPF
	/// </summary>
	public static class BitmapExtensions
	{
	#if !LIBREWPF
		[DllImport("gdi32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool DeleteObject(IntPtr hObject);
	#endif

		public static BitmapSource ToBitmapSource(this System.Drawing.Bitmap bitmap)
		{
		#if LIBREWPF
			if (bitmap == null)
				throw new ArgumentNullException("bitmap");

			using (var stream = new MemoryStream()) {
				bitmap.Save(stream, ImageFormat.Png);
				stream.Position = 0;
				var decoder = new PngBitmapDecoder(
					stream,
					BitmapCreateOptions.PreservePixelFormat,
					BitmapCacheOption.OnLoad);
				BitmapSource source = decoder.Frames[0];
				source.Freeze();
				return source;
			}
		#else
			BitmapSource bs;
			IntPtr hBitmap = bitmap.GetHbitmap();
			try {
				bs = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero,
					Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
				bs.Freeze();
			} finally {
				DeleteObject(hBitmap);
			}
			return bs;
		#endif
		}
		
		public static ImageSource ToImageSource(this Icon icon)
		{
		#if LIBREWPF
			if (icon == null)
				throw new ArgumentNullException("icon");

			using (Bitmap bitmap = icon.ToBitmap()) {
				return bitmap.ToBitmapSource();
			}
		#else
			return Imaging.CreateBitmapSourceFromHIcon(
				icon.Handle,
				Int32Rect.Empty,
				BitmapSizeOptions.FromEmptyOptions());
		#endif
		}
		
		public static ImageSource ToImageSource(this System.Windows.Forms.Cursor cursor)
		{
			int width = cursor.Size.Width;
			int height = cursor.Size.Height;
			using (System.Drawing.Bitmap b = new System.Drawing.Bitmap(width, height)) {
				using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(b)) {
					cursor.Draw(g, new System.Drawing.Rectangle(0, 0, width, height));
					return b.ToBitmapSource();
				}
			}
		}
	}
}
