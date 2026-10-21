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

            bool completed = caster.ShowingCompletion;
            float progress = completed ? 1f : caster.Progress;
            if (progressFill != null) progressFill.fillAmount = progress;
            if (progressText != null) progressText.text = $"{Mathf.FloorToInt(progress * 100f)}%";

            if (statusText == null) return;
            MagicTarget target = caster.AimedTarget;
            if (completed) statusText.text = $"{caster.CompletedTargetName} — 완료!";
            else if (!caster.isActiveAndEnabled || Time.timeScale <= 0f) statusText.text = "시전 중지";
            else if (caster.WaitingForRelease) statusText.text = "마우스를 놓은 뒤 다시 시전하세요";
            else if (target == null) statusText.text = "시전 거리 안의 마법 대상을 조준하세요";
            else if (target.RequiredMagic != caster.SelectedMagic)
                statusText.text = $"{target.DisplayName} · {MagicName(target.RequiredMagic)} 마법이 필요합니다";
            else if (caster.CastingTarget != null) statusText.text = $"{target.DisplayName} · {MagicName(caster.SelectedMagic)} 시전 중…";
            else statusText.text = $"{target.DisplayName} · 좌클릭을 유지하세요";
        }
    }
}
