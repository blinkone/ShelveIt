using System;
using System.Collections.Generic;
using System.Linq;

using ShelveIt.Domain;

namespace ShelveIt.Logic
{
	// Turns a library into a community payload and measures a payload against the installed assets.
	public static class PayloadBuilder
	{
		public const int kNameMin = 3;
		public const int kNameMax = 60;
		public const int kDescriptionMax = 500;
		public const int kTagsMax = 8;
		public const int kTagMax = 24;
		public const int kPreviewMax = 4;

		public class Analysis
		{
			public int installed;
			public int missing;
			public List<PayloadPackage> missingPackages = new List<PayloadPackage>();
			public List<string> previewThumbs = new List<string>();
		}

		public static string ModVersion => typeof(Mod).Assembly.GetName().Version?.ToString() ?? "";

		public static List<string> ParseTags(string text)
		{
			return CleanTags((text ?? "").Split(','));
		}

		// Up to kTagsMax distinct tags of at most kTagMax characters, as the server accepts them.
		public static List<string> CleanTags(IEnumerable<string> source)
		{
			var tags = new List<string>();

			foreach (var part in source ?? Enumerable.Empty<string>())
			{
				var tag = (part ?? "").Trim();

				if (tag.Length == 0)
				{
					continue;
				}

				if (tag.Length > kTagMax)
				{
					tag = tag.Substring(0, kTagMax).Trim();
				}

				if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
				{
					tags.Add(tag);
				}

				if (tags.Count == kTagsMax)
				{
					break;
				}
			}

			return tags;
		}

		public static string CleanDescription(string description)
		{
			description = (description ?? "").Trim();

			return description.Length > kDescriptionMax ? description.Substring(0, kDescriptionMax) : description;
		}

		// Asset keys with the PDX package of each asset, the titles of those packages and up to four
		// keys of assets with a real thumbnail. The package comes from the scan when the asset is
		// installed, else from what an import recorded in the store, else it is 0.
		public static CommunityPayload Build(Library library, IReadOnlyDictionary<string, AssetRecord> byKey, LibraryStore store, string description, List<string> tags)
		{
			var name = (library.name ?? "").Trim();

			if (name.Length > kNameMax)
			{
				name = name.Substring(0, kNameMax).Trim();
			}

			var payload = new CommunityPayload
			{
				name = name,
				description = CleanDescription(description),
				tags = CleanTags(tags),
				modVersion = ModVersion,
			};

			var packages = new Dictionary<int, string>();

			foreach (var key in library.assets.Distinct())
			{
				var pdxId = 0;

				if (byKey.TryGetValue(key, out var record))
				{
					pdxId = record.isVanilla ? 0 : Math.Max(0, record.pdxId);

					if (pdxId > 0 && !packages.ContainsKey(pdxId))
					{
						packages[pdxId] = record.packageTitle ?? "";
					}

					if (payload.preview.Count < kPreviewMax && record.hasThumbnail)
					{
						payload.preview.Add(key);
					}
				}
				else if (store != null && store.TryGetImportedPackage(key, out var storedId, out var storedTitle))
				{
					pdxId = storedId;

					if (!packages.TryGetValue(pdxId, out var title) || string.IsNullOrEmpty(title))
					{
						packages[pdxId] = storedTitle;
					}
				}

				payload.assets.Add(new PayloadAsset { k = key, p = pdxId });
			}

			foreach (var pair in packages)
			{
				payload.packages.Add(new PayloadPackage { id = pair.Key, title = pair.Value });
			}

			return payload;
		}

		// A package is missing when none of the installed assets comes from it.
		public static Analysis Analyze(CommunityPayload payload, IReadOnlyDictionary<string, AssetRecord> byKey, IEnumerable<AssetRecord> records)
		{
			var result = new Analysis();
			var installedPackages = new HashSet<int>();

			foreach (var record in records)
			{
				if (record.pdxId > 0)
				{
					installedPackages.Add(record.pdxId);
				}
			}

			foreach (var asset in payload.assets)
			{
				if (byKey.ContainsKey(asset.k))
				{
					result.installed++;
				}
				else
				{
					result.missing++;
				}
			}

			var titles = new Dictionary<int, string>();

			foreach (var package in payload.packages)
			{
				titles[package.id] = package.title ?? "";
			}

			var seen = new HashSet<int>();

			foreach (var asset in payload.assets)
			{
				if (asset.p > 0 && !installedPackages.Contains(asset.p) && seen.Add(asset.p))
				{
					titles.TryGetValue(asset.p, out var title);
					result.missingPackages.Add(new PayloadPackage { id = asset.p, title = title ?? "" });
				}
			}

			foreach (var key in payload.preview)
			{
				if (result.previewThumbs.Count < kPreviewMax && key != null && byKey.TryGetValue(key, out var record) && !string.IsNullOrEmpty(record.thumbnail))
				{
					result.previewThumbs.Add(record.thumbnail);
				}
			}

			return result;
		}
	}
}
