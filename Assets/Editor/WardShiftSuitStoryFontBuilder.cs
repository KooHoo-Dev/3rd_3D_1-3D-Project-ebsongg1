using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.TextCore.LowLevel;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace WardShift.Editor
{
    /// <summary>브리핑 문구와 고정 UI 문자를 SUIT SDF 아틀라스에 포함합니다.</summary>
    [InitializeOnLoad]
    public static class WardShiftSuitStoryFontBuilder
    {
        private const string SourceFontPath = "Assets/Fonts/SUIT-ttf/SUIT-Regular.ttf";
        private const string DataPath = "Assets/Resources/WardShift/DayBriefingData.asset";
        private const string OutputPath = "Assets/Fonts/SUIT-ttf/SUIT-Regular Story SDF.asset";

        static WardShiftSuitStoryFontBuilder()
        {
            EditorApplication.delayCall += BuildWhenMissing;
        }

        [MenuItem("WARD SHIFT/Fonts/Build SUIT Story Atlas")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath) != null)
                throw new InvalidOperationException($"Font asset already exists: {OutputPath}");

            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            DayBriefingData data = AssetDatabase.LoadAssetAtPath<DayBriefingData>(DataPath);
            if (sourceFont == null || data == null)
                throw new InvalidOperationException("SUIT TTF 또는 DAY 브리핑 데이터 에셋을 찾을 수 없습니다.");

            HashSet<char> uniqueCharacters = new HashSet<char>();
            AddCharacters(uniqueCharacters, "제17 마력 제어소 주간 담당자 인수인계 근무 시작 근무 주의사항 근무 인계 완료 DAY 0123456789/");
            for (int day = 1; day <= 7; day++)
            {
                DayBriefingData.DayRecord record = data.GetRecord(day);
                if (record == null)
                    throw new InvalidOperationException($"DAY {day} 브리핑 기록을 찾을 수 없습니다.");

                AddCharacters(uniqueCharacters, record.RecordType);
                AddCharacters(uniqueCharacters, record.Title);
                AddCharacters(uniqueCharacters, record.Body);
                AddCharacters(uniqueCharacters, record.Caution);
                AddCharacters(uniqueCharacters, record.HandoffMemo);
            }

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont, 90, 8,
                GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (fontAsset == null)
                throw new InvalidOperationException("SUIT TMP 폰트 에셋을 생성하지 못했습니다.");

            fontAsset.name = "SUIT-Regular Story SDF";
            if (!fontAsset.TryAddCharacters(new string(ToArray(uniqueCharacters)), out string missing)
                || !string.IsNullOrEmpty(missing))
            {
                UnityEngine.Object.DestroyImmediate(fontAsset);
                throw new InvalidOperationException($"SUIT 아틀라스에 추가하지 못한 글자: {missing}");
            }

            fontAsset.isMultiAtlasTexturesEnabled = true;
            AssetDatabase.CreateAsset(fontAsset, OutputPath);
            foreach (Texture2D atlas in fontAsset.atlasTextures)
                if (atlas != null && !AssetDatabase.Contains(atlas))
                    AssetDatabase.AddObjectToAsset(atlas, fontAsset);
            if (fontAsset.material != null && !AssetDatabase.Contains(fontAsset.material))
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

            EditorUtility.SetDirty(fontAsset);
            SerializedObject serializedData = new SerializedObject(data);
            serializedData.FindProperty("koreanFont").objectReferenceValue = fontAsset;
            serializedData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"생성 완료: {OutputPath} / {uniqueCharacters.Count}자 / 2048x2048 SDF 아틀라스");
        }

        private static void BuildWhenMissing()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath) != null) return;
            Build();
        }

        private static void AddCharacters(HashSet<char> target, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (char character in value)
                if (!char.IsControl(character)) target.Add(character);
        }

        private static char[] ToArray(HashSet<char> characters)
        {
            char[] result = new char[characters.Count];
            characters.CopyTo(result);
            Array.Sort(result);
            return result;
        }
    }
}
