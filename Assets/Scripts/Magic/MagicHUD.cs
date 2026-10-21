using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>시전 상태를 읽어 선택 마법, 안내 문구와 진행도를 표시.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10003)]
    public sealed class MagicHUD : MonoBehaviour
    {
        [Header("플레이어와 UI 연결")]
        [SerializeField] private MagicCaster caster;
        [SerializeField] private TMP_Text selectionText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private Image progressFill;
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
            if (selectionText != null)
                selectionText.text = $"[1] 봉인   [2] 정화   [3] 안정화\n선택: {MagicName(caster.SelectedMagic)}   |   [Q 유지] 마력 감지";

            bool showingResult = caster.ShowingResult;
            MagicTarget target = caster.CastingTarget != null ? caster.CastingTarget : caster.AimedTarget;
            float start = showingResult ? caster.ResultStableStart : target != null ? target.StableStart : 0.6f;
            float end = showingResult ? caster.ResultStableEnd : target != null ? target.StableEnd : 0.85f;
            bool timing = showingResult ? caster.ResultUsesTiming : target == null || target.UseTimingCast;
            SetZone(stableZone, start, end, successColor, timing);
            SetZone(overchargeZone, end, 1f, failureColor, timing);
            float progress = showingResult ? caster.ResultProgress : caster.Progress;
            if (progressFill != null)
            {
                progressFill.fillAmount = progress;
                progressFill.color = showingResult
                    ? (caster.LastResult == MagicCastResult.Success ? successColor : failureColor)
                    : timing && progress > end ? failureColor
                    : timing && progress >= start ? successColor : chargingColor;
            }
            if (progressText != null) progressText.text = $"{Mathf.FloorToInt(progress * 100f)}%";

            if (statusText == null) return;
            if (!caster.isActiveAndEnabled || caster.InputBlocked) statusText.text = "시전 중지";
            else if (showingResult) statusText.text = $"{caster.CompletedTargetName} — {ResultName(caster.LastResult)}";
            else if (caster.WaitingForRelease) statusText.text = "마우스를 놓은 뒤 다시 시전하세요";
            else if (target == null) statusText.text = "시전 거리 안의 마법 대상을 조준하세요";
            else if (target.RequiredMagic != caster.SelectedMagic)
                statusText.text = $"{target.DisplayName} · {MagicName(target.RequiredMagic)} 마법이 필요합니다";
            else if (caster.CastingTarget != null) statusText.text = timing
                ? $"{target.DisplayName} · 녹색 구간에서 마우스를 놓으세요"
                : $"{target.DisplayName} · 완료까지 유지하세요";
            else statusText.text = $"{target.DisplayName} · 좌클릭으로 충전을 시작하세요";
        }

        private static string ResultName(MagicCastResult result)
        {
            switch (result)
            {
                case MagicCastResult.Success: return "봉인 성공!";
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
