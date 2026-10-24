using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WardShift.Editor
{
    public static class WardShiftHUDSetup
    {
        [MenuItem("Tools/WARD SHIFT/Setup Readable HUD")]
        public static void SetupActiveScene()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/Studys/99. External Assets/Font/Noto_Sans_KR/static/NotoSansKR-Regular SDF.asset");
            WardShiftHUDLayout layout = WardShiftHUDLayout.Ensure(font);
            if (layout == null) throw new System.InvalidOperationException("Existing MagicHUD Canvas is required.");
            Transform legacy = layout.transform.Find("LegacyHUD");
            if (legacy == null)
            {
                var root = new GameObject("LegacyHUD", typeof(RectTransform));
                legacy = root.transform;
                legacy.SetParent(layout.transform, false);
                for (int i = layout.transform.childCount - 1; i >= 0; i--)
                {
                    Transform child = layout.transform.GetChild(i);
                    if (child != legacy && child != layout.HUDRoot) child.SetParent(legacy, false);
                }
            }
            EditorUtility.SetDirty(layout);
            foreach (MagicHUD hud in Object.FindObjectsByType<MagicHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                EditorUtility.SetDirty(hud);
            foreach (WardStatusHUD hud in Object.FindObjectsByType<WardStatusHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                EditorUtility.SetDirty(hud);
            EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);
            EditorSceneManager.SaveScene(layout.gameObject.scene);
            Debug.Log("[WARD SHIFT HUD] Existing Canvas rebuilt and saved at 1920x1080. Setup is idempotent.");
        }

        public static void SetupMainGameInBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainGame.unity");
            SetupActiveScene();
        }
    }
}
