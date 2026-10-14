using ShelveIt.Logic;
using ShelveIt.Systems;

using Colossal.IO.AssetDatabase;

using Game.Modding;
using Game.Settings;

namespace ShelveIt
{
	[FileLocation("ModsSettings/" + Mod.Id + "/" + Mod.Id)]
	[SettingsUIGroupOrder(kSuggestionsGroup, kDataGroup, kCommunityGroup)]
	[SettingsUIShowGroupName(kSuggestionsGroup, kDataGroup, kCommunityGroup)]
	public class Setting : ModSetting
	{
		public const string kSection = "Main";
		public const string kSuggestionsGroup = "Suggestions";
		public const string kDataGroup = "Data";
		public const string kCommunityGroup = "Community";

		public Setting(IMod mod) : base(mod)
		{
			SetDefaults();
		}

		[SettingsUISection(kSection, kSuggestionsGroup)]
		public bool EnableSuggestions { get; set; }

		[SettingsUISection(kSection, kSuggestionsGroup)]
		[SettingsUISlider(min = 1, max = 50, step = 1)]
		public int MinAssetsPerSuggestion { get; set; }

		[SettingsUISection(kSection, kSuggestionsGroup)]
		[SettingsUIButton]
		public bool Rescan
		{
			set => ShelveItUISystem.Instance?.Rescan();
		}

		[SettingsUISection(kSection, kDataGroup)]
		[SettingsUIButton]
		public bool ExportLibraries
		{
			set => ShelveItUISystem.Instance?.ExportAllLibraries();
		}

		[SettingsUISection(kSection, kDataGroup)]
		[SettingsUIButton]
		public bool ImportLibraries
		{
			set => ShelveItUISystem.Instance?.ImportAll();
		}

		[SettingsUISection(kSection, kDataGroup)]
		[SettingsUIButton]
		public bool ExportContribution
		{
			set => ShelveItUISystem.Instance?.ExportContribution();
		}

		// The mod contacts this server only when a Community feature is used.
		[SettingsUISection(kSection, kCommunityGroup)]
		[SettingsUITextInput]
		public string ServerUrl { get; set; }

		[SettingsUISection(kSection, kCommunityGroup)]
		[SettingsUIMultilineText]
		public string CommunityNote => string.Empty;

		[SettingsUISection(kSection, kCommunityGroup)]
		[SettingsUIButton]
		public bool ResetServerUrl
		{
			set
			{
				ServerUrl = CommunityClient.kDefaultServer;
				ApplyAndSave();
			}
		}

		// Panel layout chosen with the toggle in the title bar: "manager" or "palette". Not shown in the options.
		[SettingsUIHidden]
		public string PanelMode { get; set; }

		public override void SetDefaults()
		{
			EnableSuggestions = true;
			MinAssetsPerSuggestion = 5;
			PanelMode = ShelveItUISystem.kModeManager;
			ServerUrl = CommunityClient.kDefaultServer;
		}

		public override void Apply()
		{
			base.Apply();

			ShelveItUISystem.Instance?.OnSettingsChanged();
		}
	}
}
