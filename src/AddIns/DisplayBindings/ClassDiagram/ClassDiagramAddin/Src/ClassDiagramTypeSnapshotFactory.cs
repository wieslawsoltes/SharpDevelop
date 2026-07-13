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
using System.Linq;

using ClassDiagram;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.SharpDevelop;

namespace ClassDiagramAddin
{
	/// <summary>
	/// Projects the live NRefactory type system into immutable ClassCanvas data.
	/// ClassCanvas deliberately has no dependency on SharpDevelop parser services.
	/// </summary>
	internal sealed class ClassDiagramTypeSnapshotFactory
	{
		readonly IAmbience ambience;

		ClassDiagramTypeSnapshotFactory(ICompilation compilation)
		{
			if (compilation == null)
				throw new ArgumentNullException("compilation");
			ambience = compilation.GetAmbience();
		}

		public static ClassDiagramTypeCatalog CreateCatalog(ICompilation compilation)
		{
			var factory = new ClassDiagramTypeSnapshotFactory(compilation);
			ClassDiagramTypeSnapshot[] roots = compilation.MainAssembly.TopLevelTypeDefinitions
				.Select(factory.CreateSnapshot)
				.ToArray();
			return new ClassDiagramTypeCatalog(Flatten(roots));
		}

		public static IList<ClassDiagramTypeSnapshot> CreateTopLevelSnapshots(ICompilation compilation)
		{
			var factory = new ClassDiagramTypeSnapshotFactory(compilation);
			return compilation.MainAssembly.TopLevelTypeDefinitions
				.Select(factory.CreateSnapshot)
				.ToArray();
		}

		ClassDiagramTypeSnapshot CreateSnapshot(ITypeDefinition type)
		{
			if (type == null)
				throw new ArgumentNullException("type");

			IType baseType = type.DirectBaseTypes.FirstOrDefault(candidate => candidate.Kind != TypeKind.Interface);
			IEnumerable<IType> interfaceTypes = type.DirectBaseTypes.Where(candidate => candidate.Kind == TypeKind.Interface);
			IMethod delegateInvoke = type.Kind == TypeKind.Delegate
				? type.Methods.FirstOrDefault(method => string.Equals(method.Name, "Invoke", StringComparison.Ordinal))
				: null;

			return new ClassDiagramTypeSnapshot(
				type.FullName,
				type.Name,
				MapTypeKind(type.Kind),
				BuildModifierDisplayText(type),
				type.IsAbstract,
				type.IsSealed,
				type.IsStatic,
				CreateTypeReference(baseType),
				interfaceTypes.Select(CreateTypeReference).Where(reference => reference != null),
				type.NestedTypes.Select(CreateSnapshot),
				CreateMemberSnapshots(type.Properties, ClassDiagramMemberKind.Property),
				CreateMemberSnapshots(type.Methods, ClassDiagramMemberKind.Method),
				CreateMemberSnapshots(type.Fields, ClassDiagramMemberKind.Field),
				CreateMemberSnapshots(type.Events, ClassDiagramMemberKind.Event),
				delegateInvoke == null
					? null
					: delegateInvoke.Parameters.Select(parameter => new ClassDiagramParameterSnapshot(ConvertSymbol(parameter))));
		}

		IEnumerable<ClassDiagramMemberSnapshot> CreateMemberSnapshots<TMember>(
			IEnumerable<TMember> members,
			ClassDiagramMemberKind kind)
			where TMember : IMember
		{
			return members
				.Where(member => !member.IsSynthetic)
				.Select(member => new ClassDiagramMemberSnapshot(kind, ConvertSymbol(member)));
		}

		ClassDiagramTypeReferenceSnapshot CreateTypeReference(IType type)
		{
			if (type == null || type.Kind == TypeKind.Unknown)
				return null;

			return new ClassDiagramTypeReferenceSnapshot(
				type.FullName,
				ConvertType(type),
				MapTypeKind(type.Kind));
		}

		string ConvertSymbol(ISymbol symbol)
		{
			ConversionFlags oldFlags = ambience.ConversionFlags;
			try {
				ambience.ConversionFlags = ConversionFlags.StandardConversionFlags;
				return ambience.ConvertSymbol(symbol);
			} finally {
				ambience.ConversionFlags = oldFlags;
			}
		}

		string ConvertType(IType type)
		{
			ConversionFlags oldFlags = ambience.ConversionFlags;
			try {
				ambience.ConversionFlags = ConversionFlags.ShowTypeParameterList;
				return ambience.ConvertType(type);
			} finally {
				ambience.ConversionFlags = oldFlags;
			}
		}

		static string BuildModifierDisplayText(ITypeDefinition type)
		{
			var modifiers = new List<string>();
			switch (type.Accessibility) {
				case Accessibility.Private:
					modifiers.Add("private");
					break;
				case Accessibility.Public:
					modifiers.Add("public");
					break;
				case Accessibility.Protected:
					modifiers.Add("protected");
					break;
				case Accessibility.Internal:
					modifiers.Add("internal");
					break;
				case Accessibility.ProtectedOrInternal:
					modifiers.Add("protected internal");
					break;
				case Accessibility.ProtectedAndInternal:
					modifiers.Add("private protected");
					break;
			}

			if (type.IsStatic) {
				modifiers.Add("static");
			} else {
				if (type.IsAbstract && type.Kind == TypeKind.Class)
					modifiers.Add("abstract");
				if (type.IsSealed && type.Kind == TypeKind.Class)
					modifiers.Add("sealed");
			}

			return string.Join(" ", modifiers);
		}

		static ClassDiagramTypeKind MapTypeKind(TypeKind kind)
		{
			switch (kind) {
				case TypeKind.Interface:
					return ClassDiagramTypeKind.Interface;
				case TypeKind.Struct:
					return ClassDiagramTypeKind.Struct;
				case TypeKind.Enum:
					return ClassDiagramTypeKind.Enum;
				case TypeKind.Delegate:
					return ClassDiagramTypeKind.Delegate;
				default:
					return ClassDiagramTypeKind.Class;
			}
		}

		static IEnumerable<ClassDiagramTypeSnapshot> Flatten(IEnumerable<ClassDiagramTypeSnapshot> roots)
		{
			foreach (ClassDiagramTypeSnapshot root in roots) {
				yield return root;
				foreach (ClassDiagramTypeSnapshot nested in Flatten(root.NestedTypes))
					yield return nested;
			}
		}
	}
}
