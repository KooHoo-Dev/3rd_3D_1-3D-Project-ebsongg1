using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>시전 상태를 읽어 선택 마법, 안내 문구와 진행도를 표시.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10011)]
    public sealed class MagicHUD : MonoBehaviour
    {
        [Header("플레이어와 UI 연결")]
        [SerializeField] private MagicCaster caster;
        [SerializeField] private MagicDetector detector;
        [SerializeField] private TMP_Text selectionText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private Image progressFill;
        [SerializeField] private GameObject castPanel;
        [SerializeField] private TMP_Text aimText;
        [SerializeField] private TMP_Text castHint;
        [SerializeField] private GameObject timingTrack;
        [Header("구간 표시 (ProgressBackground 아래의 별도 Image)")]
        [SerializeField] private Image stableZone;
        [SerializeField] private Image overchargeZone;
        [SerializeField] private Color chargingColor = new Color(0.3f, 0.6f, 1f);
        [SerializeField] private Color successColor = new Color(0.2f, 0.9f, 0.4f);
        [SerializeField] private Color failureColor = new Color(1f, 0.25f, 0.2f);

        /// <summary>마법 종류의 UI 표시 이름.</summary>
        public static string MagicName(MagicType type)
        {
            switch (type)
            {
                case MagicType.Seal: return "봉인";
                case MagicType.Purify: return "정화";
                case MagicType.Stabilize: return "안정화";
                default: return "알 수 없음";
            }
        }

        private void LateUpdate()
        {
            if (caster == null) return;
            if (detector == null) detector = caster.GetComponent<MagicDetector>();
            if (selectionText != null) selectionText.text = MagicName(caster.SelectedMagic);

            bool blocked = !caster.isActiveAndEnabled || caster.InputBlocked;
            MagicTarget target = caster.CastingTarget;
            bool casting = !blocked && target != null && target.IsCasting && target.IsAvailable;
            bool showingResult = !blocked && !casting && caster.ShowingResult;
            ManaRift chain = !blocked ? caster.ActiveSealRift : null;
            bool chainingSeal = chain != null && chain.ExpectedSealPoint != null;
            // 결과는 짧은 텍스트로만 표시. 조준/결과 상태에서는 게이지를 남기지 않습니다.
            if (castPanel != null) castPanel.SetActive(casting);
            if (aimText != null)
            {
                MagicTarget aimed = caster.AimedTarget;
                ManaDeviceAnomaly diagnosed = !blocked && detector != null && detector.IsSensing
                    ? detector.DiagnosedDevice : null;
                MagicTarget focused = casting ? target : aimed;
                ManaDeviceVisuals focusedVisuals = focused != null
                    ? focused.GetComponentInParent<ManaDeviceVisuals>() : null;
                ManaDeviceAnomaly recovery = !blocked && focusedVisuals != null
                    ? focusedVisuals.ActiveAnomaly : null;
                aimText.color = diagnosed != null ? new Color(0.5f, 0.9f, 1f)
                    : recovery != null && recovery.RecoveryRemaining > 0f
                    ? new Color(0.3f, 1f, 0.7f)
                    : showingResult && caster.LastResult != MagicCastResult.Success
                    ? failureColor : chainingSeal ? successColor : Color.white;
                aimText.rectTransform.sizeDelta = diagnosed != null
                    ? new Vector2(700f, 68f) : new Vector2(600f, 38f);
                aimText.text = diagnosed != null
                    ? $"마력 진단 · 다음 방출 {diagnosed.SecondsUntilNextBurst:F1}초 · 위험 반경 {diagnosed.BurstRadius:F1}m\n<size=18>"
                        + (diagnosed.IsBurstWarningActive ? "방출 예고 중" : "방출 예고 전")
                        + (diagnosed.RecoveryRemaining > 0f
                            ? $" · 빠른 안정화 {diagnosed.RecoveryRemaining:F1}초" : "") + "</size>"
                    : recovery != null && recovery.RecoveryRemaining > 0f
                    ? $"빠른 안정화 ×{recovery.StabilizationSpeedMultiplier:F1} · {recovery.RecoveryRemaining:F1}초"
                    : blocked || casting ? ""
                    : showingResult ? $"{caster.CompletedTargetName} · {ResultName(caster.LastResult)}"
                    : chainingSeal ? $"{chain.ExpectedSealPoint.DisplayName} · {chain.SealChainTimeRemaining:F1}초"
                    : aimed == null ? ""
                    : $"{aimed.DisplayName} · {MagicName(aimed.RequiredMagic)}";
            }
            if (!casting)
            {
                if (chainingSeal && !showingResult)
                {
                    if (castHint != null) castHint.text = "두 번째 룬을 조준해 좌클릭";
                    if (statusText != null) statusText.text = "연쇄 봉인";
                }
                return;
            }
            float start = target.StableStart;
            float end = target.StableEnd;
            bool timing = target.UseTimingCast;
            if (timingTrack != null) timingTrack.SetActive(timing);
            SetZone(stableZone, start, end, successColor, timing);
            SetZone(overchargeZone, end, 1f, failureColor, timing);
            float progress = caster.Progress;
            if (progressFill != null)
            {
                progressFill.fillAmount = progress;
                progressFill.color = timing && progress > end ? failureColor
                    : timing && progress >= start ? successColor : chargingColor;
            }
            if (progressText != null) progressText.text = $"{Mathf.FloorToInt(progress * 100f)}%";

            if (statusText != null) statusText.text = $"{MagicName(caster.SelectedMagic)} · {target.DisplayName}";
            if (castHint != null) castHint.text = timing ? "녹색 구간에서 좌클릭 해제"
                : caster.SelectedMagic == MagicType.Stabilize ? "좌클릭 유지 · 중단해도 누적 진행도 보존"
                : "좌클릭 유지 · 패치를 옮겨 연속 정화";
        }

        public void ConfigureView(GameObject panel, TMP_Text title, TMP_Text percent, Image fill,
            Image stable, Image overcharge, TMP_Text aimed, TMP_Text hint, GameObject track)
        {
            selectionText = null;
            castPanel = panel;
            statusText = title;
            progressText = percent;
            progressFill = fill;
            stableZone = stable;
            overchargeZone = overcharge;
            aimText = aimed;
            castHint = hint;
            timingTrack = track;
            panel.SetActive(false);
        }

        private void OnDisable()
        {
            if (castPanel != null) castPanel.SetActive(false);
            if (aimText != null) aimText.text = "";
        }

        private static string ResultName(MagicCastResult result)
        {
            switch (result)
            {
                case MagicCastResult.Success: return "시전 성공";
                case MagicCastResult.Undercharged: return "충전 부족 · 다시 시도하세요";
                case MagicCastResult.Overcharged: return "과충전 · 다시 시도하세요";
                default: return "시전 취소";
            }
        }

        private static void SetZone(Image image, float min, float max, Color color, bool visible)
        {
            if (image == null) return;
            image.enabled = visible;
            image.raycastTarget = false;
            image.color = color;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(min, 0f);
            rect.anchorMax = new Vector2(max, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
