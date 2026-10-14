using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using ShelveIt.Domain;
using ShelveIt.Logic;

using Colossal.IO.AssetDatabase;

using Game;
using Game.Prefabs;
using Game.UI;

using Unity.Collections;
using Unity.Entities;

namespace ShelveIt.Systems
{
	// Disabled unless a scan was requested. One frame announces the scan, the next one takes the
	// snapshot, then the system only polls the background task until it is done.
	public class AssetScanSystem : GameSystemBase
	{
		public const string kIdle = "idle";
		public const string kScanning = "scanning";
		public const string kClassifying = "classifying";
		public const string kReady = "ready";

		private PrefabSystem m_PrefabSystem;
		private ImageSystem m_ImageSystem;
		private EntityQuery m_PrefabQuery;
		private Task<ClassificationResult> m_Task;
		private bool m_Requested;
		private bool m_Announced;
		private bool m_IgnoreCache;

		public string State { get; private set; } = kIdle;
		public bool HasScanned { get; private set; }
		// Prefabs were added or removed since the last scan; the next panel open rescans.
		public bool Stale { get; private set; }
		public List<AssetRecord> Records { get; private set; } = new List<AssetRecord>();
		public Dictionary<string, AssetRecord> ByKey { get; private set; } = new Dictionary<string, AssetRecord>();
		public Dictionary<int, AssetRecord> ByIndex { get; private set; } = new Dictionary<int, AssetRecord>();
		public ClassificationResult Classification { get; private set; } = new ClassificationResult();

		public Action StateChanged;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
			m_ImageSystem = World.GetOrCreateSystemManaged<ImageSystem>();
			m_PrefabQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

			Enabled = false;
		}

		public void MarkStale()
		{
			if (HasScanned)
			{
				Stale = true;
			}
		}

		public void RequestScan(bool ignoreCache)
		{
			if (m_Requested || m_Task != null)
			{
				return;
			}

			m_Requested = true;
			Stale = false;
			m_Announced = false;
			m_IgnoreCache = ignoreCache;
			Enabled = true;
		}

		protected override void OnUpdate()
		{
			if (m_Requested)
			{
				if (!m_Announced)
				{
					m_Announced = true;
					SetState(kScanning);
					return;
				}

				m_Requested = false;

				List<AssetRecord> records;

				try
				{
					records = Scan();
				}
				catch (Exception ex)
				{
					Mod.Log.Error("Asset scan failed: " + ex);
					Finish(new List<AssetRecord>(), new ClassificationResult());
					return;
				}

				var ignoreCache = m_IgnoreCache;

				// The records are only published once the background work is done with them.
				m_Task = Task.Run(() => ThemeClassifier.Run(records, ignoreCache));
				m_Pending = records;

				SetState(kClassifying);
				return;
			}

			if (m_Task == null)
			{
				Enabled = false;
				return;
			}

			if (!m_Task.IsCompleted)
			{
				return;
			}

			var task = m_Task;
			var pending = m_Pending;

			m_Task = null;
			m_Pending = null;

			if (task.IsFaulted || task.IsCanceled)
			{
				Mod.Log.Error("Classification failed: " + task.Exception?.GetBaseException());
				Finish(pending, new ClassificationResult { index = FacetIndex.Build(pending) });
			}
			else
			{
				var result = task.Result;

				Mod.Log.Info($"Classified {pending.Count} assets into {result.nodes.Count} suggestion nodes: enrich {result.enrichMs} ms, tree {result.treeMs} ms{(result.fromCache ? " (cache hit)" : "")}");
				Finish(pending, result);
			}
		}

		private List<AssetRecord> m_Pending;

		private void Finish(List<AssetRecord> records, ClassificationResult result)
		{
			var byKey = new Dictionary<string, AssetRecord>(records.Count);
			var byIndex = new Dictionary<int, AssetRecord>(records.Count);

			foreach (var record in records)
			{
				byKey[record.key] = record;
				byIndex[record.entityIndex] = record;
			}

			Records = records;
			ByKey = byKey;
			ByIndex = byIndex;
			Classification = result;
			HasScanned = true;
			Enabled = false;
			SetState(kReady);
		}

		private void SetState(string state)
		{
			State = state;

			try
			{
				StateChanged?.Invoke();
			}
			catch (Exception ex)
			{
				Mod.Log.Error("Scan state listener failed: " + ex);
			}
		}

		private List<AssetRecord> Scan()
		{
			var watch = Stopwatch.StartNew();
			var records = new List<AssetRecord>(32768);
			var seen = new HashSet<string>();
			var pdxNames = new Dictionary<int, HashSet<string>>();
			var paradoxMods = AssetDatabase<ParadoxMods>.instance;
			Thumbnails.RegisterHost();
			Thumbnails.RefreshIndex();

			var packs = new List<string>();
			var candidates = 0;

			using (var entities = m_PrefabQuery.ToEntityArray(Allocator.Temp))
			{
				for (var i = 0; i < entities.Length; i++)
				{
					var entity = entities[i];

					if (!m_PrefabSystem.TryGetPrefab<PrefabBase>(entity, out var prefab) || prefab == null || prefab.name == null)
					{
						continue;
					}

					if (!(prefab is ObjectPrefab) && !(prefab is NetGeometryPrefab) && !(prefab is SurfacePrefab))
					{
						continue;
					}

					candidates++;

					var pdxId = 0;
					var asset = prefab.asset;

					if (asset != null && asset.database == paradoxMods && int.TryParse(asset.GetMeta().platformID, out pdxId) && pdxId != 0)
					{
						// Kept for every candidate: Find It's "search by mod id" covers more than we list.
						if (!pdxNames.TryGetValue(pdxId, out var names))
						{
							pdxNames[pdxId] = names = new HashSet<string>(StringComparer.Ordinal);
						}

						names.Add(prefab.name);
					}

					var kind = GetKind(prefab, entity);

					if (kind == null)
					{
						continue;
					}

					var record = new AssetRecord
					{
						key = KeyOf(prefab),
						entityIndex = entity.Index,
						entity = entity,
						name = prefab.name,
						kind = kind,
						zone = "None",
						theme = "",
						pdxId = pdxId,
						isVanilla = prefab.isBuiltin,
						placeable = EntityManager.HasComponent<PlaceableObjectData>(entity),
					};

					if (!seen.Add(record.key))
					{
						continue;
					}

					PrefabBase metaSource = prefab;
					var zoneEntity = Entity.Null;

					if (EntityManager.HasComponent<SpawnableBuildingData>(entity))
					{
						var spawnable = EntityManager.GetComponentData<SpawnableBuildingData>(entity);

						record.level = spawnable.m_Level;

						if (spawnable.m_ZonePrefab != Entity.Null && m_PrefabSystem.TryGetPrefab<ZonePrefab>(spawnable.m_ZonePrefab, out var zonePrefab) && zonePrefab != null)
						{
							record.zone = zonePrefab.m_Office ? "Office" : zonePrefab.m_AreaType.ToString();
							record.zoneName = zonePrefab.name ?? "";
							metaSource = zonePrefab;
							zoneEntity = spawnable.m_ZonePrefab;
						}

						if (prefab is BuildingPrefab building)
						{
							record.corner = CornerOf(building.m_AccessType);
						}
					}

					if (EntityManager.HasComponent<BuildingData>(entity))
					{
						var lot = EntityManager.GetComponentData<BuildingData>(entity).m_LotSize;

						record.lotW = lot.x;
						record.lotD = lot.y;
					}

					// Zoned buildings inherit theme and packs from their zone prefab.
					if ((metaSource.TryGet<ThemeObject>(out var themeObject) || prefab.TryGet(out themeObject)) && themeObject.m_Theme != null)
					{
						record.theme = themeObject.m_Theme.name;
					}

					packs.Clear();

					if ((metaSource.TryGet<AssetPackItem>(out var packItem) || prefab.TryGet(out packItem)) && packItem.m_Packs != null)
					{
						foreach (var pack in packItem.m_Packs)
						{
							if (pack != null)
							{
								packs.Add(pack.name);
							}
						}
					}

					record.packs = packs.ToArray();

					record.thumbnail = Thumbnails.Resolve(prefab, zoneEntity, m_ImageSystem, out record.thumbKind);
					record.hasThumbnail = record.thumbKind == Thumbnails.kReal;

					records.Add(record);
				}
			}

			FindItBridge.SetPackageNames(pdxNames);

			Mod.Log.Info($"Scanned {records.Count} placeable assets out of {candidates} candidate prefabs in {watch.ElapsedMilliseconds} ms");

			return records;
		}

		public static string KeyOf(PrefabBase prefab)
		{
			return prefab.GetType().Name + ":" + prefab.name;
		}

		private static string CornerOf(BuildingAccessType access)
		{
			switch (access)
			{
				case BuildingAccessType.LeftCorner:
				case BuildingAccessType.LeftAndBackCorner:
					return "Left";

				case BuildingAccessType.RightCorner:
				case BuildingAccessType.RightAndBackCorner:
					return "Right";

				case BuildingAccessType.LeftAndRightCorner:
				case BuildingAccessType.All:
					return "Corner";

				default:
					return "";
			}
		}

		// Returns null for prefabs a player cannot place. The rules follow what Find It indexes
		// (its category processors), minus random wrappers, placeholders and sub-objects.
		private string GetKind(PrefabBase prefab, Entity entity)
		{
			if (NameParser.IsNoiseName(prefab.name))
			{
				return null;
			}

			if (prefab.TryGet<EditorAssetCategoryOverride>(out var categories) && categories.m_ExcludeCategories != null)
			{
				foreach (var category in categories.m_ExcludeCategories)
				{
					if (category != null && category.StartsWith("FindIt", StringComparison.Ordinal))
					{
						return null;
					}
				}
			}

			var manager = EntityManager;

			if (prefab is BuildingPrefab)
			{
				if (!manager.HasComponent<BuildingData>(entity) || manager.HasComponent<TrafficSpawnerData>(entity))
				{
					return null;
				}

				var unique = manager.HasComponent<SignatureBuildingData>(entity) || prefab.Has<UniqueObject>();

				if (manager.HasComponent<SpawnableBuildingData>(entity))
				{
					// A growable without a zone never shows up anywhere.
					if (manager.GetComponentData<SpawnableBuildingData>(entity).m_ZonePrefab == Entity.Null)
					{
						return null;
					}

					return unique ? "UniqueBuilding" : "Building";
				}

				if (manager.HasComponent<PlaceholderBuildingData>(entity))
				{
					return null;
				}

				if (manager.HasComponent<ServiceObjectData>(entity) || manager.HasComponent<ServiceUpgradeBuilding>(entity))
				{
					return unique ? "UniqueBuilding" : "ServiceBuilding";
				}

				if (manager.HasComponent<BuildingPropertyData>(entity))
				{
					return null;
				}

				return unique ? "UniqueBuilding" : "Building";
			}

			if (prefab is StaticObjectPrefab staticObject)
			{
				if (!manager.HasComponent<StaticObjectData>(entity)
					|| manager.HasComponent<QuantityObjectData>(entity)
					|| manager.HasComponent<PlaceholderObjectData>(entity))
				{
					return null;
				}

				var decal = IsDecal(staticObject, entity);

				if (manager.HasComponent<BuildingExtensionData>(entity))
				{
					if (manager.HasComponent<ServiceUpgradeBuilding>(entity))
					{
						return "ServiceBuilding";
					}

					if (manager.HasComponent<PlaceableObjectData>(entity) && !decal)
					{
						return "Prop";
					}

					return manager.HasComponent<SpawnableObjectData>(entity) && prefab.name.Contains("Storefront") ? "Prop" : null;
				}

				if (manager.HasComponent<PillarData>(entity) && !manager.HasComponent<SpawnableObjectData>(entity))
				{
					return null;
				}

				if (manager.HasComponent<PlantData>(entity))
				{
					return "Vegetation";
				}

				if (decal)
				{
					return manager.HasComponent<BrandObjectData>(entity) || manager.HasComponent<PlaceholderObjectElement>(entity) ? null : "Decal";
				}

				return "Prop";
			}

			if (prefab is NetGeometryPrefab)
			{
				if (prefab is RoadPrefab || prefab is TrackPrefab || prefab is PathwayPrefab || manager.HasComponent<BridgeData>(entity) || prefab.Has<UIObject>())
				{
					return "Network";
				}

				return null;
			}

			if (prefab is SurfacePrefab)
			{
				return manager.HasComponent<PlaceholderObjectElement>(entity) ? null : "Surface";
			}

			if (prefab is AssetStampPrefab)
			{
				return manager.HasComponent<SubNet>(entity) ? "Intersection" : null;
			}

			return null;
		}

		private bool IsDecal(StaticObjectPrefab prefab, Entity entity)
		{
			if (EntityManager.HasBuffer<SubMesh>(entity))
			{
				var meshes = EntityManager.GetBuffer<SubMesh>(entity, true);

				if (meshes.Length > 0 && EntityManager.HasComponent<MeshData>(meshes[0].m_SubMesh))
				{
					return (EntityManager.GetComponentData<MeshData>(meshes[0].m_SubMesh).m_State & MeshFlags.Decal) != 0;
				}
			}

			var managed = prefab.m_Meshes;

			return managed != null && managed.Length > 0 && managed[0]?.m_Mesh != null && managed[0].m_Mesh.Has<DecalProperties>();
		}
	}
}
