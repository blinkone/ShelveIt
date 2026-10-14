using System.Collections.Generic;

namespace ShelveIt.Logic
{
	public static class Tokenizer
	{
		// Splits on separators, camelCase and letter/digit boundaries. Lowercases, drops
		// tokens shorter than 2 chars and pure numbers (4-digit years are kept).
		public static string[] Tokenize(string name)
		{
			var result = new List<string>();

			if (string.IsNullOrEmpty(name))
			{
				return result.ToArray();
			}

			var seen = new HashSet<string>();
			var start = 0;

			for (var i = 0; i <= name.Length; i++)
			{
				var boundary = i == name.Length;
				var skip = false;

				if (!boundary)
				{
					var c = name[i];

					if (!char.IsLetterOrDigit(c))
					{
						boundary = true;
						skip = true;
					}
					else if (i > start)
					{
						var p = name[i - 1];

						if (char.IsDigit(c) != char.IsDigit(p))
						{
							boundary = true;
						}
						else if (char.IsUpper(c) && char.IsLower(p))
						{
							boundary = true;
						}
						else if (char.IsUpper(c) && char.IsUpper(p) && i + 1 < name.Length && char.IsLower(name[i + 1]))
						{
							// "HTTPServer" -> "HTTP", "Server"
							boundary = true;
						}
					}
				}

				if (!boundary)
				{
					continue;
				}

				if (i > start)
				{
					Add(name.Substring(start, i - start).ToLowerInvariant(), result, seen);
				}

				start = skip ? i + 1 : i;
			}

			return result.ToArray();
		}

		private static void Add(string token, List<string> result, HashSet<string> seen)
		{
			if (token.Length < 2)
			{
				return;
			}

			if (char.IsDigit(token[0]))
			{
				if (token.Length != 4 || !int.TryParse(token, out var year) || year < 1000 || year > 2999)
				{
					return;
				}
			}

			if (seen.Add(token))
			{
				result.Add(token);
			}
		}
	}
}
