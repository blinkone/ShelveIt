using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using ShelveIt.Domain;

using Newtonsoft.Json;

namespace ShelveIt.Logic
{
	// File access and caching around SuggestionEngine. Runs on a background thread, never touches ECS.
	public static class ThemeClassifier
	{
		// Bump when the cache layout or the engine output changes.
		private const int kCacheFormat = 2;

		private class CacheNode
		{
			public string id, parent, name, nameIt;
			public int depth;
			public bool group;
			public int[] keys;
		}

		private class CacheFile
		{
			public int format;
			public string hash;
			public List<CacheNode> nodes;
		}

		public static ClassificationResult Run(IReadOnlyList<AssetRecord> records, bool ignoreCache)
		{
			var result = new ClassificationResult();
			var themes = Load<ThemesFile>("themes.json") ?? new ThemesFile();
			var community = Load<CommunityFile>("community.json");

			themes.themes ??= new List<ThemeDef>();
			themes.themes.RemoveAll(x => x == null || string.IsNullOrEmpty(x.id));

			foreach (var theme in themes.themes)
			{
				result.defs[theme.id] = theme;
			}

			var watch = Stopwatch.StartNew();
			var ids = new HashSet<int>(records.Where(x => x.pdxId != 0).Select(x => x.pdxId));
			var packages = PackageInfo.Load(Paths.ModsCache, ids, Mod.Log.Warn);

			SuggestionEngine.Enrich(records, themes, community, packages);
			result.index = FacetIndex.Build(records);
			result.enrichMs = watch.ElapsedMilliseconds;

			watch.Restart();

			var sortedKeys = records.Select(x => x.key).ToArray();

			Array.Sort(sortedKeys, StringComparer.Ordinal);

			result.hash = ComputeHash(sortedKeys, records, themes.version, community?.version ?? -1);

			if (!ignoreCache && TryLoadCache(result.hash, sortedKeys, out var cached))
			{
				result.nodes = cached;
				result.fromCache = true;
			}
			else
			{
				result.nodes = SuggestionEngine.BuildTree(records, themes, community);
				WriteCache(result, sortedKeys);
				WriteDump(records);
			}

			Link(result);
			result.treeMs = watch.ElapsedMilliseconds;

			return result;
		}

		private static void Link(ClassificationResult result)
		{
			result.byId.Clear();

			foreach (var node in result.nodes)
			{
				node.children.Clear();
				result.byId[node.id] = node;
			}

			foreach (var node in result.nodes)
			{
				if (!string.IsNullOrEmpty(node.parent) && result.byId.TryGetValue(node.parent, out var parent))
				{
					parent.children.Add(node);
				}
			}
		}

		private static T Load<T>(string fileName) where T : class
		{
			try
			{
				var path = Paths.FindDataFile(fileName);

				if (path == null)
				{
					Mod.Log.Warn(fileName + " not found in the mod folder, suggestions that depend on it are skipped");
					return null;
				}

				return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
			}
			catch (Exception ex)
			{
				Mod.Log.Warn("Could not read " + fileName + ": " + ex.Message);
				return null;
			}
		}

		private static bool TryLoadCache(string hash, string[] sortedKeys, out List<SuggestionNode> nodes)
		{
			nodes = null;

			try
			{
				if (!File.Exists(Paths.Cache))
				{
					return false;
				}

				var cache = JsonConvert.DeserializeObject<CacheFile>(File.ReadAllText(Paths.Cache));

				if (cache?.nodes == null || cache.format != kCacheFormat || cache.hash != hash)
				{
					return false;
				}

				var list = new List<SuggestionNode>(cache.nodes.Count);

				foreach (var cached in cache.nodes)
				{
					var node = new SuggestionNode
					{
						id = cached.id,
						parent = cached.parent ?? "",
						depth = cached.depth,
						name = cached.name,
						nameIt = cached.nameIt,
						group = cached.group,
						keys = new List<string>(cached.keys?.Length ?? 0),
					};

					foreach (var index in cached.keys ?? new int[0])
					{
						if (index < 0 || index >= sortedKeys.Length)
						{
							return false;
						}

						node.keys.Add(sortedKeys[index]);
					}

					list.Add(node);
				}

				nodes = list;
				return true;
			}
			catch (Exception ex)
			{
				Mod.Log.Warn("Ignoring unreadable classification cache: " + ex.Message);
				return false;
			}
		}

		// Keys are stored as positions in the sorted key list, which the hash already covers.
		private static void WriteCache(ClassificationResult result, string[] sortedKeys)
		{
			try
			{
				var positions = new Dictionary<string, int>(sortedKeys.Length, StringComparer.Ordinal);

				for (var i = 0; i < sortedKeys.Length; i++)
				{
					positions[sortedKeys[i]] = i;
				}

				var cache = new CacheFile { format = kCacheFormat, hash = result.hash, nodes = new List<CacheNode>(result.nodes.Count) };

				foreach (var node in result.nodes)
				{
					cache.nodes.Add(new CacheNode
					{
						id = node.id,
						parent = node.parent,
						depth = node.depth,
						name = node.name,
						nameIt = node.nameIt,
						group = node.group,
						keys = node.keys.Select(x => positions[x]).ToArray(),
					});
				}

				Paths.WriteAtomic(Paths.Cache, JsonConvert.SerializeObject(cache, Formatting.None));
			}
			catch (Exception ex)
			{
				Mod.Log.Warn("Could not write classification cache: " + ex.Message);
			}
		}

		// Only rewritten when the classification changed, so a normal load does not pay for it.
		private static void WriteDump(IReadOnlyList<AssetRecord> records)
		{
			try
			{
				Paths.WriteAtomic(Paths.ScanDump, JsonConvert.SerializeObject(records, Formatting.None));
			}
			catch (Exception ex)
			{
				Mod.Log.Warn("Could not write scan-dump.json: " + ex.Message);
			}
		}

		private static string ComputeHash(string[] sortedKeys, IReadOnlyList<AssetRecord> records, int themesVersion, int communityVersion)
		{
			// Titles, zones and packs end up in node names and regions, so they are part of the hash.
			var extra = 17;

			foreach (var record in records)
			{
				unchecked
				{
					extra += StableHash(record.key) * (StableHash(record.packageTitle) + StableHash(record.zoneName) + StableHash(record.theme) + record.packs.Length + 31);
				}
			}

			using (var sha = SHA1.Create())
			using (var stream = new MemoryStream())
			using (var writer = new StreamWriter(stream, Encoding.UTF8))
			{
				writer.Write(kCacheFormat);
				writer.Write('|');
				writer.Write(themesVersion);
				writer.Write('|');
				writer.Write(communityVersion);
				writer.Write('|');
				writer.Write(extra);

				foreach (var key in sortedKeys)
				{
					writer.Write('\n');
					writer.Write(key);
				}

				writer.Flush();
				stream.Position = 0;

				return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
			}
		}

		// string.GetHashCode is not guaranteed to be stable between runs.
		private static int StableHash(string text)
		{
			unchecked
			{
				var hash = 23;

				foreach (var c in text ?? "")
				{
					hash = hash * 31 + c;
				}

				return hash;
			}
		}
	}
}
