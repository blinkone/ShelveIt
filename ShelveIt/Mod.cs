using System;
using System.IO;

using ShelveIt.Logic;
using ShelveIt.Systems;

using Colossal.IO.AssetDatabase;
using Colossal.Logging;

using Game;
using Game.Modding;
using Game.SceneFlow;

namespace ShelveIt
{
	public class Mod : IMod
	{
		public const string Id = "ShelveIt";

		public static ILog Log { get; } = LogManager.GetLogger(Id).SetShowsErrorsInUI(false);
		public static Setting Settings { get; private set; }
		public static LibraryStore Store { get; private set; }

		public void OnLoad(UpdateSystem updateSystem)
		{
			Log.Info(nameof(OnLoad));

			if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
			{
				Paths.InstallDir = Path.GetDirectoryName(asset.path);
				Log.Info("Mod folder: " + Paths.InstallDir);
			}
			else
			{
				Paths.InstallDir = Path.GetDirectoryName(typeof(Mod).Assembly.Location);
			}

			Paths.MigrateLegacyData();

			Store = new LibraryStore();
			Store.Load();

			Settings = new Setting(this);
			Settings.RegisterInOptionsUI();

			foreach (var locale in LocaleSource.kLocales)
			{
				GameManager.instance.localizationManager.AddSource(locale, new LocaleSource(locale, Settings));
			}

			AssetDatabase.global.LoadSettings(Id, Settings, new Setting(this));

			Thumbnails.RegisterHost();

			updateSystem.UpdateAt<AssetScanSystem>(SystemUpdatePhase.UIUpdate);
			updateSystem.UpdateAt<ShelveItUISystem>(SystemUpdatePhase.UIUpdate);
			updateSystem.UpdateAt<CommunityUISystem>(SystemUpdatePhase.UIUpdate);
			updateSystem.UpdateAt<PackageLoaderSystem>(SystemUpdatePhase.UIUpdate);
		}

		public void OnDispose()
		{
			Log.Info(nameof(OnDispose));

			if (Settings != null)
			{
				Settings.UnregisterInOptionsUI();
				Settings = null;
			}
		}

		// Hook picked up by Find It (Mod.RegisterAPIs). The returned delegate is called with the
		// display name and the prefab name of every prefab and replaces Find It's text search.
		public static Func<string, bool> GetFindItSearchMethod(string searchText)
		{
			return FindItBridge.GetSearchMethod(searchText);
		}
	}
}
