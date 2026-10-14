using System;
using System.Collections.Generic;
using System.IO;

using Colossal.UI;

using Game.Prefabs;
using Game.SceneFlow;
using Game.UI;

using Unity.Entities;

namespace ShelveIt.Logic
{
	// Single place that decides which image an asset gets. Nothing is rendered or generated here:
	// an empty url tells the UI to draw its own tile.
	public static class Thumbnails
	{
		public const string kReal = "real";
		public const string kGroup = "group";
		public const string kNone = "none";

		public const string kHost = "shelveit-thumbs";

		private static bool s_HostRegistered;
		private static Dictionary<string, string> s_UserFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public static string UserDir => Path.Combine(Paths.DataRoot, "Thumbs");

		// Main thread only. Safe to call more than once.
		public static void RegisterHost()
		{
			if (s_HostRegistered)
			{
				return;
			}

			try
			{
				Directory.CreateDirectory(UserDir);
				UIManager.defaultUISystem.AddHostLocation(kHost, UserDir, false, 0);
				s_HostRegistered = true;
				Mod.Log.Info($"User thumbnails: coui://{kHost}/ -> {UserDir}");
			}
			catch (Exception ex)
			{
				Mod.Log.Warn("Could not register the user thumbnail host: " + ex.Message);
			}
		}

		// Called once per scan so resolving an asset never touches the disk.
		public static void RefreshIndex()
		{
			var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			try
			{
				if (Directory.Exists(UserDir))
				{
					foreach (var path in Directory.GetFiles(UserDir, "*.png", SearchOption.TopDirectoryOnly))
					{
						var fileName = Path.GetFileName(path);

						files[Path.GetFileNameWithoutExtension(fileName)] = fileName;
					}
				}
			}
			catch (Exception ex)
			{
				Mod.Log.Warn("Could not list user thumbnails: " + ex.Message);
			}

			s_UserFiles = files;
		}

		// 1. the prefab's own icon, 2. the zone's group icon, 3. a user supplied png, 4. nothing.
		public static string Resolve(PrefabBase prefab, Entity zonePrefab, ImageSystem imageSystem, out string kind)
		{
			string icon = null;

			try
			{
				icon = ImageSystem.GetIcon(prefab);
			}
			catch
			{
			}

			if (IsUsable(icon))
			{
				kind = kReal;
				return icon;
			}

			string group = null;

			if (zonePrefab != Entity.Null)
			{
				try
				{
					group = imageSystem.GetIconOrGroupIcon(zonePrefab);
				}
				catch
				{
				}
			}

			if (IsUsable(group) && group != imageSystem.placeholderIcon)
			{
				kind = kGroup;
				return group;
			}

			var user = FindUserFile(prefab);

			if (user != null)
			{
				kind = kReal;
				return "coui://" + kHost + "/" + Uri.EscapeDataString(user);
			}

			kind = kNone;
			return "";
		}

		private static bool IsUsable(string url)
		{
			return !string.IsNullOrEmpty(url) && !url.StartsWith("thumbnail://", StringComparison.OrdinalIgnoreCase);
		}

		private static string FindUserFile(PrefabBase prefab)
		{
			if (s_UserFiles.Count == 0)
			{
				return null;
			}

			var name = prefab.GetType().Name + "." + prefab.name;

			if (s_UserFiles.TryGetValue(name, out var file) || s_UserFiles.TryGetValue(Paths.SafeFileName(name), out file))
			{
				return file;
			}

			return null;
		}
	}
}
