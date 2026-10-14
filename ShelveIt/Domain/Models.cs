using System.Collections.Generic;

using ShelveIt.Logic;

using Newtonsoft.Json;

using Unity.Entities;

namespace ShelveIt.Domain
{
	// Plain snapshot of one prefab. Field names are the JSON names used in scan-dump.json.
	public class AssetRecord
	{
		public string key;
		public int entityIndex;
		public string name;
		public string kind;
		public string zone;
		public string zoneName = "";
		public string density = "";
		public string theme;
		public string[] packs;
		public int lotW;
		public int lotD;
		public int level;
		public int pdxId;
		public bool isVanilla;
		public string corner = "";
		public bool placeable;
		public bool hasThumbnail;
		public string thumbKind = "none";

		// Filled by the suggestion engine on the background thread.
		public string package = "";
		public string packageTitle = "";
		public string family = "";
		public string region = "";
		public long installed;
		public string[] tokens;

		[JsonIgnore] public Entity entity;
		[JsonIgnore] public string thumbnail;
		[JsonIgnore] public string prefixCode = "";
		[JsonIgnore] public string searchText = "";
		[JsonIgnore] public int[] codes;
		[JsonIgnore] public int[] packCodes;
		[JsonIgnore] public int nameRank;
		[JsonIgnore] public int packageRank;
	}

	public class Library
	{
		public string id;
		public string name;
		public string description = "";

		// Community tags, set on publish or import. Optional in the file.
		public List<string> tags = new List<string>();
		public string source = "user";
		public List<string> assets = new List<string>();
		public string created;
	}

	public class LibraryFile
	{
		public int version = 1;
		public List<Library> libraries = new List<Library>();
		public List<string> dismissed = new List<string>();

		// Package info of assets that came from a community payload or a share code, so a library
		// re-published while they are not installed keeps it. Asset key -> PDX package id, and
		// package id (as text) -> title. Both are optional in the file.
		public Dictionary<string, int> assetPackages = new Dictionary<string, int>();
		public Dictionary<string, string> packageTitles = new Dictionary<string, string>();
	}

	public class ThemeDef
	{
		public string id;
		public Dictionary<string, string> name;

		// "region" or "thematic". Missing means "thematic" (schema version 1 files).
		public string dimension;

		// Broader region this one refines, e.g. "european" for "british".
		public string parent;

		// Case sensitive name prefixes, e.g. "UK", "GER", "EU_It".
		public List<string> prefixes;
		public List<string> kinds;
		public List<string> zones;
		public Dictionary<string, double> keywords;
		public Dictionary<string, double> negative;
		public List<string> gameThemes;
		public List<string> packs;
		public double minScore = 1;
	}

	public class ThemesFile
	{
		public int version;
		public List<ThemeDef> themes = new List<ThemeDef>();
	}

	public class CommunityFile
	{
		public int version;
		public Dictionary<string, List<string>> packages = new Dictionary<string, List<string>>();
		public Dictionary<string, List<string>> prefabs = new Dictionary<string, List<string>>();
	}

	public class PackageMeta
	{
		public string title = "";
		public string author = "";
		public long installed;
	}

	// One node of the suggestion tree. Ids are path-like: "regions/british/residential-medium".
	public class SuggestionNode
	{
		public string id;
		public string parent = "";
		public int depth;
		public string name;
		public string nameIt;
		public bool group;
		public List<string> keys = new List<string>();

		[JsonIgnore] public List<SuggestionNode> children = new List<SuggestionNode>();
	}

	public class ClassificationResult
	{
		public string hash;
		public List<SuggestionNode> nodes = new List<SuggestionNode>();
		public Dictionary<string, SuggestionNode> byId = new Dictionary<string, SuggestionNode>();
		public Dictionary<string, ThemeDef> defs = new Dictionary<string, ThemeDef>();
		public FacetIndex index;
		public bool fromCache;
		public long enrichMs;
		public long treeMs;
	}
}
