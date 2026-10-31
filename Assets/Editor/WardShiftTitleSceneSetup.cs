using TMPro;
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WardShift.Editor
{
    /// <summary>기존 시설 배경만 복사해 독립 타이틀 씬을 구성합니다.</summary>
    public static class WardShiftTitleSceneSetup
    {
        private const string GameplayScenePath = "Assets/Scenes/MainGame.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
        private const string KoreanFontPath = "Assets/Studys/99. External Assets/Font/Noto_Sans_KR/static/NotoSansKR-Regular SDF.asset";
        private const string TitleFontSourcePath = "Assets/Fonts/Cinzel/Cinzel-VariableFont_wght.ttf";
        private const string TitleFontPath = "Assets/Fonts/Cinzel/Cinzel-VariableFont_wght Title SDF.asset";
        private const string ControlsFontPath = "Assets/Fonts/SUIT-ttf/SUIT-Regular SDF.asset";

        [MenuItem("WARD SHIFT/Build Title Scene")]
        public static void BuildTitleScene()
        {
            Scene sourceScene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
            RenderSettingsSnapshot renderSettings = new RenderSettingsSnapshot();
            GameObject mapRoot = FindRoot(sourceScene, "Map_Root");
            GameObject directionalLight = FindRoot(sourceScene, "Directional Light");

            if (mapRoot == null)
            {
                Debug.LogError("WARD SHIFT: MainGame에서 Map_Root 시설 배경을 찾지 못했습니다.");
                return;
            }

            Scene titleScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(titleScene);
            renderSettings.Apply();

            GameObject environment = new GameObject("TitleEnvironment");
            SceneManager.MoveGameObjectToScene(environment, titleScene);
            environment.transform.SetPositionAndRotation(mapRoot.transform.position, mapRoot.transform.rotation);
            environment.transform.localScale = mapRoot.transform.localScale;
            CopyEnvironmentRooms(mapRoot.transform, environment.transform, titleScene);

            Light copiedDirectional = CopyRoot(directionalLight, "TitleDirectionalLight", titleScene)
                ?.GetComponent<Light>();
            if (copiedDirectional != null) RenderSettings.sun = copiedDirectional;

            BuildCamera(titleScene);
            BuildCanvas(titleScene);
            BuildEventSystem(titleScene);

            EditorSceneManager.CloseScene(sourceScene, true);
            EditorSceneManager.MarkSceneDirty(titleScene);
            EditorSceneManager.SaveScene(titleScene, TitleScenePath);
            SetTitleFirstInBuildSettings();
            SceneManager.SetActiveScene(titleScene);
            Debug.Log("WARD SHIFT: TitleScene을 생성하고 빌드 시작 씬으로 설정했습니다.");
        }

        [MenuItem("WARD SHIFT/Regenerate Cinzel Title SDF")]
        public static void RegenerateCinzelTitleSdf()
        {
            TMP_FontAsset titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
            if (titleFont == null)
            {
                Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(TitleFontSourcePath);
                if (sourceFont == null)
                {
                    Debug.LogError("WARD SHIFT: Cinzel 원본 TTF 파일을 찾지 못했습니다.");
                    return;
                }

                titleFont = TMP_FontAsset.CreateFontAsset(sourceFont, 90, 8,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048,
                    AtlasPopulationMode.Dynamic, false);
                if (titleFont == null)
                {
                    Debug.LogError("WARD SHIFT: Cinzel SDF를 초기화하지 못했습니다.");
                    return;
                }

                if (!titleFont.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZ ", out string missing))
                {
                    UnityEngine.Object.DestroyImmediate(titleFont);
                    Debug.LogError("WARD SHIFT: Cinzel SDF 생성 실패. 누락 글자: " + missing);
                    return;
                }

                titleFont.atlasPopulationMode = AtlasPopulationMode.Static;
                titleFont.name = "Cinzel-VariableFont_wght Title SDF";
                Shader sdfShader = Shader.Find("TextMeshPro/Distance Field");
                if (sdfShader != null) titleFont.material.shader = sdfShader;

                AssetDatabase.CreateAsset(titleFont, TitleFontPath);
                foreach (Texture2D atlas in titleFont.atlasTextures)
                    AssetDatabase.AddObjectToAsset(atlas, titleFont);
                AssetDatabase.AddObjectToAsset(titleFont.material, titleFont);
                EditorUtility.SetDirty(titleFont);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(TitleFontPath);
                titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
            }

            Scene scene = EditorSceneManager.GetActiveScene();
            if (scene.path != TitleScenePath)
            {
                Debug.LogError("WARD SHIFT: TitleScene을 연 뒤 Regenerate Cinzel Title SDF를 실행하세요.");
                return;
            }

            Transform gameTitle = FindTransform(scene, "GameTitle");
            TMP_Text titleText = gameTitle != null ? gameTitle.GetComponent<TMP_Text>() : null;
            if (titleText == null)
            {
                Debug.LogError("WARD SHIFT: GameTitle 텍스트를 찾지 못했습니다.");
                return;
            }

            titleText.font = titleFont;
            titleText.fontSharedMaterial = titleFont.material;
            titleText.SetAllDirty();
            EditorUtility.SetDirty(titleText);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TitleScenePath);
            Debug.Log("WARD SHIFT: 패딩 8px의 Cinzel SDF를 생성해 GameTitle에 적용했습니다.");
        }

        [MenuItem("WARD SHIFT/Apply SUIT to Controls UI")]
        public static void ApplySuitToControlsUI()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (scene.path != TitleScenePath)
            {
                Debug.LogError("WARD SHIFT: TitleScene을 연 뒤 Apply SUIT to Controls UI를 실행하세요.");
                return;
            }

            TMP_FontAsset controlsFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ControlsFontPath);
            Transform controlsPanel = FindTransform(scene, "ControlsPanel");
            if (controlsFont == null || controlsPanel == null)
            {
                Debug.LogError("WARD SHIFT: SUIT 폰트 또는 ControlsPanel을 찾지 못했습니다.");
                return;
            }

            foreach (TMP_Text text in controlsPanel.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font = controlsFont;
                text.fontSharedMaterial = controlsFont.material;
                text.overflowMode = TextOverflowModes.Truncate;
                text.SetAllDirty();
                EditorUtility.SetDirty(text);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TitleScenePath);
            Debug.Log("WARD SHIFT: ControlsPanel의 모든 텍스트에 동적 SUIT SDF를 적용했습니다.");
        }

        [MenuItem("WARD SHIFT/Clean Title UI Text")]
        public static void CleanTitleUIText()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (scene.path != TitleScenePath)
            {
                Debug.LogError("WARD SHIFT: TitleScene을 연 뒤 Clean Title UI Text를 실행하세요.");
                return;
            }

            TMP_FontAsset koreanFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath);
            TMP_FontAsset controlsFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ControlsFontPath);
            Transform controlsRoot = FindTransform(scene, "ControlsPanel");
            string[] menuButtons = { "StartButton", "ControlsButton", "QuitButton" };
            foreach (string buttonName in menuButtons)
            {
                Transform button = FindTransform(scene, buttonName);
                if (button == null) continue;
                Transform index = button.Find("Index");
                if (index != null) UnityEngine.Object.DestroyImmediate(index.gameObject);
                TMP_Text label = FindNamedText(button, "Label");
                if (label == null) continue;
                label.fontStyle = FontStyles.Bold;
                RectTransform labelRect = label.rectTransform;
                labelRect.anchoredPosition = Vector2.zero;
                labelRect.sizeDelta = new Vector2(-60f, 0f);
                EditorUtility.SetDirty(label);
                EditorUtility.SetDirty(labelRect);
            }

            foreach (string textName in new[] { "Subtitle", "Footer" })
            {
                Transform text = FindTransform(scene, textName);
                if (text != null) UnityEngine.Object.DestroyImmediate(text.gameObject);
            }

            Transform canvas = FindTransform(scene, "TitleCanvas");
            if (canvas != null)
            {
                foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.name == "GameTitle")
                    {
                        // Cinzel의 라틴 글리프는 유지하고, 폭 초과 시 없는 말줄임표 글리프를 만들지 않습니다.
                        text.overflowMode = TextOverflowModes.Overflow;
                        text.textWrappingMode = TextWrappingModes.NoWrap;
                    }
                    else
                    {
                        bool isControlsText = controlsRoot != null && text.transform.IsChildOf(controlsRoot);
                        TMP_FontAsset targetFont = isControlsText ? controlsFont : koreanFont;
                        if (targetFont != null)
                        {
                            text.font = targetFont;
                            text.fontSharedMaterial = targetFont.material;
                        }
                        text.overflowMode = TextOverflowModes.Truncate;
                    }
                    EditorUtility.SetDirty(text);
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TitleScenePath);
            Debug.Log("WARD SHIFT: Cinzel 제목은 유지하고 한국어 글꼴, 버튼 문구와 보조 문구를 정리했습니다.");
        }

        private static TMP_Text FindNamedText(Transform parent, string objectName)
        {
            Transform transform = parent.Find(objectName);
            return transform != null ? transform.GetComponent<TMP_Text>() : null;
        }

        [MenuItem("WARD SHIFT/Validate Title Scene")]
        public static void ValidateTitleScene()
        {
            Scene scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
            int failures = 0;
            void Check(bool condition, string message)
            {
                if (condition) Debug.Log("TitleScene check passed: " + message);
                else
                {
                    failures++;
                    Debug.LogError("TitleScene check failed: " + message);
                }
            }

            GameObject environment = FindRoot(scene, "TitleEnvironment");
            Camera camera = FindComponent<Camera>(scene);
            Canvas canvas = FindComponent<Canvas>(scene);
            TitleMenuController manager = FindComponent<TitleMenuController>(scene);
            int eventSystemCount = CountComponents<EventSystem>(scene);
            Check(environment != null && environment.GetComponentInChildren<Renderer>(true) != null,
                "시설 배경 모델 포함");
            Check(camera != null && camera.gameObject.name == "TitleCamera", "고정 타이틀 카메라 포함");
            if (environment != null && camera != null)
            {
                Renderer[] renderers = environment.GetComponentsInChildren<Renderer>(true);
                Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(camera);
                int visibleCount = 0;
                for (int i = 0; i < Math.Min(12, renderers.Length); i++)
                    Debug.Log($"TitleScene model sample: {renderers[i].name}, "
                        + $"center={renderers[i].bounds.center}, enabled={renderers[i].enabled}, "
                        + $"active={renderers[i].gameObject.activeInHierarchy}");
                foreach (Renderer renderer in renderers)
                {
                    Vector3 center = renderer.bounds.center;
                    Vector3 viewport = camera.WorldToViewportPoint(center);
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor")
                            : material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                        if (viewport.z > 0f && viewport.x > 0.38f && viewport.x < 0.62f
                            && viewport.y > 0.28f && viewport.y < 0.55f
                            && (color.g > 0.4f && color.g > color.r * 1.25f && color.g > color.b * 1.2f
                                || center.y < 3f && renderer.bounds.size.y > 0.3f))
                            Debug.Log($"TitleScene center-frame model: {renderer.name}, material={material.name}, "
                                + $"shader={material.shader.name}, color={color}, center={center}, "
                                + $"bounds={renderer.bounds.size}, viewport={viewport}");
                    }
                    bool inFrustum = renderer.enabled && renderer.gameObject.activeInHierarchy
                        && GeometryUtility.TestPlanesAABB(frustum, renderer.bounds);
                    if (!inFrustum) continue;
                    visibleCount++;
                    if (visibleCount <= 8)
                        Debug.Log($"TitleScene visible model: {renderer.name}, center={renderer.bounds.center}, "
                            + $"materials={renderer.sharedMaterials.Length}");
                }
                Debug.Log($"TitleScene renderers: total={renderers.Length}, camera-frustum={visibleCount}, "
                    + $"forward={camera.transform.forward}, position={camera.transform.position}");
            }
            Check(canvas != null && canvas.GetComponent<CanvasScaler>() != null
                && canvas.GetComponent<CanvasScaler>().referenceResolution == new Vector2(1920f, 1080f),
                "1920x1080 기준 Canvas Scaler 포함");
            Check(eventSystemCount == 1 && CountComponents<InputSystemUIInputModule>(scene) == 1,
                "Input System EventSystem 한 개 포함");
            Check(manager != null, "TitleManager 포함");

            Transform gameTitle = FindTransform(scene, "GameTitle");
            Transform startButton = FindTransform(scene, "StartButton");
            Transform controlsButton = FindTransform(scene, "ControlsButton");
            Transform quitButton = FindTransform(scene, "QuitButton");
            Transform controlsPanel = FindTransform(scene, "ControlsPanel");
            Transform controlsCard = FindTransform(scene, "ControlsCard");
            Check(gameTitle != null && startButton != null && controlsButton != null && quitButton != null,
                "게임 제목 및 메뉴 버튼 포함");
            Check(controlsPanel != null && controlsCard != null
                && CountNamedChildren(controlsCard, "ControlRow_") == 8,
                "조작 안내 8개 행 포함");

            if (manager != null)
            {
                SerializedObject serialized = new SerializedObject(manager);
                Check(serialized.FindProperty("startButton").objectReferenceValue is Button
                    && serialized.FindProperty("controlsButton").objectReferenceValue is Button
                    && serialized.FindProperty("quitButton").objectReferenceValue is Button
                    && serialized.FindProperty("closeControlsButton").objectReferenceValue is Button,
                    "모든 메뉴 버튼 Inspector 참조 연결");
            }

            bool hasGameplaySystems = false;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour is DayFlowManager || behaviour is AnomalySpawner || behaviour is WardStatus
                        || behaviour is PlayerInventory || behaviour is MagicCaster)
                        hasGameplaySystems = true;
            Check(!hasGameplaySystems, "플레이어 및 게임 진행 시스템 미포함");
            Check(EditorBuildSettings.scenes.Length > 0
                && EditorBuildSettings.scenes[0].path == TitleScenePath
                && EditorBuildSettings.scenes[0].enabled,
                "빌드 시작 씬이 TitleScene");

            if (failures > 0) throw new InvalidOperationException($"TitleScene 검증 실패: {failures}개 항목.");
            Debug.Log($"WARD SHIFT: TitleScene 검증 완료. 카메라 위치 {camera.transform.position}, 회전 {camera.transform.eulerAngles}.");
        }

        [MenuItem("WARD SHIFT/Capture Title Background Preview")]
        public static void CaptureTitleBackgroundPreview()
        {
            Scene scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
            Camera camera = FindComponent<Camera>(scene);
            if (camera == null) throw new InvalidOperationException("TitleCamera를 찾지 못했습니다.");
            Canvas canvas = FindComponent<Canvas>(scene);
            if (canvas != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
            }

            const int width = 1280;
            const int height = 720;
            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply();
                string outputPath = "Logs/TitleSceneBackgroundPreview.png";
                System.IO.File.WriteAllBytes(outputPath, image.EncodeToPNG());
                Transform controlsPanel = FindTransform(scene, "ControlsPanel");
                if (controlsPanel != null)
                {
                    controlsPanel.gameObject.SetActive(true);
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                    image.Apply();
                    System.IO.File.WriteAllBytes("Logs/TitleSceneControlsPreview.png", image.EncodeToPNG());
                    controlsPanel.gameObject.SetActive(false);
                }
                Debug.Log("WARD SHIFT: 타이틀과 조작 안내 미리보기를 Logs 폴더에 저장했습니다.");
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void CopyEnvironmentRooms(Transform sourceRoot, Transform targetRoot, Scene targetScene)
        {
            string[] roomNames = { "Room_00_Start", "Room_01_ControlHall", "Room_03_Sealing", "Stairs" };
            foreach (Transform sourceChild in sourceRoot)
            {
                if (System.Array.IndexOf(roomNames, sourceChild.name) < 0) continue;
                GameObject copy = UnityEngine.Object.Instantiate(sourceChild.gameObject);
                copy.name = sourceChild.name;
                SceneManager.MoveGameObjectToScene(copy, targetScene);
                copy.transform.SetParent(targetRoot, false);
                copy.transform.localPosition = sourceChild.localPosition;
                copy.transform.localRotation = sourceChild.localRotation;
                copy.transform.localScale = sourceChild.localScale;
                foreach (Transform child in copy.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
                foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null && material.name == "Dummy_01")
                            renderer.enabled = false;
                }
            }
        }

        private static GameObject FindRoot(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == objectName) return root;
            return null;
        }

        private static T FindComponent<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null) return component;
            }
            return null;
        }

        private static int CountComponents<T>(Scene scene) where T : Component
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                count += root.GetComponentsInChildren<T>(true).Length;
            return count;
        }

        private static Transform FindTransform(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindTransform(root.transform, objectName);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform FindTransform(Transform root, string objectName)
        {
            if (root.name == objectName) return root;
            foreach (Transform child in root)
            {
                Transform found = FindTransform(child, objectName);
                if (found != null) return found;
            }
            return null;
        }

        private static int CountNamedChildren(Transform root, string prefix)
        {
            int count = 0;
            foreach (Transform child in root)
                if (child.name.StartsWith(prefix, StringComparison.Ordinal)) count++;
            return count;
        }

        private static GameObject CopyRoot(GameObject source, string newName, Scene scene)
        {
            if (source == null) return null;
            GameObject copy = UnityEngine.Object.Instantiate(source);
            copy.name = newName;
            SceneManager.MoveGameObjectToScene(copy, scene);
            return copy;
        }

        private static void BuildCamera(Scene scene)
        {
            GameObject cameraObject = new GameObject("TitleCamera", typeof(Camera), typeof(AudioListener));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 50f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 300f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.03f, 0.045f, 1f);
            camera.allowHDR = true;
            camera.allowMSAA = true;

            Vector3 cameraPosition = new Vector3(-411f, 7.5f, 58f);
            Vector3 lookTarget = new Vector3(-405f, 7.2f, 4f);
            cameraObject.transform.SetPositionAndRotation(cameraPosition,
                Quaternion.LookRotation(lookTarget - cameraPosition, Vector3.up));
        }

        private static void BuildCanvas(Scene scene)
        {
            GameObject canvasObject = new GameObject("TitleCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.GetComponent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvasObject.GetComponent<CanvasScaler>().matchWidthOrHeight = 0.5f;

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath);
            if (font == null) font = TMP_Settings.defaultFontAsset;
            TMP_FontAsset controlsFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ControlsFontPath);
            if (controlsFont == null) controlsFont = font;
            TMP_FontAsset titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
            if (titleFont == null) titleFont = font;

            GameObject titlePanel = CreateRect("TitlePanel", canvasObject.transform, Vector2.zero,
                Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero);
            BuildBackdrop(titlePanel.transform);

            GameObject menuPanel = CreateRect("MenuPanel", titlePanel.transform, Vector2.zero,
                Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero);
            CreateAccent(menuPanel.transform, new Vector2(0f, 1f), new Vector2(80f, -208f),
                new Vector2(24f, 2f), new Color(0.61f, 0.53f, 0.7f, 0.8f));
            CreateText("FacilityLabel", menuPanel.transform, font, "시설 운영부  ·  야간 교대",
                18f, new Color(0.69f, 0.68f, 0.7f, 1f), TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(124f, -197f),
                new Vector2(560f, 38f), FontStyles.Normal, 0.4f);
            TMP_Text gameTitle = CreateText("GameTitle", menuPanel.transform, titleFont, "WARD SHIFT", 86f,
                new Color(0.87f, 0.86f, 0.81f, 1f), TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(116f, -238f),
                new Vector2(720f, 126f), FontStyles.Bold, 1.15f);
            gameTitle.overflowMode = TextOverflowModes.Overflow;
            CreateAccent(menuPanel.transform, new Vector2(0f, 1f), new Vector2(124f, -356f),
                new Vector2(58f, 2f), new Color(0.61f, 0.53f, 0.7f, 0.86f));
            CreateAccent(menuPanel.transform, new Vector2(0f, 1f), new Vector2(197f, -356f),
                new Vector2(210f, 1f), new Color(0.58f, 0.57f, 0.59f, 0.26f));
            CreateAccent(menuPanel.transform, new Vector2(0f, 1f), new Vector2(124f, -442f),
                new Vector2(470f, 1f), new Color(0.62f, 0.61f, 0.64f, 0.19f));
            Button start = CreateMenuButton(menuPanel.transform, font, "StartButton", "게임 시작", -477f);
            Button controls = CreateMenuButton(menuPanel.transform, font, "ControlsButton", "조작 방법", -575f);
            Button quit = CreateMenuButton(menuPanel.transform, font, "QuitButton", "게임 종료", -673f);

            GameObject controlsPanel = CreateRect("ControlsPanel", canvasObject.transform, Vector2.zero,
                Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero);
            Image blocker = controlsPanel.AddComponent<Image>();
            blocker.color = new Color(0.015f, 0.018f, 0.027f, 0.82f);
            blocker.raycastTarget = true;
            BuildControlsCard(controlsPanel.transform, controlsFont, out Button closeControls);
            controlsPanel.SetActive(false);

            GameObject managerObject = new GameObject("TitleManager", typeof(TitleMenuController));
            SceneManager.MoveGameObjectToScene(managerObject, scene);
            TitleMenuController manager = managerObject.GetComponent<TitleMenuController>();
            SerializedObject serialized = new SerializedObject(manager);
            serialized.FindProperty("startButton").objectReferenceValue = start;
            serialized.FindProperty("controlsButton").objectReferenceValue = controls;
            serialized.FindProperty("quitButton").objectReferenceValue = quit;
            serialized.FindProperty("closeControlsButton").objectReferenceValue = closeControls;
            serialized.FindProperty("controlsPanel").objectReferenceValue = controlsPanel;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
        }

        private static void BuildBackdrop(Transform parent)
        {
            GameObject shade = CreateRect("MenuShade", parent, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, Vector2.zero);
            TitleBackdropGraphic backdrop = shade.AddComponent<TitleBackdropGraphic>();
            backdrop.raycastTarget = false;
        }

        private static void BuildControlsCard(Transform parent, TMP_FontAsset font, out Button closeButton)
        {
            GameObject card = CreateRect("ControlsCard", parent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(980f, 790f));
            Image cardImage = card.AddComponent<Image>();
            cardImage.color = new Color(0.055f, 0.058f, 0.067f, 0.985f);
            cardImage.raycastTarget = true;
            Outline outline = card.AddComponent<Outline>();
            outline.effectColor = new Color(0.53f, 0.47f, 0.59f, 0.3f);
            outline.effectDistance = new Vector2(1f, -1f);
            CreateAccent(card.transform, new Vector2(0.5f, 1f), new Vector2(0f, -27f),
                new Vector2(88f, 2f), new Color(0.61f, 0.53f, 0.7f, 0.9f));
            CreateText("ControlsEyebrow", card.transform, font, "근무 조작 기록",
                16f, new Color(0.66f, 0.64f, 0.67f, 1f), TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -45f),
                new Vector2(520f, 28f), FontStyles.Normal, 0.5f);
            CreateText("ControlsTitle", card.transform, font, "조작 방법", 43f,
                new Color(0.87f, 0.86f, 0.81f, 1f), TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -76f),
                new Vector2(760f, 68f), FontStyles.Bold, 0f);
            CreateText("ControlsSubtitle", card.transform, font, "근무 중 사용하는 조작",
                19f, new Color(0.7f, 0.69f, 0.67f, 1f), TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -135f),
                new Vector2(760f, 34f), FontStyles.Normal, 0f);

            string[,] rows =
            {
                { "W  A  S  D", "이동" },
                { "마우스", "시점 이동" },
                { "마우스 왼쪽 버튼", "마법 사용 / 진압" },
                { "휠  ·  1 / 2 / 3", "마법 선택" },
                { "Q", "마력 감응" },
                { "Tab", "시설 지도" },
                { "B", "인벤토리" },
                { "E", "아이템 줍기" }
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                float y = -178f - i * 62f;
                GameObject row = CreateRect("ControlRow_" + i, card.transform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    Vector2.zero, new Vector2(800f, 52f));
                row.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, y);
                Image rowImage = row.AddComponent<Image>();
                rowImage.color = i % 2 == 0
                    ? new Color(0.09f, 0.1f, 0.135f, 0.72f)
                    : new Color(0.065f, 0.075f, 0.105f, 0.72f);
                rowImage.raycastTarget = false;
                CreateText("Key", row.transform, font, rows[i, 0], 22f,
                    new Color(0.81f, 0.75f, 0.94f, 1f), TextAlignmentOptions.Left,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f),
                    new Vector2(330f, 42f), FontStyles.Bold, 0f);
                CreateText("Action", row.transform, font, rows[i, 1], 23f,
                    new Color(0.91f, 0.92f, 0.95f, 1f), TextAlignmentOptions.Left,
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f),
                    new Vector2(390f, 42f), FontStyles.Normal, 0f);
            }

            closeButton = CreateButton(card.transform, font, "CloseControlsButton", "닫기",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 29f),
                new Vector2(250f, 62f), new Color(0.12f, 0.105f, 0.16f, 1f), true);
        }

        private static void BuildEventSystem(Scene scene)
        {
            GameObject eventObject = new GameObject("EventSystem", typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            SceneManager.MoveGameObjectToScene(eventObject, scene);
            eventObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        private static Button CreateMenuButton(Transform parent, TMP_FontAsset font, string name,
            string label, float top)
        {
            return CreateButton(parent, font, name, label,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(124f, top), new Vector2(470f, 72f), new Color(0.04f, 0.045f, 0.06f, 0.9f));
        }

        private static Button CreateButton(Transform parent, TMP_FontAsset font, string name, string label,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Color normalColor,
            bool centeredLabel = false)
        {
            GameObject buttonObject = CreateRect(name, parent, anchorMin, anchorMax,
                anchorMin == new Vector2(0f, 1f) ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f),
                position, size);
            Image image = buttonObject.AddComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = true;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.colors = new ColorBlock
            {
                normalColor = normalColor,
                highlightedColor = new Color(0.17f, 0.155f, 0.205f, 0.98f),
                pressedColor = new Color(0.27f, 0.225f, 0.34f, 1f),
                selectedColor = new Color(0.17f, 0.155f, 0.205f, 0.98f),
                disabledColor = new Color(0.12f, 0.12f, 0.15f, 0.65f),
                colorMultiplier = 1f,
                fadeDuration = 0.09f
            };
            if (!centeredLabel)
            {
                CreateImage("VioletRule", buttonObject.transform, new Color(0.61f, 0.53f, 0.7f, 0.72f),
                    new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(2f, 0f),
                    new Vector2(0.5f, 0.5f), new Vector2(2f, 0f));
            }
            if (centeredLabel)
            {
                CreateText("Label", buttonObject.transform, font, label, 25f,
                    new Color(0.87f, 0.86f, 0.81f, 1f), TextAlignmentOptions.Center,
                    Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-40f, 0f),
                    FontStyles.Bold, 0f);
            }
            else
            {
                CreateText("Label", buttonObject.transform, font, label, 29f,
                    new Color(0.87f, 0.86f, 0.81f, 1f), TextAlignmentOptions.Left,
                    Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-60f, 0f),
                    FontStyles.Bold, 0f);
            }
            return button;
        }

        private static TMP_Text CreateText(string name, Transform parent, TMP_FontAsset font, string value,
            float fontSize, Color color, TextAlignmentOptions alignment,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size,
            FontStyles style, float characterSpacing)
        {
            GameObject textObject = CreateRect(name, parent, anchorMin, anchorMax,
                anchorMin == anchorMax ? anchorMin : new Vector2(0.5f, 0.5f), position, size);
            TMP_Text text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.characterSpacing = characterSpacing;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        private static void CreateAccent(Transform parent, Vector2 anchor, Vector2 position,
            Vector2 size, Color color)
        {
            CreateImage("Accent", parent, color, anchor, anchor, position, new Vector2(0.5f, 0.5f), size);
        }

        private static Image CreateImage(string name, Transform parent, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 pivot, Vector2 size = default)
        {
            GameObject imageObject = CreateRect(name, parent, anchorMin, anchorMax, pivot, position, size);
            Image image = imageObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static GameObject CreateRect(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size = default)
        {
            GameObject rectObject = new GameObject(name, typeof(RectTransform));
            rectObject.transform.SetParent(parent, false);
            RectTransform rect = rectObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rectObject;
        }

        private static void SetTitleFirstInBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            var existing = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.Add(new EditorBuildSettingsScene(TitleScenePath, true));
            foreach (EditorBuildSettingsScene scene in existing)
            {
                if (scene.path == TitleScenePath) continue;
                scenes.Add(scene);
            }
            if (!existing.Exists(scene => scene.path == GameplayScenePath))
                scenes.Add(new EditorBuildSettingsScene(GameplayScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private sealed class RenderSettingsSnapshot
        {
            private readonly AmbientMode ambientMode = RenderSettings.ambientMode;
            private readonly Color ambientLight = RenderSettings.ambientLight;
            private readonly Color ambientSkyColor = RenderSettings.ambientSkyColor;
            private readonly Color ambientEquatorColor = RenderSettings.ambientEquatorColor;
            private readonly Color ambientGroundColor = RenderSettings.ambientGroundColor;
            private readonly float ambientIntensity = RenderSettings.ambientIntensity;
            private readonly Material skybox = RenderSettings.skybox;
            private readonly DefaultReflectionMode reflectionMode = RenderSettings.defaultReflectionMode;
            private readonly float reflectionIntensity = RenderSettings.reflectionIntensity;
            private readonly int reflectionBounces = RenderSettings.reflectionBounces;
            private readonly bool fog = RenderSettings.fog;
            private readonly Color fogColor = RenderSettings.fogColor;
            private readonly FogMode fogMode = RenderSettings.fogMode;
            private readonly float fogDensity = RenderSettings.fogDensity;
            private readonly float fogStart = RenderSettings.fogStartDistance;
            private readonly float fogEnd = RenderSettings.fogEndDistance;
            private readonly Color subtractiveShadowColor = RenderSettings.subtractiveShadowColor;
            private readonly LightmapData[] lightmaps = LightmapSettings.lightmaps;

            public void Apply()
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientSkyColor = ambientSkyColor;
                RenderSettings.ambientEquatorColor = ambientEquatorColor;
                RenderSettings.ambientGroundColor = ambientGroundColor;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.skybox = skybox;
                RenderSettings.defaultReflectionMode = reflectionMode == DefaultReflectionMode.Custom
                    ? DefaultReflectionMode.Skybox : reflectionMode;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.reflectionBounces = reflectionBounces;
                RenderSettings.fog = fog;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogStartDistance = fogStart;
                RenderSettings.fogEndDistance = fogEnd;
                RenderSettings.subtractiveShadowColor = subtractiveShadowColor;
                LightmapSettings.lightmaps = lightmaps;
            }

        }
    }
}
