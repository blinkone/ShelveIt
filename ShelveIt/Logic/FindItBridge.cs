using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ShelveIt.Logic
{
	// Everything that touches Find It lives here. Reflection only, no compile time reference.
	public static class FindItBridge
	{
		public const string kPrefix = "lib:";

		private static bool s_Resolved;
		private static Func<string, string, bool, bool> s_SearchCheck;
		private static bool s_SearchCheckFlag;
		private static PropertyInfo s_SettingsProperty;
		private static PropertyInfo s_StrictProperty;
		private static bool s_Logged;

		// PDX mod id -> prefab names, published by the scan. Replaced as a whole, never mutated.
		private static volatile Dictionary<int, HashSet<string>> s_PackageNames;

		public static void SetPackageNames(Dictionary<int, HashSet<string>> names)
		{
			s_PackageNames = names;
		}

		private static bool IsDigits(string text)
		{
			if (text.Length == 0 || text.Length > 9)
			{
				return false;
			}

			foreach (var c in text)
			{
				if (c < '0' || c > '9')
				{
					return false;
				}
			}

			return true;
		}

		public static bool IsAvailable()
		{
			try
			{
				return GetAssembly() != null;
			}
			catch (Exception ex)
			{
				LogOnce(ex);
				return false;
			}
		}

		// Find It replaces its whole text search with the returned delegate and calls it with the
		// display name and then the prefab name of each prefab. It must never be null.
		public static Func<string, bool> GetSearchMethod(string searchText)
		{
			try
			{
				var text = searchText ?? "";
				var trimmed = text.TrimStart();

				if (trimmed.StartsWith(kPrefix, StringComparison.OrdinalIgnoreCase))
				{
					var keys = Mod.Store?.FindAssetKeys(trimmed.Substring(kPrefix.Length).Trim());

					if (keys == null)
					{
						return _ => false;
					}

					var names = new HashSet<string>(keys.Select(LibraryStore.PrefabNameOf), StringComparer.Ordinal);

					return names.Contains;
				}

				var search = GetDefaultSearch(text);
				var packages = s_PackageNames;
				var id = text.Trim();

				// Find It's own text search also matches a prefab whose PDX mod id equals the search
				// text. The hook only receives names, so the id is resolved through the scan.
				if (packages != null && IsDigits(id) && int.TryParse(id, out var number) && packages.TryGetValue(number, out var packageNames))
				{
					return name => search(name) || (name != null && packageNames.Contains(name));
				}

				return search;
			}
			catch (Exception ex)
			{
				LogOnce(ex);

				var text = searchText ?? "";

				return name => name != null && name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
			}
		}

		// Reproduces Find It's own text search through its SearchUtil, honoring its strict setting.
		private static Func<string, bool> GetDefaultSearch(string text)
		{
			Resolve();

			var strict = false;

			try
			{
				if (s_SettingsProperty != null && s_StrictProperty != null)
				{
					var settings = s_SettingsProperty.GetValue(null);

					strict = settings != null && (bool)s_StrictProperty.GetValue(settings);
				}
			}
			catch (Exception ex)
			{
				LogOnce(ex);
			}

			if (strict || s_SearchCheck == null)
			{
				return name => name != null && name.IndexOf(text, StringComparison.InvariantCultureIgnoreCase) >= 0;
			}

			var check = s_SearchCheck;
			var flag = s_SearchCheckFlag;

			return name => name != null && check(text, name, flag);
		}

		private static void Resolve()
		{
			if (s_Resolved)
			{
				return;
			}

			s_Resolved = true;

			try
			{
				var assembly = GetAssembly();

				if (assembly == null)
				{
					return;
				}

				var method = assembly.GetType("FindIt.Utilities.SearchUtil")?.GetMethod("SearchCheck", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				var parameters = method?.GetParameters();

				if (parameters != null
					&& parameters.Length == 3
					&& parameters[0].ParameterType == typeof(string)
					&& parameters[1].ParameterType == typeof(string)
					&& parameters[2].ParameterType == typeof(bool)
					&& method.ReturnType == typeof(bool))
				{
					s_SearchCheckFlag = parameters[2].HasDefaultValue && parameters[2].DefaultValue is bool value && value;
					s_SearchCheck = (Func<string, string, bool, bool>)Delegate.CreateDelegate(typeof(Func<string, string, bool, bool>), method);
				}
				else
				{
					Mod.Log.Warn("Find It SearchCheck has an unexpected shape, falling back to a plain contains search");
				}

				s_SettingsProperty = assembly.GetType("FindIt.Mod")?.GetProperty("Settings", BindingFlags.Static | BindingFlags.Public);
				s_StrictProperty = s_SettingsProperty?.PropertyType.GetProperty("StrictSearch", BindingFlags.Instance | BindingFlags.Public);
			}
			catch (Exception ex)
			{
				LogOnce(ex);
			}
		}

		private static Assembly GetAssembly()
		{
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (assembly.GetName().Name == "FindIt")
				{
					return assembly;
				}
			}

			return null;
		}

		private static void LogOnce(Exception ex)
		{
			if (s_Logged)
			{
				return;
			}

			s_Logged = true;

			Mod.Log.Warn("Find It integration failed: " + ex.Message);
		}
	}
}
