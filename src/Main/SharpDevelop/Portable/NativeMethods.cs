using System;
using System.IO;

namespace ICSharpCode.SharpDevelop
{
	static class NativeMethods
	{
		public const int WM_USER = 0x400;

		public static IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
		{
			return IntPtr.Zero;
		}

		public static IntPtr SetForegroundWindow(IntPtr hWnd)
		{
			return IntPtr.Zero;
		}

		public static void DeleteToRecycleBin(string path)
		{
			if (File.Exists(path)) {
				File.Delete(path);
				return;
			}

			if (Directory.Exists(path)) {
				Directory.Delete(path, true);
			}
		}
	}
}
