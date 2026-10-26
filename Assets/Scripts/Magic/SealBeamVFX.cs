using System.Collections.Generic;
using UnityEngine;

namespace WardShift
{
    /// <summary>이미 성공한 봉인 클릭을 광선으로 표시합니다. 입력과 봉인 판정은 처리하지 않습니다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10003)]
    public sealed class SealBeamVFX : MonoBehaviour
    {
        [Header("광선 프리팹과 시간")]
        [Tooltip("LineRenderer 또는 Stretched Billboard 파티클 광선 프리팹 (예: LightBeamBlast). 입력/Raycast 데모 컨트롤러는 연결하지 않습니다.")]
        [SerializeField] private GameObject beamPrefab;
        [SerializeField, Range(0.15f, 0.25f)] private float beamLifetime = 0.2f;
        [SerializeField, Range(0.01f, 0.1f)] private float extensionTime = 0.04f;
        [Tooltip("LineRenderer 프리팹 광선 두께에 곱할 값입니다.")]
        [SerializeField, Min(0.001f)] private float widthMultiplier = 0.03f;
        [Tooltip("파티클 프리팹 광선과 주변 불꽃 크기에 곱할 값입니다. LightBeamBlast는 0.3에서 기존 0.03보다 10배 두껍습니다.")]
        [SerializeField, Range(0.05f, 1f)] private float particleScaleMultiplier = 0.3f;
        [Header("지속 정화 광선 (선택)")]
        [Tooltip("정화 중 계속 표시할 프리팹입니다. 비워 두면 위 봉인 광선 프리팹을 재사용합니다.")]
        [SerializeField] private GameObject continuousPurifyBeamPrefab;

        [Header("플레이어 발사 위치")]
        [SerializeField] private Camera playerCamera;
        [Tooltip("손/지팡이 끝 또는 카메라 자식 Transform. 비워 두면 카메라 기준 오른쪽 아래 오프셋을 사용합니다.")]
        [SerializeField] private Transform beamOrigin;
        [SerializeField] private Vector3 cameraLocalOffset = new Vector3(0.22f, -0.18f, 0.45f);

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
            public float ExtensionTime;
            public Transform FollowTarget;
            public bool IsContinuous;
            public LineState[] Lines;
            public ParticleBeamState[] ParticleBeams;
        }

        private sealed class ParticleBeamState
        {
            public ParticleSystem System;
            public ParticleSystemRenderer Renderer;
            public ParticleSystem.Particle[] Particles;
            public Color32[] Colors;
            public float Width;
        }

        private sealed class LineState
        {
            public LineRenderer Renderer;
            public float Width;
            public Gradient Gradient;
            public GradientColorKey[] OriginalColors;
            public GradientAlphaKey[] OriginalAlphas;
            public GradientColorKey[] Colors;
            public GradientAlphaKey[] Alphas;
        }

        private void OnEnable() => Active = this;

        /// <summary>MagicTargetVFX의 기존 Completed 구독에서만 호출합니다.</summary>
        public void PlaySealSuccess(MagicTarget completedTarget)
        {
            if (!isActiveAndEnabled || beamPrefab == null || completedTarget == null
                || !completedTarget.IsCompleted || completedTarget.RequiredMagic != MagicType.Seal
                || completedTarget.GetComponentInParent<ManaRift>() == null
                || !TryGetOrigin(out Vector3 start)) return;

            GameObject effect = Instantiate(beamPrefab, start, Quaternion.identity);
            foreach (Collider effectCollider in effect.GetComponentsInChildren<Collider>(true))
                effectCollider.enabled = false;
            foreach (Collider2D effectCollider in effect.GetComponentsInChildren<Collider2D>(true))
                effectCollider.enabled = false;

            LineRenderer[] lines = effect.GetComponentsInChildren<LineRenderer>(true);
            ParticleBeamState[] particleBeams = PrepareParticleBeams(effect);
            if (lines.Length == 0 && particleBeams.Length == 0)
            {
                Destroy(effect);
                Debug.LogWarning("SealBeamVFX: LineRenderer 또는 Stretched Billboard 파티클 광선 프리팹을 연결하세요.", this);
                return;
            }

            // 완료 직후 대상/균열이 제거되어도 성공한 봉인점 위치까지 재생합니다.
            var beam = new Beam
            {
                Root = effect,
                Start = start,
                End = completedTarget.transform.position,
                Lifetime = Mathf.Clamp(beamLifetime, 0.15f, 0.25f),
                ExtensionTime = Mathf.Max(0.001f, extensionTime),
                Lines = new LineState[lines.Length],
                ParticleBeams = particleBeams
            };
            for (int i = 0; i < lines.Length; i++)
            {
                LineRenderer line = lines[i];
                Gradient gradient = line.colorGradient;
                line.useWorldSpace = true;
                line.positionCount = 2;
                beam.Lines[i] = new LineState
                {
                    Renderer = line,
                    Width = line.widthMultiplier * Mathf.Max(0.001f, widthMultiplier),
                    Gradient = gradient,
                    OriginalColors = gradient.colorKeys,
                    OriginalAlphas = gradient.alphaKeys,
                    Colors = gradient.colorKeys,
                    Alphas = gradient.alphaKeys
                };
            }
            beams.Add(beam);
            UpdateBeam(beam);
        }

        /// <summary>정화가 진행되는 동안 발사 위치와 현재 패치 위치를 잇는 광선을 유지합니다.</summary>
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
                target.transform, true, 1f);
            if (continuousPurifyBeam != null) UpdateBeam(continuousPurifyBeam);
        }

        /// <summary>현재 정화가 멈췄을 때 지속 광선을 제거합니다.</summary>
        public void StopContinuousPurifyBeam()
        {
            if (continuousPurifyBeam != null && continuousPurifyBeam.Root != null)
                Destroy(continuousPurifyBeam.Root);
            continuousPurifyBeam = null;
        }

        private Beam CreateBeam(GameObject prefab, Vector3 start, Vector3 end,
            Transform followTarget, bool continuous, float lifetime)
        {
            GameObject effect = Instantiate(prefab, start, Quaternion.identity);
            MagicVfxUtility.Prepare(effect);
            LineRenderer[] lines = effect.GetComponentsInChildren<LineRenderer>(true);
            ParticleBeamState[] particleBeams = PrepareParticleBeams(effect);
            if (lines.Length == 0 && particleBeams.Length == 0)
            {
                Destroy(effect);
                Debug.LogWarning("SealBeamVFX: LineRenderer 또는 Stretched Billboard 파티클 광선 프리팹을 연결하세요.", this);
                return null;
            }

            var beam = new Beam
            {
                Root = effect,
                Start = start,
                End = end,
                Lifetime = lifetime,
                ExtensionTime = Mathf.Max(0.001f, extensionTime),
                FollowTarget = followTarget,
                IsContinuous = continuous,
                Lines = new LineState[lines.Length],
                ParticleBeams = particleBeams
            };
            for (int i = 0; i < lines.Length; i++)
            {
                LineRenderer line = lines[i];
                Gradient gradient = line.colorGradient;
                line.useWorldSpace = true;
                line.positionCount = 2;
                beam.Lines[i] = new LineState
                {
                    Renderer = line,
                    Width = line.widthMultiplier * Mathf.Max(0.001f, widthMultiplier),
                    Gradient = gradient,
                    OriginalColors = gradient.colorKeys,
                    OriginalAlphas = gradient.alphaKeys,
                    Colors = gradient.colorKeys,
                    Alphas = gradient.alphaKeys
                };
            }
            return beam;
        }

        private ParticleBeamState[] PrepareParticleBeams(GameObject effect)
        {
            var result = new List<ParticleBeamState>();
            foreach (ParticleSystem system in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule main = system.main;
                main.loop = false;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || renderer.renderMode != ParticleSystemRenderMode.Stretch)
                {
                    // 광선 주변의 불꽃은 원래 재생 방식을 유지하고 크기와 속도만 줄입니다.
                    main.startSizeMultiplier *= particleScaleMultiplier;
                    main.startSpeedMultiplier *= particleScaleMultiplier;
                    system.Play(false);
                    continue;
                }

                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                var size = system.sizeOverLifetime;
                size.enabled = false;
                var speedSize = system.sizeBySpeed;
                speedSize.enabled = false;
                var velocity = system.velocityOverLifetime;
                velocity.enabled = false;
                renderer.velocityScale = 0f;
                renderer.cameraVelocityScale = 0f;
                renderer.pivot = Vector3.zero;
                // 시간 0의 Burst도 생성하고 정지시켜 매 프레임 끝점을 직접 갱신합니다.
                system.Simulate(0.001f, false, true, false);
                system.Pause(false);
                if (system.particleCount == 0) system.Emit(1);
                var particles = new ParticleSystem.Particle[system.particleCount];
                system.GetParticles(particles);
                var colors = new Color32[particles.Length];
                for (int i = 0; i < particles.Length; i++) colors[i] = particles[i].startColor;
                result.Add(new ParticleBeamState
                {
                    System = system,
                    Renderer = renderer,
                    Particles = particles,
                    Colors = colors,
                    Width = Mathf.Max(0.001f, particles[0].startSize3D.x * particleScaleMultiplier)
                });
            }
            return result.ToArray();
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

        private void LateUpdate() => AdvanceBeams(Time.deltaTime);

        private void AdvanceBeams(float deltaTime)
        {
            if (continuousPurifyBeam != null)
            {
                if (continuousPurifyBeam.Root == null || continuousPurifyBeam.FollowTarget == null)
                {
                    StopContinuousPurifyBeam();
                }
                else
                {
                    continuousPurifyBeam.Age = 0f;
                    continuousPurifyBeam.End = continuousPurifyBeam.FollowTarget.position;
                    if (TryGetOrigin(out Vector3 start)) continuousPurifyBeam.Start = start;
                    UpdateBeam(continuousPurifyBeam);
                }
            }

            for (int i = beams.Count - 1; i >= 0; i--)
            {
                Beam beam = beams[i];
                beam.Age += deltaTime;
                if (beam.Root == null || beam.Age >= beam.Lifetime)
                {
                    if (beam.Root != null) Destroy(beam.Root);
                    beams.RemoveAt(i);
                    continue;
                }
                if (TryGetOrigin(out Vector3 start)) beam.Start = start;
                UpdateBeam(beam);
            }
        }

        private static void UpdateBeam(Beam beam)
        {
            Vector3 direction = beam.End - beam.Start;
            beam.Root.transform.position = beam.Start;
            if (direction.sqrMagnitude > 0.000001f)
                beam.Root.transform.rotation = Quaternion.LookRotation(direction);
            float extension = beam.IsContinuous ? 1f : Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(beam.Age / Mathf.Min(beam.ExtensionTime, beam.Lifetime)));
            float fade = beam.IsContinuous ? 1f : 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(beam.Lifetime * 0.4f, beam.Lifetime, beam.Age));
            Vector3 tip = Vector3.Lerp(beam.Start, beam.End, extension);
            foreach (LineState line in beam.Lines)
            {
                if (line.Renderer == null) continue;
                line.Renderer.SetPosition(0, beam.Start);
                line.Renderer.SetPosition(1, tip);
                line.Renderer.widthMultiplier = line.Width * fade;
                for (int i = 0; i < line.Colors.Length; i++)
                {
                    line.Colors[i] = line.OriginalColors[i];
                    line.Colors[i].color *= fade;
                }
                for (int i = 0; i < line.Alphas.Length; i++)
                {
                    line.Alphas[i] = line.OriginalAlphas[i];
                    line.Alphas[i].alpha *= fade;
                }
                line.Gradient.SetKeys(line.Colors, line.Alphas);
                line.Renderer.colorGradient = line.Gradient;
            }
            foreach (ParticleBeamState state in beam.ParticleBeams)
            {
                if (state.System == null || state.Renderer == null) continue;
                Transform particleTransform = state.System.transform;
                Vector3 localStart = particleTransform.InverseTransformPoint(beam.Start);
                Vector3 localTip = particleTransform.InverseTransformPoint(tip);
                Vector3 localDirection = localTip - localStart;
                float width = Mathf.Max(0.0001f, state.Width * fade);
                state.Renderer.lengthScale = localDirection.magnitude / width;
                float particleLifetime = beam.IsContinuous ? 1f : beam.Lifetime;
                for (int i = 0; i < state.Particles.Length; i++)
                {
                    // Stretched Billboard는 파티클 위치에서 속도 반대 방향으로 늘어납니다.
                    state.Particles[i].position = localTip;
                    state.Particles[i].velocity = localDirection.sqrMagnitude > 0.000001f
                        ? localDirection.normalized : Vector3.forward;
                    state.Particles[i].startSize3D = Vector3.one * width;
                    state.Particles[i].startLifetime = particleLifetime;
                    state.Particles[i].remainingLifetime = beam.IsContinuous
                        ? particleLifetime : beam.Lifetime - beam.Age;
                    state.Particles[i].startColor = (Color)state.Colors[i] * fade;
                }
                state.System.SetParticles(state.Particles, state.Particles.Length);
            }
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
