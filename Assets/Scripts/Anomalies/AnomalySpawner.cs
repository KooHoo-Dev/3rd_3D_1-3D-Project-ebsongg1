using System;
using System.Collections.Generic;
using UnityEngine;

namespace WardShift
{
    /// <summary>
    /// - 활성 이상현상 목록 관리
    /// - 호환되는 비점유 지점에 이상현상 반복 생성
    /// - DAY 상태에 따라 생성 재개·중단
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnomalySpawner : MonoBehaviour
    {
        [Header("프리팹과 생성 지점")]
        [Tooltip("기존 ManaRift 프리팹. 기존 씬 참조를 유지합니다.")]
        [SerializeField] private ManaRift riftPrefab;
        [Tooltip("새 이상현상 프리팹은 공통 AnomalyBehaviour를 구현해 추가합니다.")]
        [SerializeField] private AnomalyBehaviour[] additionalAnomalyPrefabs = new AnomalyBehaviour[0];
        [SerializeField] private AnomalySpawnPoint[] spawnPoints = new AnomalySpawnPoint[0];
        [SerializeField] private bool randomizePoint;
        [Tooltip("Randomize Point가 꺼져 있을 때 우선 사용할 배열 번호. 점유 중이면 다른 호환 지점을 사용합니다.")]
        [SerializeField, Min(0)] private int selectedPointIndex;

        [Header("반복 생성")]
        [SerializeField] private bool spawnOnStart = true;
        [SerializeField, Min(0.1f)] private float spawnIntervalSeconds = 6f;
        [SerializeField, Min(1)] private int maximumConcurrentAnomalies = 2;
        [SerializeField, Min(0f)] private float firstSpawnDelaySeconds = 1f;

        private readonly List<AnomalyBehaviour> activeAnomalies = new List<AnomalyBehaviour>();
        private float spawnCountdown;
        private bool spawningEnabled;
        private WardStatus wardStatus;

        /// <summary>- ManaRift 전용 기존 리스너 호환 이벤트</summary>
        public event Action<ManaRift> RiftSpawned;
        public event Action<AnomalyBehaviour> AnomalySpawned;

        public bool SpawningEnabled => spawningEnabled;
        public int ActiveAnomalyCount
        {
            get
            {
                PruneEndedAnomalies();
                return activeAnomalies.Count;
            }
        }
        public IReadOnlyList<AnomalyBehaviour> ActiveAnomalies => activeAnomalies;
        public ManaRift ActiveRift
        {
            get
            {
                foreach (AnomalyBehaviour anomaly in activeAnomalies)
                    if (anomaly is ManaRift rift) return rift;
                return null;
            }
        }
        public bool HasSpawnConfiguration => GetConfiguredPrefabs().Count > 0
            && GetAllSpawnPoints().Count > 0;
        public bool HasAtLeastTwoCompatibleSpawnPoints
        {
            get
            {
                List<AnomalyBehaviour> prefabs = GetConfiguredPrefabs();
                var compatiblePoints = new HashSet<AnomalySpawnPoint>();
                foreach (AnomalySpawnPoint point in GetAllSpawnPoints())
                {
                    if (point == null || !point.isActiveAndEnabled) continue;
                    foreach (AnomalyBehaviour prefab in prefabs)
                    {
                        if (prefab.SpawnSurface != point.Surface) continue;
                        if (prefab is CurseAnomaly curse
                            && !curse.CanConfigureFromSpawnPoint(point.GetCursePatchPoints().Length)) continue;
                        compatiblePoints.Add(point);
                        break;
                    }
                }
                return compatiblePoints.Count >= 2;
            }
        }
        public int MaximumConcurrentAnomalies => Mathf.Max(1, maximumConcurrentAnomalies);
        public float SpawnIntervalSeconds => Mathf.Max(0.1f, spawnIntervalSeconds);
        public float FirstSpawnDelaySeconds => Mathf.Max(0f, firstSpawnDelaySeconds);

        private void Awake()
        {
            wardStatus = FindFirstObjectByType<WardStatus>();
        }

        private void Start()
        {
            if (spawnOnStart && !spawningEnabled) SetSpawningEnabled(true);
        }

        private void Update()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !spawningEnabled) return;
            if (wardStatus == null) wardStatus = FindFirstObjectByType<WardStatus>();
            if (wardStatus != null && wardStatus.HasFailed)
            {
                SetSpawningEnabled(false);
                return;
            }

            if (spawnCountdown > 0f)
            {
                spawnCountdown -= Time.deltaTime;
                if (spawnCountdown > 0f) return;
            }

            if (ActiveAnomalyCount < MaximumConcurrentAnomalies) TrySpawnNextAnomaly();
            spawnCountdown = SpawnIntervalSeconds;
        }

        /// <summary>DAY 진행에 맞춰 생성 주기를 시작하거나 중단합니다.</summary>
        public void SetSpawningEnabled(bool enabled)
        {
            if (spawningEnabled == enabled) return;
            spawningEnabled = enabled;
            spawnCountdown = enabled ? FirstSpawnDelaySeconds : 0f;
        }

        /// <summary>기존 호출부 호환용 별칭. 이제 모든 스포너 관리 이상현상을 정리합니다.</summary>
        public void ClearActiveRift() => ClearActiveAnomalies();

        public void ClearActiveAnomalies()
        {
            foreach (AnomalyBehaviour anomaly in activeAnomalies)
            {
                if (anomaly == null) continue;
                anomaly.Ended -= OnAnomalyEnded;
                Destroy(anomaly.gameObject);
            }
            activeAnomalies.Clear();
        }

        /// <summary>기존 Inspector 메뉴와 호출부에서 즉시 Rift 생성을 요청할 수 있습니다.</summary>
        [ContextMenu("Spawn Rift (Play Mode)")]
        public void SpawnRift()
        {
            if (riftPrefab != null) TrySpawnPrefab(riftPrefab);
        }

        [ContextMenu("Spawn Anomaly (Play Mode)")]
        public void SpawnAnomaly()
        {
            TrySpawnNextAnomaly();
        }

        [ContextMenu("Spawn Device Anomaly (Play Mode)")]
        public void SpawnDeviceAnomaly()
        {
            foreach (AnomalyBehaviour prefab in GetConfiguredPrefabs())
            {
                if (!(prefab is ManaDeviceAnomaly)) continue;
                TrySpawnPrefab(prefab);
                return;
            }
            Debug.LogWarning("AnomalySpawner: 장치형 마력 폭주 프리팹이 연결되지 않았습니다.", this);
        }

        private bool TrySpawnNextAnomaly()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !spawningEnabled) return false;
            if (wardStatus != null && wardStatus.HasFailed) return false;
            PruneEndedAnomalies();
            if (activeAnomalies.Count >= MaximumConcurrentAnomalies) return false;
            List<AnomalySpawnPoint> availableSpawnPoints = GetAllSpawnPoints();
            if (availableSpawnPoints.Count == 0)
            {
                Debug.LogWarning("AnomalySpawner: 생성 지점을 연결하세요.", this);
                return false;
            }

            List<AnomalyBehaviour> prefabs = GetConfiguredPrefabs();
            if (prefabs.Count == 0)
            {
                Debug.LogWarning("AnomalySpawner: 활성 AnomalyBehaviour 프리팹을 연결하세요.", this);
                return false;
            }
            WarnMissingCursePatchPoints(availableSpawnPoints, prefabs);

            // - 고정 지점 모드에서는 선택한 지점을 우선 사용
            // - 점유 중이면 비어 있는 호환 지점을 사용해 동시 생성 지점 분리
            if (!randomizePoint && selectedPointIndex >= 0 && selectedPointIndex < availableSpawnPoints.Count)
            {
                AnomalySpawnPoint preferred = availableSpawnPoints[selectedPointIndex];
                foreach (AnomalyBehaviour prefab in prefabs)
                    if (IsValidPrefab(prefab) && CanUse(preferred, prefab))
                        return TrySpawnAt(preferred, prefab);
            }

            var choices = new List<SpawnChoice>();
            foreach (AnomalyBehaviour prefab in prefabs)
            {
                if (!IsValidPrefab(prefab)) continue;
                foreach (AnomalySpawnPoint point in availableSpawnPoints)
                {
                    if (!CanUse(point, prefab)) continue;
                    bool duplicate = false;
                    foreach (SpawnChoice choice in choices)
                        if (choice.Prefab == prefab && choice.Point == point) { duplicate = true; break; }
                    if (!duplicate) choices.Add(new SpawnChoice(prefab, point));
                }
            }

            if (choices.Count == 0)
            {
                Debug.LogWarning("AnomalySpawner: 활성 상태이며 비어 있는 호환 생성 지점이 없습니다.", this);
                return false;
            }

            SpawnChoice selected = choices[randomizePoint
                ? UnityEngine.Random.Range(0, choices.Count)
                : 0];
            return TrySpawnAt(selected.Point, selected.Prefab);
        }

        private bool TrySpawnPrefab(AnomalyBehaviour prefab)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !spawningEnabled) return false;
            if (wardStatus != null && wardStatus.HasFailed) return false;
            PruneEndedAnomalies();
            if (activeAnomalies.Count >= MaximumConcurrentAnomalies) return false;
            if (!IsValidPrefab(prefab))
            {
                Debug.LogWarning("AnomalySpawner: Project 창의 활성 이상현상 프리팹을 연결하세요.", this);
                return false;
            }

            AnomalySpawnPoint point = SelectPoint(prefab);
            if (point == null)
            {
                Debug.LogWarning("AnomalySpawner: 프리팹 표면과 맞는 빈 생성 지점이 없습니다.", this);
                return false;
            }
            return TrySpawnAt(point, prefab);
        }

        private AnomalySpawnPoint SelectPoint(AnomalyBehaviour prefab)
        {
            List<AnomalySpawnPoint> availableSpawnPoints = GetAllSpawnPoints();
            if (!randomizePoint && selectedPointIndex >= 0 && selectedPointIndex < availableSpawnPoints.Count
                && CanUse(availableSpawnPoints[selectedPointIndex], prefab))
                return availableSpawnPoints[selectedPointIndex];

            var available = new List<AnomalySpawnPoint>();
            foreach (AnomalySpawnPoint point in availableSpawnPoints)
                if (CanUse(point, prefab) && !available.Contains(point)) available.Add(point);
            if (available.Count == 0) return null;
            return randomizePoint
                ? available[UnityEngine.Random.Range(0, available.Count)]
                : available[0];
        }

        private bool TrySpawnAt(AnomalySpawnPoint point, AnomalyBehaviour prefab)
        {
            if (!CanUse(point, prefab)) return false;
            if (!point.TrySpawn(prefab, out AnomalyBehaviour instance)) return false;
            activeAnomalies.Add(instance);
            instance.Ended += OnAnomalyEnded;
            AnomalySpawned?.Invoke(instance);
            if (instance is ManaRift rift) RiftSpawned?.Invoke(rift);
            Debug.Log($"이상현상 생성: {point.name} ({instance.GetType().Name})", this);
            return true;
        }

        private void OnAnomalyEnded(AnomalyBehaviour anomaly, AnomalyEndReason reason)
        {
            if (anomaly == null) return;
            anomaly.Ended -= OnAnomalyEnded;
            activeAnomalies.Remove(anomaly);
        }

        private void PruneEndedAnomalies()
        {
            for (int i = activeAnomalies.Count - 1; i >= 0; i--)
            {
                AnomalyBehaviour anomaly = activeAnomalies[i];
                if (anomaly != null && !anomaly.IsEnded) continue;
                if (anomaly != null) anomaly.Ended -= OnAnomalyEnded;
                activeAnomalies.RemoveAt(i);
            }
        }

        private List<AnomalyBehaviour> GetConfiguredPrefabs()
        {
            var prefabs = new List<AnomalyBehaviour>();
            if (IsValidPrefab(riftPrefab)) prefabs.Add(riftPrefab);
            if (additionalAnomalyPrefabs == null) return prefabs;
            foreach (AnomalyBehaviour prefab in additionalAnomalyPrefabs)
                if (IsValidPrefab(prefab) && !prefabs.Contains(prefab)) prefabs.Add(prefab);
            return prefabs;
        }

        private static bool IsValidPrefab(AnomalyBehaviour prefab)
        {
            return prefab != null && !prefab.gameObject.scene.IsValid()
                && prefab.gameObject.activeSelf && prefab.enabled;
        }

        private static bool CanUse(AnomalySpawnPoint point, AnomalyBehaviour prefab)
        {
            return point != null && point.isActiveAndEnabled && !point.IsOccupied
                && point.Surface == prefab.SpawnSurface && point.CanSpawnPrefab(prefab);
        }

        private List<AnomalySpawnPoint> GetAllSpawnPoints()
        {
            var points = new List<AnomalySpawnPoint>();
            var seen = new HashSet<AnomalySpawnPoint>();
            if (spawnPoints != null)
                foreach (AnomalySpawnPoint point in spawnPoints)
                    AddSpawnPoint(points, seen, point);

            // Scene discovery lets a copied Floor anchor join the rotation without rewriting this serialized array.
            foreach (AnomalySpawnPoint point in FindObjectsByType<AnomalySpawnPoint>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (point != null && point.gameObject.scene == gameObject.scene)
                    AddSpawnPoint(points, seen, point);
            return points;
        }

        private static void AddSpawnPoint(List<AnomalySpawnPoint> points,
            HashSet<AnomalySpawnPoint> seen, AnomalySpawnPoint point)
        {
            if (point == null || !point.gameObject.scene.IsValid() || !seen.Add(point)) return;
            points.Add(point);
        }

        private static void WarnMissingCursePatchPoints(List<AnomalySpawnPoint> points,
            List<AnomalyBehaviour> prefabs)
        {
            foreach (AnomalyBehaviour anomalyPrefab in prefabs)
            {
                if (!(anomalyPrefab is CurseAnomaly cursePrefab)) continue;
                foreach (AnomalySpawnPoint point in points)
                    if (point != null && point.Surface == AnomalySpawnPoint.SurfaceType.Floor
                        && point.GetCursePatchPoints().Length < cursePrefab.MinimumPatchPointCount)
                        point.WarnPatchPointShortage(cursePrefab);
            }
        }

        private void OnDestroy()
        {
            foreach (AnomalyBehaviour anomaly in activeAnomalies)
                if (anomaly != null) anomaly.Ended -= OnAnomalyEnded;
            activeAnomalies.Clear();
        }

        private readonly struct SpawnChoice
        {
            public readonly AnomalyBehaviour Prefab;
            public readonly AnomalySpawnPoint Point;
            public SpawnChoice(AnomalyBehaviour prefab, AnomalySpawnPoint point)
            {
                Prefab = prefab;
                Point = point;
            }
        }
    }
}
