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
using System.Collections.ObjectModel;

namespace ClassDiagram
{
	public enum ClassDiagramTypeKind
	{
		Class,
		Interface,
		Struct,
		Enum,
		Delegate
	}

	public enum ClassDiagramMemberKind
	{
		Method,
		Property,
		Field,
		Event
	}

	public sealed class ClassDiagramTypeReferenceSnapshot
	{
		public ClassDiagramTypeReferenceSnapshot(
			string fullName,
			string displayName,
			ClassDiagramTypeKind kind = ClassDiagramTypeKind.Class)
		{
			if (String.IsNullOrEmpty(fullName))
				throw new ArgumentException("A stable type identity is required.", "fullName");
			if (String.IsNullOrEmpty(displayName))
				throw new ArgumentException("A type display name is required.", "displayName");

			FullName = fullName;
			DisplayName = displayName;
			Kind = kind;
		}

		public string FullName { get; private set; }

		public string DisplayName { get; private set; }

		public ClassDiagramTypeKind Kind { get; private set; }
	}

	public sealed class ClassDiagramMemberSnapshot
	{
		public ClassDiagramMemberSnapshot(ClassDiagramMemberKind kind, string displayText)
		{
			if (String.IsNullOrEmpty(displayText))
				throw new ArgumentException("Member display text is required.", "displayText");

			Kind = kind;
			DisplayText = displayText;
		}

		public ClassDiagramMemberKind Kind { get; private set; }

		public string DisplayText { get; private set; }
	}

	public sealed class ClassDiagramParameterSnapshot
	{
		public ClassDiagramParameterSnapshot(string displayText)
		{
			if (String.IsNullOrEmpty(displayText))
				throw new ArgumentException("Parameter display text is required.", "displayText");

			DisplayText = displayText;
		}

		public string DisplayText { get; private set; }
	}

	public sealed class ClassDiagramTypeSnapshot
	{
		public ClassDiagramTypeSnapshot(
			string fullName,
			string name,
			ClassDiagramTypeKind kind,
			string modifierDisplayText = "",
			bool isAbstract = false,
			bool isSealed = false,
			bool isStatic = false,
			ClassDiagramTypeReferenceSnapshot baseClass = null,
			IEnumerable<ClassDiagramTypeReferenceSnapshot> interfaces = null,
			IEnumerable<ClassDiagramTypeSnapshot> nestedTypes = null,
			IEnumerable<ClassDiagramMemberSnapshot> properties = null,
			IEnumerable<ClassDiagramMemberSnapshot> methods = null,
			IEnumerable<ClassDiagramMemberSnapshot> fields = null,
			IEnumerable<ClassDiagramMemberSnapshot> events = null,
			IEnumerable<ClassDiagramParameterSnapshot> delegateParameters = null)
		{
			if (String.IsNullOrEmpty(fullName))
				throw new ArgumentException("A stable type identity is required.", "fullName");
			if (String.IsNullOrEmpty(name))
				throw new ArgumentException("A type name is required.", "name");

			FullName = fullName;
			Name = name;
			Kind = kind;
			ModifierDisplayText = modifierDisplayText ?? String.Empty;
			IsAbstract = isAbstract;
			IsSealed = isSealed;
			IsStatic = isStatic;
			BaseClass = baseClass;
			Interfaces = Copy(interfaces);
			NestedTypes = Copy(nestedTypes);
			Properties = Copy(properties);
			Methods = Copy(methods);
			Fields = Copy(fields);
			Events = Copy(events);
			DelegateParameters = Copy(delegateParameters);
		}

		public string FullName { get; private set; }

		public string Name { get; private set; }

		public ClassDiagramTypeKind Kind { get; private set; }

		public string ModifierDisplayText { get; private set; }

		public bool IsAbstract { get; private set; }

		public bool IsSealed { get; private set; }

		public bool IsStatic { get; private set; }

		public ClassDiagramTypeReferenceSnapshot BaseClass { get; private set; }

		public ReadOnlyCollection<ClassDiagramTypeReferenceSnapshot> Interfaces { get; private set; }

		public ReadOnlyCollection<ClassDiagramTypeSnapshot> NestedTypes { get; private set; }

		public ReadOnlyCollection<ClassDiagramMemberSnapshot> Properties { get; private set; }

		public ReadOnlyCollection<ClassDiagramMemberSnapshot> Methods { get; private set; }

		public ReadOnlyCollection<ClassDiagramMemberSnapshot> Fields { get; private set; }

		public ReadOnlyCollection<ClassDiagramMemberSnapshot> Events { get; private set; }

		public ReadOnlyCollection<ClassDiagramParameterSnapshot> DelegateParameters { get; private set; }

		public string DeclarationDisplayText
		{
			get
			{
				string kindText = Kind.ToString();
				if (ModifierDisplayText.Length == 0)
					return kindText;
				return ModifierDisplayText + " " + kindText;
			}
		}

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> source)
		{
			if (source == null)
				return new List<T>().AsReadOnly();

			List<T> items = new List<T>();
			foreach (T item in source)
			{
				if (item == null)
					throw new ArgumentException("Snapshot collections cannot contain null values.", "source");
				items.Add(item);
			}
			return items.AsReadOnly();
		}
	}

	public interface IClassDiagramTypeResolver
	{
		bool TryResolve(string fullName, out ClassDiagramTypeSnapshot type);
	}

	public sealed class ClassDiagramTypeCatalog : IClassDiagramTypeResolver
	{
		readonly Dictionary<string, ClassDiagramTypeSnapshot> types =
			new Dictionary<string, ClassDiagramTypeSnapshot>(StringComparer.Ordinal);

		public ClassDiagramTypeCatalog(IEnumerable<ClassDiagramTypeSnapshot> types)
		{
			if (types == null)
				throw new ArgumentNullException("types");

			foreach (ClassDiagramTypeSnapshot type in types)
			{
				if (type == null)
					throw new ArgumentException("The type catalog cannot contain null values.", "types");
				this.types.Add(type.FullName, type);
			}
		}

		public bool TryResolve(string fullName, out ClassDiagramTypeSnapshot type)
		{
			if (fullName == null)
			{
				type = null;
				return false;
			}
			return types.TryGetValue(fullName, out type);
		}
	}
}
