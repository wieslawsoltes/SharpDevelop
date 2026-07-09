namespace ICSharpCode.Core.WinForms;

public static class RightToLeftConverter
{
	public static bool IsRightToLeft { get; set; }

	public static void Convert(System.Windows.Forms.Control control)
	{
		if (control != null) {
			control.RightToLeft = IsRightToLeft ? System.Windows.Forms.RightToLeft.Yes : System.Windows.Forms.RightToLeft.No;
		}
	}

	public static void ConvertRecursive(System.Windows.Forms.Control control)
	{
		Convert(control);
	}

	public static void ReConvertRecursive(System.Windows.Forms.Control control)
	{
		Convert(control);
	}
}
