using System.Collections.Generic;
using UnityEngine;

namespace WardShift
{
    /// <summary>지정한 벽 또는 빈 벽 중 무작위 지점에 균열 하나 생성.</summary>
    [DisallowMultipleComponent]
    public sealed class AnomalySpawner : MonoBehaviour
    {
        [Header("프리팹과 벽 생성 지점")]
        [Tooltip("Hierarchy의 테스트 오브젝트가 아닌 Project 창의 프리팹을 연결.")]
        [SerializeField] private ManaRift riftPrefab;
        [SerializeField] private AnomalySpawnPoint[] spawnPoints = new AnomalySpawnPoint[0];
        [SerializeField] private bool randomizePoint;
        [Tooltip("Randomize Point가 꺼져 있을 때 사용할 배열 번호. 첫 항목은 0.")]
        [SerializeField, Min(0)] private int selectedPointIndex;
        [SerializeField] private bool spawnOnStart = true;
        private GameObject activeRift;

        private void Start()
        {
            if (spawnOnStart) SpawnRift();
        }

        /// <summary>Play 중 Inspector 컨텍스트 메뉴 또는 다른 코드에서 생성 요청.</summary>
        [ContextMenu("Spawn Rift (Play Mode)")]
        public void SpawnRift()
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            WardStatus status = FindFirstObjectByType<WardStatus>();
            if (status != null && status.HasFailed) return;
            if (activeRift != null)
            {
                Debug.Log("AnomalySpawner: 기존 균열이 남아 있어 추가 생성하지 않습니다.", this);
                return;
            }
            AnomalySpawnPoint point = SelectPoint();
            if (point == null)
            {
                Debug.LogWarning("AnomalySpawner: 선택한 지점이 없거나 비활성/점유 상태이거나 Wall이 아닙니다.", this);
                return;
            }
            if (point.TrySpawn(riftPrefab, out ManaRift instance))
            {
                activeRift = instance.gameObject;
                Debug.Log($"마력 균열 생성: {point.name}", this);
            }
        }

        private AnomalySpawnPoint SelectPoint()
        {
            if (spawnPoints == null) return null;
            if (!randomizePoint)
            {
                if (selectedPointIndex < 0 || selectedPointIndex >= spawnPoints.Length) return null;
                AnomalySpawnPoint point = spawnPoints[selectedPointIndex];
                return CanUse(point) ? point : null;
            }
            var available = new List<AnomalySpawnPoint>();
            foreach (AnomalySpawnPoint point in spawnPoints)
                if (CanUse(point) && !available.Contains(point)) available.Add(point);
            return available.Count == 0 ? null : available[Random.Range(0, available.Count)];
        }

        private static bool CanUse(AnomalySpawnPoint point)
        {
            return point != null && point.isActiveAndEnabled && !point.IsOccupied
                && point.Surface == AnomalySpawnPoint.SurfaceType.Wall;
        }
    }
}
