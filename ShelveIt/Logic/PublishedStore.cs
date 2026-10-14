using System;
using System.Collections.Generic;
using System.IO;

using ShelveIt.Domain;

using Newtonsoft.Json;

namespace ShelveIt.Logic
{
	// published.json (local library id -> server id and owner token) and the install id sent as
	// X-Shelve-Client. Owner tokens are secrets: they are never written to the log.
	public class PublishedStore
	{
		private PublishedFile m_File;
		private string m_ClientId;

		private PublishedFile File
		{
			get
			{
				if (m_File != null)
				{
					return m_File;
				}

				try
				{
					if (System.IO.File.Exists(Paths.Published))
					{
						m_File = JsonConvert.DeserializeObject<PublishedFile>(System.IO.File.ReadAllText(Paths.Published));
					}
				}
				catch (Exception ex)
				{
					Mod.Log.Error("Could not read published.json: " + ex.Message);
				}

				m_File ??= new PublishedFile();
				m_File.libraries ??= new Dictionary<string, PublishedEntry>();

				return m_File;
			}
		}

		public string ClientId
		{
			get
			{
				if (m_ClientId != null)
				{
					return m_ClientId;
				}

				try
				{
					if (System.IO.File.Exists(Paths.ClientId))
					{
						var text = System.IO.File.ReadAllText(Paths.ClientId).Trim();

						if (Guid.TryParse(text, out _))
						{
							return m_ClientId = text;
						}
					}

					m_ClientId = Guid.NewGuid().ToString();
					Paths.WriteAtomic(Paths.ClientId, m_ClientId);
				}
				catch (Exception ex)
				{
					Mod.Log.Warn("Could not store the install id: " + ex.Message);
					m_ClientId ??= Guid.NewGuid().ToString();
				}

				return m_ClientId;
			}
		}

		public bool TryGet(string libraryId, out PublishedEntry entry)
		{
			entry = null;

			return !string.IsNullOrEmpty(libraryId) && File.libraries.TryGetValue(libraryId, out entry) && entry != null;
		}

		public IEnumerable<string> LibraryIds => File.libraries.Keys;

		public void Set(string libraryId, PublishedEntry entry)
		{
			File.libraries[libraryId] = entry;
			Save();
		}

		public void Remove(string libraryId)
		{
			if (File.libraries.Remove(libraryId))
			{
				Save();
			}
		}

		private void Save()
		{
			try
			{
				Paths.WriteAtomic(Paths.Published, JsonConvert.SerializeObject(File, Formatting.Indented));
			}
			catch (Exception ex)
			{
				Mod.Log.Error("Could not save published.json: " + ex.Message);
			}
		}
	}
}
