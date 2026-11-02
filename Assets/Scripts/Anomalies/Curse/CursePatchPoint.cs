using UnityEngine;

namespace WardShift
{
    /// <summary>Floor 생성 지점 아래에 두어 저주 오염 패치의 수동 생성 위치를 지정합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class CursePatchPoint : MonoBehaviour
    {
        [SerializeField, Min(1)] private int pointNumber = 1;
        [SerializeField, Min(0.1f)] private float previewRadius = 0.65f;

        /// <summary>Scene 뷰 라벨과 정렬에 사용할 번호입니다.</summary>
        public int PointNumber => Mathf.Max(1, pointNumber);
        /// <summary>Scene 뷰에 표시할 대략적인 오염 반경입니다.</summary>
        public float PreviewRadius => Mathf.Max(0.1f, previewRadius);

        /// <summary>Editor 도구에서 번호를 정리할 때 사용합니다.</summary>
        public void SetPointNumber(int number)
        {
            pointNumber = Mathf.Max(1, number);
        }

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            Vector3 position = transform.position + transform.up * 0.015f;
            Color pointColor = new Color(1f, 0.55f, 0.12f, 0.95f);
            Gizmos.color = pointColor;
            Gizmos.DrawSphere(position, 0.055f);
            Gizmos.DrawLine(position, position + transform.up * 0.12f);

            UnityEditor.Handles.color = pointColor;
            UnityEditor.Handles.DrawWireDisc(position, transform.up, PreviewRadius);
            UnityEditor.Handles.Label(position + transform.up * 0.16f,
                $"PatchPoint_{PointNumber:00}  r≈{PreviewRadius:0.0}m");
#endif
        }
    }
}
