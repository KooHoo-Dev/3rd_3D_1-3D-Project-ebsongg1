using System;
using System.Collections.Generic;
using UnityEngine;

namespace WardShift
{
    /// <summary>
    /// - 유형별 생성 표면 제공
    /// - 이상현상 종료까지 생성 지점 점유
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnomalySpawnPoint : MonoBehaviour
    {
        public enum SurfaceType { Wall, Floor, Device }

        [Header("생성 위치")]
        [SerializeField] private SurfaceType surface = SurfaceType.Wall;
        [SerializeField] private ZoneIdentity zoneIdentity;
        [Tooltip("플레이어 HUD에 표시할 구역명. 비워 두면 생성 지점 이름을 사용합니다.")]
        [SerializeField] private string locationName;
        [Tooltip("Device 지점에 고정 배치된 장치 외형. 폭주 로직은 이 외형을 재사용해 진압 후에도 장치가 남습니다.")]
        [SerializeField] private ManaDeviceVisuals deviceVisuals;
        [Tooltip("Wall/Device는 로컬 +Z, Floor는 로컬 +Y 방향으로 표면에서 띄울 거리.")]
        [SerializeField, Min(0f)] private float surfaceOffset = 0.03f;
        private AnomalyBehaviour occupant;
        private bool spawning;
        private bool warnedPatchPointShortage;

        public SurfaceType Surface => surface;
        public ZoneIdentity Zone => zoneIdentity;
        public ManaDeviceVisuals DeviceVisuals => ResolveDeviceVisuals();
        public bool IsOccupied => spawning || occupant != null;
        public Vector3 SpawnPosition => transform.position
            + (surface == SurfaceType.Floor ? transform.up : transform.forward) * Mathf.Max(0f, surfaceOffset);

        /// <summary>직접 자식으로 등록된 저주 패치 위치를 번호 순서로 반환합니다.</summary>
        public CursePatchPoint[] GetCursePatchPoints()
        {
            var points = new List<CursePatchPoint>();
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                CursePatchPoint point = child.GetComponent<CursePatchPoint>();
                if (point != null && point.isActiveAndEnabled && child.gameObject.activeInHierarchy)
                    points.Add(point);
            }

            points.Sort((left, right) =>
            {
                int numberOrder = left.PointNumber.CompareTo(right.PointNumber);
                return numberOrder != 0 ? numberOrder : string.CompareOrdinal(left.name, right.name);
            });
            return points.ToArray();
        }

        /// <summary>표면과 Curse PatchPoint 등록 수를 포함해 이 지점에서 프리팹을 만들 수 있는지 반환합니다.</summary>
        public bool CanSpawnPrefab(AnomalyBehaviour prefab)
        {
            if (prefab == null || prefab.SpawnSurface != surface || !isActiveAndEnabled || IsOccupied)
                return false;
            if (prefab is ManaRift rift) return rift.HasValidSealPoints;
            if (prefab is CurseAnomaly curse)
                return curse.CanConfigureFromSpawnPoint(GetCursePatchPoints().Length);
            if (prefab is ManaDeviceAnomaly)
            {
                ManaDeviceVisuals device = ResolveDeviceVisuals();
                return device != null && device.CanBeginOverload;
            }
            return true;
        }

        /// <summary>PatchPoint 수가 부족한 경우 원인과 필요한 개수를 한 번 기록합니다.</summary>
        public void WarnPatchPointShortage(CurseAnomaly prefab)
        {
            if (prefab == null || surface != SurfaceType.Floor) return;
            int count = GetCursePatchPoints().Length;
            if (count >= prefab.MinimumPatchPointCount)
            {
                warnedPatchPointShortage = false;
                return;
            }
            if (warnedPatchPointShortage) return;
            warnedPatchPointShortage = true;
            Debug.LogWarning($"{name}: 저주 오염 생성을 건너뜁니다. 직접 자식 PatchPoint가 "
                + $"{count}개이며 {prefab.MinimumPatchPointCount}개 이상 필요합니다.", this);
        }

        /// <summary>표면 종류가 일치하는 비어 있는 지점에 이상현상을 생성하고 점유를 예약합니다.</summary>
        public bool TrySpawn(AnomalyBehaviour prefab, out AnomalyBehaviour instance)
        {
            return TrySpawn(prefab, 1, out instance);
        }

        /// <summary>기존 생성 지점을 재사용하고, 필요하면 균열 최초 단계를 활성화 전에 설정합니다.</summary>
        public bool TrySpawn(AnomalyBehaviour prefab, int initialManaRiftStage,
            out AnomalyBehaviour instance)
        {
            instance = null;
            if (!Application.isPlaying || !isActiveAndEnabled || IsOccupied) return false;
            if (prefab == null || prefab.SpawnSurface != surface || prefab.gameObject.scene.IsValid()
                || !prefab.gameObject.activeSelf || !prefab.enabled)
            {
                Debug.LogWarning("AnomalySpawnPoint: 활성 Project 프리팹이며 Surface 종류가 일치해야 합니다.", this);
                return false;
            }

            if (prefab is CurseAnomaly cursePrefab && !CanSpawnPrefab(prefab))
            {
                WarnPatchPointShortage(cursePrefab);
                return false;
            }
            if ((prefab is ManaDeviceAnomaly || prefab is ManaRift) && !CanSpawnPrefab(prefab)) return false;

            spawning = true;
            try
            {
                // 부모의 Scale은 전달하지 않고 월드 위치/회전만 적용합니다.
                if (prefab is ManaRift riftPrefab && initialManaRiftStage > 1)
                {
                    GameObject stagingRoot = new GameObject("ManaRiftSpawnStaging");
                    stagingRoot.SetActive(false);
                    ManaRift stagedRift = Instantiate(riftPrefab, SpawnPosition, transform.rotation,
                        stagingRoot.transform);
                    if (!stagedRift.ConfigureInitialDangerStage(initialManaRiftStage))
                    {
                        Destroy(stagedRift.gameObject);
                        Destroy(stagingRoot);
                        return false;
                    }

                    // 비활성 부모에서 단계/나이를 먼저 설정한 뒤, 씬 루트로 옮겨 OnEnable 초기화를 진행합니다.
                    stagedRift.transform.SetParent(null, true);
                    Destroy(stagingRoot);
                    instance = stagedRift;
                }
                else instance = Instantiate(prefab, SpawnPosition, transform.rotation);
                if (instance is CurseAnomaly curseInstance && !curseInstance.ConfigureFromSpawnPoint(this))
                {
                    Destroy(instance.gameObject);
                    instance = null;
                    return false;
                }
                if (instance is ManaDeviceAnomaly deviceInstance
                    && !deviceInstance.ConfigureFromSpawnPoint(this))
                {
                    Destroy(instance.gameObject);
                    instance = null;
                    return false;
                }
                instance.SetZoneIdentity(zoneIdentity);
                string label = zoneIdentity != null ? zoneIdentity.DisplayName
                    : string.IsNullOrWhiteSpace(locationName) ? name.Replace("SP_", string.Empty) : locationName;
                instance.SetSpawnLocationLabel(label);
                occupant = instance;
                occupant.Ended += OnOccupantEnded;
                return true;
            }
            finally { spawning = false; }
        }

        public void ConfigureZoneIdentity(ZoneIdentity zone)
        {
            zoneIdentity = zone;
        }

        public void ConfigureDeviceVisuals(ManaDeviceVisuals visuals)
        {
            deviceVisuals = visuals;
        }

        private ManaDeviceVisuals ResolveDeviceVisuals()
        {
            if (deviceVisuals != null && deviceVisuals.transform.IsChildOf(transform)) return deviceVisuals;
            deviceVisuals = GetComponentInChildren<ManaDeviceVisuals>(true);
            return deviceVisuals;
        }

        /// <summary>- 기존 ManaRift 호출부 호환용 오버로드</summary>
        public bool TrySpawn(ManaRift prefab, out ManaRift instance)
        {
            instance = null;
            if (!TrySpawn((AnomalyBehaviour)prefab, out AnomalyBehaviour anomaly)) return false;
            instance = anomaly as ManaRift;
            return instance != null;
        }

        public bool TrySpawn(ManaRift prefab, int initialDangerStage, out ManaRift instance)
        {
            instance = null;
            if (!TrySpawn((AnomalyBehaviour)prefab, initialDangerStage, out AnomalyBehaviour anomaly)) return false;
            instance = anomaly as ManaRift;
            return instance != null;
        }

        private void OnOccupantEnded(AnomalyBehaviour anomaly, AnomalyEndReason reason)
        {
            if (occupant != anomaly) return;
            occupant.Ended -= OnOccupantEnded;
            occupant = null;
        }

        private void OnDestroy()
        {
            if (occupant != null) occupant.Ended -= OnOccupantEnded;
            occupant = null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsOccupied ? Color.red : Color.cyan;
            Gizmos.DrawWireSphere(SpawnPosition, 0.12f);
            Gizmos.DrawLine(SpawnPosition, SpawnPosition + transform.forward * 0.6f);
        }
    }
}
