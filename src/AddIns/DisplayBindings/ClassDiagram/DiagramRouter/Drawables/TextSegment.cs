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
using System.Drawing.Drawing2D;

namespace Tools.Diagrams.Drawables
{
	public class TextSegment : BaseRectangle, IDrawableRectangle
	{
		float tw;
		Font font;
		Brush brush = Brushes.Black;
		Graphics g;
		string text;
		StringFormat sf = new StringFormat();
		bool ownsFont;
		
		public TextSegment (Graphics graphics, string text)
			: this (graphics, text, new Font(System.Drawing.FontFamily.GenericSansSerif, 10.0f), false, true)
		{
		}
		
		public TextSegment (Graphics graphics, string text, Font font, bool resizable)
			: this(graphics, text, font, resizable, false)
		{
		}

		TextSegment(Graphics graphics, string text, Font font, bool resizable, bool ownsFont)
		{
			if (graphics == null) throw new ArgumentNullException("graphics");
			this.g = graphics;
			this.text = text;
			this.font = font;
			this.ownsFont = ownsFont;
			sf.Trimming = StringTrimming.EllipsisCharacter;
			MeasureString();
			if (resizable)
				Width = -1;
			else
				Width = float.NaN;
		}
			
		private void MeasureString ()
		{
			if (text != null && font != null && g != null)
				tw = g.MeasureString(text, font).Width;
			else
				tw = float.NaN;
		}
		
		public override float Width
		{
			get
			{
				if (float.IsNaN(base.Width)) return tw;
				return base.Width;
			}
			set { base.Width = value; }
		}

		public float TextWidth
		{
			get { return tw; }
		}
		
		public override float Height
		{
			get { return font.Size * 1.2f; }
			set {}
		}
		
		public override float ActualHeight
		{
			get { return Height; }
			set { base.ActualHeight = value; }
		}

		public string Text
		{
			get { return text; }
			set
			{
				text = value;
				MeasureString();
			}
		}
		
		public Font Font
		{
			get { return font; }
			set
			{
				if (Object.ReferenceEquals(font, value))
					return;
				if (ownsFont && font != null)
					font.Dispose();
				font = value;
				ownsFont = false;
				MeasureString();
			}
		}
		
		public Brush Brush
		{
			get { return brush; }
			set { brush = value; }
		}
		
		public void DrawToGraphics (Graphics graphics)
		{
			if (graphics == null) return;
			RectangleF rect = new RectangleF(AbsoluteX, AbsoluteY, ActualWidth, ActualHeight);
			graphics.DrawString(Text, Font, Brush, rect, sf);
		}
		
		protected override void Dispose(bool disposing)
		{
			if (IsDisposed)
				return;
			if (disposing)
			{
				if (sf != null)
				{
					sf.Dispose();
					sf = null;
				}
				if (ownsFont && font != null)
				{
					font.Dispose();
					font = null;
				}
				g = null;
			}
			base.Dispose(disposing);
		}
		
		public override float GetAbsoluteContentWidth()
		{
			return TextWidth + 20;
		}
		
		public override float GetAbsoluteContentHeight()
		{
			return Height;
		}
	}
}
