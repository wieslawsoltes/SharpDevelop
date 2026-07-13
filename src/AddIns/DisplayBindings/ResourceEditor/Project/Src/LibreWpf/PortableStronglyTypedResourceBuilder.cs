#if LIBREWPF
// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
// of the Software, and to permit persons to whom the Software is furnished to do
// so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Resources.Tools;
using System.Runtime.CompilerServices;

namespace ResourceEditor
{
	/// <summary>
	/// Builds the portable subset of a strongly typed resource class. LibreWinForms
	/// exposes resource-name verification, while the desktop-only builder remains in
	/// System.Windows.Forms.Design; keeping generation here avoids loading that
	/// Windows-specific designer assembly into the LibreWPF workbench.
	/// </summary>
	static class PortableStronglyTypedResourceBuilder
	{
		const string ResourceManagerFieldName = "resourceMan";
		const string ResourceCultureFieldName = "resourceCulture";
		const string ResourceManagerPropertyName = "ResourceManager";
		const string CulturePropertyName = "Culture";

		public static CodeCompileUnit Create(
			IDictionary resources,
			string baseName,
			string generatedCodeNamespace,
			CodeDomProvider codeProvider,
			bool createInternalClass,
			out string[] unmatchable)
		{
			if (resources == null)
				throw new ArgumentNullException("resources");
			if (baseName == null)
				throw new ArgumentNullException("baseName");
			if (codeProvider == null)
				throw new ArgumentNullException("codeProvider");

			string className = StronglyTypedResourceBuilder.VerifyResourceName(baseName, codeProvider);
			if (string.IsNullOrEmpty(className) || !codeProvider.IsValidIdentifier(className)) {
				throw new ArgumentException("The resource file name cannot be converted to a valid class name.", "baseName");
			}

			var compileUnit = new CodeCompileUnit();
			var codeNamespace = new CodeNamespace(generatedCodeNamespace ?? String.Empty);
			compileUnit.Namespaces.Add(codeNamespace);

			var resourceClass = new CodeTypeDeclaration(className) {
				IsClass = true,
				TypeAttributes = (createInternalClass ? TypeAttributes.NotPublic : TypeAttributes.Public)
					| TypeAttributes.Sealed
					| TypeAttributes.BeforeFieldInit
			};
			resourceClass.CustomAttributes.Add(CreateAttribute(
				typeof(GeneratedCodeAttribute),
				typeof(StronglyTypedResourceBuilder).FullName,
				"LibreWPF"));
			resourceClass.CustomAttributes.Add(CreateAttribute(typeof(DebuggerNonUserCodeAttribute)));
			resourceClass.CustomAttributes.Add(CreateAttribute(typeof(CompilerGeneratedAttribute)));
			codeNamespace.Types.Add(resourceClass);

			resourceClass.Members.Add(new CodeConstructor {
				Attributes = MemberAttributes.Private
			});
			resourceClass.Members.Add(new CodeMemberField(typeof(ResourceManager), ResourceManagerFieldName) {
				Attributes = MemberAttributes.Private | MemberAttributes.Static
			});
			resourceClass.Members.Add(new CodeMemberField(typeof(CultureInfo), ResourceCultureFieldName) {
				Attributes = MemberAttributes.Private | MemberAttributes.Static
			});

			MemberAttributes publicScope = createInternalClass ? MemberAttributes.Assembly : MemberAttributes.Public;
			resourceClass.Members.Add(CreateResourceManagerProperty(
				className,
				String.IsNullOrEmpty(generatedCodeNamespace)
					? baseName
					: generatedCodeNamespace + "." + baseName,
				publicScope));
			resourceClass.Members.Add(CreateCultureProperty(publicScope));

			var invalidNames = new List<string>();
			var generatedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
				className,
				ResourceManagerPropertyName,
				CulturePropertyName
			};

			foreach (DictionaryEntry entry in resources) {
				string key = entry.Key as string;
				string propertyName = key == null
					? null
					: StronglyTypedResourceBuilder.VerifyResourceName(key, codeProvider);
				if (String.IsNullOrEmpty(key)
				    || String.IsNullOrEmpty(propertyName)
				    || !codeProvider.IsValidIdentifier(propertyName)
				    || !generatedNames.Add(propertyName))
				{
					invalidNames.Add(key ?? String.Empty);
					continue;
				}

				resourceClass.Members.Add(CreateResourceProperty(
					key,
					propertyName,
					entry.Value == null ? typeof(object) : entry.Value.GetType(),
					publicScope));
			}

			unmatchable = invalidNames.ToArray();
			return compileUnit;
		}

		static CodeMemberProperty CreateResourceManagerProperty(
			string className,
			string resourceBaseName,
			MemberAttributes publicScope)
		{
			var field = new CodeFieldReferenceExpression(null, ResourceManagerFieldName);
			var property = new CodeMemberProperty {
				Name = ResourceManagerPropertyName,
				Type = new CodeTypeReference(typeof(ResourceManager)),
				Attributes = publicScope | MemberAttributes.Static,
				HasGet = true,
				HasSet = false
			};
			property.CustomAttributes.Add(CreateEditorBrowsableAttribute());
			property.GetStatements.Add(new CodeConditionStatement(
				new CodeBinaryOperatorExpression(
					field,
					CodeBinaryOperatorType.IdentityEquality,
					new CodePrimitiveExpression(null)),
				new CodeAssignStatement(
					field,
					new CodeObjectCreateExpression(
						typeof(ResourceManager),
						new CodePrimitiveExpression(resourceBaseName),
						new CodePropertyReferenceExpression(
							new CodeTypeOfExpression(className),
							"Assembly")))));
			property.GetStatements.Add(new CodeMethodReturnStatement(field));
			return property;
		}

		static CodeMemberProperty CreateCultureProperty(MemberAttributes publicScope)
		{
			var field = new CodeFieldReferenceExpression(null, ResourceCultureFieldName);
			var property = new CodeMemberProperty {
				Name = CulturePropertyName,
				Type = new CodeTypeReference(typeof(CultureInfo)),
				Attributes = publicScope | MemberAttributes.Static,
				HasGet = true,
				HasSet = true
			};
			property.CustomAttributes.Add(CreateEditorBrowsableAttribute());
			property.GetStatements.Add(new CodeMethodReturnStatement(field));
			property.SetStatements.Add(new CodeAssignStatement(
				field,
				new CodePropertySetValueReferenceExpression()));
			return property;
		}

		static CodeMemberProperty CreateResourceProperty(
			string resourceName,
			string propertyName,
			Type resourceType,
			MemberAttributes publicScope)
		{
			var property = new CodeMemberProperty {
				Name = propertyName,
				Type = new CodeTypeReference(resourceType),
				Attributes = publicScope | MemberAttributes.Static,
				HasGet = true,
				HasSet = false
			};

			var resourceManager = new CodePropertyReferenceExpression(null, ResourceManagerPropertyName);
			var culture = new CodeFieldReferenceExpression(null, ResourceCultureFieldName);
			var lookup = new CodeMethodInvokeExpression(
				resourceManager,
				resourceType == typeof(string) ? "GetString" : "GetObject",
				new CodePrimitiveExpression(resourceName),
				culture);
			CodeExpression value = resourceType == typeof(string)
				? (CodeExpression)lookup
				: new CodeCastExpression(resourceType, lookup);
			property.GetStatements.Add(new CodeMethodReturnStatement(value));
			return property;
		}

		static CodeAttributeDeclaration CreateEditorBrowsableAttribute()
		{
			return new CodeAttributeDeclaration(
				new CodeTypeReference(typeof(EditorBrowsableAttribute)),
				new CodeAttributeArgument(
					new CodeFieldReferenceExpression(
						new CodeTypeReferenceExpression(typeof(EditorBrowsableState)),
						"Advanced")));
		}

		static CodeAttributeDeclaration CreateAttribute(Type attributeType, params object[] arguments)
		{
			var attribute = new CodeAttributeDeclaration(new CodeTypeReference(attributeType));
			foreach (object argument in arguments) {
				attribute.Arguments.Add(new CodeAttributeArgument(new CodePrimitiveExpression(argument)));
			}
			return attribute;
		}
	}
}
#endif
