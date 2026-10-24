using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using TMPro;
using WardShift;

namespace WardShift.Editor
{
    /// <summary>DAY 진행 시스템 필수 오브젝트를 현재 게임 씬에 중복 없이 연결합니다.</summary>
    public static class WardShiftDay5Setup
    {
        private const string MainGamePath = "Assets/Scenes/MainGame.unity";
        private const string SpawnPointName = "WardShift_Day5_StartPoint";
        private const string PickupName = "WardShift_PurificationPotion_Pickup";
        private const string KoreanFontAssetPath = "Assets/Studys/99. External Assets/Font/Noto_Sans_KR/static/NotoSansKR-Regular SDF.asset";

        [MenuItem("Tools/WARD SHIFT/Setup Day 5")]
        [MenuItem("Tools/WARD SHIFT/Setup DAY Progression")]
        public static void SetupActiveScene()
        {
            WardStatus status = Object.FindFirstObjectByType<WardStatus>();
            AnomalySpawner spawner = Object.FindFirstObjectByType<AnomalySpawner>();
            PlayerInteractor interactor = Object.FindFirstObjectByType<PlayerInteractor>();
            MagicCaster caster = Object.FindFirstObjectByType<MagicCaster>();
            if (status == null || spawner == null || status.Player == null || caster == null || interactor == null)
            {
                EditorUtility.DisplayDialog("WARD SHIFT DAY 5",
                    "현재 씬에 WardStatus, AnomalySpawner, PlayerContamination, PlayerInteractor, MagicCaster가 필요합니다.\n"
                    + "MainGame 씬을 열고 다시 실행하세요.", "확인");
                return;
            }

            GameObject systems = spawner.gameObject;
            PlayerInventory inventory = Object.FindFirstObjectByType<PlayerInventory>(FindObjectsInactive.Include);
            if (inventory == null) inventory = Undo.AddComponent<PlayerInventory>(systems);

            Transform playerRoot = interactor.PlayerRoot != null ? interactor.PlayerRoot : status.Player.transform;
            Transform startPoint = FindNamedTransform(SpawnPointName);
            if (startPoint == null)
            {
                GameObject startObject = new GameObject(SpawnPointName);
                Undo.RegisterCreatedObjectUndo(startObject, "Create DAY 5 start point");
                SceneManager.MoveGameObjectToScene(startObject, SceneManager.GetActiveScene());
                startObject.transform.SetPositionAndRotation(playerRoot.position, playerRoot.rotation);
                startPoint = startObject.transform;
            }

            DayFlowManager dayFlow = Object.FindFirstObjectByType<DayFlowManager>(FindObjectsInactive.Include);
            bool createdDayFlow = dayFlow == null;
            if (createdDayFlow) dayFlow = Undo.AddComponent<DayFlowManager>(systems);
            dayFlow.Configure(status, spawner, inventory, caster, playerRoot, startPoint,
                Object.FindFirstObjectByType<PlayerPickupInteractor>(FindObjectsInactive.Include));
            if (createdDayFlow) dayFlow.ConfigureDaySettings(180f, 1, 2);
            EditorUtility.SetDirty(dayFlow);

            Day5HUD hud = Object.FindFirstObjectByType<Day5HUD>(FindObjectsInactive.Include);
            if (hud == null) hud = Undo.AddComponent<Day5HUD>(dayFlow.gameObject);
            TMP_FontAsset koreanFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontAssetPath);
            if (koreanFont == null)
                Debug.LogError($"WARD SHIFT DAY 5 한국어 폰트를 찾을 수 없습니다: {KoreanFontAssetPath}");
            hud.Configure(dayFlow, inventory, koreanFont);
            EditorUtility.SetDirty(hud);

            PlayerPickupInteractor pickupInteractor = Object.FindFirstObjectByType<PlayerPickupInteractor>(FindObjectsInactive.Include);
            if (pickupInteractor == null) pickupInteractor = Undo.AddComponent<PlayerPickupInteractor>(interactor.gameObject);
            pickupInteractor.Configure(interactor, inventory, dayFlow);
            dayFlow.Configure(status, spawner, inventory, caster, playerRoot, startPoint, pickupInteractor);
            EditorUtility.SetDirty(pickupInteractor);
            EditorUtility.SetDirty(dayFlow);

            ConfigureSpawner(spawner);
            EnsureEventSystem();
            EnsurePotionPickup(playerRoot);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            if (EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))
                Debug.Log("WARD SHIFT DAY 5 setup completed and scene saved.", systems);
            else
                Debug.LogWarning("WARD SHIFT DAY 5 setup completed, but the active scene was not saved.", systems);
        }

        [MenuItem("Tools/WARD SHIFT/Validate Day 5 Scene")]
        [MenuItem("Tools/WARD SHIFT/Validate DAY Progression Scene")]
        public static void ValidateActiveScene()
        {
            var missing = new System.Collections.Generic.List<string>();
            if (Object.FindFirstObjectByType<WardStatus>() == null) missing.Add("WardStatus");
            if (Object.FindFirstObjectByType<AnomalySpawner>() == null) missing.Add("AnomalySpawner");
            if (Object.FindFirstObjectByType<PlayerInventory>() == null) missing.Add("PlayerInventory");
            if (Object.FindFirstObjectByType<DayFlowManager>() == null) missing.Add("DayFlowManager");
            Day5HUD hud = Object.FindFirstObjectByType<Day5HUD>();
            if (hud == null) missing.Add("Day5HUD");
            else if (!hud.HasKoreanFont) missing.Add("Day5HUD 한글 폰트");
            if (Object.FindFirstObjectByType<PlayerPickupInteractor>() == null) missing.Add("PlayerPickupInteractor");
            if (Object.FindFirstObjectByType<ItemPickup>() == null) missing.Add("정화약 월드 픽업");
            DayFlowManager dayFlow = Object.FindFirstObjectByType<DayFlowManager>();
            if (dayFlow != null && !dayFlow.IsConfigured) missing.Add("DayFlowManager 씬 참조");
            AnomalySpawner spawner = Object.FindFirstObjectByType<AnomalySpawner>();
            if (spawner != null && !spawner.HasSpawnConfiguration) missing.Add("AnomalySpawner 프리팹/생성 지점");
            if (spawner != null && spawner.MaximumConcurrentAnomalies > 1
                && !spawner.HasAtLeastTwoCompatibleSpawnPoints)
                missing.Add("동시 발생 수에 맞는 서로 다른 이상현상 생성 지점 2개 이상");
            if (spawner != null)
            {
                for (int dayNumber = 1; dayNumber <= 7; dayNumber++)
                {
                    AnomalySpawner.DaySpawnProfile profile = spawner.GetDayProfile(dayNumber);
                    if (profile == null)
                    {
                        missing.Add($"DAY {dayNumber} 이상현상 생성 프로필");
                        continue;
                    }
                    int compatible = spawner.CountCompatibleSpawnPointsForDay(dayNumber);
                    if (compatible < profile.MaximumConcurrentAnomalies)
                        Debug.LogWarning($"DAY {dayNumber}: 동시 상한은 {profile.MaximumConcurrentAnomalies}개지만 "
                            + $"호환 생성 지점은 {compatible}개입니다. 씬 위치를 추가하거나 상한을 조정하세요.", spawner);
                }
            }
            // - EventSystem.current는 런타임에 보장
            // - Edit Mode 검증에서는 씬 오브젝트를 직접 확인
            EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            InputSystemUIInputModule uiInputModule = eventSystem != null
                ? eventSystem.GetComponent<InputSystemUIInputModule>()
                : null;
            if (eventSystem == null || !eventSystem.isActiveAndEnabled || uiInputModule == null || !uiInputModule.enabled)
                missing.Add("InputSystemUIInputModule");

            if (missing.Count == 0)
                Debug.Log("WARD SHIFT DAY 5 scene references: OK.");
            else
                Debug.LogError("WARD SHIFT DAY 5 scene setup is incomplete: " + string.Join(", ", missing));
        }

        /// <summary>Unity batch mode에서 MainGame 씬을 자동 설정할 때 사용하는 진입점입니다.</summary>
        public static void SetupMainGameForBatch()
        {
            EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Single);
            SetupActiveScene();
        }

        [MenuItem("Tools/WARD SHIFT/Setup Multi Anomaly Spawning")]
        public static void SetupMultiAnomalySpawning()
        {
            AnomalySpawner spawner = Object.FindFirstObjectByType<AnomalySpawner>(FindObjectsInactive.Include);
            if (spawner == null)
            {
                EditorUtility.DisplayDialog("WARD SHIFT 이상현상 설정",
                    "현재 씬에서 AnomalySpawner를 찾을 수 없습니다.", "확인");
                return;
            }

            ConfigureSpawner(spawner);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            if (EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))
                Debug.Log("WARD SHIFT multi-anomaly spawning setup completed.", spawner);
        }

        /// <summary>Unity batch mode에서 MainGame의 반복 스폰 설정만 자동 연결합니다.</summary>
        public static void SetupMainGameAnomaliesForBatch()
        {
            EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Single);
            SetupMultiAnomalySpawning();
            EditorApplication.Exit(0);
        }

        private static void ConfigureSpawner(AnomalySpawner spawner)
        {
            SerializedObject serialized = new SerializedObject(spawner);
            SerializedProperty spawnOnStart = serialized.FindProperty("spawnOnStart");
            if (spawnOnStart != null) spawnOnStart.boolValue = false;
            SerializedProperty spawnInterval = serialized.FindProperty("spawnIntervalSeconds");
            if (spawnInterval != null) spawnInterval.floatValue = 6f;
            SerializedProperty maximumConcurrent = serialized.FindProperty("maximumConcurrentAnomalies");
            if (maximumConcurrent != null) maximumConcurrent.intValue = 2;
            SerializedProperty firstDelay = serialized.FindProperty("firstSpawnDelaySeconds");
            if (firstDelay != null) firstDelay.floatValue = 1f;

            SerializedProperty dayProfiles = serialized.FindProperty("daySpawnProfiles");
            if (dayProfiles != null && dayProfiles.arraySize == 0)
            {
                int[] limits = { 1, 2, 2, 2, 2, 3, 3 };
                float[] intervals = { 8f, 7f, 6f, 5.5f, 5f, 4.5f, 4f };
                dayProfiles.arraySize = 7;
                for (int i = 0; i < dayProfiles.arraySize; i++)
                {
                    SerializedProperty profile = dayProfiles.GetArrayElementAtIndex(i);
                    profile.FindPropertyRelative("day").intValue = i + 1;
                    profile.FindPropertyRelative("allowManaRift").boolValue = true;
                    profile.FindPropertyRelative("allowCurseContamination").boolValue = i >= 1;
                    profile.FindPropertyRelative("allowManaDeviceOverload").boolValue = i >= 2;
                    profile.FindPropertyRelative("allowOtherAnomalies").boolValue = false;
                    profile.FindPropertyRelative("maximumConcurrentAnomalies").intValue = limits[i];
                    profile.FindPropertyRelative("spawnIntervalSeconds").floatValue = intervals[i];
                }
            }

            AnomalySpawnPoint[] scenePoints = Object.FindObjectsByType<AnomalySpawnPoint>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(point => point.name).ToArray();
            SerializedProperty spawnPoints = serialized.FindProperty("spawnPoints");
            if (spawnPoints != null)
            {
                spawnPoints.arraySize = scenePoints.Length;
                for (int i = 0; i < scenePoints.Length; i++)
                    spawnPoints.GetArrayElementAtIndex(i).objectReferenceValue = scenePoints[i];
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(spawner);
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (eventSystem == null)
            {
                GameObject eventObject = new GameObject("EventSystem", typeof(EventSystem));
                Undo.RegisterCreatedObjectUndo(eventObject, "Create UI Event System");
                SceneManager.MoveGameObjectToScene(eventObject, SceneManager.GetActiveScene());
                eventSystem = eventObject.GetComponent<EventSystem>();
            }
            InputSystemUIInputModule module = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (module == null) module = Undo.AddComponent<InputSystemUIInputModule>(eventSystem.gameObject);
            foreach (BaseInputModule otherModule in eventSystem.GetComponents<BaseInputModule>())
                if (otherModule != module && otherModule.enabled) otherModule.enabled = false;
            module.AssignDefaultActions();
            EditorUtility.SetDirty(eventSystem);
            EditorUtility.SetDirty(module);
        }

        private static void EnsurePotionPickup(Transform playerRoot)
        {
            Transform existing = FindNamedTransform(PickupName);
            if (existing != null) return;
            GameObject pickup = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Undo.RegisterCreatedObjectUndo(pickup, "Create purification potion pickup");
            SceneManager.MoveGameObjectToScene(pickup, SceneManager.GetActiveScene());
            pickup.name = PickupName;
            pickup.transform.SetPositionAndRotation(
                playerRoot.position + playerRoot.forward * 1.5f + playerRoot.right * 0.8f + Vector3.up * 0.25f,
                Quaternion.identity);
            pickup.transform.localScale = Vector3.one * 0.55f;
            Undo.AddComponent<ItemPickup>(pickup);
        }

        private static Transform FindNamedTransform(string objectName)
        {
            return SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(transform => transform.name == objectName);
        }
    }
}
