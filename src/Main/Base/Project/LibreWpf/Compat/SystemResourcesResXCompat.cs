#if LIBREWPF_LEGACY_RESX_COMPAT
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace System.Resources
{
	public sealed class ResXResourceReader : IResourceReader
	{
		readonly List<PortableResXEntry> dataEntries;
		readonly List<PortableResXEntry> metadataEntries;

		public ResXResourceReader(string fileName)
		{
			FileName = fileName;
			dataEntries = new List<PortableResXEntry>();
			metadataEntries = new List<PortableResXEntry>();
			Load(fileName);
		}

		public ResXResourceReader(Stream stream)
		{
			if (stream == null)
				throw new ArgumentNullException("stream");
			FileName = string.Empty;
			dataEntries = new List<PortableResXEntry>();
			metadataEntries = new List<PortableResXEntry>();
			Load(stream);
		}

		public string FileName { get; }

		public string BasePath { get; set; }

		public bool UseResXDataNodes { get; set; }

		public IDictionaryEnumerator GetEnumerator()
		{
			return new PortableDictionaryEnumerator(dataEntries.Select(CreateDictionaryEntry).ToList());
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public IDictionaryEnumerator GetMetadataEnumerator()
		{
			return new PortableDictionaryEnumerator(metadataEntries.Select(CreateDictionaryEntry).ToList());
		}

		public void Dispose()
		{
		}

		public void Close()
		{
			Dispose();
		}

		DictionaryEntry CreateDictionaryEntry(PortableResXEntry entry)
		{
			return new DictionaryEntry(entry.Name, UseResXDataNodes ? (object)entry : entry.GetRuntimeValue());
		}

		void Load(string fileName)
		{
			if (!File.Exists(fileName))
				return;

			using (FileStream stream = File.OpenRead(fileName)) {
				Load(stream);
			}
		}

		void Load(Stream stream)
		{
			XDocument document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
			XElement root = document.Root;
			if (root == null)
				return;

			foreach (XElement element in root.Elements()) {
				if (element.Name.LocalName == "data") {
					dataEntries.Add(PortableResXEntry.FromElement(element, false));
				} else if (element.Name.LocalName == "metadata") {
					metadataEntries.Add(PortableResXEntry.FromElement(element, true));
				}
			}
		}
	}

	public sealed class ResXResourceWriter : IResourceWriter
	{
		readonly Stream stream;
		readonly bool ownsStream;
		readonly List<PortableResXEntry> dataEntries = new List<PortableResXEntry>();
		readonly List<PortableResXEntry> metadataEntries = new List<PortableResXEntry>();
		bool generated;

		public ResXResourceWriter(string fileName)
			: this(File.Create(fileName), null)
		{
			ownsStream = true;
		}

		public ResXResourceWriter(Stream stream)
			: this(stream, null)
		{
		}

		public ResXResourceWriter(Stream stream, Func<Type, string> typeNameConverter)
		{
			if (stream == null)
				throw new ArgumentNullException("stream");
			this.stream = stream;
			TypeNameConverter = typeNameConverter ?? DefaultTypeNameConverter;
		}

		public Stream Stream {
			get { return stream; }
		}

		public Func<Type, string> TypeNameConverter { get; }

		public void AddResource(string name, object value)
		{
			dataEntries.Add(PortableResXEntry.FromObject(name, value, false, TypeNameConverter));
		}

		public void AddResource(string name, string value)
		{
			AddResource(name, (object)value);
		}

		public void AddResource(string name, byte[] value)
		{
			AddResource(name, (object)value);
		}

		public void AddMetadata(string name, object value)
		{
			metadataEntries.Add(PortableResXEntry.FromObject(name, value, true, TypeNameConverter));
		}

		public void Generate()
		{
			if (generated)
				return;
			generated = true;

			if (stream.CanSeek) {
				stream.Position = 0;
				stream.SetLength(0);
			}

			XDocument document = CreateDocument();
			var settings = new XmlWriterSettings {
				Encoding = new UTF8Encoding(false),
				Indent = true
			};
			using (XmlWriter writer = XmlWriter.Create(stream, settings)) {
				document.Save(writer);
			}
		}

		public void Close()
		{
			Dispose();
		}

		public void Dispose()
		{
			Generate();
			if (ownsStream)
				stream.Dispose();
		}

		XDocument CreateDocument()
		{
			var root = new XElement("root",
				new XElement("resheader",
					new XAttribute("name", "resmimetype"),
					new XElement("value", "text/microsoft-resx")),
				new XElement("resheader",
					new XAttribute("name", "version"),
					new XElement("value", "2.0")),
				new XElement("resheader",
					new XAttribute("name", "reader"),
					new XElement("value", "System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")),
				new XElement("resheader",
					new XAttribute("name", "writer"),
					new XElement("value", "System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")));

			foreach (PortableResXEntry entry in metadataEntries)
				root.Add(entry.ToElement());
			foreach (PortableResXEntry entry in dataEntries)
				root.Add(entry.ToElement());

			return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
		}

		static string DefaultTypeNameConverter(Type type)
		{
			return type.AssemblyQualifiedName ?? type.FullName;
		}
	}

	sealed class PortableResXEntry
	{
		public string Name;
		public string Value;
		public string TypeName;
		public string MimeType;
		public string Comment;
		public bool IsMetadata;

		public static PortableResXEntry FromElement(XElement element, bool isMetadata)
		{
			return new PortableResXEntry {
				Name = (string)element.Attribute("name") ?? string.Empty,
				TypeName = (string)element.Attribute("type"),
				MimeType = (string)element.Attribute("mimetype"),
				Value = (string)element.Element("value") ?? string.Empty,
				Comment = (string)element.Element("comment"),
				IsMetadata = isMetadata
			};
		}

		public static PortableResXEntry FromObject(string name, object value, bool isMetadata, Func<Type, string> typeNameConverter)
		{
			if (name == null)
				throw new ArgumentNullException("name");

			var existing = value as PortableResXEntry;
			if (existing != null) {
				return new PortableResXEntry {
					Name = name,
					TypeName = existing.TypeName,
					MimeType = existing.MimeType,
					Value = existing.Value,
					Comment = existing.Comment,
					IsMetadata = isMetadata
				};
			}

			if (value == null) {
				return new PortableResXEntry {
					Name = name,
					TypeName = typeNameConverter(typeof(string)),
					Value = string.Empty,
					IsMetadata = isMetadata
				};
			}

			byte[] bytes = value as byte[];
			if (bytes != null) {
				return new PortableResXEntry {
					Name = name,
					TypeName = typeNameConverter(typeof(byte[])),
					Value = Convert.ToBase64String(bytes),
					IsMetadata = isMetadata
				};
			}

			string text = value as string;
			if (text != null) {
				return new PortableResXEntry {
					Name = name,
					Value = text,
					IsMetadata = isMetadata
				};
			}

			return new PortableResXEntry {
				Name = name,
				TypeName = typeNameConverter(value.GetType()),
				Value = Convert.ToString(value, global::System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
				IsMetadata = isMetadata
			};
		}

		public object GetRuntimeValue()
		{
			if (TypeName == null && MimeType == null)
				return Value;

			if (IsType("System.Byte[]")) {
				try {
					return Convert.FromBase64String(Value);
				} catch (FormatException) {
					return Array.Empty<byte>();
				}
			}

			if (IsType("System.String"))
				return Value;

			if (IsType("System.Boolean")) {
				bool result;
				return bool.TryParse(Value, out result) ? result : false;
			}

			if (IsType("System.Int32")) {
				int result;
				return int.TryParse(Value, global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out result) ? result : 0;
			}

			return Value;
		}

		public XElement ToElement()
		{
			var element = new XElement(IsMetadata ? "metadata" : "data",
				new XAttribute("name", Name),
				new XAttribute(XNamespace.Xml + "space", "preserve"));
			if (!string.IsNullOrEmpty(TypeName))
				element.Add(new XAttribute("type", TypeName));
			if (!string.IsNullOrEmpty(MimeType))
				element.Add(new XAttribute("mimetype", MimeType));
			element.Add(new XElement("value", Value ?? string.Empty));
			if (!string.IsNullOrEmpty(Comment))
				element.Add(new XElement("comment", Comment));
			return element;
		}

		bool IsType(string typeName)
		{
			return TypeName != null
				&& (string.Equals(TypeName, typeName, StringComparison.Ordinal)
					|| TypeName.StartsWith(typeName + ",", StringComparison.Ordinal));
		}
	}

	sealed class PortableDictionaryEnumerator : IDictionaryEnumerator
	{
		readonly IList<DictionaryEntry> entries;
		int index = -1;

		public PortableDictionaryEnumerator(IList<DictionaryEntry> entries)
		{
			this.entries = entries;
		}

		public DictionaryEntry Entry {
			get { return entries[index]; }
		}

		public object Key {
			get { return Entry.Key; }
		}

		public object Value {
			get { return Entry.Value; }
		}

		public object Current {
			get { return Entry; }
		}

		public bool MoveNext()
		{
			if (index + 1 >= entries.Count)
				return false;
			index++;
			return true;
		}

		public void Reset()
		{
			index = -1;
		}
	}
}
#endif
