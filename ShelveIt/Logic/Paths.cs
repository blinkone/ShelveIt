using System.IO;

using Colossal.PSI.Environment;

namespace ShelveIt.Logic
{
	public static class Paths
	{
		public static string DataRoot => Path.Combine(EnvPath.kUserDataPath, "ModsData", "ShelveIt");
		public static string Libraries => Path.Combine(DataRoot, "libraries.json");
		public static string Cache => Path.Combine(DataRoot, "cache.json");
		public static string ScanDump => Path.Combine(DataRoot, "scan-dump.json");
		public static string ExportDir => Path.Combine(DataRoot, "Export");
		public static string ImportDir => Path.Combine(DataRoot, "Import");
		public static string Published => Path.Combine(DataRoot, "published.json");
		public static string Subscribed => Path.Combine(DataRoot, "subscribed.json");
		public static string ClientId => Path.Combine(DataRoot, "client-id.txt");

		// The mod was called "Asset Libraries" before the rename. Moves the old data folder once.
		private const string kLegacyDataFolder = "AssetLibraries";

		public static void MigrateLegacyData()
		{
			try
			{
				var legacy = Path.Combine(EnvPath.kUserDataPath, "ModsData", kLegacyDataFolder);

				if (Directory.Exists(DataRoot) || !Directory.Exists(legacy))
				{
					return;
				}

				Directory.Move(legacy, DataRoot);
				Mod.Log.Info("Moved data folder " + legacy + " to " + DataRoot);
			}
			catch (System.Exception e)
			{
				Mod.Log.Warn("Could not move the old data folder: " + e.Message);
			}
		}

		// Where the game keeps subscribed PDX mods and their metadata.
		public static string ModsCache => Path.Combine(EnvPath.kUserDataPath, ".cache", "Mods");

		// Set by Mod.OnLoad from the executable asset path.
		public static string InstallDir { get; set; }

		// Data files are deployed to <install>\Data, the install root is accepted too.
		public static string FindDataFile(string fileName)
		{
			if (string.IsNullOrEmpty(InstallDir))
			{
				return null;
			}

			var path = Path.Combine(InstallDir, "Data", fileName);

			if (File.Exists(path))
			{
				return path;
			}

			path = Path.Combine(InstallDir, fileName);

			return File.Exists(path) ? path : null;
		}

		// Write to a temp file first so a crash never leaves a truncated file behind.
		public static void WriteAtomic(string path, string content)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));

			var temp = path + ".tmp";

			File.WriteAllText(temp, content);

			if (File.Exists(path))
			{
				File.Replace(temp, path, null);
			}
			else
			{
				File.Move(temp, path);
			}
		}

		public static string SafeFileName(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return "library";
			}

			foreach (var c in Path.GetInvalidFileNameChars())
			{
				name = name.Replace(c, '_');
			}

			return name.Trim();
		}
	}
}
