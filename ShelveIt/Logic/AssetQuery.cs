using System;
using System.Collections.Generic;

using ShelveIt.Domain;

namespace ShelveIt.Logic
{
	// Maps every facet value to a small integer so counting and filtering never touch strings.
	public sealed class FacetIndex
	{
		public const int kKind = 0, kZone = 1, kDensity = 2, kRegion = 3, kTheme = 4, kPack = 5, kSource = 6, kPackage = 7,
			kLevel = 8, kLotW = 9, kLotD = 10, kThumb = 11, kCorner = 12;

		public static readonly string[] Ids =
		{
			"kind", "zone", "density", "region", "theme", "pack", "source", "package", "level", "lotW", "lotD", "thumb", "corner",
		};

		public readonly List<string>[] values = new List<string>[Ids.Length];
		public readonly List<string>[] labels = new List<string>[Ids.Length];

		private readonly Dictionary<string, int>[] m_Lookup = new Dictionary<string, int>[Ids.Length];

		public static int FacetOf(string id)
		{
			return Array.IndexOf(Ids, id);
		}

		public static bool IsNumeric(int facet)
		{
			return facet == kLevel || facet == kLotW || facet == kLotD;
		}

		public static FacetIndex Build(IReadOnlyList<AssetRecord> records)
		{
			var index = new FacetIndex();

			for (var i = 0; i < Ids.Length; i++)
			{
				// Code 0 is always the empty value.
				index.values[i] = new List<string> { "" };
				index.labels[i] = new List<string> { "" };
				index.m_Lookup[i] = new Dictionary<string, int>(StringComparer.Ordinal) { { "", 0 } };
			}

			var packs = new List<int>(4);

			foreach (var record in records)
			{
				var codes = new int[Ids.Length];

				codes[kKind] = index.Code(kKind, record.kind);
				codes[kZone] = index.Code(kZone, record.zone == "None" ? "" : record.zone);
				codes[kDensity] = index.Code(kDensity, record.density);
				codes[kRegion] = index.Code(kRegion, record.region);
				codes[kTheme] = index.Code(kTheme, record.theme);
				codes[kSource] = index.Code(kSource, record.isVanilla ? "vanilla" : "custom");
				codes[kPackage] = index.Code(kPackage, record.package, record.packageTitle);
				codes[kLevel] = index.Code(kLevel, record.level > 0 ? record.level.ToString() : "");
				codes[kLotW] = index.Code(kLotW, record.lotW > 0 ? record.lotW.ToString() : "");
				codes[kLotD] = index.Code(kLotD, record.lotD > 0 ? record.lotD.ToString() : "");
				codes[kThumb] = index.Code(kThumb, record.hasThumbnail ? "yes" : "no");
				codes[kCorner] = index.Code(kCorner, record.corner);

				packs.Clear();

				if (record.packs != null)
				{
					foreach (var pack in record.packs)
					{
						if (!string.IsNullOrEmpty(pack))
						{
							packs.Add(index.Code(kPack, pack));
						}
					}
				}

				if (packs.Count == 0)
				{
					packs.Add(0);
				}

				record.codes = codes;
				record.packCodes = packs.ToArray();
			}

			return index;
		}

		public bool TryGetCode(int facet, string value, out int code)
		{
			return m_Lookup[facet].TryGetValue(value ?? "", out code);
		}

		private int Code(int facet, string value, string label = null)
		{
			value ??= "";

			if (!m_Lookup[facet].TryGetValue(value, out var code))
			{
				code = values[facet].Count;
				m_Lookup[facet][value] = code;
				values[facet].Add(value);
				labels[facet].Add(string.IsNullOrEmpty(label) ? value : label);
			}

			return code;
		}
	}

	public sealed class QueryState
	{
		public readonly Dictionary<string, HashSet<string>> filters = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
		public string search = "";
		public string sort = "";

		public bool HasFilters => filters.Count > 0 || search.Length > 0;
	}

	public struct FacetOption
	{
		public string value, label;
		public int count;
		public bool selected;
	}

	public sealed class FacetResult
	{
		public string id;
		public int total;
		public List<FacetOption> options = new List<FacetOption>();
	}

	public static class AssetQuery
	{
		public const int kMaxOptions = 40;

		public const string kSortName = "name";
		public const string kSortPackage = "package";
		public const string kSortLot = "lot";
		public const string kSortNewest = "newest";

		// Filters the base set (AND across facets, OR inside one) and counts, for every facet,
		// the options left under the other active filters. One pass over the set.
		public static List<AssetRecord> Run(IReadOnlyList<AssetRecord> baseSet, FacetIndex index, QueryState state, List<FacetResult> facets)
		{
			var facetCount = FacetIndex.Ids.Length;
			var allowed = new bool[facetCount][];
			var counts = new int[facetCount][];
			var active = new List<int>(4);

			for (var f = 0; f < facetCount; f++)
			{
				counts[f] = new int[index.values[f].Count];

				if (state.filters.TryGetValue(FacetIndex.Ids[f], out var selected) && selected.Count > 0)
				{
					allowed[f] = new bool[index.values[f].Count];
					active.Add(f);

					foreach (var value in selected)
					{
						if (index.TryGetCode(f, value, out var code))
						{
							allowed[f][code] = true;
						}
					}
				}
			}

			var terms = state.search.Length == 0 ? null : state.search.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			var result = new List<AssetRecord>(baseSet.Count);

			for (var i = 0; i < baseSet.Count; i++)
			{
				var record = baseSet[i];
				var codes = record.codes;

				if (codes == null || (terms != null && !Matches(record, terms)))
				{
					continue;
				}

				var failed = -1;
				var fails = 0;

				for (var a = 0; a < active.Count && fails < 2; a++)
				{
					var f = active[a];
					var pass = false;

					if (f == FacetIndex.kPack)
					{
						foreach (var pack in record.packCodes)
						{
							if (allowed[f][pack])
							{
								pass = true;
								break;
							}
						}
					}
					else
					{
						pass = allowed[f][codes[f]];
					}

					if (!pass)
					{
						fails++;
						failed = f;
					}
				}

				if (fails == 0)
				{
					result.Add(record);

					for (var f = 0; f < facetCount; f++)
					{
						if (f != FacetIndex.kPack)
						{
							counts[f][codes[f]]++;
						}
					}

					foreach (var pack in record.packCodes)
					{
						counts[FacetIndex.kPack][pack]++;
					}
				}
				else if (fails == 1)
				{
					if (failed == FacetIndex.kPack)
					{
						foreach (var pack in record.packCodes)
						{
							counts[failed][pack]++;
						}
					}
					else
					{
						counts[failed][codes[failed]]++;
					}
				}
			}

			if (facets != null)
			{
				facets.Clear();

				for (var f = 0; f < facetCount; f++)
				{
					var facet = BuildFacet(index, f, counts[f], allowed[f]);

					if (facet != null)
					{
						facets.Add(facet);
					}
				}
			}

			Sort(result, state.sort);

			return result;
		}

		private static bool Matches(AssetRecord record, string[] terms)
		{
			foreach (var term in terms)
			{
				if (record.searchText.IndexOf(term, StringComparison.Ordinal) < 0)
				{
					return false;
				}
			}

			return true;
		}

		private static FacetResult BuildFacet(FacetIndex index, int f, int[] counts, bool[] allowed)
		{
			var facet = new FacetResult { id = FacetIndex.Ids[f] };
			var anySelected = false;

			for (var code = 0; code < counts.Length; code++)
			{
				var selected = allowed != null && allowed[code];

				if (counts[code] == 0 && !selected)
				{
					continue;
				}

				anySelected |= selected;
				facet.options.Add(new FacetOption { value = index.values[f][code], label = index.labels[f][code], count = counts[code], selected = selected });
			}

			// A facet with a single option cannot narrow anything down.
			if (facet.options.Count < 2 && !anySelected)
			{
				return null;
			}

			facet.total = facet.options.Count;

			if (FacetIndex.IsNumeric(f))
			{
				facet.options.Sort((a, b) => Number(a.value).CompareTo(Number(b.value)));
			}
			else
			{
				facet.options.Sort((a, b) =>
				{
					if (a.selected != b.selected)
					{
						return a.selected ? -1 : 1;
					}

					return a.count != b.count ? b.count.CompareTo(a.count) : string.CompareOrdinal(a.value, b.value);
				});

				if (facet.options.Count > kMaxOptions)
				{
					facet.options.RemoveRange(kMaxOptions, facet.options.Count - kMaxOptions);
				}
			}

			return facet;
		}

		private static int Number(string value)
		{
			return int.TryParse(value, out var number) ? number : 0;
		}

		private static void Sort(List<AssetRecord> list, string sort)
		{
			switch (sort)
			{
				case kSortName:
					list.Sort((a, b) => a.nameRank.CompareTo(b.nameRank));
					break;

				case kSortPackage:
					list.Sort((a, b) => a.packageRank.CompareTo(b.packageRank));
					break;

				case kSortLot:
					list.Sort((a, b) =>
					{
						var result = (a.lotW * a.lotD).CompareTo(b.lotW * b.lotD);

						if (result == 0)
						{
							result = a.lotW.CompareTo(b.lotW);
						}

						return result != 0 ? result : a.nameRank.CompareTo(b.nameRank);
					});
					break;

				case kSortNewest:
					list.Sort((a, b) => a.installed != b.installed ? b.installed.CompareTo(a.installed) : a.nameRank.CompareTo(b.nameRank));
					break;
			}
		}
	}
}
