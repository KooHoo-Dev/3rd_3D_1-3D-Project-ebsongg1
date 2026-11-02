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
        private enum SpawnKind { ManaRift, CurseContamination, ManaDeviceOverload, Other }
        private enum SpecialSpawnAttempt { NotDue, Spawned, Waiting }

        [Serializable]
        public sealed class DaySpawnProfile
        {
            [SerializeField, Min(1)] private int day = 1;
            [SerializeField] private bool allowManaRift = true;
            [SerializeField] private bool allowCurseContamination;
            [SerializeField] private bool allowManaDeviceOverload;
            [Tooltip("DAY 4 이후 장치 공격을 원형 방출부터 시작해 마력탄과 번갈아 사용합니다.")]
            [SerializeField] private bool alternateDeviceAttacks;
            [SerializeField] private bool allowOtherAnomalies;
            [Header("이상현상 종류별 생성 가중치 (0이면 생성 안 함)")]
            [SerializeField, Range(0, 100)] private int manaRiftWeight = 1;
            [SerializeField, Range(0, 100)] private int curseContaminationWeight = 1;
            [SerializeField, Range(0, 100)] private int manaDeviceOverloadWeight = 1;
            [SerializeField, Range(0, 100)] private int otherAnomaliesWeight = 1;
            [SerializeField, Min(1)] private int maximumConcurrentAnomalies = 1;
            [SerializeField, Min(0.1f)] private float spawnIntervalSeconds = 8f;

            public int Day => Mathf.Max(1, day);
            public bool AllowManaRift => allowManaRift;
            public bool AllowCurseContamination => allowCurseContamination;
            public bool AllowManaDeviceOverload => allowManaDeviceOverload;
            public bool AlternateDeviceAttacks => alternateDeviceAttacks;
            public bool AllowOtherAnomalies => allowOtherAnomalies;
            public int MaximumConcurrentAnomalies => Mathf.Max(1, maximumConcurrentAnomalies);
            public float SpawnIntervalSeconds => Mathf.Max(0.1f, spawnIntervalSeconds);

            public DaySpawnProfile() { }

            public DaySpawnProfile(int day, bool rift, bool curse, bool device,
                int maximumConcurrent, float interval)
            {
                this.day = day;
                allowManaRift = rift;
                allowCurseContamination = curse;
                allowManaDeviceOverload = device;
                alternateDeviceAttacks = day >= 4;
                maximumConcurrentAnomalies = maximumConcurrent;
                spawnIntervalSeconds = interval;
            }

            public bool Allows(AnomalyBehaviour anomaly)
            {
                if (anomaly is ManaRift) return allowManaRift;
                if (anomaly is CurseAnomaly) return allowCurseContamination;
                if (anomaly is ManaDeviceAnomaly) return allowManaDeviceOverload;
                return allowOtherAnomalies;
            }

            public int GetSpawnWeight(AnomalyBehaviour anomaly)
            {
                if (!Allows(anomaly)) return 0;
                if (anomaly is ManaRift) return Mathf.Clamp(manaRiftWeight, 0, 100);
                if (anomaly is CurseAnomaly) return Mathf.Clamp(curseContaminationWeight, 0, 100);
                if (anomaly is ManaDeviceAnomaly) return Mathf.Clamp(manaDeviceOverloadWeight, 0, 100);
                return Mathf.Clamp(otherAnomaliesWeight, 0, 100);
            }
        }

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

        [Header("DAY별 생성 규칙")]
        [SerializeField] private DaySpawnProfile[] daySpawnProfiles = CreateDefaultDayProfiles();

        [Header("DAY 5~7 특별 마력 균열")]
        [SerializeField] private SpecialManaRiftSchedule specialManaRiftSchedule = new SpecialManaRiftSchedule();

        private readonly List<AnomalyBehaviour> activeAnomalies = new List<AnomalyBehaviour>();
        private float spawnCountdown;
        private bool spawningEnabled;
        private bool spawnWindowOpen = true;
        private WardStatus wardStatus;
        private DayFlowManager dayFlow;
        private DaySpawnProfile activeDayProfile;
        private System.Random dayRandom;
        private int activeDay = 1;
        private bool specialSpawnWaitLogged;

        /// <summary>- ManaRift 전용 기존 리스너 호환 이벤트</summary>
        public event Action<ManaRift> RiftSpawned;
        public event Action<AnomalyBehaviour> AnomalySpawned;
        /// <summary>특별 마력 균열이 실제 생성된 뒤에만 DAY와 함께 발생합니다.</summary>
        public event Action<ManaRift, int> SpecialManaRiftSpawned;

        public bool SpawningEnabled => spawningEnabled;
        public bool SpawnWindowOpen => spawnWindowOpen;
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
                    foreach (AnomalyBehaviour prefab in prefabs)
                    {
                        if (!IsCompatibleForCapacity(point, prefab)) continue;
                        compatiblePoints.Add(point);
                        break;
                    }
                }
                return compatiblePoints.Count >= 2;
            }
        }
        public int MaximumConcurrentAnomalies => activeDayProfile != null
            ? activeDayProfile.MaximumConcurrentAnomalies : Mathf.Max(1, maximumConcurrentAnomalies);
        public float SpawnIntervalSeconds => activeDayProfile != null
            ? activeDayProfile.SpawnIntervalSeconds : Mathf.Max(0.1f, spawnIntervalSeconds);
        public float FirstSpawnDelaySeconds => Mathf.Max(0f, firstSpawnDelaySeconds);
        public DaySpawnProfile ActiveDayProfile => activeDayProfile;

        private void Awake()
        {
            wardStatus = FindFirstObjectByType<WardStatus>();
            dayFlow = FindFirstObjectByType<DayFlowManager>();
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

            ExpireSpecialEventIfSpawnWindowClosed();
            if (!CanSpawnNow()) return;

            if (spawnCountdown > 0f)
            {
                spawnCountdown -= Time.deltaTime;
                if (spawnCountdown > 0f) return;
            }

            TrySpawnNextAnomaly();
            spawnCountdown = SpawnIntervalSeconds;
        }

        /// <summary>DAY 진행에 맞춰 생성 주기를 시작하거나 중단합니다.</summary>
        public void SetSpawningEnabled(bool enabled)
        {
            if (spawningEnabled == enabled) return;
            spawningEnabled = enabled;
            spawnCountdown = enabled ? FirstSpawnDelaySeconds : 0f;
        }

        /// <summary>DAY별 허용 현상, 동시 상한, 생성 간격과 재도전용 결정적 난수를 적용합니다.</summary>
        public void ConfigureForDay(int day, int deterministicSeed)
        {
            activeDay = Mathf.Max(1, day);
            activeDayProfile = FindDayProfile(day);
            if (specialManaRiftSchedule == null) specialManaRiftSchedule = new SpecialManaRiftSchedule();
            specialManaRiftSchedule.ConfigureForDay(activeDay);
            specialSpawnWaitLogged = false;
            dayRandom = new System.Random(deterministicSeed);
            spawnCountdown = FirstSpawnDelaySeconds;
            spawnWindowOpen = true;
        }

        public void SetSpawnWindowOpen(bool open) => spawnWindowOpen = open;

        private bool CanSpawnNow()
        {
            if (!spawnWindowOpen) return false;
            if (dayFlow == null) dayFlow = FindFirstObjectByType<DayFlowManager>();
            return dayFlow == null || (dayFlow.State == DayState.Playing
                && dayFlow.RemainingSeconds - Time.deltaTime > dayFlow.SpawnStopBeforeEndSeconds);
        }

        public DaySpawnProfile GetDayProfile(int day) => FindDayProfile(day);

        public bool IsAnomalyEnabledForDay(AnomalyBehaviour anomaly, int day)
        {
            if (anomaly == null) return false;
            DaySpawnProfile profile = FindDayProfile(day);
            return profile == null || profile.GetSpawnWeight(anomaly) > 0;
        }

        /// <summary>검증된 생성 지점 기준으로 이 DAY의 동시 슬롯을 채울 수 있는지 확인합니다.</summary>
        public int CountCompatibleSpawnPointsForDay(int day)
        {
            DaySpawnProfile profile = FindDayProfile(day);
            var compatiblePoints = new HashSet<AnomalySpawnPoint>();
            foreach (AnomalySpawnPoint point in GetAllSpawnPoints())
            {
                foreach (AnomalyBehaviour prefab in GetConfiguredPrefabs())
                {
                    if (profile != null && profile.GetSpawnWeight(prefab) <= 0) continue;
                    if (!IsCompatibleForCapacity(point, prefab)) continue;
                    // Preserve this DAY count's device-readiness check; the general count has no such check.
                    if (prefab is ManaDeviceAnomaly && !point.CanSpawnPrefab(prefab)) continue;
                    compatiblePoints.Add(point);
                    break;
                }
            }
            return compatiblePoints.Count;
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

        /// <summary>기존 Play Mode 테스트용 수동 생성. 자동 생성의 DAY 규칙은 변경하지 않습니다.</summary>
        [ContextMenu("Spawn Rift (Play Mode)")]
        public void SpawnRift()
        {
            if (riftPrefab != null) TrySpawnPrefab(riftPrefab, true);
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
                TrySpawnPrefab(prefab, true);
                return;
            }
            Debug.LogWarning("AnomalySpawner: 장치형 마력 폭주 프리팹이 연결되지 않았습니다.", this);
        }

        private bool TrySpawnNextAnomaly()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !spawningEnabled || !CanSpawnNow()) return false;
            if (wardStatus != null && wardStatus.HasFailed) return false;
            PruneEndedAnomalies();

            SpecialSpawnAttempt specialAttempt = TrySpawnDueSpecialRift();
            if (specialAttempt != SpecialSpawnAttempt.NotDue)
                return specialAttempt == SpecialSpawnAttempt.Spawned;

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

            // 가능한 지점 수와 무관하게 종류를 먼저 뽑고, 그 종류의 지점을 나중에 뽑습니다.
            var groups = new List<SpawnGroup>();
            foreach (AnomalyBehaviour prefab in prefabs)
            {
                if (!IsValidPrefab(prefab)) continue;
                int weight = activeDayProfile != null ? activeDayProfile.GetSpawnWeight(prefab) : 1;
                if (weight <= 0) continue;

                List<AnomalySpawnPoint> points = GetUsableSpawnPoints(availableSpawnPoints, prefab);
                if (points.Count == 0) continue;

                SpawnKind kind = GetSpawnKind(prefab);
                SpawnGroup group = groups.Find(candidate => candidate.Kind == kind);
                if (group == null)
                {
                    group = new SpawnGroup(kind, weight);
                    groups.Add(group);
                }
                group.Options.Add(new SpawnOption(prefab, points));
            }

            if (groups.Count == 0)
            {
                Debug.LogWarning("AnomalySpawner: 활성 상태이며 비어 있는 호환 생성 지점이 없습니다.", this);
                return false;
            }

            int totalWeight = 0;
            foreach (SpawnGroup group in groups) totalWeight += group.Weight;
            int draw = NextRandom(totalWeight);
            SpawnGroup selectedGroup = groups[0];
            foreach (SpawnGroup group in groups)
            {
                if (draw < group.Weight) { selectedGroup = group; break; }
                draw -= group.Weight;
            }

            SpawnOption selectedOption = selectedGroup.Options[NextRandom(selectedGroup.Options.Count)];
            AnomalySpawnPoint selectedPoint = SelectPoint(selectedOption.Points, availableSpawnPoints);
            return TrySpawnAt(selectedPoint, selectedOption.Prefab);
        }

        private SpecialSpawnAttempt TrySpawnDueSpecialRift()
        {
            if (specialManaRiftSchedule == null || !specialManaRiftSchedule.IsPendingForDay(activeDay))
                return SpecialSpawnAttempt.NotDue;
            if (dayFlow == null) dayFlow = FindFirstObjectByType<DayFlowManager>();
            if (dayFlow == null) return SpecialSpawnAttempt.NotDue;

            float elapsedSeconds = Mathf.Max(0f, dayFlow.DayDurationSeconds - dayFlow.RemainingSeconds);
            if (!specialManaRiftSchedule.TryGetDueStage(activeDay, elapsedSeconds, out int initialStage))
                return SpecialSpawnAttempt.NotDue;

            if (activeAnomalies.Count >= MaximumConcurrentAnomalies)
            {
                LogSpecialSpawnWaiting("the active anomaly limit is full");
                return SpecialSpawnAttempt.Waiting;
            }

            if (!IsValidPrefab(riftPrefab))
            {
                LogSpecialSpawnWaiting("the ManaRift prefab is missing or inactive");
                return SpecialSpawnAttempt.Waiting;
            }

            List<AnomalySpawnPoint> availableSpawnPoints = GetAllSpawnPoints();
            List<AnomalySpawnPoint> compatiblePoints = GetUsableSpawnPoints(availableSpawnPoints, riftPrefab);
            if (compatiblePoints.Count == 0)
            {
                LogSpecialSpawnWaiting("no unoccupied compatible ManaRift spawn point is available");
                return SpecialSpawnAttempt.Waiting;
            }

            AnomalySpawnPoint selectedPoint = SelectPoint(compatiblePoints, availableSpawnPoints);
            if (!TrySpawnAt(selectedPoint, riftPrefab, initialStage, true))
            {
                LogSpecialSpawnWaiting("the selected spawn point rejected the ManaRift");
                return SpecialSpawnAttempt.Waiting;
            }

            specialSpawnWaitLogged = false;
            return SpecialSpawnAttempt.Spawned;
        }

        private void ExpireSpecialEventIfSpawnWindowClosed()
        {
            if (specialManaRiftSchedule == null
                || !specialManaRiftSchedule.IsPendingForDay(activeDay)) return;

            bool windowClosed = !spawnWindowOpen;
            if (dayFlow == null) dayFlow = FindFirstObjectByType<DayFlowManager>();
            if (dayFlow != null && dayFlow.State == DayState.Playing)
                windowClosed |= dayFlow.RemainingSeconds - Time.deltaTime
                    <= dayFlow.SpawnStopBeforeEndSeconds;
            if (!windowClosed) return;

            float elapsedSeconds = dayFlow != null
                ? Mathf.Max(0f, dayFlow.DayDurationSeconds - dayFlow.RemainingSeconds) : 0f;
            bool eventWasDue = specialManaRiftSchedule.TryGetDueStage(activeDay, elapsedSeconds, out _);
            specialManaRiftSchedule.MarkMissed(activeDay);
            string reason = specialSpawnWaitLogged
                ? "대기 중이던 빈 이상현상 슬롯을 확보하지 못했습니다."
                : eventWasDue
                    ? "특별 현상이 우선할 다음 일반 스폰 순서 전에 스폰 제한 시간이 끝났습니다."
                    : "설정된 출현 시간이 스폰 제한 시간 뒤에 있습니다.";
            Debug.LogWarning($"AnomalySpawner: DAY {activeDay} 특별 마력 균열 미출현. {reason}", this);
            specialSpawnWaitLogged = false;
        }

        private void LogSpecialSpawnWaiting(string reason)
        {
            if (specialSpawnWaitLogged) return;
            specialSpawnWaitLogged = true;
            Debug.Log($"AnomalySpawner: DAY {activeDay} 특별 마력 균열 출현 조건 충족. "
                + $"기존 슬롯을 비우지 않고 대기합니다: {reason}.", this);
        }

        private AnomalySpawnPoint SelectPoint(List<AnomalySpawnPoint> compatiblePoints,
            List<AnomalySpawnPoint> allPoints)
        {
            if (!randomizePoint && selectedPointIndex >= 0 && selectedPointIndex < allPoints.Count
                && compatiblePoints.Contains(allPoints[selectedPointIndex]))
                return allPoints[selectedPointIndex];
            return compatiblePoints[randomizePoint ? NextRandom(compatiblePoints.Count) : 0];
        }

        private bool TrySpawnPrefab(AnomalyBehaviour prefab, bool manualDebugSpawn = false)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !spawningEnabled || !CanSpawnNow()) return false;
            if (wardStatus != null && wardStatus.HasFailed) return false;
            PruneEndedAnomalies();
            int concurrencyLimit = manualDebugSpawn
                ? Mathf.Max(1, maximumConcurrentAnomalies) : MaximumConcurrentAnomalies;
            if (activeAnomalies.Count >= concurrencyLimit) return false;
            if (!IsValidPrefab(prefab)
                || (!manualDebugSpawn && activeDayProfile != null
                    && activeDayProfile.GetSpawnWeight(prefab) <= 0))
            {
                Debug.LogWarning("AnomalySpawner: 현재 DAY에서 허용되지 않았거나 활성 Project 프리팹이 아닙니다.", this);
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
                ? available[NextRandom(available.Count)]
                : available[0];
        }

        private int NextRandom(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 1) return 0;
            return dayRandom != null ? dayRandom.Next(exclusiveMaximum)
                : UnityEngine.Random.Range(0, exclusiveMaximum);
        }

        private DaySpawnProfile FindDayProfile(int day)
        {
            if (daySpawnProfiles == null) return null;
            foreach (DaySpawnProfile profile in daySpawnProfiles)
                if (profile != null && profile.Day == Mathf.Max(1, day)) return profile;
            return null;
        }

        private static DaySpawnProfile[] CreateDefaultDayProfiles()
        {
            return new[]
            {
                new DaySpawnProfile(1, true, false, false, 1, 8f),
                new DaySpawnProfile(2, true, true, false, 2, 7f),
                new DaySpawnProfile(3, true, true, true, 2, 6f),
                new DaySpawnProfile(4, true, true, true, 2, 5.5f),
                new DaySpawnProfile(5, true, true, true, 2, 5f),
                new DaySpawnProfile(6, true, true, true, 3, 4.5f),
                new DaySpawnProfile(7, true, true, true, 3, 4f)
            };
        }

        private bool TrySpawnAt(AnomalySpawnPoint point, AnomalyBehaviour prefab,
            int initialRiftStage = 1, bool isSpecialManaRift = false)
        {
            if (!CanUse(point, prefab)) return false;
            if (!point.TrySpawn(prefab, initialRiftStage, out AnomalyBehaviour instance)) return false;
            if (instance is ManaDeviceAnomaly spawnedDevice)
                spawnedDevice.ConfigureAttackPattern(activeDayProfile != null
                    && activeDayProfile.AlternateDeviceAttacks);
            activeAnomalies.Add(instance);
            instance.Ended += OnAnomalyEnded;
            if (isSpecialManaRift && instance is ManaRift)
                specialManaRiftSchedule?.MarkSpawned(activeDay);
            AnomalySpawned?.Invoke(instance);
            if (instance is ManaRift rift)
            {
                RiftSpawned?.Invoke(rift);
                if (isSpecialManaRift)
                {
                    SpecialManaRiftSpawned?.Invoke(rift, activeDay);
                }
            }
            Debug.Log($"이상현상 생성: {point.name} ({instance.GetType().Name})", this);
            if (isSpecialManaRift)
                Debug.Log($"특별 마력 균열 생성: DAY {activeDay}, stage {initialRiftStage}", this);
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

        private static SpawnKind GetSpawnKind(AnomalyBehaviour prefab)
        {
            if (prefab is ManaRift) return SpawnKind.ManaRift;
            if (prefab is CurseAnomaly) return SpawnKind.CurseContamination;
            if (prefab is ManaDeviceAnomaly) return SpawnKind.ManaDeviceOverload;
            return SpawnKind.Other;
        }

        private static bool CanUse(AnomalySpawnPoint point, AnomalyBehaviour prefab)
        {
            return point != null && point.CanSpawnPrefab(prefab);
        }

        // Capacity checks retain their original scope: they do not require an unoccupied point.
        private static bool IsCompatibleForCapacity(AnomalySpawnPoint point, AnomalyBehaviour prefab)
        {
            return point != null && point.isActiveAndEnabled
                && prefab.SpawnSurface == point.Surface
                && (!(prefab is CurseAnomaly curse)
                    || curse.CanConfigureFromSpawnPoint(point.GetCursePatchPoints().Length));
        }

        private static List<AnomalySpawnPoint> GetUsableSpawnPoints(
            List<AnomalySpawnPoint> points, AnomalyBehaviour prefab)
        {
            var usablePoints = new List<AnomalySpawnPoint>();
            foreach (AnomalySpawnPoint point in points)
                if (CanUse(point, prefab)) usablePoints.Add(point);
            return usablePoints;
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

        private sealed class SpawnGroup
        {
            public readonly SpawnKind Kind;
            public readonly int Weight;
            public readonly List<SpawnOption> Options = new List<SpawnOption>();
            public SpawnGroup(SpawnKind kind, int weight)
            {
                Kind = kind;
                Weight = weight;
            }
        }

        private readonly struct SpawnOption
        {
            public readonly AnomalyBehaviour Prefab;
            public readonly List<AnomalySpawnPoint> Points;
            public SpawnOption(AnomalyBehaviour prefab, List<AnomalySpawnPoint> points)
            {
                Prefab = prefab;
                Points = points;
            }
        }
    }
}
