using UnityEngine;

namespace WardShift
{
    /// <summary>DAY 전환에 필요한 씬 오브젝트 정리와 플레이어 배치를 수행한다.</summary>
    [DisallowMultipleComponent]
    public sealed class DaySceneController : MonoBehaviour
    {
        private AnomalySpawner spawner;
        private Transform playerRoot;
        private Transform startPoint;

        public void Configure(AnomalySpawner anomalySpawner, Transform player, Transform start)
        {
            spawner = anomalySpawner;
            playerRoot = player;
            startPoint = start;
        }

        public void ClearAllAnomalies()
        {
            foreach (AnomalyBehaviour anomaly in FindObjectsByType<AnomalyBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (anomaly != null) Destroy(anomaly.gameObject);
            if (spawner != null) spawner.ClearActiveAnomalies();
        }

        public void ResetManaDevices()
        {
            foreach (ManaDeviceVisuals device in FindObjectsByType<ManaDeviceVisuals>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (device != null) device.ResetForNewDay();
        }

        public void TeleportPlayerToStart()
        {
            if (playerRoot == null || startPoint == null) return;
            CharacterController controller = playerRoot.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled) controller.enabled = false;
            playerRoot.SetPositionAndRotation(startPoint.position, startPoint.rotation);
            if (wasEnabled) controller.enabled = true;
        }
    }
}
