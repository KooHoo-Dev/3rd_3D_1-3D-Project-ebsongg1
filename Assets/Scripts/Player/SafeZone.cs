using UnityEngine;

namespace WardShift
{
    /// <summary>트리거 콜백 대신 플레이어 기준점이 박스 안에 있는지 검사.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class SafeZone : MonoBehaviour
    {
        private BoxCollider area;
        private void Awake() => area = GetComponent<BoxCollider>();
        private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

        public bool Contains(Vector3 position)
        {
            if (!isActiveAndEnabled || area == null || !area.enabled || !area.isTrigger) return false;
            Vector3 local = transform.InverseTransformPoint(position) - area.center;
            Vector3 half = area.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        private void OnDrawGizmosSelected()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null) return;
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = previous;
        }
    }
}
