using System.Collections.Generic;
using UnityEngine;

namespace WardShift
{
    /// <summary>봉인 성공 후 원본 광선 프리팹을 목표점 방향으로 재생합니다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10003)]
    public sealed class SealBeamVFX : MonoBehaviour
    {
        [Header("원본 광선 프리팹")]
        [SerializeField] private GameObject beamPrefab;
        [SerializeField, Range(0.2f, 0.3f)] private float beamLifetime = 0.25f;
        [Tooltip("비워 두면 MainCamera를 사용합니다.")]
        [SerializeField] private Camera playerCamera;
        [Tooltip("손/지팡이 위치 또는 카메라 자식. 비우면 카메라 로컬 오프셋을 사용합니다.")]
        [SerializeField] private Transform beamOrigin;
        [SerializeField] private Vector3 cameraLocalOffset = new Vector3(0.25f, -0.22f, 0.65f);
        [Header("정화 광선 (선택)")]
        [SerializeField] private GameObject continuousPurifyBeamPrefab;
        [SerializeField] private bool logBeamDiagnostics = true;

        public static SealBeamVFX Active { get; private set; }
        private readonly List<Beam> beams = new List<Beam>();
        private Beam continuousPurifyBeam;

        private sealed class Beam
        {
            public GameObject Root;
            public Vector3 Start;
            public Vector3 End;
            public float Age;
            public float Lifetime;
            public Transform FollowTarget;
            public bool TravelsToTarget;
        }

        private void OnEnable() => Active = this;

        /// <summary>성공 판정 이후 원본 파티클 프리팹을 봉인점 방향으로 발사합니다.</summary>
        public void PlaySealSuccess(MagicTarget completedTarget)
        {
            if (!isActiveAndEnabled || beamPrefab == null || completedTarget == null
                || !completedTarget.IsCompleted || completedTarget.RequiredMagic != MagicType.Seal
                || completedTarget.GetComponentInParent<ManaRift>() == null) return;
            if (!TryGetOrigin(out Vector3 start))
            {
                Debug.LogWarning("SealBeamVFX: 발사점과 Main Camera를 찾을 수 없습니다.", this);
                return;
            }

            Vector3 end = completedTarget.transform.position;
            Beam beam = CreateBeam(beamPrefab, start, end, null,
                Mathf.Clamp(beamLifetime, 0.2f, 0.3f), true);
            if (beam == null) return;
            beams.Add(beam);
            if (logBeamDiagnostics)
                Debug.Log($"SealBeamVFX: 원본 {beamPrefab.name} 발사, 시작 {start}, 목표 {end}", this);
        }

        /// <summary>정화 중 원본 프리팹을 카메라에서 현재 패치 방향으로 재생합니다.</summary>
        public void SetContinuousPurifyTarget(MagicTarget target)
        {
            if (!isActiveAndEnabled || target == null || !target.isActiveAndEnabled
                || !target.IsCasting || target.RequiredMagic != MagicType.Purify)
            {
                StopContinuousPurifyBeam();
                return;
            }

            if (continuousPurifyBeam != null && continuousPurifyBeam.Root != null)
            {
                continuousPurifyBeam.FollowTarget = target.transform;
                return;
            }

            GameObject prefab = continuousPurifyBeamPrefab != null ? continuousPurifyBeamPrefab : beamPrefab;
            if (prefab == null || !TryGetOrigin(out Vector3 start)) return;
            continuousPurifyBeam = CreateBeam(prefab, start, target.transform.position,
                target.transform, 0f, false);
        }

        public void StopContinuousPurifyBeam()
        {
            if (continuousPurifyBeam != null && continuousPurifyBeam.Root != null)
                Destroy(continuousPurifyBeam.Root);
            continuousPurifyBeam = null;
        }

        private Beam CreateBeam(GameObject prefab, Vector3 start, Vector3 end,
            Transform followTarget, float lifetime, bool travelsToTarget)
        {
            Vector3 direction = end - start;
            Quaternion rotation = direction.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(direction) : Quaternion.identity;
            GameObject effect = Instantiate(prefab, start, rotation);
            effect.SetActive(true);
            MagicVfxUtility.Prepare(effect);

            // 프리팹의 파티클 시스템, 재질, 크기, 속도, 좌표 설정은 그대로 둡니다.
            foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                if (!particles.isPlaying) particles.Play(false);

            return new Beam
            {
                Root = effect,
                Start = start,
                End = end,
                Lifetime = lifetime,
                FollowTarget = followTarget,
                TravelsToTarget = travelsToTarget
            };
        }

        private bool TryGetOrigin(out Vector3 position)
        {
            if (beamOrigin != null)
            {
                position = beamOrigin.position;
                return true;
            }
            if (playerCamera == null) playerCamera = Camera.main;
            position = playerCamera != null
                ? playerCamera.transform.TransformPoint(cameraLocalOffset) : Vector3.zero;
            return playerCamera != null;
        }

        private void LateUpdate()
        {
            if (continuousPurifyBeam != null)
            {
                if (continuousPurifyBeam.Root == null || continuousPurifyBeam.FollowTarget == null)
                {
                    StopContinuousPurifyBeam();
                }
                else
                {
                    continuousPurifyBeam.Start = GetOriginOrFallback(continuousPurifyBeam.Start);
                    continuousPurifyBeam.End = continuousPurifyBeam.FollowTarget.position;
                    UpdateBeamTransform(continuousPurifyBeam, 1f);
                }
            }

            for (int i = beams.Count - 1; i >= 0; i--)
            {
                Beam beam = beams[i];
                // 마지막 진압 직후 DAY CLEAR가 게임 시간을 멈춰도 봉인 빔과 반복 사운드를 종료한다.
                beam.Age += Time.unscaledDeltaTime;
                if (beam.Root == null || beam.Age >= beam.Lifetime)
                {
                    if (beam.Root != null) Destroy(beam.Root);
                    beams.RemoveAt(i);
                    continue;
                }
                beam.Start = GetOriginOrFallback(beam.Start);
                float travel = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(beam.Age / beam.Lifetime));
                UpdateBeamTransform(beam, travel);
            }
        }

        private Vector3 GetOriginOrFallback(Vector3 fallback)
        {
            return TryGetOrigin(out Vector3 position) ? position : fallback;
        }

        private static void UpdateBeamTransform(Beam beam, float travel)
        {
            if (beam.Root == null) return;
            Vector3 direction = beam.End - beam.Start;
            if (direction.sqrMagnitude > 0.000001f)
                beam.Root.transform.rotation = Quaternion.LookRotation(direction);
            beam.Root.transform.position = beam.TravelsToTarget
                ? Vector3.Lerp(beam.Start, beam.End, travel) : beam.Start;
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
            StopContinuousPurifyBeam();
            foreach (Beam beam in beams)
                if (beam.Root != null) Destroy(beam.Root);
            beams.Clear();
        }
    }
}
