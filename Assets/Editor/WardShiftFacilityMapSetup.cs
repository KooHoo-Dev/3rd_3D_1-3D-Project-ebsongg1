using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WardShift.Editor
{
    /// <summary>MainGame의 방 앵커와 시설 지도의 좌표 기준을 연결합니다.</summary>
    public static class WardShiftFacilityMapSetup
    {
        private const string MainGamePath = "Assets/Scenes/MainGame.unity";
        private const string MapRootName = "Map_Root";
        private const string KoreanFontAssetPath =
            "Assets/Studys/99. External Assets/Font/Noto_Sans_KR/static/NotoSansKR-Regular SDF.asset";

        private sealed class ZoneDefinition
        {
            public readonly FacilityZoneId Id;
            public readonly string AnchorName;
            public readonly string Label;

            public ZoneDefinition(FacilityZoneId id, string anchorName, string label)
            {
                Id = id;
                AnchorName = anchorName;
                Label = label;
            }
        }

        private static readonly ZoneDefinition[] Definitions =
        {
            new ZoneDefinition(FacilityZoneId.StartRoom, "Room_00_Start", "시작 구역"),
            new ZoneDefinition(FacilityZoneId.ControlHall, "Room_01_ControlHall", "중앙 복도"),
            new ZoneDefinition(FacilityZoneId.PurificationRoom, "Room_02_Purification", "정화실"),
            new ZoneDefinition(FacilityZoneId.SealingRoom, "Room_03_Sealing", "봉인실"),
            new ZoneDefinition(FacilityZoneId.ManaCoreRoom, "Room_04_ManaCore", "마력핵실")
        };

        [MenuItem("Tools/WARD SHIFT/Setup Facility Map")]
        public static void SetupActiveScene()
        {
            if (ConfigureActiveScene())
                EditorUtility.DisplayDialog("WARD SHIFT 시설 지도", "구역 식별과 시설 지도 참조를 연결했습니다.", "확인");
            else
                EditorUtility.DisplayDialog("WARD SHIFT 시설 지도", "설정을 완료하지 못했습니다. Console 오류를 확인하세요.", "확인");
        }

        [MenuItem("Tools/WARD SHIFT/Validate Facility Map")]
        public static void ValidateActiveScene()
        {
            List<string> missing = ValidateScene();
            if (missing.Count == 0) Debug.Log("WARD SHIFT facility map references: OK.");
            else Debug.LogError("WARD SHIFT facility map setup is incomplete: " + string.Join(", ", missing));
        }

        /// <summary>Unity batch mode용: MainGame을 열고 설정 후 오류 코드와 함께 종료합니다.</summary>
        public static void SetupMainGameForBatch()
        {
            EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Single);
            bool success = ConfigureActiveScene();
            EditorApplication.Exit(success ? 0 : 1);
        }

        private static bool ConfigureActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("WARD SHIFT facility map: 유효한 씬이 열려 있지 않습니다.");
                return false;
            }

            Transform mapRoot = FindNamedTransform(scene, MapRootName);
            if (mapRoot == null)
            {
                Debug.LogError($"WARD SHIFT facility map: 씬에서 {MapRootName}를 찾을 수 없습니다.");
                return false;
            }

            var zonesByAnchor = new Dictionary<string, ZoneIdentity>();
            var zones = new List<ZoneIdentity>();
            foreach (ZoneDefinition definition in Definitions)
            {
                Transform anchor = FindDirectChild(mapRoot, definition.AnchorName);
                if (anchor == null)
                {
                    Debug.LogError($"WARD SHIFT facility map: {MapRootName}/{definition.AnchorName}를 찾을 수 없습니다.");
                    return false;
                }

                ZoneIdentity identity = anchor.GetComponent<ZoneIdentity>();
                if (identity == null) identity = Undo.AddComponent<ZoneIdentity>(anchor.gameObject);
                identity.Configure(definition.Id, definition.Label);
                EditorUtility.SetDirty(identity);
                PrefabUtility.RecordPrefabInstancePropertyModifications(identity);
                zonesByAnchor[definition.AnchorName] = identity;
                zones.Add(identity);
            }

            DayFlowManager dayFlow = FindFirst<DayFlowManager>();
            if (dayFlow == null || dayFlow.Player == null)
            {
                Debug.LogError("WARD SHIFT facility map: DayFlowManager와 PlayerContamination이 필요합니다.");
                return false;
            }

            FacilityZoneTracker tracker = FindFirst<FacilityZoneTracker>();
            if (tracker == null)
                tracker = Undo.AddComponent<FacilityZoneTracker>(dayFlow.Player.gameObject);
            else if (tracker.transform != dayFlow.Player.transform)
                Debug.LogWarning("WARD SHIFT facility map: FacilityZoneTracker가 플레이어 오브젝트가 아닌 곳에 있습니다.", tracker);
            tracker.Configure(zones.ToArray(), dayFlow);
            dayFlow.ConfigureZoneTracker(tracker);
            EditorUtility.SetDirty(tracker);
            EditorUtility.SetDirty(dayFlow);

            int linkedSpawnPoints = 0;
            foreach (AnomalySpawnPoint point in FindAll<AnomalySpawnPoint>())
            {
                string roomAnchor = FindRoomAnchorName(point.transform);
                if (roomAnchor == null || !zonesByAnchor.TryGetValue(roomAnchor, out ZoneIdentity identity)) continue;
                point.ConfigureZoneIdentity(identity);
                EditorUtility.SetDirty(point);
                PrefabUtility.RecordPrefabInstancePropertyModifications(point);
                linkedSpawnPoints++;
            }

            if (linkedSpawnPoints < 2 || FindAll<AnomalySpawnPoint>()
                    .Where(point => point.Zone != null)
                    .Select(point => point.Zone.Id)
                    .Distinct().Count() < 2)
            {
                Debug.LogError("WARD SHIFT facility map: 서로 다른 두 구역 이상의 AnomalySpawnPoint 연결이 필요합니다.");
                return false;
            }

            WardStatusHUD statusHud = FindFirst<WardStatusHUD>();
            if (statusHud == null)
            {
                Debug.LogError("WARD SHIFT facility map: WardStatusHUD를 찾지 못했습니다.");
                return false;
            }
            else
            {
                statusHud.ConfigureZoneTracker(tracker);
                EditorUtility.SetDirty(statusHud);
            }

            FacilityMapHUD mapHud = dayFlow.GetComponent<FacilityMapHUD>();
            if (mapHud == null) mapHud = Undo.AddComponent<FacilityMapHUD>(dayFlow.gameObject);
            TMP_FontAsset koreanFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontAssetPath);
            if (koreanFont == null)
                Debug.LogWarning($"WARD SHIFT facility map: 지정 한글 폰트를 못 찾아 TMP 기본 폰트를 사용합니다: {KoreanFontAssetPath}");
            mapHud.Configure(dayFlow, tracker, zones.ToArray(), koreanFont);
            mapHud.ConfigureMapCoordinates(mapRoot,
                zonesByAnchor["Room_01_ControlHall"].transform);
            EditorUtility.SetDirty(mapHud);

            AnomalyScreenPingHUD pingHud = dayFlow.GetComponent<AnomalyScreenPingHUD>();
            if (pingHud == null) pingHud = Undo.AddComponent<AnomalyScreenPingHUD>(dayFlow.gameObject);
            pingHud.Configure(dayFlow, zones.ToArray(), koreanFont);
            EditorUtility.SetDirty(pingHud);

            List<string> missing = ValidateScene();
            if (missing.Count > 0)
            {
                Debug.LogError("WARD SHIFT facility map setup validation failed: " + string.Join(", ", missing));
                return false;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError("WARD SHIFT facility map: 씬 저장에 실패했습니다.");
                return false;
            }
            Debug.Log($"WARD SHIFT facility map setup completed. Zones: {zones.Count}, spawn points linked: {linkedSpawnPoints}.");
            return true;
        }

        private static List<string> ValidateScene()
        {
            var missing = new List<string>();
            Transform mapRoot = FindNamedTransform(SceneManager.GetActiveScene(), MapRootName);
            if (mapRoot == null)
            {
                missing.Add(MapRootName);
                return missing;
            }

            var zoneByAnchor = new Dictionary<string, ZoneIdentity>();
            foreach (ZoneDefinition definition in Definitions)
            {
                Transform anchor = FindDirectChild(mapRoot, definition.AnchorName);
                ZoneIdentity identity = anchor != null ? anchor.GetComponent<ZoneIdentity>() : null;
                if (identity == null || identity.Id != definition.Id)
                    missing.Add(definition.Label + " 구역 식별 정보");
                else zoneByAnchor[definition.AnchorName] = identity;
            }

            DayFlowManager dayFlow = FindFirst<DayFlowManager>();
            if (dayFlow == null) missing.Add("DayFlowManager");
            else
            {
                if (dayFlow.ZoneTracker == null) missing.Add("DayFlowManager.ZoneTracker");
                FacilityMapHUD mapHud = dayFlow.GetComponent<FacilityMapHUD>();
                if (mapHud == null || !mapHud.IsConfigured) missing.Add("FacilityMapHUD 참조");
                AnomalyScreenPingHUD pingHud = dayFlow.GetComponent<AnomalyScreenPingHUD>();
                if (pingHud == null || !pingHud.IsConfigured) missing.Add("AnomalyScreenPingHUD 참조");
            }

            FacilityZoneTracker tracker = FindFirst<FacilityZoneTracker>();
            if (tracker == null) missing.Add("FacilityZoneTracker");
            WardStatusHUD hud = FindFirst<WardStatusHUD>();
            if (hud == null || hud.ZoneTracker != tracker) missing.Add("WardStatusHUD.ZoneTracker");
            int linkedPointCount = FindAll<AnomalySpawnPoint>()
                .Count(point => point.Zone != null && zoneByAnchor.Values.Contains(point.Zone));
            int linkedZoneCount = FindAll<AnomalySpawnPoint>()
                .Where(point => point.Zone != null && zoneByAnchor.Values.Contains(point.Zone))
                .Select(point => point.Zone.Id).Distinct().Count();
            if (linkedPointCount < 2 || linkedZoneCount < 2)
                missing.Add("서로 다른 두 구역 이상의 AnomalySpawnPoint");
            return missing;
        }

        private static string FindRoomAnchorName(Transform child)
        {
            for (Transform current = child; current != null; current = current.parent)
                if (Definitions.Any(definition => definition.AnchorName == current.name)) return current.name;
            return null;
        }

        private static Transform FindDirectChild(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == childName) return parent.GetChild(i);
            return null;
        }

        private static Transform FindNamedTransform(Scene scene, string objectName)
        {
            if (!scene.IsValid()) return null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == objectName) return child;
            return null;
        }

        private static T FindFirst<T>() where T : Object
        {
            return Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        }

        private static T[] FindAll<T>() where T : Object
        {
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }
    }
}
