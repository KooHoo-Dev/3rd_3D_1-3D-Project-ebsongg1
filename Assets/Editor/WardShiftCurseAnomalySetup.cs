using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WardShift;

namespace WardShift.Editor
{
    /// <summary>MainGame의 저주 오염 프리팹과 Floor 생성 지점을 중복 없이 연결합니다.</summary>
    public static class WardShiftCurseAnomalySetup
    {
        private const string MainGamePath = "Assets/Scenes/MainGame.unity";
        private const string PrototypeFolder = "Assets/Prefabs/WardShiftPrototype";
        private const string AnomalyFolder = PrototypeFolder + "/Anomalies";
        private const string MaterialFolder = PrototypeFolder + "/Materials";
        private const string CursePrefabPath = AnomalyFolder + "/CurseAnomaly.prefab";
        private const string PatchMaterialPath = MaterialFolder + "/CursePatch_Transparent.mat";
        private const string ParticleMaterialPath = MaterialFolder + "/CursePatch_Particles.mat";

        private const int PatchTemplateCount = 6;

        [MenuItem("Tools/WARD SHIFT/Setup Curse Anomaly")]
        public static void SetupActiveMainGame()
        {
            if (SceneManager.GetActiveScene().path != MainGamePath)
            {
                EditorUtility.DisplayDialog("WARD SHIFT 저주 오염 설정",
                    "MainGame 씬을 열고 다시 실행하세요.", "확인");
                return;
            }
            SetupLoadedMainGame(true);
        }

        /// <summary>Unity Batch Mode에서 MainGame 설정을 생성하고 저장하는 진입점입니다.</summary>
        public static void SetupMainGameForBatch()
        {
            EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Single);
            SetupLoadedMainGame(true);
        }

        /// <summary>Unity Batch Mode에서 저장된 저주 오염 설정을 검사합니다.</summary>
        public static void ValidateMainGameForBatch()
        {
            EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Single);
            ValidateLoadedMainGame();
            EditorApplication.Exit(0);
        }

        private static void SetupLoadedMainGame(bool saveScene)
        {
            AnomalySpawner spawner = Object.FindFirstObjectByType<AnomalySpawner>(FindObjectsInactive.Include);
            if (spawner == null)
            {
                Debug.LogError("WARD SHIFT curse setup: MainGame에서 AnomalySpawner를 찾을 수 없습니다.");
                return;
            }

            EnsureFolder(PrototypeFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(AnomalyFolder);

            CurseAnomaly cursePrefab = AssetDatabase.LoadAssetAtPath<CurseAnomaly>(CursePrefabPath);
            if (cursePrefab == null)
            {
                Material patchMaterial = EnsureMaterial(PatchMaterialPath,
                    "Universal Render Pipeline/Unlit", new Color(0.055f, 0.018f, 0.09f, 0.72f));
                Material particleMaterial = EnsureMaterial(ParticleMaterialPath,
                    "Universal Render Pipeline/Particles/Unlit", new Color(0.55f, 0.22f, 0.9f, 0.8f));
                if (patchMaterial == null || particleMaterial == null) return;
                cursePrefab = CreateCursePrefab(patchMaterial, particleMaterial);
            }
            if (cursePrefab == null) return;

            AssignCursePrefab(spawner, cursePrefab);
            AnomalySpawnPoint[] floorPoints = Object.FindObjectsByType<AnomalySpawnPoint>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(point => point != null && point.gameObject.scene == SceneManager.GetActiveScene()
                    && point.Surface == AnomalySpawnPoint.SurfaceType.Floor).ToArray();
            foreach (AnomalySpawnPoint point in floorPoints)
            {
                int created = WardShiftCursePatchPointSetup.EnsureExamplesForSpawnPoint(point);
                Debug.Log($"WARD SHIFT Floor 앵커 {point.name}: PatchPoint {point.GetCursePatchPoints().Length}개, "
                    + $"변경 {created}건. 위치를 Scene 뷰에서 확인·조정하세요.", point);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            if (saveScene && EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))
                Debug.Log("WARD SHIFT curse anomaly setup completed and MainGame was saved.", spawner);
            else if (saveScene)
                Debug.LogError("WARD SHIFT curse anomaly setup was not saved.", spawner);
        }

        private static CurseAnomaly CreateCursePrefab(Material patchMaterial, Material particleMaterial)
        {
            GameObject root = new GameObject("CurseAnomaly");
            try
            {
                CurseAnomaly anomaly = root.AddComponent<CurseAnomaly>();
                var patchComponents = new CursePatch[PatchTemplateCount];
                for (int i = 0; i < PatchTemplateCount; i++)
                    patchComponents[i] = CreatePatch(root.transform, i, Vector3.zero, patchMaterial, particleMaterial);

                SerializedObject anomalySerialized = new SerializedObject(anomaly);
                SerializedProperty patches = anomalySerialized.FindProperty("patches");
                patches.arraySize = patchComponents.Length;
                for (int i = 0; i < patchComponents.Length; i++)
                    patches.GetArrayElementAtIndex(i).objectReferenceValue = patchComponents[i];
                anomalySerialized.FindProperty("minimumPatchCount").intValue = 4;
                anomalySerialized.FindProperty("maximumPatchCount").intValue = 6;
                anomalySerialized.FindProperty("stageOnePatchSizeMultiplier").floatValue = 1f;
                anomalySerialized.FindProperty("stageTwoPatchSizeMultiplier").floatValue = 1.6f;
                anomalySerialized.FindProperty("stageThreePatchSizeMultiplier").floatValue = 2.3f;
                anomalySerialized.FindProperty("stageSizeTransitionSeconds").floatValue = 0.65f;
                anomalySerialized.FindProperty("purificationAreaMultiplier").floatValue = 1f;
                anomalySerialized.FindProperty("damageAreaMultiplier").floatValue = 1f;
                anomalySerialized.FindProperty("stageTwoAt").floatValue = 20f;
                anomalySerialized.FindProperty("stageThreeAt").floatValue = 40f;
                anomalySerialized.FindProperty("ruptureAt").floatValue = 60f;
                anomalySerialized.FindProperty("stageTwoStabilityLossPerSecond").floatValue = 0.5f;
                anomalySerialized.FindProperty("stageThreeStabilityLossPerSecond").floatValue = 1f;
                anomalySerialized.FindProperty("ruptureStabilityDamage").floatValue = 20f;
                anomalySerialized.FindProperty("stageOneContamination").floatValue = 1f;
                anomalySerialized.FindProperty("stageTwoContamination").floatValue = 3f;
                anomalySerialized.FindProperty("stageThreeContamination").floatValue = 6f;
                anomalySerialized.ApplyModifiedPropertiesWithoutUndo();

                CurseAnomaly saved = PrefabUtility.SaveAsPrefabAsset(root, CursePrefabPath)
                    .GetComponent<CurseAnomaly>();
                AssetDatabase.SaveAssets();
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static CursePatch CreatePatch(Transform parent, int index, Vector3 localPosition,
            Material patchMaterial, Material particleMaterial)
        {
            GameObject patchObject = new GameObject($"CursePatch_{index + 1:00}");
            patchObject.transform.SetParent(parent, false);
            patchObject.transform.localPosition = localPosition;

            BoxCollider aimCollider = patchObject.AddComponent<BoxCollider>();
            const float radius = 0.65f;
            // Visual hazard patches overlap slightly; keep aim areas centered and separate.
            const float aimWidth = 0.9f;
            aimCollider.size = new Vector3(aimWidth, 0.04f, aimWidth);
            aimCollider.center = Vector3.zero;

            MagicTarget target = patchObject.AddComponent<MagicTarget>();
            ConfigureMagicTarget(target, index);

            GameObject visualObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visualObject.name = "Patch_Visual";
            visualObject.transform.SetParent(patchObject.transform, false);
            visualObject.transform.localPosition = Vector3.zero;
            visualObject.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);
            Object.DestroyImmediate(visualObject.GetComponent<Collider>());
            MeshRenderer patchRenderer = visualObject.GetComponent<MeshRenderer>();
            patchRenderer.sharedMaterial = patchMaterial;

            GameObject particleObject = new GameObject("Corruption_Motes", typeof(ParticleSystem));
            particleObject.transform.SetParent(patchObject.transform, false);
            particleObject.transform.localPosition = new Vector3(0f, 0.055f, 0f);
            ParticleSystem particles = particleObject.GetComponent<ParticleSystem>();
            ConfigureParticles(particles, particleMaterial);

            CursePatch patch = patchObject.AddComponent<CursePatch>();
            SerializedObject patchSerialized = new SerializedObject(patch);
            patchSerialized.FindProperty("purificationTarget").objectReferenceValue = target;
            patchSerialized.FindProperty("patchRenderer").objectReferenceValue = patchRenderer;
            patchSerialized.FindProperty("aimCollider").objectReferenceValue = aimCollider;
            patchSerialized.FindProperty("moteParticles").objectReferenceValue = particles;
            patchSerialized.FindProperty("dangerRadius").floatValue = radius;
            patchSerialized.FindProperty("surfaceOffset").floatValue = 0.025f;
            patchSerialized.FindProperty("stageOneColor").colorValue = new Color(0.055f, 0.018f, 0.09f, 0.72f);
            patchSerialized.FindProperty("stageTwoColor").colorValue = new Color(0.18f, 0.025f, 0.18f, 0.82f);
            patchSerialized.FindProperty("stageThreeColor").colorValue = new Color(0.28f, 0.025f, 0.12f, 0.86f);
            patchSerialized.FindProperty("purifyingColor").colorValue = new Color(0.72f, 0.48f, 1f, 0.5f);
            patchSerialized.FindProperty("stageColorTransitionSeconds").floatValue = 0.45f;
            patchSerialized.FindProperty("stageOnePulseEnabled").boolValue = false;
            patchSerialized.FindProperty("stageOnePulseSpeed").floatValue = 0.25f;
            patchSerialized.FindProperty("stageOnePulseIntensity").floatValue = 0.02f;
            patchSerialized.FindProperty("stageTwoPulseEnabled").boolValue = true;
            patchSerialized.FindProperty("stageTwoPulseSpeed").floatValue = 0.45f;
            patchSerialized.FindProperty("stageTwoPulseIntensity").floatValue = 0.05f;
            patchSerialized.FindProperty("stageThreePulseEnabled").boolValue = true;
            patchSerialized.FindProperty("stageThreePulseSpeed").floatValue = 0.8f;
            patchSerialized.FindProperty("stageThreePulseIntensity").floatValue = 0.08f;
            patchSerialized.ApplyModifiedPropertiesWithoutUndo();
            return patch;
        }

        private static void ConfigureMagicTarget(MagicTarget target, int index)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty("displayName").stringValue = $"저주 오염 패치 {index + 1}";
            serialized.FindProperty("requiredMagic").enumValueIndex = (int)MagicType.Purify;
            serialized.FindProperty("castDuration").floatValue = 0.5f;
            serialized.FindProperty("useTimingCast").boolValue = false;
            serialized.FindProperty("allowHeldPurifyChain").boolValue = true;
            serialized.FindProperty("deactivateOnComplete").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureParticles(ParticleSystem particles, Material particleMaterial)
        {
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.025f, 0.09f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.58f, 0.28f, 0.92f, 0.75f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 2f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.48f;
            shape.radiusThickness = 0.1f;

            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.sharedMaterial = particleMaterial;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.Play(false);
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static void AssignCursePrefab(AnomalySpawner spawner, CurseAnomaly cursePrefab)
        {
            Undo.RecordObject(spawner, "Register Curse Anomaly Prefab");
            SerializedObject serialized = new SerializedObject(spawner);
            SerializedProperty additionalPrefabs = serialized.FindProperty("additionalAnomalyPrefabs");
            var prefabs = new List<AnomalyBehaviour>();
            for (int i = 0; i < additionalPrefabs.arraySize; i++)
            {
                AnomalyBehaviour existing = additionalPrefabs.GetArrayElementAtIndex(i).objectReferenceValue
                    as AnomalyBehaviour;
                if (existing != null && !prefabs.Contains(existing)) prefabs.Add(existing);
            }
            if (cursePrefab != null && !prefabs.Contains(cursePrefab)) prefabs.Add(cursePrefab);
            additionalPrefabs.arraySize = prefabs.Count;
            for (int i = 0; i < prefabs.Count; i++)
                additionalPrefabs.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(spawner);
        }

        private static Material EnsureMaterial(string path, string shaderName, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName) ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError($"WARD SHIFT curse setup: 사용할 셰이더를 찾을 수 없습니다 ({shaderName}).");
                return null;
            }
            if (material == null)
            {
                material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ValidateLoadedMainGame()
        {
            var errors = new List<string>();
            AnomalySpawner spawner = Object.FindFirstObjectByType<AnomalySpawner>(FindObjectsInactive.Include);
            if (spawner == null) errors.Add("AnomalySpawner 없음");
            else
            {
                if (!spawner.HasSpawnConfiguration) errors.Add("프리팹 또는 생성 지점 누락");
                if (!spawner.HasAtLeastTwoCompatibleSpawnPoints) errors.Add("동시 발생 호환 지점 2개 미만");
                if (!spawner.ActiveAnomalies.Any(anomaly => anomaly is ManaRift))
                {
                    SerializedObject serialized = new SerializedObject(spawner);
                    if (serialized.FindProperty("riftPrefab").objectReferenceValue == null)
                        errors.Add("ManaRift 프리팹 참조 누락");
                }
            }

            CurseAnomaly prefab = AssetDatabase.LoadAssetAtPath<CurseAnomaly>(CursePrefabPath);
            if (prefab == null) errors.Add("CurseAnomaly 프리팹 누락");
            else if (prefab.Patches == null || prefab.Patches.Length != 6) errors.Add("저주 오염 패치 6개 누락");

            AnomalySpawnPoint[] floorPoints = Object.FindObjectsByType<AnomalySpawnPoint>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(point => point != null && point.gameObject.scene == SceneManager.GetActiveScene()
                    && point.Surface == AnomalySpawnPoint.SurfaceType.Floor).ToArray();
            if (floorPoints.Length == 0) errors.Add("Floor AnomalySpawnPoint 없음");
            foreach (AnomalySpawnPoint point in floorPoints)
            {
                int pointCount = point.GetCursePatchPoints().Length;
                if (pointCount < 4) errors.Add($"{point.name} PatchPoint {pointCount}/4 (최소) 등록");
            }

            if (errors.Count == 0) Debug.Log("WARD SHIFT curse scene validation passed.");
            else Debug.LogError("WARD SHIFT curse scene validation failed: " + string.Join("; ", errors));
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string folder = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
