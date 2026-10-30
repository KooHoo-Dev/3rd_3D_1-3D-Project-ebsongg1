using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace WardShift
{
    /// <summary>Floor에서 여러 정화 패치를 생성해 성장, 오염 위험, 진압과 파열을 관리합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class CurseAnomaly : AnomalyBehaviour
    {
        [Header("개별 정화 패치")]
        [SerializeField] private CursePatch[] patches = new CursePatch[0];
        [SerializeField, Range(4, 6)] private int minimumPatchCount = 4;
        [SerializeField, Range(4, 6)] private int maximumPatchCount = 6;
        [Tooltip("첫 패치는 즉시 나타나며, 저주 오염이 남아 있을 때 다음 패치가 나타나는 간격입니다.")]
        [SerializeField, Min(0.1f)] private float patchSpawnIntervalSeconds = 3f;
        [Header("성장 단계별 패치 크기")]
        [Tooltip("원래 프리팹 크기에 대한 단계별 평면 크기 배율입니다. 패치 겹침은 허용됩니다.")]
        [SerializeField, Min(0.1f)] private float stageOnePatchSizeMultiplier = 1f;
        [Tooltip("2단계의 원래 크기 대비 평면 배율입니다.")]
        [SerializeField, Min(0.1f)] private float stageTwoPatchSizeMultiplier = 1.6f;
        [Tooltip("3단계의 원래 크기 대비 평면 배율입니다.")]
        [SerializeField, Min(0.1f)] private float stageThreePatchSizeMultiplier = 2.3f;
        [Tooltip("성장 단계가 바뀔 때 기존 패치의 평면 크기가 새 배율로 확장되는 시간입니다.")]
        [SerializeField, Min(0f)] private float stageSizeTransitionSeconds = 0.65f;
        [Header("정화 및 피해 범위")]
        [Tooltip("CursePatch의 정화 조준 반경에 적용하는 배율입니다. 시각 크기 성장과 함께 적용됩니다.")]
        [SerializeField, Min(0.1f)] private float purificationAreaMultiplier = 1f;
        [Tooltip("CursePatch의 플레이어 오염 판정 반경에 적용하는 배율입니다. 피해량 자체에는 영향을 주지 않습니다.")]
        [SerializeField, Min(0.1f)] private float damageAreaMultiplier = 1f;
        [Header("저주 이상현상 VFX (선택)")]
        [Tooltip("저주 이상현상이 처음 발생할 때 한 번 재생합니다.")]
        [SerializeField] private GameObject spawnVfxPrefab;
        [Tooltip("현재 생성된 패치를 모두 정상 정화해 진압했을 때 한 번 재생합니다.")]
        [SerializeField] private GameObject resolvedVfxPrefab;
        [Tooltip("저주 오염이 최종 단계에서 파열될 때 한 번 재생합니다.")]
        [SerializeField] private GameObject ruptureVfxPrefab;
        [SerializeField, Min(0.1f)] private float anomalyVfxLifetime = 2f;
        [Header("전체 정화 완료 효과")]
        [SerializeField, Min(0.1f)] private float resolvedVfxLifetime = 1.5f;
        [SerializeField, Min(0.1f)] private float resolvedVfxSizeMultiplier = 1.2f;
        [Tooltip("완료 프리팹이 비어 있을 때 사용할 청록색 정화 파동 크기입니다.")]
        [SerializeField, Min(1)] private int resolvedFallbackParticleCount = 28;
        [SerializeField, Min(0.005f)] private float resolvedFallbackParticleSize = 0.055f;
        [SerializeField, Min(0.1f)] private float resolvedFallbackParticleLifetime = 0.95f;
        [SerializeField, Min(0f)] private float resolvedFallbackParticleSpeed = 1.8f;
        [Tooltip("마지막 패치까지 모두 정화되어 진압에 성공했을 때 한 번 재생합니다.")]
        [SerializeField] private AudioClip resolvedSfxClip;
        [SerializeField, Range(0f, 1f)] private float resolvedSfxVolume = 0.55f;
        [Header("바닥 위치 자동 맞춤")]
        [SerializeField, Min(0.1f)] private float floorSnapProbeHeight = 2f;
        [SerializeField, Min(0.1f)] private float floorSnapProbeDistance = 4f;
        [SerializeField, Min(0f)] private float maximumFloorHeightCorrection = 1f;
        [SerializeField, Range(0f, 1f)] private float minimumFloorNormalY = 0.7f;
        [Header("성장·파열 (생성 후 누적 초)")]
        [SerializeField, Min(0.1f)] private float stageTwoAt = 20f;
        [SerializeField, Min(0.2f)] private float stageThreeAt = 40f;
        [SerializeField, Min(0.3f)] private float ruptureAt = 60f;
        [Header("시설 안정도 손실")]
        [SerializeField, Min(0f)] private float stageTwoStabilityLossPerSecond = 0.5f;
        [SerializeField, Min(0f)] private float stageThreeStabilityLossPerSecond = 1f;
        [SerializeField, Min(0f)] private float ruptureStabilityDamage = 20f;
        [Header("패치 범위 내 플레이어 오염도")]
        [SerializeField, Min(0f)] private float stageOneContamination = 1f;
        [SerializeField, Min(0f)] private float stageTwoContamination = 3f;
        [SerializeField, Min(0f)] private float stageThreeContamination = 6f;
        [Header("진압 완료")]
        [SerializeField] private UnityEvent onResolved = new UnityEvent();
        [Header("위험 상태 (실행 중 확인용)")]
        [SerializeField] private float age;
        [SerializeField] private int dangerStage = 1;
        [SerializeField] private int activePatchCount;
        [SerializeField] private bool hasRuptured;
        [SerializeField] private bool isResolved;

        private bool[] registeredPatches;
        private bool hasSpawnPointConfiguration;
        private int selectedPatchCount;
        private int spawnedPatchCount;
        private float nextPatchSpawnAge;
        private bool hasPlacedPatches;
        private WardStatus subscribedWardStatus;

        /// <summary>이상현상 생성 후 흐른 시간입니다.</summary>
        public float Age => age;
        /// <summary>성장 단계와 같은 수의 마력 결정을 진압 보상으로 지급합니다.</summary>
        public override int DangerStage => dangerStage;
        public override float SecondsUntilRupture
        {
            get
            {
                float second = Mathf.Max(0.1f, stageTwoAt);
                float third = Mathf.Max(second + 0.1f, stageThreeAt);
                float rupture = Mathf.Max(third + 0.1f, ruptureAt);
                return Mathf.Max(0f, rupture - age);
            }
        }
        /// <summary>파열 후 공통 시설 피해 처리가 이미 끝났는지 반환합니다.</summary>
        public override bool HasOverloaded => hasRuptured;
        /// <summary>정상 진압 시 지급할 마력 결정 수입니다.</summary>
        public override int ResolutionReward => dangerStage;
        /// <summary>시설 지도와 상태 HUD에 표시할 이름입니다.</summary>
        public override string DisplayName => "저주 오염";
        /// <summary>Floor 앵커에서만 생성할 수 있습니다.</summary>
        public override AnomalySpawnPoint.SurfaceType SpawnSurface => AnomalySpawnPoint.SurfaceType.Floor;
        /// <summary>진압 완료 또는 파열 전까지 공통 이상현상 위험으로 집계합니다.</summary>
        public override bool IsHazardActive => isActiveAndEnabled && hasSpawnPointConfiguration
            && !isResolved && !hasRuptured && !HasEnded;
        /// <summary>현재 활성 패치 수입니다.</summary>
        public int ActivePatchCount => activePatchCount;
        /// <summary>최대 확산 시 생성될 패치 수입니다. 현재 생성된 패치를 모두 정화하면 조기 진압할 수 있습니다.</summary>
        public int SelectedPatchCount => selectedPatchCount;
        /// <summary>이 이상현상에 등록해야 하는 최소 PatchPoint 개수입니다.</summary>
        public int MinimumPatchPointCount => Mathf.Clamp(minimumPatchCount, 4, 6);
        /// <summary>패치 하나를 정화할 때 카메라 중앙에 조준할 수 있는 대상들입니다.</summary>
        public CursePatch[] Patches => patches;
        /// <summary>다음 성장 단계 또는 파열까지 남은 시간입니다.</summary>
        public override float SecondsUntilNextStage
        {
            get
            {
                float second = Mathf.Max(0.1f, stageTwoAt);
                float third = Mathf.Max(second + 0.1f, stageThreeAt);
                float rupture = Mathf.Max(third + 0.1f, ruptureAt);
                float target = dangerStage >= 3 ? rupture : dangerStage == 2 ? third : second;
                return Mathf.Max(0f, target - age);
            }
        }
        /// <summary>현재 HUD에서 사용할 다음 위험 시점 문구입니다.</summary>
        public string NextStageLabel => dangerStage >= 3 ? "파열까지" : $"{dangerStage + 1}단계까지";

        /// <summary>등록 위치와 프리팹 템플릿이 최소 정화 패치 수를 지원하는지 확인합니다.</summary>
        public bool CanConfigureFromSpawnPoint(int patchPointCount)
        {
            int templateCount = 0;
            if (patches != null)
                foreach (CursePatch patch in patches)
                    if (patch != null) templateCount++;
            return patchPointCount >= MinimumPatchPointCount && templateCount >= MinimumPatchPointCount;
        }

        private void Awake()
        {
            if (!Application.isPlaying) return;
            if (patches == null) patches = new CursePatch[0];
            registeredPatches = new bool[patches.Length];
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (Application.isPlaying)
            {
                subscribedWardStatus = FindFirstObjectByType<WardStatus>();
                if (subscribedWardStatus != null)
                {
                    subscribedWardStatus.Failed -= OnDayFailed;
                    subscribedWardStatus.Failed += OnDayFailed;
                    if (subscribedWardStatus.HasFailed) StopPatchEffects();
                }
            }
            if (patches == null) return;
            foreach (CursePatch patch in patches)
            {
                if (patch == null) continue;
                patch.Purified -= OnPatchPurified;
                patch.Purified += OnPatchPurified;
                if (Application.isPlaying) patch.SetStageAvailability(false, dangerStage);
            }
            activePatchCount = 0;
        }

        private void Start()
        {
            if (!Application.isPlaying) return;
            if (!hasSpawnPointConfiguration)
            {
                Debug.LogError($"{name}: Floor AnomalySpawnPoint 아래에 PatchPoint를 4개 이상 등록해야 합니다.", this);
                Destroy(gameObject);
                return;
            }
            spawnedPatchCount = Mathf.Min(1, selectedPatchCount);
            nextPatchSpawnAge = Mathf.Max(0.1f, patchSpawnIntervalSeconds);
            hasPlacedPatches = true;
            RefreshPatchAvailability();
            SpawnAnomalyVfx(spawnVfxPrefab);
        }

        /// <summary>Floor 생성 지점의 자식 PatchPoint에 패치 템플릿을 배치합니다.</summary>
        public bool ConfigureFromSpawnPoint(AnomalySpawnPoint spawnPoint)
        {
            if (spawnPoint == null || patches == null) return false;

            CursePatchPoint[] registeredPoints = spawnPoint.GetCursePatchPoints();
            var validPatches = new List<CursePatch>(patches.Length);
            foreach (CursePatch patch in patches)
                if (patch != null) validPatches.Add(patch);

            int minimum = MinimumPatchPointCount;
            int maximum = Mathf.Clamp(maximumPatchCount, minimum, 6);
            maximum = Mathf.Min(maximum, registeredPoints.Length, validPatches.Count);
            if (registeredPoints.Length < minimum || validPatches.Count < minimum || maximum < minimum)
            {
                Debug.LogWarning($"{spawnPoint.name}: 저주 오염을 생성하려면 PatchPoint와 패치 템플릿이 "
                    + $"각각 {minimum}개 이상 필요합니다 (위치 {registeredPoints.Length}, 템플릿 {validPatches.Count}).", spawnPoint);
                return false;
            }

            // 후보 위치만 섞습니다. 각 지점의 X/Z를 유지하고 바닥 콜라이더 높이에 패치를 자동으로 맞춥니다.
            var shuffledPoints = new List<CursePatchPoint>(registeredPoints);
            for (int i = shuffledPoints.Count - 1; i > 0; i--)
            {
                int swapIndex = UnityEngine.Random.Range(0, i + 1);
                (shuffledPoints[i], shuffledPoints[swapIndex]) = (shuffledPoints[swapIndex], shuffledPoints[i]);
            }

            patches = validPatches.ToArray();
            selectedPatchCount = UnityEngine.Random.Range(minimum, maximum + 1);
            selectedPatchCount = Mathf.Min(selectedPatchCount, patches.Length);
            registeredPatches = new bool[patches.Length];
            Vector3 unusedTemplatePosition = spawnPoint.SpawnPosition;
            Quaternion unusedTemplateRotation = spawnPoint.transform.rotation;
            int floorSnapMisses = 0;
            for (int i = 0; i < patches.Length; i++)
            {
                CursePatch patch = patches[i];
                patch.gameObject.SetActive(false);
                patch.transform.SetPositionAndRotation(unusedTemplatePosition, unusedTemplateRotation);
                if (i >= selectedPatchCount) continue;

                CursePatchPoint patchPoint = shuffledPoints[i];
                if (TryGetSnappedFloorPose(patchPoint, patch.SurfaceOffset,
                        out Vector3 position, out Quaternion rotation))
                {
                    patch.transform.SetPositionAndRotation(position, rotation);
                }
                else
                {
                    floorSnapMisses++;
                    Vector3 fallbackPosition = patchPoint.transform.position
                        + patchPoint.transform.up * patch.SurfaceOffset;
                    patch.transform.SetPositionAndRotation(fallbackPosition, patchPoint.transform.rotation);
                }
                registeredPatches[i] = true;
            }

            if (floorSnapMisses > 0)
                Debug.LogWarning($"{spawnPoint.name}: {floorSnapMisses}개 저주 패치 지점에서 가까운 바닥 콜라이더를 찾지 못했습니다. "
                    + "PatchPoint 높이와 바닥 콜라이더를 확인하세요.", spawnPoint);

            hasSpawnPointConfiguration = true;
            return true;
        }

        private bool TryGetSnappedFloorPose(CursePatchPoint patchPoint, float surfaceOffset,
            out Vector3 position, out Quaternion rotation)
        {
            Vector3 pointPosition = patchPoint.transform.position;
            Vector3 origin = pointPosition + Vector3.up * Mathf.Max(0.1f, floorSnapProbeHeight);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down,
                Mathf.Max(0.1f, floorSnapProbeDistance), Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            bool foundFloor = false;
            float nearestDistance = float.PositiveInfinity;
            RaycastHit floorHit = default;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || hit.normal.y < minimumFloorNormalY
                    || hit.collider.GetComponentInParent<CursePatch>() != null
                    || hit.collider.GetComponentInParent<AnomalyBehaviour>() != null
                    || Mathf.Abs(hit.point.y - pointPosition.y) > maximumFloorHeightCorrection)
                    continue;
                if (hit.distance >= nearestDistance) continue;
                nearestDistance = hit.distance;
                floorHit = hit;
                foundFloor = true;
            }

            if (!foundFloor)
            {
                position = pointPosition;
                rotation = patchPoint.transform.rotation;
                return false;
            }

            position = floorHit.point + floorHit.normal * Mathf.Max(0f, surfaceOffset);
            rotation = Quaternion.FromToRotation(Vector3.up, floorHit.normal) * patchPoint.transform.rotation;
            return true;
        }

        /// <summary>성장 경계를 넘은 시간을 적분하고 파열 시 공통 시설 피해량을 반환합니다.</summary>
        public override float AdvanceHazard(float deltaTime)
        {
            float persistentLoss = AdvanceHazard(deltaTime, out float immediateDamage);
            return persistentLoss + immediateDamage;
        }

        public override float AdvanceHazard(float deltaTime, out float immediateStabilityDamage)
        {
            immediateStabilityDamage = 0f;
            if (!IsHazardActive || deltaTime <= 0f) return 0f;
            float previousAge = Mathf.Max(0f, age);
            float second = Mathf.Max(0.1f, stageTwoAt);
            float third = Mathf.Max(second + 0.1f, stageThreeAt);
            float rupture = Mathf.Max(third + 0.1f, ruptureAt);
            float nextAge = previousAge + deltaTime;
            bool ruptureReached = nextAge >= rupture;
            float currentAge = Mathf.Min(nextAge, rupture);
            age = currentAge;
            int nextStage = currentAge >= third ? 3 : currentAge >= second ? 2 : 1;
            bool availabilityChanged = nextStage != dangerStage;
            dangerStage = nextStage;
            availabilityChanged |= AdvancePatchSpawns(currentAge);
            if (hasPlacedPatches && availabilityChanged) RefreshPatchAvailability();

            float stageTwoSeconds = Mathf.Max(0f, Mathf.Min(currentAge, third) - Mathf.Max(previousAge, second));
            float stageThreeSeconds = Mathf.Max(0f, Mathf.Min(currentAge, rupture) - Mathf.Max(previousAge, third));
            float stabilityLoss = stageTwoSeconds * Mathf.Max(0f, stageTwoStabilityLossPerSecond)
                + stageThreeSeconds * Mathf.Max(0f, stageThreeStabilityLossPerSecond);

            if (!ruptureReached) return stabilityLoss;
            hasRuptured = true;
            immediateStabilityDamage = Mathf.Max(0f, ruptureStabilityDamage);
            Debug.Log("저주 오염 파열: 시설 안정도 피해 후 오염 제거", this);
            SpawnAnomalyVfx(ruptureVfxPrefab);
            NotifyRuptured();
            Destroy(gameObject);
            return stabilityLoss;
        }

        /// <summary>활성화된 패치들의 시각 범위 안에 플레이어가 있으면 단계별 오염량을 반환합니다.</summary>
        public override float ContaminationRateAt(Vector3 position)
        {
            if (!IsHazardActive || patches == null) return 0f;
            bool insidePatch = false;
            foreach (CursePatch patch in patches)
                if (patch != null && patch.ContainsDangerPosition(position)) { insidePatch = true; break; }
            if (!insidePatch) return 0f;
            return dangerStage >= 3 ? Mathf.Max(0f, stageThreeContamination)
                : dangerStage == 2 ? Mathf.Max(0f, stageTwoContamination)
                : Mathf.Max(0f, stageOneContamination);
        }

        /// <summary>한 번 정화된 패치가 성장 단계 갱신 후에도 돌아오지 않는지 확인합니다.</summary>
        public bool IsPatchPurified(int index)
        {
            return patches != null && index >= 0 && index < patches.Length
                && patches[index] != null && patches[index].IsPurified;
        }

        private void RefreshPatchAvailability()
        {
            if (patches == null) return;
            activePatchCount = 0;
            for (int i = 0; i < patches.Length; i++)
            {
                CursePatch patch = patches[i];
                if (patch == null) continue;
                bool validPoint = registeredPatches != null && i < registeredPatches.Length && registeredPatches[i];
                bool activate = hasPlacedPatches && validPoint && i < spawnedPatchCount;
                patch.SetGrowthProfile(dangerStage, GetPatchSizeMultiplier(dangerStage),
                    stageSizeTransitionSeconds, purificationAreaMultiplier, damageAreaMultiplier);
                patch.SetStageAvailability(activate, dangerStage);
                if (patch.IsHazardActive) activePatchCount++;
            }
        }

        private float GetPatchSizeMultiplier(int stage)
        {
            return stage >= 3 ? Mathf.Max(0.1f, stageThreePatchSizeMultiplier)
                : stage == 2 ? Mathf.Max(0.1f, stageTwoPatchSizeMultiplier)
                : Mathf.Max(0.1f, stageOnePatchSizeMultiplier);
        }

        private bool AdvancePatchSpawns(float currentAge)
        {
            bool spawnedAny = false;
            float interval = Mathf.Max(0.1f, patchSpawnIntervalSeconds);
            while (spawnedPatchCount < selectedPatchCount && currentAge >= nextPatchSpawnAge)
            {
                spawnedPatchCount++;
                nextPatchSpawnAge += interval;
                spawnedAny = true;
            }
            return spawnedAny;
        }

        private void OnPatchPurified(CursePatch patch)
        {
            if (isResolved || hasRuptured || HasEnded) return;
            RefreshPatchAvailability();
            if (!AllSpawnedPatchesPurified()) return;
            isResolved = true;
            Debug.Log($"저주 오염 진압 완료 ({spawnedPatchCount}개 패치)", this);
            SpawnResolvedEffect();
            try
            {
                onResolved.Invoke();
                NotifyResolved();
            }
            finally
            {
                if (this != null) Destroy(gameObject);
            }
        }

        private bool AllSpawnedPatchesPurified()
        {
            // 의도된 조기 진압: 아직 예약된 패치가 있어도 현재 생성된 패치가 모두 정화되면 종료합니다.
            // OnPatchPurified가 isResolved를 먼저 세우므로 이후 확산은 멈추고 보상은 한 번만 발생합니다.
            if (patches == null || spawnedPatchCount <= 0) return false;
            for (int i = 0; i < spawnedPatchCount; i++)
                if (patches[i] == null || !patches[i].IsPurified) return false;
            return true;
        }

        protected override void OnDisable()
        {
            if (subscribedWardStatus != null)
                subscribedWardStatus.Failed -= OnDayFailed;
            subscribedWardStatus = null;
            if (patches != null)
                foreach (CursePatch patch in patches)
                    if (patch != null) patch.Purified -= OnPatchPurified;
            base.OnDisable();
        }

        private void OnDayFailed(string reason)
        {
            StopPatchEffects();
        }

        private void StopPatchEffects()
        {
            if (patches == null) return;
            foreach (CursePatch patch in patches)
                if (patch != null) patch.StopEffectsForDayEnd();
        }

        private void SpawnAnomalyVfx(GameObject prefab)
        {
            if (prefab == null) return;
            MagicVfxUtility.SpawnDetached(prefab, transform.position, transform.rotation, anomalyVfxLifetime);
        }

        private void SpawnResolvedEffect()
        {
            if (resolvedVfxPrefab != null)
            {
                GameObject effect = MagicVfxUtility.SpawnDetached(resolvedVfxPrefab,
                    transform.position, transform.rotation, resolvedVfxLifetime, true);
                if (effect != null)
                {
                    effect.transform.localScale *= Mathf.Max(0.1f, resolvedVfxSizeMultiplier);
                    MagicVfxUtility.UseUnscaledParticleTime(effect);
                }
            }
            else
            {
                MagicVfxUtility.SpawnParticleBurst(transform.position, transform.rotation,
                    new Color(0.25f, 0.95f, 0.88f, 1f), resolvedFallbackParticleCount,
                    resolvedFallbackParticleSize, resolvedFallbackParticleLifetime,
                    resolvedFallbackParticleSpeed, 0.08f);
            }

            MagicVfxUtility.PlayDetachedSound(resolvedSfxClip, transform.position, resolvedSfxVolume);
        }

        private void OnDrawGizmosSelected()
        {
            if (patches == null) return;
            Gizmos.color = dangerStage >= 3 ? new Color(1f, 0.2f, 0.3f, 0.8f)
                : dangerStage == 2 ? new Color(0.7f, 0.3f, 1f, 0.8f)
                : new Color(0.4f, 0.2f, 0.8f, 0.8f);
            foreach (CursePatch patch in patches)
            {
                if (patch == null || (!Application.isPlaying && !patch.gameObject.activeSelf)
                    || (Application.isPlaying && !patch.IsHazardActive)) continue;
                Vector3 position = patch.transform.position;
                position.y += 0.025f;
                Gizmos.DrawWireSphere(position, patch.DangerRadius);
            }
        }
    }
}
