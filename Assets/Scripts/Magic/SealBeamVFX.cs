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
        [Tooltip("LineRenderer가 포함된 GameObject 프리팹. 입력/Raycast를 처리하는 데모 컨트롤러 대신 순수 Beam 프리팹을 연결합니다.")]
        [SerializeField] private GameObject beamPrefab;
        [SerializeField, Range(0.15f, 0.25f)] private float beamLifetime = 0.2f;
        [SerializeField, Range(0.01f, 0.1f)] private float extensionTime = 0.04f;
        [Tooltip("프리팹에 설정된 광선 두께에 곱할 값입니다.")]
        [SerializeField, Min(0.001f)] private float widthMultiplier = 0.03f;

        [Header("플레이어 발사 위치")]
        [SerializeField] private Camera playerCamera;
        [Tooltip("손/지팡이 끝 또는 카메라 자식 Transform. 비워 두면 카메라 기준 오른쪽 아래 오프셋을 사용합니다.")]
        [SerializeField] private Transform beamOrigin;
        [SerializeField] private Vector3 cameraLocalOffset = new Vector3(0.22f, -0.18f, 0.45f);

        public static SealBeamVFX Active { get; private set; }
        private readonly List<Beam> beams = new List<Beam>();

        private sealed class Beam
        {
            public GameObject Root;
            public Vector3 Start;
            public Vector3 End;
            public float Age;
            public float Lifetime;
            public float ExtensionTime;
            public LineState[] Lines;
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
            if (lines.Length == 0)
            {
                Destroy(effect);
                Debug.LogWarning("SealBeamVFX: LineRenderer가 포함된 Beam 프리팹을 연결하세요.", this);
                return;
            }

            var beam = new Beam
            {
                Root = effect,
                Start = start,
                // 완료 직후 대상/균열이 제거되어도 성공한 봉인점 위치까지 재생합니다.
                End = completedTarget.transform.position,
                Lifetime = Mathf.Clamp(beamLifetime, 0.15f, 0.25f),
                ExtensionTime = Mathf.Max(0.001f, extensionTime),
                Lines = new LineState[lines.Length]
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
            float extension = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(beam.Age / Mathf.Min(beam.ExtensionTime, beam.Lifetime)));
            float fade = 1f - Mathf.SmoothStep(0f, 1f,
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
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
            foreach (Beam beam in beams)
                if (beam.Root != null) Destroy(beam.Root);
            beams.Clear();
        }
    }
}
