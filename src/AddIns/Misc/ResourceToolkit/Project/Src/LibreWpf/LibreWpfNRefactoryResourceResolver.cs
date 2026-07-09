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
using System.Collections.Generic;

using ICSharpCode.NRefactory.Editor;

namespace Hornung.ResourceToolkit.Resolver
{
	/// <summary>
	/// Keeps the existing ResourceToolkit add-in resolver identity loadable in the
	/// LibreWPF build while the old NRefactory AST-backed resolver is disabled.
	/// </summary>
	public sealed class NRefactoryResourceResolver : AbstractResourceResolver
	{
		static readonly string[] noPatterns = new string[0];

		public override bool SupportsFile(string fileName)
		{
			return false;
		}

		public override IEnumerable<string> GetPossiblePatternsForFile(string fileName)
		{
			return noPatterns;
		}

		protected override ResourceResolveResult Resolve(string fileName, IDocument document, int caretLine, int caretColumn, int caretOffset, char? charTyped)
		{
			return null;
		}
	}
}
#endif
