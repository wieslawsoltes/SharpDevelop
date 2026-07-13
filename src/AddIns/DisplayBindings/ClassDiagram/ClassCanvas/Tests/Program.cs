using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

using ClassDiagram;

internal static class Program
{
    [STAThread]
    static int Main()
    {
        ClassDiagramTypeSnapshot baseType = CreateBaseType();
        ClassDiagramTypeSnapshot widgetType = CreateWidgetType(baseType);
        ClassDiagramTypeSnapshot delegateType = CreateDelegateType();
        ClassDiagramTypeSnapshot interfaceType = CreateInterfaceType();
        ClassDiagramTypeSnapshot[] types = { baseType, widgetType, delegateType, interfaceType };

        VerifySnapshotCollectionsAreReadOnly(widgetType);

        using (ClassCanvas canvas = new ClassCanvas())
        {
            canvas.Size = new Size(480, 360);
            canvas.Zoom = 1.25f;

            ClassCanvasItem baseItem = AddType(canvas, baseType, 60, 60);
            ClassCanvasItem widgetItem = AddType(canvas, widgetType, 60, 220);
            ClassCanvasItem delegateItem = AddType(canvas, delegateType, 310, 60);
            ClassCanvasItem interfaceItem = AddType(canvas, interfaceType, 310, 210);
            NoteCanvasItem note = new NoteCanvasItem {
                Note = "LibreWinForms ClassDiagram",
                X = 310,
                Y = 300,
                Width = 180,
                Height = 70
            };
            canvas.AddCanvasItem(note);

            Require(canvas.Contains(widgetType), "Snapshot identity was not indexed.");
            Require(canvas.Contains("Demo.Widget"), "Full-name identity was not indexed.");

            string xml = canvas.WriteToXml().OuterXml;
            Require(xml.Contains("Name=\"Demo.Widget\"", StringComparison.Ordinal), "Type identity was not persisted.");
            Require(xml.Contains("CommentText=\"LibreWinForms ClassDiagram\"", StringComparison.Ordinal), "Note text was not persisted.");

            int pngLength;
            int bitmapWidth;
            int bitmapHeight;
            using (Bitmap bitmap = canvas.GetAsBitmap())
            using (MemoryStream png = new MemoryStream())
            {
                bitmap.Save(png, ImageFormat.Png);
                pngLength = checked((int)png.Length);
                bitmapWidth = bitmap.Width;
                bitmapHeight = bitmap.Height;
            }
            Require(bitmapWidth >= 400 && bitmapHeight >= 300, "Canvas bitmap bounds were truncated.");
            Require(pngLength > 256, "Canvas PNG payload was empty.");

            SmokeWindowsFormsHost host = new SmokeWindowsFormsHost { Child = canvas };
            host.Measure(new System.Windows.Size(480, 360));
            host.Arrange(new System.Windows.Rect(0, 0, 480, 360));
            System.Windows.Media.DrawingVisual visual = new System.Windows.Media.DrawingVisual();
            using (System.Windows.Media.DrawingContext context = visual.RenderOpen())
                host.RenderForSmoke(context);
            int drawingLeaves = CountLeaves(visual.Drawing);
            host.Child = null;
            Require(drawingLeaves > 0, "WindowsFormsHost produced no WPF drawing leaves.");

            using (ClassCanvas reloaded = new ClassCanvas())
            {
                reloaded.LoadFromXml(canvas.WriteToXml(), new ClassDiagramTypeCatalog(types));
                Require(reloaded.Contains("Demo.Base"), "Base type did not resolve during XML load.");
                Require(reloaded.Contains("Demo.Widget"), "Derived type did not resolve during XML load.");
                Require(reloaded.GetCanvasItems().Length == 5, "XML load did not restore all canvas items.");
                using (Bitmap reloadedBitmap = reloaded.GetAsBitmap())
                    Require(reloadedBitmap.Width >= 400, "Reloaded canvas bitmap bounds were truncated.");
                reloaded.ClearCanvas();
                Require(reloaded.GetCanvasItems().Length == 0, "ClearCanvas retained items.");
                reloaded.Dispose();
            }

            canvas.RemoveCanvasItem(delegateItem);
            delegateItem.Dispose();
            Require(!canvas.Contains(delegateType), "RemoveCanvasItem retained type identity.");
            canvas.RemoveCanvasItem(interfaceItem);
            interfaceItem.Dispose();
            canvas.ClearCanvas();
            Require(canvas.GetCanvasItems().Length == 0, "Canvas retained items after teardown.");
            baseItem.Dispose();
            widgetItem.Dispose();
            note.Dispose();

            Console.WriteLine(
                "ClassCanvas LibreWPF smoke passed: bitmap={0}x{1} png={2} leaves={3}",
                bitmapWidth,
                bitmapHeight,
                pngLength,
                drawingLeaves);
        }

        return 0;
    }

    static ClassCanvasItem AddType(ClassCanvas canvas, ClassDiagramTypeSnapshot type, float x, float y)
    {
        ClassCanvasItem item = ClassCanvas.CreateItemFromType(type);
        item.X = x;
        item.Y = y;
        canvas.AddCanvasItem(item);
        return item;
    }

    static ClassDiagramTypeSnapshot CreateBaseType()
    {
        return new ClassDiagramTypeSnapshot(
            "Demo.Base",
            "Base",
            ClassDiagramTypeKind.Class,
            "public",
            methods: new[] {
                new ClassDiagramMemberSnapshot(ClassDiagramMemberKind.Method, "Run() : void")
            });
    }

    static ClassDiagramTypeSnapshot CreateWidgetType(ClassDiagramTypeSnapshot baseType)
    {
        return new ClassDiagramTypeSnapshot(
            "Demo.Widget",
            "Widget",
            ClassDiagramTypeKind.Class,
            "public abstract",
            isAbstract: true,
            baseClass: new ClassDiagramTypeReferenceSnapshot(baseType.FullName, baseType.Name),
            interfaces: new[] {
                new ClassDiagramTypeReferenceSnapshot("Demo.IWidget", "IWidget", ClassDiagramTypeKind.Interface)
            },
            nestedTypes: new[] {
                new ClassDiagramTypeSnapshot(
                    "Demo.Widget.State",
                    "State",
                    ClassDiagramTypeKind.Enum,
                    "public",
                    fields: new[] {
                        new ClassDiagramMemberSnapshot(ClassDiagramMemberKind.Field, "Ready : State")
                    })
            },
            properties: new[] {
                new ClassDiagramMemberSnapshot(ClassDiagramMemberKind.Property, "Name : string")
            },
            methods: new[] {
                new ClassDiagramMemberSnapshot(ClassDiagramMemberKind.Method, "Render(int width) : void")
            },
            fields: new[] {
                new ClassDiagramMemberSnapshot(ClassDiagramMemberKind.Field, "count : int")
            },
            events: new[] {
                new ClassDiagramMemberSnapshot(ClassDiagramMemberKind.Event, "Changed : EventHandler")
            });
    }

    static ClassDiagramTypeSnapshot CreateDelegateType()
    {
        return new ClassDiagramTypeSnapshot(
            "Demo.WidgetChanged",
            "WidgetChanged",
            ClassDiagramTypeKind.Delegate,
            "public",
            delegateParameters: new[] {
                new ClassDiagramParameterSnapshot("sender : object"),
                new ClassDiagramParameterSnapshot("name : string")
            });
    }

    static ClassDiagramTypeSnapshot CreateInterfaceType()
    {
        return new ClassDiagramTypeSnapshot(
            "Demo.IWidget",
            "IWidget",
            ClassDiagramTypeKind.Interface,
            "public",
            methods: new[] {
                new ClassDiagramMemberSnapshot(ClassDiagramMemberKind.Method, "Render() : void")
            });
    }

    static void VerifySnapshotCollectionsAreReadOnly(ClassDiagramTypeSnapshot type)
    {
        bool rejectedMutation = false;
        try {
            ((IList<ClassDiagramMemberSnapshot>)type.Methods).Add(
                new ClassDiagramMemberSnapshot(ClassDiagramMemberKind.Method, "Invalid() : void"));
        } catch (NotSupportedException) {
            rejectedMutation = true;
        }
        Require(rejectedMutation, "Snapshot member collections were mutable.");
    }

    static int CountLeaves(System.Windows.Media.Drawing drawing)
    {
        if (drawing == null)
            return 0;
        System.Windows.Media.DrawingGroup group = drawing as System.Windows.Media.DrawingGroup;
        if (group == null)
            return 1;

        int count = 0;
        foreach (System.Windows.Media.Drawing child in group.Children)
            count += CountLeaves(child);
        return count;
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    sealed class SmokeWindowsFormsHost : System.Windows.Forms.Integration.WindowsFormsHost
    {
        public void RenderForSmoke(System.Windows.Media.DrawingContext context)
        {
            OnRender(context);
        }
    }
}
