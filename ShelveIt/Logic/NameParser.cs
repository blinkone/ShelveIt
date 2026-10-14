using System;
using System.Collections.Generic;
using System.Text;

namespace ShelveIt.Logic
{
	// Pure string work on prefab names. No game types, safe on any thread.
	public static class NameParser
	{
		public struct Parsed
		{
			public string family;
			public string code;
			public string corner;
			public string density;
			public string eaiPack;
		}

		private static readonly HashSet<string> s_Variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"Left", "Right", "Corner", "Middle", "Mid", "End", "Ext",
		};

		private static readonly HashSet<string> s_RowWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"Row", "Rowhouse", "Rowhouses", "Rowhome", "Rowhomes", "Terrace", "Terraced", "Townhouse", "Townhouses",
		};

		private static readonly string[] s_BrandProps = { "Billboard", "Sign", "Poster", "GasStation", "Screen", "AStand", "Stand" };

		// Folder names used by ExtraAssetsImporter between the pack name and the asset name.
		private static readonly HashSet<string> s_EaiCategories = new HashSet<string>(StringComparer.Ordinal)
		{
			"Ground", "RoadMarkings", "WallDecor", "Graffiti", "Stains", "RoadAssets", "Misc", "Puddles", "Trash", "Beach",
			"Grass", "Rock", "Sand", "Water", "Tiles", "Concrete", "Pavement", "Asphalt", "Brick", "Wood", "Moss", "Numbers",
			"Alphabet", "Leaf", "Industry", "Cobblestone", "Detail", "Parking", "Posters", "Dirt", "Snow", "Metal", "Stone", "Gravel",
		};

		// Words that usually close an EAI pack name. A category right after one of them wins.
		private static readonly HashSet<string> s_PackTails = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"Pack", "Decals", "Surfaces", "Decal", "Surface", "Textures", "Texture", "CS2", "Set",
		};

		// Sub-objects, random wrappers and helper prefabs that are never placed on their own.
		public static bool IsNoiseName(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return true;
			}

			if (name.IndexOf("Placeholder", StringComparison.Ordinal) >= 0
				|| name.EndsWith("Dependency", StringComparison.Ordinal)
				|| name.EndsWith("InstaLOD", StringComparison.Ordinal)
				|| name.EndsWith(" Collision", StringComparison.Ordinal))
			{
				return true;
			}

			var end = name.Length;

			while (end > 0 && char.IsDigit(name[end - 1]))
			{
				end--;
			}

			if (end < name.Length && end >= 4 && string.CompareOrdinal(name, end - 4, "_Sub", 0, 4) == 0)
			{
				return true;
			}

			return name.Length > 8 && name.EndsWith("Details", StringComparison.Ordinal) && char.IsDigit(name[name.Length - 8]);
		}

		// Vanilla brand props are named "BillboardWallSmall02 - MouthwaterPress". The brand part
		// says nothing about the asset, so classification only looks at what is before it.
		public static string StripBrand(string name)
		{
			var split = name.IndexOf(" - ", StringComparison.Ordinal);

			if (split <= 0 || name.IndexOf(' ', split + 3) >= 0)
			{
				return name;
			}

			foreach (var prefix in s_BrandProps)
			{
				if (name.StartsWith(prefix, StringComparison.Ordinal))
				{
					return name.Substring(0, split);
				}
			}

			return name;
		}

		// "Pack name Category Asset name Decal" -> pack name and category, or false.
		public static bool TryParseEai(string name, out string pack, out string category)
		{
			pack = category = null;

			if (!name.EndsWith(" Decal", StringComparison.Ordinal) && !name.EndsWith(" Surface", StringComparison.Ordinal))
			{
				return false;
			}

			var parts = name.Split(' ');
			var first = -1;
			var best = -1;

			for (var i = 1; i < parts.Length - 1; i++)
			{
				if (!s_EaiCategories.Contains(parts[i]))
				{
					continue;
				}

				if (first < 0)
				{
					first = i;
				}

				// The asset name often starts with the creator tag again: "G87 ... Stains G87 Rust 10".
				if (parts[i + 1] == parts[0])
				{
					best = i;
					break;
				}

				if (best < 0 && (s_PackTails.Contains(parts[i - 1]) || IsNumber(parts[i - 1])))
				{
					best = i;
				}
			}

			if (best < 0)
			{
				best = first;
			}

			if (best < 0)
			{
				return false;
			}

			pack = string.Join(" ", parts, 0, best);
			category = parts[best];
			return true;
		}

		// codes: region prefixes as written in themes.json ("UK", "GER", "EU_It").
		public static Parsed Parse(string name, ICollection<string> codes)
		{
			var result = new Parsed { family = "", code = "", corner = "", density = "", eaiPack = "" };
			var words = new List<string>(8);
			var chunkStart = new List<bool>(8);

			SplitWords(name, words, chunkStart);

			if (words.Count == 0)
			{
				result.family = name ?? "";
				return result;
			}

			var skip = FindCode(words, chunkStart, codes, out result.code);
			var kept = new List<string>(words.Count);
			var low = false;
			var rent = false;

			for (var i = skip; i < words.Count; i++)
			{
				var word = words[i];

				if (IsNumber(word) || word.Length == 1)
				{
					continue;
				}

				if (s_Variants.Contains(word))
				{
					if (word.Equals("Left", StringComparison.OrdinalIgnoreCase) || word.Equals("Right", StringComparison.OrdinalIgnoreCase))
					{
						result.corner = result.corner.Length == 0 || result.corner == "Corner" ? Capitalize(word) : result.corner;
					}
					else if (word.Equals("Corner", StringComparison.OrdinalIgnoreCase) && result.corner.Length == 0)
					{
						result.corner = "Corner";
					}

					continue;
				}

				if (result.density.Length == 0 || result.density == "Low" || result.density == "Medium" || result.density == "High")
				{
					if (s_RowWords.Contains(word))
					{
						result.density = "Row";
					}
					else if (word.Equals("Mixed", StringComparison.OrdinalIgnoreCase))
					{
						result.density = "Mixed";
					}
					else if (result.density.Length == 0)
					{
						if (word.Equals("Low", StringComparison.OrdinalIgnoreCase))
						{
							result.density = "Low";
							low = true;
						}
						else if (word.Equals("Medium", StringComparison.OrdinalIgnoreCase))
						{
							result.density = "Medium";
						}
						else if (word.Equals("High", StringComparison.OrdinalIgnoreCase))
						{
							result.density = "High";
						}
					}
				}

				if (low && word.Equals("Rent", StringComparison.OrdinalIgnoreCase))
				{
					rent = true;
				}

				kept.Add(word);
			}

			// Low rent housing is a high density zone.
			if (rent && result.density == "Low")
			{
				result.density = "High";
			}

			if (kept.Count == 0)
			{
				for (var i = skip; i < words.Count; i++)
				{
					if (!IsNumber(words[i]))
					{
						kept.Add(words[i]);
					}
				}
			}

			result.family = Join(kept, 0);
			return result;
		}

		public static string Join(List<string> words, int from)
		{
			var builder = new StringBuilder();

			for (var i = from; i < words.Count; i++)
			{
				if (builder.Length > 0)
				{
					builder.Append(' ');
				}

				builder.Append(Capitalize(words[i]));
			}

			return builder.ToString();
		}

		public static string Humanize(string name)
		{
			var words = new List<string>(8);

			SplitWords(name, words, new List<bool>(8));

			return words.Count == 0 ? name ?? "" : Join(words, 0);
		}

		// Lower case, dash separated id fragment.
		public static string Slug(string text)
		{
			var builder = new StringBuilder(text.Length);
			var dash = false;

			foreach (var c in text)
			{
				if (char.IsLetterOrDigit(c))
				{
					builder.Append(char.ToLowerInvariant(c));
					dash = false;
				}
				else if (!dash && builder.Length > 0)
				{
					builder.Append('-');
					dash = true;
				}
			}

			return builder.ToString().TrimEnd('-');
		}

		// Returns how many leading words belong to the author and region prefix.
		private static int FindCode(List<string> words, List<bool> chunkStart, ICollection<string> codes, out string code)
		{
			code = "";

			if (codes == null || codes.Count == 0)
			{
				return 0;
			}

			for (var start = 0; start <= 1 && start < words.Count; start++)
			{
				// An author prefix is only accepted as its own chunk: "Seriphyn_NA_...".
				if (start == 1 && (!chunkStart[1] || words.Count < 3))
				{
					break;
				}

				var word = words[start];
				var nextIsNumber = start + 1 < words.Count && !chunkStart[start + 1] && IsNumber(words[start + 1]);

				if (start + 1 < words.Count && codes.Contains(word + "_" + words[start + 1]))
				{
					code = word + "_" + words[start + 1];
					return start + 2;
				}

				if (codes.Contains(word) && !nextIsNumber)
				{
					code = word;
					return start + 1;
				}

				// "RRUKTerrace": author initials glued to the region code.
				if (start == 0 && word.Length >= 4 && word.Length <= 6 && IsUpper(word))
				{
					for (var length = 3; length >= 2; length--)
					{
						var tail = word.Substring(word.Length - length);

						if (word.Length - length >= 2 && codes.Contains(tail))
						{
							code = tail;
							return 1;
						}
					}
				}
			}

			return 0;
		}

		// Splits on separators first, drops lot size, level and version chunks, then splits camelCase.
		private static void SplitWords(string name, List<string> words, List<bool> chunkStart)
		{
			if (string.IsNullOrEmpty(name))
			{
				return;
			}

			var start = 0;

			for (var i = 0; i <= name.Length; i++)
			{
				if (i < name.Length && (char.IsLetterOrDigit(name[i]) || name[i] == '\'' || name[i] == '&'))
				{
					continue;
				}

				if (i > start)
				{
					AddChunk(name, start, i, words, chunkStart);
				}

				start = i + 1;
			}
		}

		private static void AddChunk(string name, int start, int end, List<string> words, List<bool> chunkStart)
		{
			var length = end - start;

			// "L3", "v2"
			if (length >= 2 && length <= 3 && (name[start] == 'L' || name[start] == 'v' || name[start] == 'V') && AllDigits(name, start + 1, end))
			{
				return;
			}

			// "2x3"
			var x = name.IndexOf('x', start, length);

			if (x > start && x < end - 1 && AllDigits(name, start, x) && AllDigits(name, x + 1, end))
			{
				return;
			}

			var first = true;
			var wordStart = start;

			for (var i = start + 1; i <= end; i++)
			{
				var boundary = i == end;

				if (!boundary)
				{
					var c = name[i];
					var p = name[i - 1];

					if (char.IsDigit(c) != char.IsDigit(p))
					{
						boundary = true;
					}
					else if (char.IsUpper(c) && char.IsLower(p))
					{
						boundary = true;
					}
					else if (char.IsUpper(c) && char.IsUpper(p) && i + 1 < end && char.IsLower(name[i + 1]))
					{
						boundary = true;
					}
				}

				if (!boundary)
				{
					continue;
				}

				words.Add(name.Substring(wordStart, i - wordStart));
				chunkStart.Add(first);
				first = false;
				wordStart = i;
			}
		}

		private static bool AllDigits(string text, int start, int end)
		{
			if (start >= end)
			{
				return false;
			}

			for (var i = start; i < end; i++)
			{
				if (!char.IsDigit(text[i]))
				{
					return false;
				}
			}

			return true;
		}

		private static bool IsNumber(string word)
		{
			return AllDigits(word, 0, word.Length);
		}

		private static bool IsUpper(string word)
		{
			foreach (var c in word)
			{
				if (!char.IsUpper(c))
				{
					return false;
				}
			}

			return true;
		}

		private static string Capitalize(string word)
		{
			return word.Length > 0 && char.IsLower(word[0]) ? char.ToUpperInvariant(word[0]) + word.Substring(1) : word;
		}
	}
}
