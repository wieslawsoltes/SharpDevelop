#if LIBREWPF
using System.Collections.ObjectModel;

namespace System.ServiceModel.Description
{
	public sealed class MetadataSet
	{
		public Collection<MetadataSection> MetadataSections { get; } = new Collection<MetadataSection>();
	}

	public sealed class MetadataSection
	{
		public object Metadata { get; set; }
	}

	public enum MetadataExchangeClientMode
	{
		MetadataExchange = 0,
		HttpGet = 1
	}

	public sealed class MetadataExchangeClient
	{
		public MetadataExchangeClient(Uri address, MetadataExchangeClientMode mode)
		{
			Address = address;
			Mode = mode;
		}

		public Uri Address { get; }

		public MetadataExchangeClientMode Mode { get; }

		public MetadataSet GetMetadata()
		{
			throw new PlatformNotSupportedException("WCF metadata exchange discovery is not available on this LibreWPF target.");
		}
	}
}
#endif
