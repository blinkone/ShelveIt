using System;
using System.Collections.Generic;
using System.Linq;

using ShelveIt.Domain;

namespace ShelveIt.Logic
{
	// Pure managed code, safe to run on a background thread. Never touches ECS or the game.
	public static class SuggestionEngine
	{
		public const string kRegions = "regions";
		public const string kCreators = "creators";
		public const string kThematic = "thematic";
		public const string kRegionDimension = "region";
		public const string kVanillaPackage = "vanilla";

		public const int kMinPackAssets = 4;
		public const int kMinFamilyAssets = 4;

		private const double kGameThemeBonus = 3;
		private const double kPackBonus = 3;

		// A leading word shared by a whole package is dropped from family names unless it is one of these.
		private static readonly HashSet<string> s_KeepWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"Residential", "Commercial", "Industrial", "Office", "Mixed", "Low", "Medium", "High", "Historic", "Modern", "Old",
			"Small", "Large", "Big", "Street", "Road", "Train", "Tram", "Bus", "Subway", "Park", "Parking", "House", "Tree",
			"Shipping", "Truck", "Car", "Sign", "Billboard", "Poster", "Fence", "Wall", "Bridge", "Station", "Farm", "School",
		};

		private static readonly Dictionary<string, string> s_ZoneIt = new Dictionary<string, string>
		{
			{ "Residential", "Residenziale" }, { "Commercial", "Commerciale" }, { "Industrial", "Industriale" }, { "Office", "Uffici" },
		};

		private static readonly Dictionary<string, string[]> s_Density = new Dictionary<string, string[]>
		{
			{ "Low", new[] { "low", "bassa densità" } },
			{ "Medium", new[] { "medium", "media densità" } },
			{ "High", new[] { "high", "alta densità" } },
			{ "Row", new[] { "row", "a schiera" } },
			{ "Mixed", new[] { "mixed", "misto" } },
		};

		private sealed class Compiled
		{
			public ThemeDef def;
			public double minScore;
			public HashSet<string> kinds, zones, gameThemes, packs;
			public bool region;
		}

		#region Enrichment

		// Fills package, family, region, density and the search helpers of every record.
		public static void Enrich(IReadOnlyList<AssetRecord> records, ThemesFile themes, CommunityFile community, Dictionary<int, PackageMeta> packages)
		{
			var compiled = Compile(themes);
			var regions = compiled.Where(x => x.region).ToList();
			var regionIds = new HashSet<string>(regions.Select(x => x.def.id));
			var parents = regions.ToDictionary(x => x.def.id, x => x.def.parent ?? "");
			var codes = new Dictionary<string, string>(StringComparer.Ordinal);
			var byPack = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var byGameTheme = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach (var region in regions)
			{
				foreach (var prefix in region.def.prefixes ?? new List<string>())
				{
					codes[prefix] = region.def.id;
				}

				foreach (var pack in region.def.packs ?? new List<string>())
				{
					byPack[pack] = region.def.id;
				}

				foreach (var theme in region.def.gameThemes ?? new List<string>())
				{
					byGameTheme[theme] = region.def.id;
				}
			}

			var byPackage = new Dictionary<int, string>();
			var byPrefab = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			if (community != null)
			{
				foreach (var pair in community.packages ?? new Dictionary<string, List<string>>())
				{
					var region = pair.Value?.FirstOrDefault(regionIds.Contains);

					if (region != null && int.TryParse(pair.Key, out var id))
					{
						byPackage[id] = region;
					}
				}

				foreach (var pair in community.prefabs ?? new Dictionary<string, List<string>>())
				{
					var region = pair.Value?.FirstOrDefault(regionIds.Contains);

					if (region != null)
					{
						byPrefab[pair.Key] = region;
					}
				}
			}

			foreach (var record in records)
			{
				var name = record.isVanilla ? NameParser.StripBrand(record.name) : record.name;

				record.tokens = Tokenizer.Tokenize(name);
				record.packs ??= new string[0];
				record.theme ??= "";
				record.zone ??= "None";
				record.zoneName ??= "";

				var parsed = NameParser.Parse(name, codes.Keys);

				record.family = parsed.family;
				record.prefixCode = parsed.code;

				if (string.IsNullOrEmpty(record.corner))
				{
					record.corner = record.zone != "None" ? parsed.corner : "";
				}

				record.density = "";

				if (record.zone != "None")
				{
					var zoneDensity = record.zoneName.Length > 0 ? NameParser.Parse(record.zoneName, null).density : "";

					record.density = zoneDensity.Length > 0 ? zoneDensity : parsed.density;
				}

				if (record.pdxId != 0)
				{
					record.package = "pdx:" + record.pdxId;

					if (packages != null && packages.TryGetValue(record.pdxId, out var meta))
					{
						record.packageTitle = meta.title ?? "";
						record.installed = meta.installed;
					}
					else
					{
						record.packageTitle = "";
					}
				}
				else if (record.isVanilla)
				{
					record.package = kVanillaPackage;
					record.packageTitle = "";
				}
				else if (NameParser.TryParseEai(record.name, out var eaiPack, out var category))
				{
					record.package = "eai:" + NameParser.Slug(eaiPack);
					record.packageTitle = eaiPack;
					record.family = NameParser.Humanize(category);
				}
				else
				{
					record.package = "";
					record.packageTitle = "";
				}

				record.region = ResolveRegion(record, regions, codes, parents, byPack, byPackage, byPrefab, byGameTheme);
				record.searchText = (record.name + " " + record.packageTitle).ToLowerInvariant();
			}

			StripPackagePrefixes(records);
			Rank(records);
		}

		private static string ResolveRegion(
			AssetRecord record,
			List<Compiled> regions,
			Dictionary<string, string> codes,
			Dictionary<string, string> parents,
			Dictionary<string, string> byPack,
			Dictionary<int, string> byPackage,
			Dictionary<string, string> byPrefab,
			Dictionary<string, string> byGameTheme)
		{
			foreach (var pack in record.packs)
			{
				if (pack != null && byPack.TryGetValue(pack, out var fromPack))
				{
					return fromPack;
				}
			}

			if (byPrefab.TryGetValue(record.name, out var fromPrefab))
			{
				return fromPrefab;
			}

			if (record.pdxId != 0 && byPackage.TryGetValue(record.pdxId, out var fromPackage))
			{
				return fromPackage;
			}

			var fromCode = record.prefixCode.Length > 0 && codes.TryGetValue(record.prefixCode, out var code) ? code : null;

			if (record.theme.Length > 0 && byGameTheme.TryGetValue(record.theme, out var fromTheme))
			{
				// A prefix may only narrow the game theme down ("EU_It" inside European).
				return fromCode != null && parents.TryGetValue(fromCode, out var parent) && parent == fromTheme ? fromCode : fromTheme;
			}

			if (fromCode != null)
			{
				return fromCode;
			}

			Compiled best = null;
			var bestScore = 0d;

			foreach (var region in regions)
			{
				if (region.def.keywords == null || region.def.keywords.Count == 0)
				{
					continue;
				}

				var score = Score(record, region);

				if (score >= region.minScore && score > bestScore)
				{
					best = region;
					bestScore = score;
				}
			}

			return best?.def.id ?? "";
		}

		// "StarQ MRB Extractors Wood" -> "Extractors Wood" when the whole package starts with "StarQ MRB".
		private static void StripPackagePrefixes(IReadOnlyList<AssetRecord> records)
		{
			foreach (var group in records.Where(x => x.package.Length > 0 && x.package != kVanillaPackage && !x.package.StartsWith("eai:")).GroupBy(x => x.package))
			{
				var members = group.ToList();

				if (members.Count < 6)
				{
					continue;
				}

				for (var round = 0; round < 2; round++)
				{
					var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

					foreach (var member in members)
					{
						var space = member.family.IndexOf(' ');

						if (space > 0)
						{
							var word = member.family.Substring(0, space);

							counts.TryGetValue(word, out var count);
							counts[word] = count + 1;
						}
					}

					if (counts.Count == 0)
					{
						break;
					}

					var top = counts.OrderByDescending(x => x.Value).First();

					if (top.Value < members.Count * 0.85 || s_KeepWords.Contains(top.Key))
					{
						break;
					}

					foreach (var member in members)
					{
						if (member.family.Length > top.Key.Length + 1
							&& member.family[top.Key.Length] == ' '
							&& member.family.StartsWith(top.Key, StringComparison.OrdinalIgnoreCase))
						{
							member.family = member.family.Substring(top.Key.Length + 1);
						}
					}
				}
			}
		}

		// Integer ranks make the name and package sorts cheap at query time.
		private static void Rank(IReadOnlyList<AssetRecord> records)
		{
			var sorted = records.ToArray();

			Array.Sort(sorted, (a, b) =>
			{
				var result = string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);

				return result != 0 ? result : string.CompareOrdinal(a.key, b.key);
			});

			for (var i = 0; i < sorted.Length; i++)
			{
				sorted[i].nameRank = i;
			}

			Array.Sort(sorted, (a, b) =>
			{
				var result = (a.packageTitle.Length == 0).CompareTo(b.packageTitle.Length == 0);

				if (result == 0)
				{
					result = string.Compare(a.packageTitle, b.packageTitle, StringComparison.OrdinalIgnoreCase);
				}

				if (result == 0)
				{
					result = string.CompareOrdinal(a.package, b.package);
				}

				return result != 0 ? result : a.nameRank.CompareTo(b.nameRank);
			});

			for (var i = 0; i < sorted.Length; i++)
			{
				sorted[i].packageRank = i;
			}
		}

		#endregion

		#region Tree

		// Records must be enriched first. Returns every node, parents before children.
		public static List<SuggestionNode> BuildTree(IReadOnlyList<AssetRecord> records, ThemesFile themes, CommunityFile community)
		{
			var compiled = Compile(themes);
			var nodes = new List<SuggestionNode>();
			var ordered = records.OrderBy(x => x.nameRank).ToList();

			var regionsRoot = Root(kRegions, "Regions and styles", "Regioni e stili");
			var creatorsRoot = Root(kCreators, "Creator packs", "Pacchetti dei creatori");
			var thematicRoot = Root(kThematic, "Thematic sets", "Set tematici");

			BuildRegions(ordered, compiled, regionsRoot);
			BuildCreators(ordered, creatorsRoot);
			BuildThematic(ordered, compiled, community, thematicRoot);

			foreach (var root in new[] { regionsRoot, creatorsRoot, thematicRoot })
			{
				Flatten(root, nodes);
			}

			return nodes;
		}

		private static SuggestionNode Root(string id, string name, string nameIt)
		{
			return new SuggestionNode { id = id, name = name, nameIt = nameIt, group = true };
		}

		private static SuggestionNode Child(SuggestionNode parent, string slug, string name, string nameIt, List<string> keys)
		{
			var id = parent.id + "/" + (slug.Length > 0 ? slug : "set");
			var unique = id;
			var suffix = 2;

			while (parent.children.Any(x => x.id == unique))
			{
				unique = id + "-" + suffix++;
			}

			var node = new SuggestionNode { id = unique, parent = parent.id, depth = parent.depth + 1, name = name, nameIt = nameIt, keys = keys };

			parent.children.Add(node);

			return node;
		}

		private static void Flatten(SuggestionNode node, List<SuggestionNode> nodes)
		{
			nodes.Add(node);
			node.children.Sort((a, b) => b.keys.Count != a.keys.Count ? b.keys.Count.CompareTo(a.keys.Count) : string.CompareOrdinal(a.id, b.id));

			foreach (var child in node.children)
			{
				Flatten(child, nodes);
			}
		}

		private static void BuildRegions(List<AssetRecord> ordered, List<Compiled> compiled, SuggestionNode root)
		{
			var defs = compiled.Where(x => x.region).ToDictionary(x => x.def.id, x => x.def);

			foreach (var group in ordered.Where(x => x.region.Length > 0).GroupBy(x => x.region))
			{
				if (!defs.TryGetValue(group.Key, out var def))
				{
					continue;
				}

				var members = group.ToList();
				var region = Child(root, group.Key, Name(def, "en-US"), Name(def, "it-IT"), members.Select(x => x.key).ToList());
				var parts = new Dictionary<string, List<AssetRecord>>();

				foreach (var member in members)
				{
					string part;

					if (member.kind == "Building" && member.zone != "None")
					{
						part = member.zone + "|" + member.density;
					}
					else if (member.kind == "Building" || member.kind == "ServiceBuilding" || member.kind == "UniqueBuilding")
					{
						part = "service";
					}
					else
					{
						part = "props";
					}

					if (!parts.TryGetValue(part, out var list))
					{
						parts[part] = list = new List<AssetRecord>();
					}

					list.Add(member);
				}

				if (parts.Count < 2)
				{
					continue;
				}

				foreach (var pair in parts)
				{
					var keys = pair.Value.Select(x => x.key).ToList();

					if (pair.Key == "service")
					{
						Child(region, "service-unique", "Service and unique", "Servizi e unici", keys);
					}
					else if (pair.Key == "props")
					{
						Child(region, "props-decals", "Props and decals", "Oggetti e decalcomanie", keys);
					}
					else
					{
						var split = pair.Key.Split('|');
						var zoneIt = s_ZoneIt.TryGetValue(split[0], out var it) ? it : split[0];
						var name = split[0];
						var nameIt = zoneIt;

						if (s_Density.TryGetValue(split[1], out var labels))
						{
							name += " " + labels[0];
							nameIt += " " + labels[1];
						}

						Child(region, NameParser.Slug(pair.Key), name, nameIt, keys);
					}
				}
			}
		}

		private static void BuildCreators(List<AssetRecord> ordered, SuggestionNode root)
		{
			var signatures = new HashSet<long>();
			var packNodes = new HashSet<string>(StringComparer.Ordinal);

			// Asset packs first: their node wins over a package with exactly the same content.
			var byPack = new Dictionary<string, List<AssetRecord>>(StringComparer.Ordinal);

			foreach (var record in ordered)
			{
				foreach (var pack in record.packs)
				{
					if (string.IsNullOrEmpty(pack))
					{
						continue;
					}

					if (!byPack.TryGetValue(pack, out var list))
					{
						byPack[pack] = list = new List<AssetRecord>();
					}

					list.Add(record);
				}
			}

			foreach (var pair in byPack.OrderBy(x => x.Key, StringComparer.Ordinal))
			{
				if (pair.Value.Count < kMinPackAssets)
				{
					continue;
				}

				signatures.Add(Signature(pair.Value));
				packNodes.Add(pair.Key);
				AddFamilies(Child(root, "pack-" + NameParser.Slug(pair.Key), PackTitle(pair.Key, pair.Value), null, pair.Value.Select(x => x.key).ToList()), pair.Value);
			}

			foreach (var group in ordered.Where(x => x.package.Length > 0).GroupBy(x => x.package).OrderBy(x => x.Key, StringComparer.Ordinal))
			{
				var members = group.ToList();

				if (members.Count < kMinPackAssets || !signatures.Add(Signature(members)) || IsCoveredByPack(members, packNodes))
				{
					continue;
				}

				var vanilla = group.Key == kVanillaPackage;
				var title = vanilla ? "Base game" : members[0].packageTitle;

				if (string.IsNullOrEmpty(title))
				{
					title = "Mod " + members[0].pdxId;
				}

				AddFamilies(Child(root, group.Key.Replace(':', '-'), title, vanilla ? "Gioco base" : null, members.Select(x => x.key).ToList()), members);
			}
		}

		// A package that sits almost entirely inside one asset pack would only repeat that pack's node.
		private static bool IsCoveredByPack(List<AssetRecord> members, HashSet<string> packNodes)
		{
			var counts = new Dictionary<string, int>(StringComparer.Ordinal);

			foreach (var member in members)
			{
				foreach (var pack in member.packs)
				{
					if (!string.IsNullOrEmpty(pack) && packNodes.Contains(pack))
					{
						counts.TryGetValue(pack, out var count);
						counts[pack] = count + 1;
					}
				}
			}

			return counts.Values.Any(x => x >= members.Count * 0.9);
		}

		private static void AddFamilies(SuggestionNode node, List<AssetRecord> members)
		{
			var codes = new HashSet<string>(members.Where(x => x.family.Length > 0 && x.prefixCode.Length > 0).Select(x => x.prefixCode));
			var families = new Dictionary<string, List<AssetRecord>>(StringComparer.OrdinalIgnoreCase);

			foreach (var member in members)
			{
				if (member.family.Length == 0)
				{
					continue;
				}

				var name = codes.Count > 1 && member.prefixCode.Length > 0 ? member.prefixCode.Replace('_', ' ') + " " + member.family : member.family;

				if (!families.TryGetValue(name, out var list))
				{
					families[name] = list = new List<AssetRecord>();
				}

				list.Add(member);
			}

			foreach (var pair in families.OrderBy(x => x.Key, StringComparer.Ordinal))
			{
				if (pair.Value.Count >= kMinFamilyAssets && pair.Value.Count < members.Count)
				{
					Child(node, NameParser.Slug(pair.Key), pair.Key, null, pair.Value.Select(x => x.key).ToList());
				}
			}
		}

		private static void BuildThematic(List<AssetRecord> ordered, List<Compiled> compiled, CommunityFile community, SuggestionNode root)
		{
			var prefabHits = new Dictionary<string, List<string>>(community?.prefabs ?? new Dictionary<string, List<string>>(), StringComparer.OrdinalIgnoreCase);
			var packageHits = community?.packages ?? new Dictionary<string, List<string>>();
			var scored = new List<KeyValuePair<string, double>>();

			foreach (var theme in compiled)
			{
				if (theme.region)
				{
					continue;
				}

				scored.Clear();

				foreach (var record in ordered)
				{
					var forced = IsForced(record, theme.def.id, prefabHits, packageHits);
					var score = Score(record, theme);

					if (forced || score >= theme.minScore)
					{
						scored.Add(new KeyValuePair<string, double>(record.key, Math.Max(score, theme.minScore)));
					}
				}

				if (scored.Count == 0)
				{
					continue;
				}

				// Stable sort keeps name order inside one score.
				var keys = scored.OrderByDescending(x => x.Value).Select(x => x.Key).ToList();

				Child(root, theme.def.id, Name(theme.def, "en-US"), Name(theme.def, "it-IT"), keys);
			}
		}

		#endregion

		#region Helpers

		private static List<Compiled> Compile(ThemesFile themes)
		{
			var list = new List<Compiled>();

			foreach (var def in themes?.themes ?? new List<ThemeDef>())
			{
				if (def == null || string.IsNullOrEmpty(def.id))
				{
					continue;
				}

				list.Add(new Compiled
				{
					def = def,
					minScore = def.minScore > 0 ? def.minScore : 1,
					kinds = ToSet(def.kinds),
					zones = ToSet(def.zones),
					gameThemes = ToSet(def.gameThemes),
					packs = ToSet(def.packs),
					region = def.dimension == kRegionDimension,
				});
			}

			return list;
		}

		private static double Score(AssetRecord asset, Compiled theme)
		{
			if (theme.kinds.Count > 0 && !theme.kinds.Contains(asset.kind))
			{
				return 0;
			}

			if (theme.zones.Count > 0 && !theme.zones.Contains(asset.zone))
			{
				return 0;
			}

			var def = theme.def;
			var score = 0d;

			foreach (var token in asset.tokens)
			{
				if (def.keywords != null && def.keywords.TryGetValue(token, out var weight))
				{
					score += weight;
				}

				if (def.negative != null && def.negative.TryGetValue(token, out var penalty))
				{
					score -= penalty;
				}
			}

			if (asset.theme.Length > 0 && theme.gameThemes.Contains(asset.theme))
			{
				score += kGameThemeBonus;
			}

			if (theme.packs.Count > 0)
			{
				foreach (var pack in asset.packs)
				{
					if (theme.packs.Contains(pack))
					{
						score += kPackBonus;
						break;
					}
				}
			}

			return score;
		}

		private static bool IsForced(AssetRecord asset, string themeId, Dictionary<string, List<string>> prefabHits, Dictionary<string, List<string>> packageHits)
		{
			if (prefabHits.TryGetValue(asset.name, out var byPrefab) && byPrefab != null && byPrefab.Contains(themeId))
			{
				return true;
			}

			return asset.pdxId != 0
				&& packageHits.TryGetValue(asset.pdxId.ToString(), out var byPackage)
				&& byPackage != null
				&& byPackage.Contains(themeId);
		}

		private static string Name(ThemeDef def, string locale)
		{
			if (def.name != null && (def.name.TryGetValue(locale, out var name) || def.name.TryGetValue("en-US", out name)))
			{
				return name;
			}

			return def.id;
		}

		// "UK Pack Filter" -> "UK Pack", "BridgesAndPorts_AssetPack" -> "Bridges And Ports".
		private static string PackTitle(string pack, List<AssetRecord> members)
		{
			var title = pack;

			foreach (var tail in new[] { " Asset Pack Filter", " Assets Pack", " Pack Filter", " Asset Pack", " AssetPack", "_AssetPack", "AssetFilter", " Filter" })
			{
				if (title.EndsWith(tail, StringComparison.OrdinalIgnoreCase) && title.Length > tail.Length)
				{
					title = title.Substring(0, title.Length - tail.Length);
					break;
				}
			}

			title = title.IndexOf(' ') < 0 ? NameParser.Humanize(title) : title.Trim();

			// A pack that mostly comes from one package takes the package title.
			var top = members.Where(x => x.packageTitle.Length > 0).GroupBy(x => x.package).OrderByDescending(x => x.Count()).FirstOrDefault();

			if (top != null && top.Count() >= members.Count * 0.6)
			{
				return top.First().packageTitle;
			}

			return title.All(c => !char.IsLower(c)) ? title + " Pack" : title;
		}

		// Order independent fingerprint of a member set.
		private static long Signature(List<AssetRecord> members)
		{
			long hash = members.Count;

			foreach (var member in members)
			{
				hash += (uint)member.nameRank * 2654435761L + 1;
			}

			return hash;
		}

		private static HashSet<string> ToSet(List<string> values)
		{
			return new HashSet<string>(values ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
		}

		#endregion
	}
}
