using System;
using UnityEngine;
using UnityEngine.Events;

namespace WardShift
{
    /// <summary>대상 설정, 감지 강조, 시전 진행도와 한 번만 발생하는 완료를 관리.</summary>
    [DisallowMultipleComponent]
    public sealed class MagicTarget : MonoBehaviour
    {
        [Header("마법 대상 설정")]
        [Tooltip("화면과 로그에 표시할 대상 이름.")]
        [SerializeField] private string displayName = "테스트 봉인점";
        [SerializeField] private MagicType requiredMagic = MagicType.Seal;
        [SerializeField, Min(0.1f)] private float castDuration = 2f;
        [Header("타이밍 판정")]
        [Tooltip("켜면 버튼을 놓을 때 판정. 끄면 기존처럼 100%에서 자동 성공.")]
        [SerializeField] private bool useTimingCast = true;
        [SerializeField, Range(0f, 1f)] private float stableStart = 0.6f;
        [SerializeField, Range(0f, 0.99f)] private float stableEnd = 0.85f;
        [Tooltip("완료 후 이 오브젝트와 자식을 비활성화. 현재 테스트 구체에 사용.")]
        [SerializeField] private bool deactivateOnComplete = true;
        [Tooltip("봉인점 완료 후 균열 처리나 효과를 연결할 수 있는 이벤트.")]
        [SerializeField] private UnityEvent onCompleted = new UnityEvent();

        [Header("시전 상태 (실행 중 확인용)")]
        [SerializeField, Range(0f, 1f)] private float progress;
        [SerializeField] private bool isCompleted;

        public string DisplayName => displayName;
        public MagicType RequiredMagic => requiredMagic;
        public float CastDuration => castDuration;
        public bool UseTimingCast => useTimingCast;
        public float StableStart => Mathf.Clamp(stableStart, 0f, StableEnd);
        public float StableEnd => Mathf.Clamp(stableEnd, 0f, 0.99f);
        public MagicCastResult LastResult { get; private set; }
        public bool IsAvailable => isActiveAndEnabled && !isCompleted;
        public bool IsDetected { get; private set; }
        public float Progress => progress;
        public bool IsCompleted => isCompleted;
        public bool IsCasting { get; private set; }
        public event Action CastStarted;
        public event Action CastStopped;
        public event Action<MagicTarget> Completed;
        /// <summary>성공, 실패 또는 취소가 확정될 때 한 번 발생.</summary>
        public event Action<MagicTarget, MagicCastResult> CastResolved;

        // 시전자는 입력과 조준을 검사하고, 대상은 마법 종류와 진행도 및 완료를 관리.
        /// <summary>충전을 진행하고 자동 성공인 경우 true를 반환.</summary>
        public bool AdvanceCast(MagicType magic, float deltaTime)
        {
            if (!IsAvailable || magic != requiredMagic || deltaTime <= 0f) return false;
            if (!IsCasting)
            {
                IsCasting = true;
                LastResult = MagicCastResult.None;
                CastStarted?.Invoke();
            }
            if (!IsAvailable || !IsCasting) return false;
            progress = Mathf.Clamp01(progress + deltaTime / Mathf.Max(0.1f, castDuration));
            if (progress < 1f) return false;
            return Resolve(useTimingCast ? MagicCastResult.Overcharged : MagicCastResult.Success) == MagicCastResult.Success;
        }

        /// <summary>버튼을 놓은 시점의 충전량으로 결과를 판정.</summary>
        public MagicCastResult ReleaseCast()
        {
            if (!IsCasting || !IsAvailable) return MagicCastResult.None;
            if (!useTimingCast) return Resolve(MagicCastResult.Canceled);
            return Resolve(progress < StableStart ? MagicCastResult.Undercharged
                : progress > StableEnd ? MagicCastResult.Overcharged : MagicCastResult.Success);
        }

        private MagicCastResult Resolve(MagicCastResult result)
        {
            if (!IsCasting) return MagicCastResult.None;
            LastResult = result;
            if (result != MagicCastResult.Success)
            {
                progress = 0f;
                StopCasting();
                if (result == MagicCastResult.Undercharged || result == MagicCastResult.Overcharged)
                    Debug.Log($"{displayName}: {result}", this);
                CastResolved?.Invoke(this, result);
                return result;
            }
            // 이벤트나 오브젝트 비활성화보다 먼저 완료를 기록하여 중복 처리를 막음.
            isCompleted = true;
            StopCasting();
            SetDetected(false, Color.white);
            // 완료 효과가 위치를 읽고 생성될 수 있도록 비활성화 전에 이벤트를 전달.
            Completed?.Invoke(this);
            onCompleted.Invoke();
            CastResolved?.Invoke(this, result);
            if (this != null && deactivateOnComplete) gameObject.SetActive(false);
            return result;
        }

        /// <summary>진행 중인 시전을 취소하고 반복 효과를 중단.</summary>
        public void CancelCast()
        {
            if (IsCasting) Resolve(MagicCastResult.Canceled);
            if (!isCompleted) progress = 0f;
            StopCasting();
        }

        private void StopCasting()
        {
            if (!IsCasting) return;
            IsCasting = false;
            CastStopped?.Invoke();
        }

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

            // 감지 색상이 시전 파티클의 보라색을 덮어쓰지 않도록 파티클은 제외.
            highlightedRenderers = Array.FindAll(GetComponentsInChildren<Renderer>(),
                renderer => !(renderer is ParticleSystemRenderer));
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
            CancelCast();
            IsDetected = false;
            RestoreAppearance();
        }
    }
}
