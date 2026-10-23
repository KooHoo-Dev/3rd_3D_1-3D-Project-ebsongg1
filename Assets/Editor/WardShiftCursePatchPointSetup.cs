using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WardShift;

namespace WardShift.Editor
{
    /// <summary>각 Floor 생성 지점에 수동 편집용 저주 패치 위치 예시를 중복 없이 만듭니다.</summary>
    public static class WardShiftCursePatchPointSetup
    {
        private const string MainGamePath = "Assets/Scenes/MainGame.unity";
        private const int ExamplePointCount = 6;

        // SpawnPoint 기준 로컬 예시 배치입니다. 사용자가 Scene 뷰에서 방 구조에 맞춰 이동합니다.
        private static readonly Vector3[] ExampleLocalPositions =
        {
            new Vector3(-2.5f, 0f, -2f),
            new Vector3(0f, 0f, -2f),
            new Vector3(2.5f, 0f, -2f),
            new Vector3(-2.5f, 0f, 2f),
            new Vector3(0f, 0f, 2f),
            new Vector3(2.5f, 0f, 2f)
        };

        /// <summary>MainGame의 Floor 앵커 아래에 없는 PatchPoint 예시만 추가합니다.</summary>
        [MenuItem("Tools/WARD SHIFT/Create Curse PatchPoint Examples")]
        public static void CreateExamplesInActiveMainGame()
        {
            if (SceneManager.GetActiveScene().path != MainGamePath)
            {
                EditorUtility.DisplayDialog("WARD SHIFT PatchPoint 설정",
                    "MainGame 씬을 열고 다시 실행하세요.", "확인");
                return;
            }

            int created = EnsureExamples(SceneManager.GetActiveScene());
            if (created > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"WARD SHIFT PatchPoint 설정: {created}개 추가·정리. Scene 뷰에서 각 위치를 확인·조정하세요.");
        }

        /// <summary>Unity Batch Mode에서 MainGame Floor 앵커의 PatchPoint 예시를 만들고 저장합니다.</summary>
        public static void CreateMainGameExamplesForBatch()
        {
            EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Single);
            int created = EnsureExamples(SceneManager.GetActiveScene());
            if (created > 0)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            }
            Debug.Log($"WARD SHIFT PatchPoint batch setup completed. Changed {created} point(s).");
            EditorApplication.Exit(0);
        }

        private static int EnsureExamples(Scene scene)
        {
            AnomalySpawnPoint[] allPoints = Object.FindObjectsByType<AnomalySpawnPoint>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            int created = 0;
            foreach (AnomalySpawnPoint spawnPoint in allPoints
                         .Where(point => point != null && point.gameObject.scene == scene
                             && point.Surface == AnomalySpawnPoint.SurfaceType.Floor)
                         .OrderBy(point => point.name))
                created += EnsureExamplesForSpawnPoint(spawnPoint);
            return created;
        }

        /// <summary>한 Floor 생성 지점 아래 예시 PatchPoint를 중복 없이 추가합니다.</summary>
        public static int EnsureExamplesForSpawnPoint(AnomalySpawnPoint spawnPoint)
        {
            if (spawnPoint == null || spawnPoint.Surface != AnomalySpawnPoint.SurfaceType.Floor) return 0;
            var existing = new List<CursePatchPoint>();
            int changed = 0;
            for (int i = 0; i < spawnPoint.transform.childCount; i++)
            {
                Transform child = spawnPoint.transform.GetChild(i);
                CursePatchPoint point = child.GetComponent<CursePatchPoint>();
                if (point == null && child.name.StartsWith("PatchPoint_"))
                {
                    point = Undo.AddComponent<CursePatchPoint>(child.gameObject);
                    changed++;
                }
                if (point != null) existing.Add(point);
            }
            existing.Sort((left, right) =>
            {
                int numberOrder = left.PointNumber.CompareTo(right.PointNumber);
                return numberOrder != 0 ? numberOrder : string.CompareOrdinal(left.name, right.name);
            });

            var usedNumbers = new HashSet<int>();
            foreach (CursePatchPoint point in existing)
            {
                int number = point.PointNumber;
                if (number > ExamplePointCount || !usedNumbers.Add(number))
                {
                    number = 1;
                    while (usedNumbers.Contains(number)) number++;
                    Undo.RecordObject(point, "Number Curse PatchPoint");
                    point.SetPointNumber(number);
                    EditorUtility.SetDirty(point);
                    usedNumbers.Add(number);
                    changed++;
                }
            }

            for (int number = 1; number <= ExamplePointCount; number++)
            {
                if (usedNumbers.Contains(number)) continue;
                var pointObject = new GameObject($"PatchPoint_{number:00}");
                Undo.RegisterCreatedObjectUndo(pointObject, "Create Curse PatchPoint");
                pointObject.transform.SetParent(spawnPoint.transform, false);
                pointObject.transform.localPosition = ExampleLocalPositions[number - 1];
                pointObject.transform.localRotation = Quaternion.identity;
                pointObject.transform.localScale = Vector3.one;
                CursePatchPoint point = Undo.AddComponent<CursePatchPoint>(pointObject);
                point.SetPointNumber(number);
                EditorUtility.SetDirty(point);
                changed++;
            }

            EditorUtility.SetDirty(spawnPoint);
            if (changed > 0)
                Debug.Log($"{spawnPoint.name}: PatchPoint 예시 {changed}개를 추가·정리했습니다. 이 로컬 위치는 배치 예시이며 Scene 뷰에서 확인·이동하세요.", spawnPoint);
            return changed;
        }
    }
}
