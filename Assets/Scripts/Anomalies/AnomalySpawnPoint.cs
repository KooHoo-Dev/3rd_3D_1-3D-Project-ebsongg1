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
        [Tooltip("로컬 +Z 방향으로 표면에서 띄울 거리. 파란 화살표를 방 안쪽으로 향하게 설정.")]
        [SerializeField, Min(0f)] private float surfaceOffset = 0.03f;
        private AnomalyBehaviour occupant;
        private bool spawning;

        public SurfaceType Surface => surface;
        public ZoneIdentity Zone => zoneIdentity;
        public bool IsOccupied => spawning || occupant != null;
        public Vector3 SpawnPosition => transform.position + transform.forward * Mathf.Max(0f, surfaceOffset);

        /// <summary>표면 종류가 일치하는 비어 있는 지점에 이상현상을 생성하고 점유를 예약합니다.</summary>
        public bool TrySpawn(AnomalyBehaviour prefab, out AnomalyBehaviour instance)
        {
            instance = null;
            if (!Application.isPlaying || !isActiveAndEnabled || IsOccupied) return false;
            if (prefab == null || prefab.SpawnSurface != surface || prefab.gameObject.scene.IsValid()
                || !prefab.gameObject.activeSelf || !prefab.enabled)
            {
                Debug.LogWarning("AnomalySpawnPoint: 활성 Project 프리팹이며 Surface 종류가 일치해야 합니다.", this);
                return false;
            }

            spawning = true;
            try
            {
                // 부모의 Scale은 전달하지 않고 월드 위치/회전만 적용합니다.
                instance = Instantiate(prefab, SpawnPosition, transform.rotation);
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

        /// <summary>- 기존 ManaRift 호출부 호환용 오버로드</summary>
        public bool TrySpawn(ManaRift prefab, out ManaRift instance)
        {
            instance = null;
            if (!TrySpawn((AnomalyBehaviour)prefab, out AnomalyBehaviour anomaly)) return false;
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
