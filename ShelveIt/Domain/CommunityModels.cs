using System.Collections.Generic;

namespace ShelveIt.Domain
{
	// Wire format of the community server (api/v1) and of share codes. Field names are the JSON names.

	public class PayloadAsset
	{
		// Asset key, as in libraries.json.
		public string k;

		// PDX Mods package id, 0 for the base game or unknown.
		public int p;
	}

	public class PayloadPackage
	{
		public int id;
		public string title = "";
	}

	public class CommunityPayload
	{
		public const int kSchema = 1;

		public int schema = kSchema;
		public string name = "";
		public string description = "";
		public List<string> tags = new List<string>();
		public string modVersion = "";
		public List<PayloadAsset> assets = new List<PayloadAsset>();
		public List<PayloadPackage> packages = new List<PayloadPackage>();
		public List<string> preview = new List<string>();
	}

	public class CommunitySummary
	{
		public string id = "";
		public string name = "";
		public string author = "";
		public string description = "";
		public List<string> tags = new List<string>();
		public int assetCount;
		public int packageCount;
		public int downloads;
		public string createdAt = "";
		public string updatedAt = "";
	}

	public class CommunityDetail : CommunitySummary
	{
		public CommunityPayload payload;
	}

	public class CommunityPage
	{
		public int page;
		public int pageSize;
		public int total;
		public List<CommunitySummary> items = new List<CommunitySummary>();
	}

	public class PublishResult
	{
		public string id;
		public string ownerToken;
	}

	public class ServerError
	{
		public string error;
		public string message;
	}

	public class ReportRequest
	{
		public string reason;
		public string note;
	}

	// published.json: local library id -> server entry created by this install.
	public class PublishedEntry
	{
		public string serverId;
		public string ownerToken;
	}

	public class PublishedFile
	{
		public int version = 1;
		public Dictionary<string, PublishedEntry> libraries = new Dictionary<string, PublishedEntry>();
	}
}
