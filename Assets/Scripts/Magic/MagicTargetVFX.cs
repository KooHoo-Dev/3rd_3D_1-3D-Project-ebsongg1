using UnityEngine;

namespace WardShift
{
    /// <summary>대상의 시전 이벤트에 따라 반복 효과와 완료 효과를 재생.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MagicTarget))]
    public sealed class MagicTargetVFX : MonoBehaviour
    {
        [Header("대상과 생성 위치")]
        [SerializeField] private MagicTarget target;
        [Tooltip("대상 자식의 CastVFXAnchor를 연결. 이 위치와 방향에 효과가 생성.")]
        [SerializeField] private Transform castVFXAnchor;

        [Header("효과 프리팹")]
        [SerializeField] private GameObject castLoopPrefab;
        [SerializeField] private GameObject castCompletePrefab;
        [Tooltip("완료 효과는 대상과 독립적으로 재생된 뒤 지정한 시간에 삭제.")]
        [SerializeField, Min(0.1f)] private float completeLifetime = 1.5f;

        private GameObject loopInstance;
        public bool IsLoopPlaying => loopInstance != null && loopInstance.activeSelf;

        private void Awake()
        {
            if (target == null) target = GetComponent<MagicTarget>();
            if (castVFXAnchor == null) castVFXAnchor = transform;
        }

        private void OnEnable()
        {
            target.CastStarted += StartLoop;
            target.CastStopped += StopLoop;
            target.Completed += PlayComplete;
            if (target.IsCasting) StartLoop();
        }

        private void StartLoop()
        {
            if (loopInstance != null || castLoopPrefab == null) return;
            loopInstance = Instantiate(castLoopPrefab, castVFXAnchor, false);
            PlayParticles(loopInstance);
        }

        private void StopLoop()
        {
            if (loopInstance == null) return;
            // 잔여 파티클도 즉시 지워 취소가 화면에 바로 드러나도록 함.
            foreach (ParticleSystem particles in loopInstance.GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            loopInstance.SetActive(false);
            Destroy(loopInstance);
            loopInstance = null;
        }

        private void PlayComplete(MagicTarget completedTarget)
        {
            StopLoop();
            if (castCompletePrefab == null) return;
            // 대상이 사라져도 재생되도록 부모 없이 월드 공간에 생성.
            GameObject effect = Instantiate(castCompletePrefab, castVFXAnchor.position, castVFXAnchor.rotation);
            PlayParticles(effect);
            Destroy(effect, completeLifetime);
        }

        private static void PlayParticles(GameObject effect)
        {
            // 각 시스템을 한 번씩 재생하여 자식 파티클의 중복 재시작을 피함.
            foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                particles.Play(false);
        }

        private void OnDisable()
        {
            if (target != null)
            {
                target.CastStarted -= StartLoop;
                target.CastStopped -= StopLoop;
                target.Completed -= PlayComplete;
            }
            StopLoop();
        }
    }
}
