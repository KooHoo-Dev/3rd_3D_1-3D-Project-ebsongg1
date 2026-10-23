using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WardShift
{
    /// <summary>테스트 장치 외형·이상현상 프리팹과 MainGame의 Device 지점을 구성합니다.</summary>
    public static class WardShiftManaDeviceSetup
    {
        private const string MainGamePath = "Assets/Scenes/MainGame.unity";
        private const string DeviceFolder = "Assets/WardShiftPrototype/Devices";
        private const string MaterialFolder = DeviceFolder + "/Materials";
        private const string DevicePrefabPath = DeviceFolder + "/ManaDevice_Test.prefab";
        private const string AnomalyPrefabPath = DeviceFolder + "/ManaDevice_Overload.prefab";
        private const string SpawnPointPrefabPath = DeviceFolder + "/ManaDevice_SpawnPoint.prefab";
        private const string MetalMaterialPath = MaterialFolder + "/ManaDevice_Metal.mat";
        private const string CrystalMaterialPath = MaterialFolder + "/ManaDevice_Crystal.mat";
        private const string AlarmMaterialPath = MaterialFolder + "/ManaDevice_Alarm.mat";
        private const string RingMaterialPath = MaterialFolder + "/ManaDevice_WarningRing.mat";

        [MenuItem("Tools/WARD SHIFT/Setup Test Mana Device")]
        public static void Run()
        {
            EnsureFolder("Assets/WardShiftPrototype");
            EnsureFolder(DeviceFolder);
            EnsureFolder(MaterialFolder);

            Material metal = LoadOrCreateMaterial(MetalMaterialPath,
                new Color(0.12f, 0.18f, 0.24f, 1f), Color.black, 0f);
            Material crystal = LoadOrCreateMaterial(CrystalMaterialPath,
                new Color(0.15f, 0.78f, 1f, 1f), new Color(0.15f, 0.78f, 1f, 1f), 1.7f);
            Material alarm = LoadOrCreateMaterial(AlarmMaterialPath,
                new Color(1f, 0.08f, 0.025f, 1f), new Color(1f, 0.08f, 0.025f, 1f), 4f);
            Material ring = LoadOrCreateMaterial(RingMaterialPath,
                new Color(1f, 0.2f, 0.04f, 1f), Color.black, 0f, true);

            GameObject devicePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DevicePrefabPath);
            if (devicePrefab == null) devicePrefab = CreateDevicePrefab(metal, crystal, alarm, ring);
            if (devicePrefab != null) EnsureDevicePrefabNormalEditorState();
            GameObject anomalyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnomalyPrefabPath);
            if (anomalyPrefab == null) anomalyPrefab = CreateAnomalyPrefab();
            GameObject spawnPointPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpawnPointPrefabPath);
            if (spawnPointPrefab == null) spawnPointPrefab = CreateSpawnPointPrefab(devicePrefab);
            if (devicePrefab == null || anomalyPrefab == null || spawnPointPrefab == null)
            {
                Debug.LogError("WARD SHIFT 마력 장치 프리팹 생성에 실패했습니다.");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Single);
            ZoneIdentity coreZone = UnityEngine.Object.FindObjectsByType<ZoneIdentity>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .FirstOrDefault(zone => zone != null && zone.gameObject.scene == scene
                    && zone.Id == FacilityZoneId.ManaCoreRoom);
            if (coreZone == null)
            {
                Debug.LogError("WARD SHIFT 마력 장치 설정: MainGame에서 마력핵실 ZoneIdentity를 찾지 못했습니다.");
                return;
            }

            GameObject pointObject = FindSceneObject("SP_CORE_Device_01", scene);
            if (pointObject == null)
            {
                Debug.LogError("WARD SHIFT 마력 장치 설정: 기존 SP_CORE_Device_01 지점을 찾지 못했습니다.");
                return;
            }

            Undo.RecordObject(pointObject.transform, "Place test mana device spawn point");
            Undo.RecordObject(pointObject, "Enable test mana device spawn point");
            Vector3 oldPosition = pointObject.transform.position;
            // 마력핵실의 볼륨 중심에서 동쪽·북쪽으로 옮겨 Floor/Wall 지점과 실제 간격을 둡니다.
            Vector3 devicePosition = coreZone.transform.position + new Vector3(2f, 0f, 1.5f);
            pointObject.transform.SetPositionAndRotation(devicePosition, Quaternion.identity);
            pointObject.SetActive(true);

            AnomalySpawnPoint spawnPoint = pointObject.GetComponent<AnomalySpawnPoint>();
            if (spawnPoint == null) spawnPoint = Undo.AddComponent<AnomalySpawnPoint>(pointObject);
            SerializedObject spawnPointData = new SerializedObject(spawnPoint);
            spawnPointData.FindProperty("surface").enumValueIndex = (int)AnomalySpawnPoint.SurfaceType.Device;
            spawnPointData.FindProperty("zoneIdentity").objectReferenceValue = coreZone;
            spawnPointData.FindProperty("locationName").stringValue = coreZone.DisplayName;
            spawnPointData.FindProperty("surfaceOffset").floatValue = 0f;

            ManaDeviceVisuals visuals = pointObject.GetComponentsInChildren<ManaDeviceVisuals>(true)
                .FirstOrDefault(component => component != null);
            if (visuals == null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(devicePrefab, scene);
                instance.name = devicePrefab.name;
                instance.transform.SetParent(pointObject.transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                visuals = instance.GetComponent<ManaDeviceVisuals>();
                Undo.RegisterCreatedObjectUndo(instance, "Add test mana device visual");
            }
            spawnPointData.FindProperty("deviceVisuals").objectReferenceValue = visuals;
            spawnPointData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(spawnPoint);

            AnomalySpawner spawner = UnityEngine.Object.FindFirstObjectByType<AnomalySpawner>(FindObjectsInactive.Include);
            if (spawner == null)
            {
                Debug.LogError("WARD SHIFT 마력 장치 설정: MainGame에서 AnomalySpawner를 찾지 못했습니다.");
                return;
            }
            SerializedObject spawnerData = new SerializedObject(spawner);
            AddReference(spawnerData.FindProperty("additionalAnomalyPrefabs"),
                anomalyPrefab.GetComponent<ManaDeviceAnomaly>());
            AddReference(spawnerData.FindProperty("spawnPoints"), spawnPoint);
            spawnerData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(spawner);

            float nearestDistance = float.PositiveInfinity;
            AnomalySpawnPoint nearestPoint = null;
            foreach (AnomalySpawnPoint candidate in UnityEngine.Object.FindObjectsByType<AnomalySpawnPoint>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (candidate == null || candidate == spawnPoint || candidate.gameObject.scene != scene) continue;
                float distance = Vector3.Distance(candidate.transform.position, devicePosition);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearestPoint = candidate;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError("WARD SHIFT 마력 장치 설정: MainGame 씬을 저장하지 못했습니다.");
                return;
            }

            string nearestText = nearestPoint == null ? "다른 활성 생성 지점 없음"
                : $"가장 가까운 기존 생성 지점 {nearestPoint.name}: {nearestDistance:F1}m";
            Debug.Log($"WARD SHIFT 마력 장치 설정 완료. 기존 위치 {oldPosition:F2} → "
                + $"마력핵실 {devicePosition:F2}; {nearestText}. "
                + "영구 외형·Device SpawnPoint·폭주 프리팹을 연결했습니다.", spawnPoint);
        }

        private static GameObject CreateDevicePrefab(Material metal, Material crystal, Material alarm, Material ring)
        {
            GameObject root = new GameObject("ManaDevice_Test");
            try
            {
                Transform visualRoot = new GameObject("VisualRoot_Replaceable").transform;
                visualRoot.SetParent(root.transform, false);

                CreatePrimitive(PrimitiveType.Cylinder, "Pedestal_Base", visualRoot,
                    new Vector3(0f, 0.22f, 0f), new Vector3(1.25f, 0.22f, 1.25f), metal, false);
                CreatePrimitive(PrimitiveType.Cylinder, "Pedestal_Column", visualRoot,
                    new Vector3(0f, 0.56f, 0f), new Vector3(0.62f, 0.28f, 0.62f), metal, false);
                Renderer accentRenderer = CreatePrimitive(PrimitiveType.Cylinder, "Crystal_Ring",
                    visualRoot, new Vector3(0f, 0.87f, 0f), new Vector3(0.78f, 0.07f, 0.78f), crystal, false);
                GameObject crystalObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crystalObject.name = "ManaCrystal_Target";
                crystalObject.transform.SetParent(visualRoot, false);
                crystalObject.transform.localPosition = new Vector3(0f, 1.48f, 0f);
                crystalObject.transform.localRotation = Quaternion.Euler(0f, 45f, 45f);
                crystalObject.transform.localScale = new Vector3(0.48f, 0.88f, 0.48f);
                Renderer crystalRenderer = crystalObject.GetComponent<Renderer>();
                crystalRenderer.sharedMaterial = crystal;
                MagicTarget target = crystalObject.AddComponent<MagicTarget>();
                ConfigureTarget(target);

                Renderer beaconRenderer = CreatePrimitive(PrimitiveType.Sphere, "Warning_Beacon",
                    root.transform, new Vector3(0f, 2.35f, 0f), new Vector3(0.18f, 0.18f, 0.18f), alarm, false);
                Light crystalLight = CreatePointLight(root.transform, "Crystal_Glow", new Vector3(0f, 1.48f, 0f),
                    new Color(0.15f, 0.78f, 1f, 1f), 0.65f, 4f);
                Light warningLight = CreatePointLight(root.transform, "Burst_Warning_Light",
                    new Vector3(0f, 2.35f, 0f), new Color(1f, 0.08f, 0.025f, 1f), 0f, 7f);
                warningLight.enabled = false;
                beaconRenderer.enabled = false;

                GameObject ringObject = new GameObject("Burst_Warning_Ring");
                ringObject.transform.SetParent(root.transform, false);
                LineRenderer line = ringObject.AddComponent<LineRenderer>();
                line.sharedMaterial = ring;
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = 96;
                line.startWidth = 0.075f;
                line.endWidth = 0.075f;
                line.numCornerVertices = 2;
                line.numCapVertices = 2;
                line.alignment = LineAlignment.View;
                line.enabled = false;

                ManaDeviceVisuals visuals = root.AddComponent<ManaDeviceVisuals>();
                SerializedObject visualData = new SerializedObject(visuals);
                visualData.FindProperty("visualRoot").objectReferenceValue = visualRoot;
                visualData.FindProperty("stabilizationTarget").objectReferenceValue = target;
                SerializedProperty stateRenderers = visualData.FindProperty("stateRenderers");
                stateRenderers.arraySize = 2;
                stateRenderers.GetArrayElementAtIndex(0).objectReferenceValue = crystalRenderer;
                stateRenderers.GetArrayElementAtIndex(1).objectReferenceValue = accentRenderer;
                visualData.FindProperty("warningBeaconRenderer").objectReferenceValue = beaconRenderer;
                visualData.FindProperty("crystalLight").objectReferenceValue = crystalLight;
                visualData.FindProperty("warningLight").objectReferenceValue = warningLight;
                visualData.FindProperty("warningRing").objectReferenceValue = line;
                visualData.FindProperty("stabilizeRange").floatValue = 2.75f;
                visualData.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, DevicePrefabPath);
                if (saved == null) Debug.LogError("ManaDevice_Test.prefab 저장에 실패했습니다.");
                return saved;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static GameObject CreateAnomalyPrefab()
        {
            GameObject root = new GameObject("ManaDevice_Overload");
            try
            {
                ManaDeviceAnomaly anomaly = root.AddComponent<ManaDeviceAnomaly>();
                SerializedObject data = new SerializedObject(anomaly);
                data.FindProperty("firstBurstDelaySeconds").floatValue = 4.5f;
                data.FindProperty("burstWarningSeconds").floatValue = 1.5f;
                data.FindProperty("burstPeriodSeconds").floatValue = 6.5f;
                data.FindProperty("burstRadius").floatValue = 4.25f;
                data.FindProperty("stageOneBurstContamination").floatValue = 10f;
                data.FindProperty("stageTwoBurstContamination").floatValue = 15f;
                data.FindProperty("stageThreeBurstContamination").floatValue = 20f;
                data.FindProperty("stageTwoAt").floatValue = 20f;
                data.FindProperty("stageThreeAt").floatValue = 40f;
                data.FindProperty("ruptureAt").floatValue = 60f;
                data.FindProperty("stageTwoStabilityLossPerSecond").floatValue = 0.5f;
                data.FindProperty("stageThreeStabilityLossPerSecond").floatValue = 1f;
                data.FindProperty("ruptureStabilityDamage").floatValue = 20f;
                data.ApplyModifiedPropertiesWithoutUndo();
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, AnomalyPrefabPath);
                if (saved == null) Debug.LogError("ManaDevice_Overload.prefab 저장에 실패했습니다.");
                return saved;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static GameObject CreateSpawnPointPrefab(GameObject devicePrefab)
        {
            GameObject root = new GameObject("ManaDevice_SpawnPoint");
            try
            {
                root.name = "SP_DEVICE_Example";
                AnomalySpawnPoint point = root.AddComponent<AnomalySpawnPoint>();
                GameObject deviceInstance = (GameObject)PrefabUtility.InstantiatePrefab(devicePrefab, root.transform);
                deviceInstance.transform.localPosition = Vector3.zero;
                deviceInstance.transform.localRotation = Quaternion.identity;
                deviceInstance.transform.localScale = Vector3.one;
                SerializedObject pointData = new SerializedObject(point);
                pointData.FindProperty("surface").enumValueIndex = (int)AnomalySpawnPoint.SurfaceType.Device;
                pointData.FindProperty("zoneIdentity").objectReferenceValue = null;
                pointData.FindProperty("locationName").stringValue = "장치 구역";
                pointData.FindProperty("surfaceOffset").floatValue = 0f;
                pointData.FindProperty("deviceVisuals").objectReferenceValue =
                    deviceInstance.GetComponent<ManaDeviceVisuals>();
                pointData.ApplyModifiedPropertiesWithoutUndo();
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, SpawnPointPrefabPath);
                if (saved == null) Debug.LogError("ManaDevice_SpawnPoint.prefab 저장에 실패했습니다.");
                return saved;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void ConfigureTarget(MagicTarget target)
        {
            SerializedObject data = new SerializedObject(target);
            data.FindProperty("displayName").stringValue = "마력 장치 수정";
            data.FindProperty("requiredMagic").enumValueIndex = (int)MagicType.Stabilize;
            data.FindProperty("castDuration").floatValue = 18f;
            data.FindProperty("useTimingCast").boolValue = false;
            data.FindProperty("preserveProgressOnCancel").boolValue = true;
            data.FindProperty("allowHeldPurifyChain").boolValue = false;
            data.FindProperty("deactivateOnComplete").boolValue = false;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureDevicePrefabNormalEditorState()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(DevicePrefabPath);
            try
            {
                ManaDeviceVisuals visuals = contents.GetComponent<ManaDeviceVisuals>();
                if (visuals == null) return;
                SerializedObject data = new SerializedObject(visuals);
                Renderer beacon = data.FindProperty("warningBeaconRenderer").objectReferenceValue as Renderer;
                Light warningLight = data.FindProperty("warningLight").objectReferenceValue as Light;
                if (beacon != null) beacon.enabled = false;
                if (warningLight != null) warningLight.enabled = false;
                EditorUtility.SetDirty(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, DevicePrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static Renderer CreatePrimitive(PrimitiveType type, string name, Transform parent,
            Vector3 localPosition, Vector3 localScale, Material material, bool keepCollider)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localScale = localScale;
            Renderer renderer = primitive.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            Collider collider = primitive.GetComponent<Collider>();
            if (!keepCollider && collider != null) UnityEngine.Object.DestroyImmediate(collider);
            return renderer;
        }

        private static Light CreatePointLight(Transform parent, string name, Vector3 localPosition,
            Color color, float intensity, float range)
        {
            GameObject lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = localPosition;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        private static Material LoadOrCreateMaterial(string path, Color baseColor, Color emissionColor,
            float emissionIntensity, bool unlit = false)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogError($"WARD SHIFT 마력 장치: 재질 셰이더를 찾을 수 없습니다 ({path}).");
                return null;
            }

            Material material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", emissionColor * emissionIntensity);
                if (emissionIntensity > 0f) material.EnableKeyword("_EMISSION");
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void AddReference(SerializedProperty array, UnityEngine.Object value)
        {
            if (array == null || value == null) return;
            for (int i = 0; i < array.arraySize; i++)
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == value) return;
            int index = array.arraySize;
            array.InsertArrayElementAtIndex(index);
            array.GetArrayElementAtIndex(index).objectReferenceValue = value;
        }

        private static GameObject FindSceneObject(string objectName, Scene scene)
        {
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
                if (candidate != null && candidate.scene == scene && candidate.name == objectName)
                    return candidate;
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folder = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent)) AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
