#if LIBREWPF
using System;
using System.CodeDom;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace System.Web.Services.Description
{
	[Flags]
	public enum ServiceDescriptionImportWarnings
	{
		NoCodeGenerated = 1,
		OptionalExtensionsIgnored = 2,
		RequiredExtensionsIgnored = 4,
		UnsupportedOperationsIgnored = 8,
		UnsupportedBindingsIgnored = 16
	}

	public sealed class ServiceDescriptionImporter
	{
		public XmlSchemas Schemas { get; } = new XmlSchemas();

		public void AddServiceDescription(ServiceDescription serviceDescription, string appSettingUrlKey, string appSettingBaseUrl)
		{
			throw new PlatformNotSupportedException("ASMX web-reference proxy generation is not available on this LibreWPF target.");
		}

		public ServiceDescriptionImportWarnings Import(CodeNamespace codeNamespace, CodeCompileUnit codeCompileUnit)
		{
			throw new PlatformNotSupportedException("ASMX web-reference proxy generation is not available on this LibreWPF target.");
		}
	}
}
#endif
