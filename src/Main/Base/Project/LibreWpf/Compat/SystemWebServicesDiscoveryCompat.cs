#if LIBREWPF
#nullable enable
using System;
using System.Collections;
using System.Net;
using System.IO;
using System.Web.Services.Description;
using System.Xml.Schema;

namespace System.Web.Services.Discovery
{
	public class DiscoveryClientProtocol : IDisposable
	{
		public ICredentials? Credentials { get; set; }

		public bool AllowAutoRedirect { get; set; }

		public DiscoveryClientReferenceCollection References { get; } = new DiscoveryClientReferenceCollection();

		public DiscoveryDocument DiscoverAny(string url)
		{
			throw new PlatformNotSupportedException("ASMX discovery is not available on this LibreWPF target.");
		}

		public void ResolveOneLevel()
		{
		}

		public void WriteAll(string directory, string topLevelFilename)
		{
			throw new PlatformNotSupportedException("ASMX discovery persistence is not available on this LibreWPF target.");
		}

		public void Abort()
		{
		}

		public void Dispose()
		{
		}

		protected virtual WebResponse GetWebResponse(WebRequest request)
		{
			throw new PlatformNotSupportedException("ASMX discovery is not available on this LibreWPF target.");
		}
	}

	public sealed class DiscoveryDocument
	{
	}

	public sealed class DiscoveryNetworkCredential : NetworkCredential
	{
		public const string DefaultAuthenticationType = "Default";

		public DiscoveryNetworkCredential(ICredentials credentials, string authenticationType)
		{
			Credentials = credentials;
			AuthenticationType = authenticationType;
		}

		public string AuthenticationType { get; }

		public ICredentials Credentials { get; }
	}

	public sealed class DiscoveryClientReferenceCollection : Hashtable
	{
	}

	public sealed class ContractReference
		: DiscoveryReference
	{
		public ServiceDescription? Contract { get; set; }
	}

	public class DiscoveryReference
	{
		public string DefaultFilename { get; set; } = string.Empty;
	}

	public sealed class DiscoveryDocumentReference : DiscoveryReference
	{
	}

	public sealed class SchemaReference : DiscoveryReference
	{
		public XmlSchema? Schema { get; set; }
	}
}
#nullable restore
#endif
