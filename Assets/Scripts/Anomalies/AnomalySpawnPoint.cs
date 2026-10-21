using UnityEngine;

namespace WardShift
{
    /// <summary>생성 지점의 표면 종류와 점유 상태를 관리. 이름이나 방 종류와 무관.</summary>
    [DisallowMultipleComponent]
    public sealed class AnomalySpawnPoint : MonoBehaviour
    {
        public enum SurfaceType { Wall, Floor, Device }

        [Header("생성 위치")]
        [SerializeField] private SurfaceType surface = SurfaceType.Wall;
        [Tooltip("로컬 +Z 방향으로 벽에서 띄울 거리. 파란 화살표를 방 안쪽으로 향하게 설정.")]
        [SerializeField, Min(0f)] private float surfaceOffset = 0.03f;
        private GameObject occupant;
        private bool spawning;

        public SurfaceType Surface => surface;
        public bool IsOccupied => spawning || occupant != null;
        public Vector3 SpawnPosition => transform.position + transform.forward * Mathf.Max(0f, surfaceOffset);

        /// <summary>비어 있는 벽 지점에 균열 하나 생성. 비활성화된 균열도 삭제 전까지 점유 유지.</summary>
        public bool TrySpawn(ManaRift prefab, out ManaRift instance)
        {
            instance = null;
            if (!Application.isPlaying || !isActiveAndEnabled || surface != SurfaceType.Wall || IsOccupied)
                return false;
            if (prefab == null || prefab.gameObject.scene.IsValid() || !prefab.gameObject.activeSelf || !prefab.enabled)
            {
                Debug.LogWarning("AnomalySpawnPoint: Project 창의 활성 ManaRift 프리팹 루트를 연결하세요.", this);
                return false;
            }
            spawning = true;
            try
            {
                // 부모의 Scale을 물려받지 않고 월드 위치/회전만 적용.
                instance = Instantiate(prefab, SpawnPosition, transform.rotation);
                occupant = instance.gameObject;
                return true;
            }
            finally { spawning = false; }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsOccupied ? Color.red : Color.cyan;
            Gizmos.DrawWireSphere(SpawnPosition, 0.12f);
            Gizmos.DrawLine(SpawnPosition, SpawnPosition + transform.forward * 0.6f);
        }
    }
}
