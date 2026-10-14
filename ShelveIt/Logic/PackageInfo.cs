using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using ShelveIt.Domain;

using Newtonsoft.Json;

namespace ShelveIt.Logic
{
	// Reads the title and author of subscribed PDX mods from the game's own mod cache:
	// <mods cache>\pdx_mods_cache.json first, then <id>_<version>\.metadata\metadata.json.
	public static class PackageInfo
	{
		private static readonly string[] s_Folders = { "pdx_mods", "mods_subscribed" };

		private class MetadataFile
		{
			public string displayName = null;
			public string author = null;
		}

		public static Dictionary<int, PackageMeta> Load(string modsCacheDir, ICollection<int> ids, Action<string> warn)
		{
			var result = new Dictionary<int, PackageMeta>();

			if (string.IsNullOrEmpty(modsCacheDir) || ids == null || ids.Count == 0)
			{
				return result;
			}

			var wanted = new HashSet<int>(ids);

			try
			{
				ReadCacheFile(Path.Combine(modsCacheDir, "pdx_mods_cache.json"), wanted, result);
			}
			catch (Exception ex)
			{
				warn?.Invoke("Could not read pdx_mods_cache.json: " + ex.Message);
			}

			wanted.ExceptWith(result.Keys);

			if (wanted.Count > 0)
			{
				ReadFolders(modsCacheDir, wanted, result, warn);
			}

			return result;
		}

		// The file is several megabytes of descriptions and changelogs, so it is streamed
		// and everything but four properties is skipped.
		private static void ReadCacheFile(string path, HashSet<int> wanted, Dictionary<int, PackageMeta> result)
		{
			if (!File.Exists(path))
			{
				return;
			}

			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			using (var text = new StreamReader(stream))
			using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
			{
				while (reader.Read())
				{
					if (reader.TokenType == JsonToken.PropertyName && reader.Depth == 1 && (string)reader.Value == "items")
					{
						break;
					}
				}

				if (!reader.Read() || reader.TokenType != JsonToken.StartArray)
				{
					return;
				}

				while (reader.Read() && reader.TokenType == JsonToken.StartObject)
				{
					string id = null, title = null, author = null, installed = null;

					while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
					{
						var property = (string)reader.Value;

						reader.Read();

						switch (property)
						{
							case "id":
								id = reader.Value?.ToString();
								break;
							case "displayName":
								title = reader.Value?.ToString();
								break;
							case "author":
								author = reader.Value?.ToString();
								break;
							case "installedDate":
								installed = reader.Value?.ToString();
								break;
							default:
								reader.Skip();
								break;
						}
					}

					if (int.TryParse(id, out var number) && wanted.Contains(number) && !string.IsNullOrEmpty(title))
					{
						var meta = new PackageMeta { title = title.Trim(), author = author?.Trim() ?? "" };

						if (DateTime.TryParse(installed, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var date))
						{
							meta.installed = date.Ticks;
						}

						result[number] = meta;
					}
				}
			}
		}

		private static void ReadFolders(string modsCacheDir, HashSet<int> wanted, Dictionary<int, PackageMeta> result, Action<string> warn)
		{
			var best = new Dictionary<int, KeyValuePair<int, string>>();

			foreach (var folder in s_Folders)
			{
				try
				{
					var root = Path.Combine(modsCacheDir, folder);

					if (!Directory.Exists(root))
					{
						continue;
					}

					foreach (var directory in Directory.GetDirectories(root))
					{
						var name = Path.GetFileName(directory);
						var split = name.IndexOf('_');

						if (split <= 0
							|| !int.TryParse(name.Substring(0, split), out var id)
							|| !int.TryParse(name.Substring(split + 1), out var version)
							|| !wanted.Contains(id))
						{
							continue;
						}

						var hasMetadata = File.Exists(Path.Combine(directory, ".metadata", "metadata.json"));

						if (!best.TryGetValue(id, out var current) || (hasMetadata && version >= current.Key))
						{
							best[id] = new KeyValuePair<int, string>(version, directory);
						}
					}
				}
				catch (Exception ex)
				{
					warn?.Invoke("Could not list " + folder + ": " + ex.Message);
				}
			}

			foreach (var pair in best)
			{
				try
				{
					var meta = new PackageMeta { installed = Directory.GetCreationTimeUtc(pair.Value.Value).Ticks };
					var path = Path.Combine(pair.Value.Value, ".metadata", "metadata.json");

					if (File.Exists(path))
					{
						var file = JsonConvert.DeserializeObject<MetadataFile>(File.ReadAllText(path));

						meta.title = file?.displayName?.Trim() ?? "";
						meta.author = file?.author?.Trim() ?? "";
					}

					result[pair.Key] = meta;
				}
				catch
				{
					// A broken metadata file only costs the title of that package.
				}
			}
		}
	}
}
