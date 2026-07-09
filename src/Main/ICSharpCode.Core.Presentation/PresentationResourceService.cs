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
using System.Drawing;
using System.Windows;
#if LIBREWPF
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
#else
using System.Windows.Interop;
#endif
using System.Windows.Media.Imaging;

namespace ICSharpCode.Core.Presentation
{
	/// <summary>
	/// Creates WPF BitmapSource objects from images in the ResourceService.
	/// </summary>
	public static class PresentationResourceService
	{
		static readonly Dictionary<string, BitmapSource> bitmapCache = new Dictionary<string, BitmapSource>();
		static readonly IResourceService resourceService;
		
		static PresentationResourceService()
		{
			resourceService = ServiceSingleton.GetRequiredService<IResourceService>();
			resourceService.LanguageChanged += OnLanguageChanged;
		}
		
		static void OnLanguageChanged(object sender, EventArgs e)
		{
			lock (bitmapCache) {
				bitmapCache.Clear();
			}
		}
		
		/// <summary>
		/// Creates a new System.Windows.Controls.Image object containing the image with the
		/// specified resource name.
		/// </summary>
		/// <param name="name">
		/// The name of the requested bitmap.
		/// </param>
		/// <exception cref="ResourceNotFoundException">
		/// Is thrown when the GlobalResource manager can't find a requested resource.
		/// </exception>
		[Obsolete("Use SD.ResourceService.GetImage(name).CreateImage() instead, or just create the image manually")]
		public static System.Windows.Controls.Image GetImage(string name)
		{
			return new System.Windows.Controls.Image {
				Source = GetBitmapSource(name)
			};
		}
		
		/// <summary>
		/// Returns a BitmapSource from the resource database, it handles localization
		/// transparent for the user.
		/// </summary>
		/// <param name="name">
		/// The name of the requested bitmap.
		/// </param>
		/// <exception cref="ResourceNotFoundException">
		/// Is thrown when the GlobalResource manager can't find a requested resource.
		/// </exception>
		public static BitmapSource GetBitmapSource(string name)
		{
			if (resourceService == null)
				throw new ArgumentNullException("resourceService");
			lock (bitmapCache) {
				BitmapSource bs;
				if (bitmapCache.TryGetValue(name, out bs))
					return bs;
					System.Drawing.Bitmap bmp = (System.Drawing.Bitmap)resourceService.GetImageResource(name);
					if (bmp == null) {
#if LIBREWPF
						LoggingService.Warn("Image resource not found, using LibreWPF placeholder: " + name);
						bs = CreatePlaceholderBitmapSource();
						bitmapCache[name] = bs;
						return bs;
#else
						throw new ResourceNotFoundException(name);
#endif
					}
#if LIBREWPF
					bs = CreateBitmapSourceFromBitmap(bmp);
				bitmapCache[name] = bs;
#else
				IntPtr hBitmap = bmp.GetHbitmap();
				try {
					bs = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero,
					                                           Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
					bs.Freeze();
					bitmapCache[name] = bs;
				} finally {
					NativeMethods.DeleteObject(hBitmap);
				}
#endif
				return bs;
			}
		}

#if LIBREWPF
		static BitmapSource CreateBitmapSourceFromBitmap(System.Drawing.Bitmap bitmap)
		{
			using (var stream = new MemoryStream()) {
				bitmap.Save(stream, ImageFormat.Png);
				stream.Position = 0;

				var image = new BitmapImage();
				image.BeginInit();
				image.CacheOption = BitmapCacheOption.OnLoad;
				image.StreamSource = stream;
				image.EndInit();
				image.Freeze();
				return image;
			}
		}

		static BitmapSource CreatePlaceholderBitmapSource()
		{
			const int size = 16;
			const int stride = size * 4;
			byte[] pixels = new byte[size * stride];
			for (int y = 0; y < size; y++) {
				for (int x = 0; x < size; x++) {
					int offset = y * stride + x * 4;
					bool border = x == 0 || y == 0 || x == size - 1 || y == size - 1 || x == y || x == size - y - 1;
					pixels[offset + 0] = border ? (byte)0x90 : (byte)0xe0;
					pixels[offset + 1] = border ? (byte)0x90 : (byte)0xe0;
					pixels[offset + 2] = border ? (byte)0x90 : (byte)0xe0;
					pixels[offset + 3] = 0xff;
				}
			}

			BitmapSource source = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
			source.Freeze();
			return source;
		}
#endif
	}
}
