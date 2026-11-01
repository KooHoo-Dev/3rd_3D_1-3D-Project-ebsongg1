using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace WardShift.Editor
{
    internal static class WardShiftAudioAssetSetup
    {
        private const string LibraryPath = "Assets/Resources/WardShiftAudioLibrary.asset";
        private const string MixerPath = "Assets/WardShiftMixer.mixer";

        [MenuItem("WARD SHIFT/Audio/Refresh Audio Library References")]
        private static void RefreshAudioLibraryReferences()
        {
            EnsureFolder("Assets/Audio");
            EnsureFolder("Assets/Resources");

            WardShiftAudioLibrary library = AssetDatabase.LoadAssetAtPath<WardShiftAudioLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<WardShiftAudioLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            AssignClipIfEmpty(library, WardShiftSoundId.PlayerContaminationHit,
                "139331ff6cd0b8845bf9cd5982c7bbcf");
            AssignClipIfEmpty(library, WardShiftSoundId.CursePatchPurified,
                "517a38c489bf17042a033bc233bdbebb");
            AssignClipIfEmpty(library, WardShiftSoundId.CurseAnomalyResolved,
                "5013426c8cc95a64cb9c03673a716213");
            AssignClipIfEmpty(library, WardShiftSoundId.ManaDeviceStabilized,
                "73cc4036a5bfa204d83d13119b4ab44a");

            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer != null)
            {
                AudioMixerGroup[] groups = AssetDatabase.LoadAllAssetsAtPath(MixerPath)
                    .OfType<AudioMixerGroup>().ToArray();
                AudioMixerGroup master = FindGroup(groups, "Master");
                AudioMixerGroup sfx = FindGroup(groups, "SFX");
                AudioMixerGroup ui = FindGroup(groups, "UI");
                AudioMixerGroup bgm = FindGroup(groups, "BGM");
                AudioMixerGroup ambient = FindGroup(groups, "Ambient");
                library.AssignMixerGroups(master, sfx, ui, bgm, ambient);
                Debug.Log("WARD SHIFT mixer groups: " + string.Join(", ",
                    groups.Select(group => group != null ? group.name : "<null>")), mixer);
                RoutePrefabAudioSources(sfx);
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("WARD SHIFT 오디오 라이브러리 갱신 완료: " + LibraryPath, library);
        }

        private static void AssignClipIfEmpty(WardShiftAudioLibrary library,
            WardShiftSoundId id, string guid)
        {
            WardShiftAudioEntry entry = library.Find(id);
            if (entry == null || entry.Clip != null) return;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioClip clip = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null) library.AssignClip(id, clip);
            else Debug.LogWarning("오디오 파일 참조를 찾지 못했습니다. " + id + " (GUID " + guid + ")", library);
        }

        private static AudioMixerGroup FindGroup(AudioMixerGroup[] groups, string groupName)
        {
            return groups?.FirstOrDefault(group => group != null
                && string.Equals(group.name, groupName, StringComparison.OrdinalIgnoreCase));
        }

        private static void RoutePrefabAudioSources(AudioMixerGroup sfxGroup)
        {
            if (sfxGroup == null) return;
            string[] paths =
            {
                "Assets/Prefabs/WardShiftPrototype/VFX/VFX_CastComplete.prefab",
                "Assets/Assets/Effects/MagicArsenal/Effects/Prefabs/Missiles & Explosions/Arcane/ArcaneMissileNormal.prefab",
                "Assets/Assets/Effects/MagicArsenal/Effects/Prefabs/Beams/Setup/Impact/LightBeamImpact.prefab"
            };

            foreach (string path in paths)
            {
                GameObject prefabContents = PrefabUtility.LoadPrefabContents(path);
                if (prefabContents == null)
                {
                    Debug.LogWarning("AudioSource 출력 그룹을 설정할 프리팹을 찾지 못했습니다: " + path);
                    continue;
                }

                try
                {
                    bool changed = false;
                    foreach (AudioSource source in prefabContents.GetComponentsInChildren<AudioSource>(true))
                    {
                        if (source.outputAudioMixerGroup == sfxGroup) continue;
                        source.outputAudioMixerGroup = sfxGroup;
                        EditorUtility.SetDirty(source);
                        changed = true;
                    }
                    if (changed) PrefabUtility.SaveAsPrefabAsset(prefabContents, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabContents);
                }
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent ?? "Assets", name);
        }
    }
}
