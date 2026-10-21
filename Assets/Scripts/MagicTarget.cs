using UnityEngine;

namespace WardShift
{
    /// <summary>마법 대상의 설정과 감지 강조를 관리. 시전과 완료 처리는 다음 단계에 추가.</summary>
    [DisallowMultipleComponent]
    public sealed class MagicTarget : MonoBehaviour
    {
        [Header("마법 대상 설정")]
        [Tooltip("화면과 로그에 표시할 대상 이름.")]
        [SerializeField] private string displayName = "테스트 봉인점";
        [SerializeField] private MagicType requiredMagic = MagicType.Seal;
        [SerializeField, Min(0.1f)] private float castDuration = 2f;

        public string DisplayName => displayName;
        public MagicType RequiredMagic => requiredMagic;
        public float CastDuration => castDuration;
        public bool IsAvailable => isActiveAndEnabled;
        public bool IsDetected { get; private set; }

        private Renderer[] highlightedRenderers;
        private MaterialPropertyBlock[] originalBlocks;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        // 공유 Material을 수정하지 않고 이 대상의 Renderer에만 색상을 덮어씀.
        public void SetDetected(bool detected, Color color)
        {
            detected &= IsAvailable;
            if (IsDetected == detected) return;
            IsDetected = detected;
            if (!detected)
            {
                RestoreAppearance();
                return;
            }

            highlightedRenderers = GetComponentsInChildren<Renderer>();
            originalBlocks = new MaterialPropertyBlock[highlightedRenderers.Length];
            for (int i = 0; i < highlightedRenderers.Length; i++)
            {
                var renderer = highlightedRenderers[i];
                originalBlocks[i] = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(originalBlocks[i]);
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                block.SetColor(LegacyColor, color);
                // 발광 기능이 활성화된 재질에서는 발광색도 변경합니다.
                block.SetColor(EmissionColor, color * 2f);
                renderer.SetPropertyBlock(block);
            }
        }

        private void RestoreAppearance()
        {
            if (highlightedRenderers == null) return;
            for (int i = 0; i < highlightedRenderers.Length; i++)
                if (highlightedRenderers[i] != null)
                    highlightedRenderers[i].SetPropertyBlock(originalBlocks[i]);
            highlightedRenderers = null;
            originalBlocks = null;
        }

        private void OnDisable()
        {
            IsDetected = false;
            RestoreAppearance();
        }
    }
}
