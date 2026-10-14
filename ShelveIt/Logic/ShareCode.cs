using System;
using System.IO;
using System.IO.Compression;
using System.Text;

using ShelveIt.Domain;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShelveIt.Logic
{
	// Offline share code: "SHELVE1:" + base64url (no padding) of the gzip of the payload JSON (UTF-8,
	// no BOM, compact). The payload is the same object the community server stores.
	public static class ShareCode
	{
		public const string kPrefix = "SHELVE1:";

		// Decoded codes larger than this are rejected before parsing.
		private const int kMaxJsonBytes = 4 * 1024 * 1024;

		public class DecodeException : Exception
		{
			// "format", "schema" or "empty". The UI localizes the code.
			public string Code { get; }

			public DecodeException(string code, string message) : base(message)
			{
				Code = code;
			}
		}

		public static readonly JsonSerializerSettings kJson = new JsonSerializerSettings
		{
			NullValueHandling = NullValueHandling.Ignore,
			Formatting = Formatting.None,
		};

		public static string Encode(CommunityPayload payload)
		{
			var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload, kJson));

			using (var output = new MemoryStream())
			{
				using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
				{
					gzip.Write(json, 0, json.Length);
				}

				return kPrefix + ToBase64Url(output.ToArray());
			}
		}

		public static CommunityPayload Decode(string code)
		{
			var text = StripWhitespace(code ?? "");

			if (text.Length == 0)
			{
				throw new DecodeException("empty", "The code is empty.");
			}

			if (!text.StartsWith(kPrefix, StringComparison.OrdinalIgnoreCase))
			{
				throw new DecodeException("format", "The code does not start with " + kPrefix);
			}

			string json;

			try
			{
				var bytes = FromBase64Url(text.Substring(kPrefix.Length));

				using (var input = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
				using (var output = new MemoryStream())
				{
					var buffer = new byte[8192];
					int read;

					while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
					{
						output.Write(buffer, 0, read);

						if (output.Length > kMaxJsonBytes)
						{
							throw new DecodeException("format", "The code is too large.");
						}
					}

					json = Encoding.UTF8.GetString(output.ToArray());
				}
			}
			catch (DecodeException)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new DecodeException("format", "The code is damaged: " + ex.Message);
			}

			JObject root;

			try
			{
				root = JObject.Parse(json);
			}
			catch (Exception ex)
			{
				throw new DecodeException("format", "The code is damaged: " + ex.Message);
			}

			var schema = root.Value<int?>("schema") ?? 0;

			if (schema != CommunityPayload.kSchema)
			{
				throw new DecodeException("schema", $"The code uses library format {schema}, this version of Shelve It reads format {CommunityPayload.kSchema}.");
			}

			var payload = root.ToObject<CommunityPayload>();

			Normalize(payload);

			return payload;
		}

		// Fills nulls left by a hand-made or partial payload.
		public static void Normalize(CommunityPayload payload)
		{
			payload.name ??= "";
			payload.description ??= "";
			payload.tags ??= new System.Collections.Generic.List<string>();
			payload.assets ??= new System.Collections.Generic.List<PayloadAsset>();
			payload.packages ??= new System.Collections.Generic.List<PayloadPackage>();
			payload.preview ??= new System.Collections.Generic.List<string>();
			payload.assets.RemoveAll(x => x == null || string.IsNullOrEmpty(x.k));
			payload.packages.RemoveAll(x => x == null || x.id <= 0);
		}

		private static string StripWhitespace(string text)
		{
			var builder = new StringBuilder(text.Length);

			foreach (var c in text)
			{
				if (!char.IsWhiteSpace(c))
				{
					builder.Append(c);
				}
			}

			return builder.ToString();
		}

		public static string ToBase64Url(byte[] bytes)
		{
			return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		}

		public static byte[] FromBase64Url(string text)
		{
			var s = text.Replace('-', '+').Replace('_', '/').TrimEnd('=');

			switch (s.Length % 4)
			{
				case 2: s += "=="; break;
				case 3: s += "="; break;
				case 1: throw new FormatException("bad length");
			}

			return Convert.FromBase64String(s);
		}
	}
}
