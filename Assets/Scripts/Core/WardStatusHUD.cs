using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10005)]
    public sealed class WardStatusHUD : MonoBehaviour
    {
        [SerializeField] private WardStatus status;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text warningText;
        [SerializeField] private Image stabilityFill;
        [SerializeField] private Image contaminationFill;
        [SerializeField] private GameObject failurePanel;
        [SerializeField] private TMP_Text failureText;

        private void LateUpdate()
        {
            if (status == null || status.Player == null) return;
            PlayerContamination player = status.Player;
            if (statusText != null)
                statusText.text = $"시설 안정도: {status.Stability:F1}/100\n마력 오염도: {player.Value:F1}/100\n"
                    + $"최고 위험 단계: {status.HighestStage}\n"
                    + (player.InSafeZone ? "안전 구역 · 오염 회복" : $"오염 증가: {player.ExposurePerSecond:F1}/초");
            if (stabilityFill != null) stabilityFill.fillAmount = status.Stability / 100f;
            if (contaminationFill != null) contaminationFill.fillAmount = player.Value / 100f;
            if (warningText != null)
            {
                warningText.color = Color.red;
                warningText.text = status.HasFailed ? "게임 실패"
                    : player.Value >= 80f ? "위험: 오염도 80 이상! 안전 구역으로 이동하세요."
                    : status.Stability <= 20f ? "위험: 시설 안정도 20 이하!"
                    : status.HasOverloadedRift ? "폭주한 균열이 남아 있습니다! 봉인하세요."
                    : status.HighestStage >= 3 ? "3단계 균열: 폭주 임박!"
                    : player.ExposurePerSecond > 0f ? "위험 균열 근처: 오염도 증가 중"
                    : status.HighestStage == 2 ? "균열이 2단계로 성장했습니다." : "";
            }
            if (failurePanel != null && failurePanel != gameObject)
                failurePanel.SetActive(status.HasFailed);
            if (failureText != null && status.HasFailed)
                failureText.text = $"게임 실패\n{status.FailureReason}\nPlay 모드를 종료한 뒤 다시 시작하세요.";
        }
    }
}
