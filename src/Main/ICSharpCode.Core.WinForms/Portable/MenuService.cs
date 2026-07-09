using System;
using System.Windows.Input;

namespace ICSharpCode.Core.WinForms;

public static class MenuService
{
	public static Action<ICommand, object> ExecuteCommand;
	public static Func<ICommand, object, bool> CanExecuteCommand;
	public static bool IsContextMenuOpen { get; }
}
