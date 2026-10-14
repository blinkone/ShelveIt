using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ShelveIt.Domain;

using Newtonsoft.Json;

namespace ShelveIt.Logic
{
	// Libraries are tag-like sets of asset keys. Access is locked because the Find It
	// search hook may read from a background thread.
	public class LibraryStore
	{
		public const string kSuggestedPrefix = "suggested:";

		private readonly object m_Lock = new object();
		private LibraryFile m_File = new LibraryFile();

		// Union of every library, rebuilt lazily after a change.
		private HashSet<string> m_AllKeys;

		public void Load()
		{
			try
			{
				if (File.Exists(Paths.Libraries))
				{
					var file = JsonConvert.DeserializeObject<LibraryFile>(File.ReadAllText(Paths.Libraries));

					if (file != null)
					{
						file.libraries ??= new List<Library>();
						file.dismissed ??= new List<string>();
						file.assetPackages ??= new Dictionary<string, int>();
						file.packageTitles ??= new Dictionary<string, string>();
						file.libraries.RemoveAll(x => x == null || string.IsNullOrEmpty(x.id));

						foreach (var library in file.libraries)
						{
							library.assets ??= new List<string>();
							library.description ??= "";
							library.tags ??= new List<string>();
						}

						lock (m_Lock)
						{
							m_File = file;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Mod.Log.Error("Could not read libraries.json, starting empty: " + ex.Message);
			}
		}

		public void Save()
		{
			try
			{
				string json;

				lock (m_Lock)
				{
					m_AllKeys = null;
					json = JsonConvert.SerializeObject(m_File, Formatting.Indented);
				}

				Paths.WriteAtomic(Paths.Libraries, json);
			}
			catch (Exception ex)
			{
				Mod.Log.Error("Could not save libraries.json: " + ex.Message);
			}
		}

		public List<Library> Snapshot()
		{
			lock (m_Lock)
			{
				return new List<Library>(m_File.libraries);
			}
		}

		public Library Get(string id)
		{
			lock (m_Lock)
			{
				return m_File.libraries.FirstOrDefault(x => x.id == id);
			}
		}

		// Lookup used by the "lib:" search prefix: id, then exact name, then name prefix.
		public string[] FindAssetKeys(string idOrName)
		{
			lock (m_Lock)
			{
				var library = m_File.libraries.FirstOrDefault(x => string.Equals(x.id, idOrName, StringComparison.OrdinalIgnoreCase))
					?? m_File.libraries.FirstOrDefault(x => string.Equals(x.name, idOrName, StringComparison.OrdinalIgnoreCase))
					?? (idOrName.Length == 0 ? null : m_File.libraries.FirstOrDefault(x => x.name != null && x.name.StartsWith(idOrName, StringComparison.OrdinalIgnoreCase)));

				return library?.assets.ToArray();
			}
		}

		public string[] GetAssetKeys(string id)
		{
			lock (m_Lock)
			{
				return m_File.libraries.FirstOrDefault(x => x.id == id)?.assets.ToArray();
			}
		}

		public bool IsInAnyLibrary(string key)
		{
			lock (m_Lock)
			{
				if (m_AllKeys == null)
				{
					m_AllKeys = new HashSet<string>(StringComparer.Ordinal);

					foreach (var library in m_File.libraries)
					{
						m_AllKeys.UnionWith(library.assets);
					}
				}

				return m_AllKeys.Contains(key);
			}
		}

		public Library Create(string name, string source = "user", IEnumerable<string> assets = null, string description = "", IEnumerable<string> tags = null)
		{
			var library = new Library
			{
				id = Guid.NewGuid().ToString(),
				name = string.IsNullOrWhiteSpace(name) ? "Library" : name.Trim(),
				description = PayloadBuilder.CleanDescription(description),
				tags = PayloadBuilder.CleanTags(tags),
				source = source,
				created = DateTime.UtcNow.ToString("o"),
			};

			if (assets != null)
			{
				library.assets.AddRange(assets.Distinct());
			}

			lock (m_Lock)
			{
				m_File.libraries.Add(library);
			}

			Save();

			return library;
		}

		public bool Rename(string id, string name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return false;
			}

			lock (m_Lock)
			{
				var library = m_File.libraries.FirstOrDefault(x => x.id == id);

				if (library == null)
				{
					return false;
				}

				library.name = name.Trim();
			}

			Save();

			return true;
		}

		// Description and tags as last published (or typed in the Share bar before publishing).
		public bool SetShareInfo(string id, string description, List<string> tags)
		{
			lock (m_Lock)
			{
				var library = m_File.libraries.FirstOrDefault(x => x.id == id);

				if (library == null)
				{
					return false;
				}

				library.description = PayloadBuilder.CleanDescription(description);
				library.tags = PayloadBuilder.CleanTags(tags);
			}

			Save();

			return true;
		}

		// Remembers the package of every asset of an imported payload, and the package titles.
		// Entries of keys that no library holds any more are dropped.
		public void RecordPackages(CommunityPayload payload)
		{
			lock (m_Lock)
			{
				foreach (var asset in payload.assets)
				{
					if (asset.p > 0)
					{
						m_File.assetPackages[asset.k] = asset.p;
					}
				}

				foreach (var package in payload.packages)
				{
					if (package.id > 0 && !string.IsNullOrEmpty(package.title))
					{
						m_File.packageTitles[package.id.ToString()] = package.title;
					}
				}

				var held = new HashSet<string>(m_File.libraries.SelectMany(x => x.assets), StringComparer.Ordinal);

				foreach (var key in m_File.assetPackages.Keys.Where(x => !held.Contains(x)).ToList())
				{
					m_File.assetPackages.Remove(key);
				}

				var used = new HashSet<string>(m_File.assetPackages.Values.Select(x => x.ToString()));

				foreach (var id in m_File.packageTitles.Keys.Where(x => !used.Contains(x)).ToList())
				{
					m_File.packageTitles.Remove(id);
				}
			}

			Save();
		}

		// Package of an asset known only from an import. Title is "" when unknown.
		public bool TryGetImportedPackage(string key, out int pdxId, out string title)
		{
			lock (m_Lock)
			{
				title = "";

				if (!m_File.assetPackages.TryGetValue(key, out pdxId) || pdxId <= 0)
				{
					pdxId = 0;
					return false;
				}

				if (m_File.packageTitles.TryGetValue(pdxId.ToString(), out var known))
				{
					title = known ?? "";
				}

				return true;
			}
		}

		public bool Delete(string id)
		{
			bool removed;

			lock (m_Lock)
			{
				removed = m_File.libraries.RemoveAll(x => x.id == id) > 0;
			}

			if (removed)
			{
				Save();
			}

			return removed;
		}

		public bool AddAsset(string id, string key)
		{
			lock (m_Lock)
			{
				var library = m_File.libraries.FirstOrDefault(x => x.id == id);

				if (library == null || library.assets.Contains(key))
				{
					return false;
				}

				library.assets.Add(key);
			}

			Save();

			return true;
		}

		public int AddAssets(string id, IEnumerable<string> keys)
		{
			var added = 0;

			lock (m_Lock)
			{
				var library = m_File.libraries.FirstOrDefault(x => x.id == id);

				if (library == null)
				{
					return 0;
				}

				var existing = new HashSet<string>(library.assets, StringComparer.Ordinal);

				foreach (var key in keys)
				{
					if (!string.IsNullOrEmpty(key) && existing.Add(key))
					{
						library.assets.Add(key);
						added++;
					}
				}
			}

			if (added > 0)
			{
				Save();
			}

			return added;
		}

		public int RemoveAssets(string id, HashSet<string> keys)
		{
			int removed;

			lock (m_Lock)
			{
				var library = m_File.libraries.FirstOrDefault(x => x.id == id);

				if (library == null)
				{
					return 0;
				}

				removed = library.assets.RemoveAll(keys.Contains);
			}

			if (removed > 0)
			{
				Save();
			}

			return removed;
		}

		public bool RemoveAsset(string id, string key)
		{
			lock (m_Lock)
			{
				var library = m_File.libraries.FirstOrDefault(x => x.id == id);

				if (library == null || !library.assets.Remove(key))
				{
					return false;
				}
			}

			Save();

			return true;
		}

		public bool IsDismissed(string themeId)
		{
			lock (m_Lock)
			{
				return m_File.dismissed.Contains(themeId);
			}
		}

		public HashSet<string> DismissedSet()
		{
			lock (m_Lock)
			{
				return new HashSet<string>(m_File.dismissed, StringComparer.Ordinal);
			}
		}

		// Suggestion node id -> content of the library created from it. Legacy sources are left out.
		public Dictionary<string, HashSet<string>> SuggestedLibraries()
		{
			var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

			lock (m_Lock)
			{
				foreach (var library in m_File.libraries)
				{
					if (SourceKind(library.source) == "suggested")
					{
						result[SourceId(library.source)] = new HashSet<string>(library.assets, StringComparer.Ordinal);
					}
				}
			}

			return result;
		}

		// "user", "suggested" (made from a tree node, the id is a path) or "legacy"
		// (made from a flat theme suggestion of the first version).
		public static string SourceKind(string source)
		{
			if (source == null || !source.StartsWith(kSuggestedPrefix, StringComparison.Ordinal))
			{
				return "user";
			}

			return source.IndexOf('/') >= 0 ? "suggested" : "legacy";
		}

		public static string SourceId(string source)
		{
			return source != null && source.StartsWith(kSuggestedPrefix, StringComparison.Ordinal) ? source.Substring(kSuggestedPrefix.Length) : "";
		}

		// Theme id behind a suggested library: the legacy id, or the second path segment of
		// "regions/<theme>" and "thematic/<theme>" nodes. Creator pack nodes have none.
		private static string ThemeIdOf(string source)
		{
			var id = SourceId(source);

			if (id.Length == 0)
			{
				return null;
			}

			var parts = id.Split('/');

			if (parts.Length == 1)
			{
				return id;
			}

			return parts[0] == SuggestionEngine.kRegions || parts[0] == SuggestionEngine.kThematic ? parts[1] : null;
		}

		public void Dismiss(string themeId)
		{
			lock (m_Lock)
			{
				if (m_File.dismissed.Contains(themeId))
				{
					return;
				}

				m_File.dismissed.Add(themeId);
			}

			Save();
		}

		public string Export(string id)
		{
			var library = Get(id);

			if (library == null)
			{
				return null;
			}

			var path = Path.Combine(Paths.ExportDir, Paths.SafeFileName(library.name) + ".json");

			Paths.WriteAtomic(path, JsonConvert.SerializeObject(library, Formatting.Indented));

			return path;
		}

		public int ExportAll()
		{
			var count = 0;

			foreach (var library in Snapshot())
			{
				if (Export(library.id) != null)
				{
					count++;
				}
			}

			return count;
		}

		// Imports every library file found in the Import folder. A library with a known id is
		// merged, anything else is added. Unknown asset keys are kept as they are.
		public int ImportAll()
		{
			Directory.CreateDirectory(Paths.ImportDir);

			var count = 0;

			foreach (var path in Directory.GetFiles(Paths.ImportDir, "*.json"))
			{
				try
				{
					var imported = JsonConvert.DeserializeObject<Library>(File.ReadAllText(path));

					if (imported == null || (imported.assets == null && string.IsNullOrEmpty(imported.name)))
					{
						Mod.Log.Warn("Skipped " + Path.GetFileName(path) + ": not a library file");
						continue;
					}

					imported.assets = (imported.assets ?? new List<string>()).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();

					lock (m_Lock)
					{
						var existing = string.IsNullOrEmpty(imported.id) ? null : m_File.libraries.FirstOrDefault(x => x.id == imported.id);

						if (existing != null)
						{
							foreach (var key in imported.assets)
							{
								if (!existing.assets.Contains(key))
								{
									existing.assets.Add(key);
								}
							}
						}
						else
						{
							if (string.IsNullOrEmpty(imported.id))
							{
								imported.id = Guid.NewGuid().ToString();
							}

							if (string.IsNullOrWhiteSpace(imported.name))
							{
								imported.name = Path.GetFileNameWithoutExtension(path);
							}

							imported.description ??= "";
							imported.tags ??= new List<string>();
							imported.source = string.IsNullOrEmpty(imported.source) ? "user" : imported.source;
							imported.created ??= DateTime.UtcNow.ToString("o");

							m_File.libraries.Add(imported);
						}
					}

					count++;
				}
				catch (Exception ex)
				{
					Mod.Log.Warn("Skipped " + Path.GetFileName(path) + ": " + ex.Message);
				}
			}

			if (count > 0)
			{
				Save();
			}

			return count;
		}

		// community.json shaped file built from libraries that started as suggestions,
		// including whatever the user added to them by hand.
		public string ExportContribution()
		{
			var file = new CommunityFile { version = 1 };

			foreach (var library in Snapshot())
			{
				var themeId = ThemeIdOf(library.source);

				if (themeId == null)
				{
					continue;
				}

				foreach (var key in library.assets)
				{
					var prefabName = PrefabNameOf(key);

					if (!file.prefabs.TryGetValue(prefabName, out var themes))
					{
						file.prefabs[prefabName] = themes = new List<string>();
					}

					if (!themes.Contains(themeId))
					{
						themes.Add(themeId);
					}
				}
			}

			var path = Path.Combine(Paths.ExportDir, "contribution.json");

			Paths.WriteAtomic(path, JsonConvert.SerializeObject(file, Formatting.Indented));

			return path;
		}

		// Keys are "<prefab type>:<prefab name>".
		public static string PrefabNameOf(string key)
		{
			var index = key.IndexOf(':');

			return index < 0 ? key : key.Substring(index + 1);
		}
	}
}
