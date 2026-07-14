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
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using System.Linq;
using Drawing = System.Drawing;

using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Designer;
using ICSharpCode.SharpDevelop.Dom;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Widgets.SideBar;
using ICSharpCode.SharpDevelop.Workbench;
using ICSharpCode.WpfDesign.Designer.Services;
using WPF = System.Windows.Controls;

namespace ICSharpCode.WpfDesign.AddIn
{
	internal readonly struct WpfToolboxDragSource
	{
		internal WpfToolboxDragSource(
			Control inputControl,
			CreateComponentTool tool,
			Drawing.Point inputPoint,
			System.Windows.Forms.IDataObject dragData)
		{
			InputControl = inputControl;
			Tool = tool;
			InputPoint = inputPoint;
			DragData = dragData;
		}

		internal System.Windows.Forms.IDataObject DragData { get; }

		internal Control InputControl { get; }

		internal CreateComponentTool Tool { get; }

		internal Drawing.Point InputPoint { get; }
	}

	/// <summary>
	/// Manages the WpfToolbox.
	/// </summary>
	public class WpfToolbox
	{
		static WpfToolbox instance;
		
		public static WpfToolbox Instance {
			get {
				SD.MainThread.VerifyAccess();
				if (instance == null) {
					instance = new WpfToolbox();
				}
				return instance;
			}
		}
		
		IToolService toolService;
		WpfSideBar sideBar;
		
		public WpfToolbox()
		{
			sideBar = new WpfSideBar();
			SideTab sideTab = new SideTab(sideBar, "Windows Presentation Foundation");
			sideTab.DisplayName = StringParser.Parse(sideTab.Name);
			sideTab.CanBeDeleted = false;
			sideTab.ChosenItemChanged += OnChosenItemChanged;
			
			sideTab.Items.Add(new WpfSideTabItem());
			foreach (Type t in Metadata.GetPopularControls())
				sideTab.Items.Add(new WpfSideTabItem(t));
			
			sideBar.Tabs.Add(sideTab);
			sideBar.ActiveTab = sideTab;
		}
		
		static bool IsControl(Type t)
		{
			return !t.IsAbstract && !t.IsGenericTypeDefinition && t.IsSubclassOf(typeof(FrameworkElement));
		}

		static readonly HashSet<string> addedAssemblies = new HashSet<string>();
		public void AddProjectDlls(OpenedFile file)
		{
			var compilation = SD.ParserService.GetCompilationForFile(file.FileName);
			var typeResolutionService = new TypeResolutionService(file.FileName);
			
			foreach (var reference in compilation.ReferencedAssemblies) {
				string assemblyFileName = reference.GetReferenceAssemblyLocation();
				
				if (assemblyFileName != null && !addedAssemblies.Contains(assemblyFileName)) {
					try {
						// DO NOT USE Assembly.LoadFrom!!!
						// see http://community.sharpdevelop.net/forums/t/19968.aspx
						Assembly assembly = typeResolutionService.LoadAssembly(assemblyFileName);
						if (assembly == null) continue;

						SideTab sideTab = new SideTab(sideBar, assembly.FullName.Split(new[] {','})[0]);
						sideTab.DisplayName = StringParser.Parse(sideTab.Name);
						sideTab.CanBeDeleted = false;
						sideTab.ChosenItemChanged += OnChosenItemChanged;

						sideTab.Items.Add(new WpfSideTabItem());

						foreach (var t in assembly.GetExportedTypes())
						{
							if (IsControl(t))
							{
								sideTab.Items.Add(new WpfSideTabItem(t));
							}
						}

						if (sideTab.Items.Count > 1)
							sideBar.Tabs.Add(sideTab);

						addedAssemblies.Add(assemblyFileName);
					} catch (Exception ex) {
						WpfViewContent.DllLoadErrors.Add(new SDTask(new BuildError(assemblyFileName, ex.Message)));
					}
				}
			}
		}

		void OnChosenItemChanged(object sender, EventArgs e)
		{
			if (toolService != null) {
				ITool newTool = null;
				if (sideBar.ActiveTab != null && sideBar.ActiveTab.ChosenItem != null) {
					newTool = sideBar.ActiveTab.ChosenItem.Tag as ITool;
				}
				toolService.CurrentTool = newTool ?? toolService.PointerTool;
			}
		}

		internal bool TrySelectComponentTool(Type componentType, out CreateComponentTool selectedTool)
		{
			if (componentType == null)
				throw new ArgumentNullException("componentType");

			selectedTool = null;
			if (toolService == null)
				return false;

			foreach (SideTab tab in sideBar.Tabs) {
				foreach (SideTabItem item in tab.Items) {
					var componentTool = item.Tag as CreateComponentTool;
					if (componentTool == null || componentTool.ComponentType != componentType)
						continue;

					sideBar.ActiveTab = tab;
					tab.ChosenItem = item;
					selectedTool = componentTool;
					return ReferenceEquals(toolService.CurrentTool, componentTool);
				}
			}

			return false;
		}

		internal bool TryLocateComponentDragSource(
			Type componentType,
			out WpfToolboxDragSource dragSource)
		{
			if (componentType == null)
				throw new ArgumentNullException("componentType");

			dragSource = default(WpfToolboxDragSource);
			foreach (SideTab tab in sideBar.Tabs) {
				foreach (SideTabItem item in tab.Items) {
					var componentTool = item.Tag as CreateComponentTool;
					if (componentTool == null || componentTool.ComponentType != componentType)
						continue;

					sideBar.ActiveTab = tab;
					sideBar.EnsureVisible(item);
					sideBar.PerformLayout();
					sideBar.Refresh();
					return sideBar.TryLocateDragSource(item, componentTool, out dragSource);
				}
			}

			return false;
		}

		internal bool TryStartComponentDrag(WpfToolboxDragSource dragSource)
		{
			return sideBar.TryStartComponentDrag(dragSource);
		}

		internal bool TryInsertSelectedComponent(
			DesignItem container,
			Rect bounds,
			out DesignItem createdItem)
		{
			if (container == null)
				throw new ArgumentNullException("container");

			createdItem = null;
			var componentTool = toolService == null
				? null
				: toolService.CurrentTool as CreateComponentTool;
			if (componentTool == null
				|| !ReferenceEquals(container.Context.Services.Tool, toolService)
				|| bounds.IsEmpty
				|| bounds.Width <= 0
				|| bounds.Height <= 0
				|| !IsFinite(bounds.X)
				|| !IsFinite(bounds.Y)
				|| !IsFinite(bounds.Width)
				|| !IsFinite(bounds.Height))
				return false;

			ChangeGroup changeGroup = null;
			PlacementOperation placement = null;
			try {
				object instance = container.Context.Services.ExtensionManager
					.CreateInstanceWithCustomInstanceFactory(componentTool.ComponentType, null);
				createdItem = container.Context.Services.Component.RegisterComponentForDesigner(instance);
				changeGroup = createdItem.OpenGroup("Add " + componentTool.ComponentType.Name);
				container.Context.Services.Component.SetDefaultPropertyValues(createdItem);
				container.Context.Services.ExtensionManager.ApplyDefaultInitializers(createdItem);

				placement = PlacementOperation.TryStartInsertNewComponents(
					container,
					new[] { createdItem },
					new[] { bounds },
					PlacementType.AddItem);
				if (placement == null) {
					createdItem = null;
					return false;
				}

				container.Context.Services.Selection.SetSelectedComponents(
					new[] { createdItem },
					SelectionTypes.Primary);
				placement.Commit();
				placement = null;
				changeGroup.Commit();
				changeGroup = null;
				return true;
			} finally {
				if (placement != null)
					placement.Abort();
				if (changeGroup != null)
					changeGroup.Abort();
				if (toolService != null)
					toolService.CurrentTool = toolService.PointerTool;
			}
		}

		static bool IsFinite(double value)
		{
			return !double.IsNaN(value) && !double.IsInfinity(value);
		}
		
		public Control ToolboxControl {
			get { return sideBar; }
		}
		
		public IToolService ToolService {
			get { return toolService; }
			set {
				if (toolService != null) {
					toolService.CurrentToolChanged -= OnCurrentToolChanged;
				}
				toolService = value;
				if (toolService != null) {
					toolService.CurrentToolChanged += OnCurrentToolChanged;
					OnCurrentToolChanged(null, null);
				}
			}
		}
		
		void OnCurrentToolChanged(object sender, EventArgs e)
		{
			object tagToFind;
			if (toolService.CurrentTool == toolService.PointerTool) {
				tagToFind = null;
			} else {
				tagToFind = toolService.CurrentTool;
			}
			if (sideBar.ActiveTab.ChosenItem != null) {
				if (sideBar.ActiveTab.ChosenItem.Tag == tagToFind)
					return;
			}
			foreach (SideTabItem item in sideBar.ActiveTab.Items) {
				if (item.Tag == tagToFind) {
					sideBar.ActiveTab.ChosenItem = item;
					sideBar.Refresh();
					return;
				}
			}
			foreach (SideTab tab in sideBar.Tabs) {
				foreach (SideTabItem item in tab.Items) {
					if (item.Tag == tagToFind) {
						sideBar.ActiveTab = tab;
						sideBar.ActiveTab.ChosenItem = item;
						sideBar.Refresh();
						return;
					}
				}
			}
			sideBar.ActiveTab.ChosenItem = null;
			sideBar.Refresh();
		}
		
		sealed class WpfSideBar : SharpDevelopSideBar
		{
			internal bool TryStartComponentDrag(WpfToolboxDragSource dragSource)
			{
				if (dragSource.InputControl == null
					|| dragSource.Tool == null
					|| dragSource.DragData == null
					|| !ReferenceEquals(dragSource.InputControl, sideTabContent)
					|| ActiveTab == null)
					return false;

				SideTabItem item = ActiveTab.Items
					.Cast<SideTabItem>()
					.FirstOrDefault(candidate => ReferenceEquals(candidate.Tag, dragSource.Tool));
				if (item == null)
					return false;

				System.Windows.Forms.DragDropEffects effect = sideTabContent.DoDragDrop(
					dragSource.DragData,
					ActiveTab.CanDragDrop
						? System.Windows.Forms.DragDropEffects.All
						: System.Windows.Forms.DragDropEffects.Copy
							| System.Windows.Forms.DragDropEffects.None);
				return effect != System.Windows.Forms.DragDropEffects.None;
			}

			internal bool TryLocateDragSource(
				SideTabItem item,
				CreateComponentTool tool,
				out WpfToolboxDragSource dragSource)
			{
				dragSource = default(WpfToolboxDragSource);
				if (item == null
					|| tool == null
					|| ActiveTab == null
					|| !ActiveTab.Items.Contains(item)
					|| !ReferenceEquals(item.Tag, tool)
					|| sideTabContent == null
					|| !sideTabContent.Visible
					|| sideTabContent.Width <= 2
					|| sideTabContent.Height < ActiveTab.ItemHeight)
					return false;

				int itemIndex = ActiveTab.Items.IndexOf(item);
				int visibleIndex = itemIndex - ActiveTab.ScrollIndex;
				int itemTop = visibleIndex * ActiveTab.ItemHeight;
				int itemBottom = itemTop + ActiveTab.ItemHeight;
				if (visibleIndex < 0
					|| itemTop < 0
					|| itemBottom > sideTabContent.ClientSize.Height)
					return false;

				var inputPoint = new Drawing.Point(
					Math.Max(1, sideTabContent.ClientSize.Width / 2),
					itemTop + ActiveTab.ItemHeight / 2);
				if (!sideTabContent.ClientRectangle.Contains(inputPoint))
					return false;

				dragSource = new WpfToolboxDragSource(
					sideTabContent,
					tool,
					inputPoint,
					new System.Windows.Forms.DataObject(
						new System.Windows.DataObject(tool)));
				return true;
			}

			protected override object StartItemDrag(SideTabItem draggedItem)
			{
				if (this.ActiveTab.ChosenItem != draggedItem && this.ActiveTab.Items.Contains(draggedItem)) {
					this.ActiveTab.ChosenItem = draggedItem;
				}
				
				if (draggedItem.Tag != null) {
					return new System.Windows.DataObject(draggedItem.Tag);
				}
				else {
					return new System.Windows.DataObject();
				}
			}
		}
	}
}
