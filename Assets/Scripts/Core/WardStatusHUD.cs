using TMPro;
using System.Text;
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
        private DayFlowManager dayFlow;
        private FacilityZoneTracker zoneTracker;
        private ZoneIdentity[] zones;
        public FacilityZoneTracker ZoneTracker => zoneTracker;

        private void Awake()
        {
            dayFlow = FindFirstObjectByType<DayFlowManager>();
            zoneTracker = dayFlow != null ? dayFlow.ZoneTracker : FindFirstObjectByType<FacilityZoneTracker>();
            zones = FindObjectsByType<ZoneIdentity>(FindObjectsSortMode.None);
        }

        private void LateUpdate()
        {
            if (status == null || status.Player == null) return;
            PlayerContamination player = status.Player;
            if (statusText != null)
            {
                var lines = new StringBuilder();
                lines.Append($"안정도 {status.Stability:F0} · 오염도 {player.Value:F0}\n");
                lines.Append($"최고 위험 {status.HighestStage} · ");
                lines.AppendLine(player.InSafeZone ? "안전 구역" : $"노출 +{player.ExposurePerSecond:F1}/초");
                ZoneIdentity currentZone = zoneTracker != null ? zoneTracker.CurrentZone : null;
                if (currentZone != null) lines.AppendLine($"현재 위치 · {currentZone.DisplayName}");
                foreach (AnomalyBehaviour anomaly in AnomalyBehaviour.ActiveAnomalies)
                {
                    if (anomaly == null || !anomaly.IsHazardActive) continue;
                    ZoneIdentity anomalyZone = anomaly.Zone != null ? anomaly.Zone
                        : ZoneIdentity.FindNearest(anomaly.transform.position, zones);
                    string location = anomalyZone != null ? anomalyZone.DisplayName
                        : string.IsNullOrWhiteSpace(anomaly.SpawnLocationLabel) ? anomaly.name : anomaly.SpawnLocationLabel;
                    string anomalyName = anomaly is ManaRift ? "마력 균열" : anomaly.name.Replace("(Clone)", "").Trim();
                    if (anomaly is ManaRift rift)
                        lines.AppendLine($"{location} · {anomalyName} {rift.DangerStage}단계 · {rift.NextStageLabel} {Mathf.CeilToInt(rift.SecondsUntilNextStage)}초");
                    else
                        lines.AppendLine($"{location} · {anomalyName} {anomaly.DangerStage}단계");
                }
                statusText.text = lines.ToString().TrimEnd();
            }
            if (stabilityFill != null) stabilityFill.fillAmount = status.Stability / 100f;
            if (contaminationFill != null) contaminationFill.fillAmount = player.Value / 100f;
            if (warningText != null)
            {
                warningText.color = Color.red;
                warningText.text = status.HasFailed ? "근무 실패"
                    : player.Value >= 80f ? "위험: 오염도 80 이상! 안전 구역으로 이동하세요."
                    : status.Stability <= 20f ? "위험: 시설 안정도 20 이하!"
                    : status.HighestStage >= 3 ? "3단계 균열: 파열 임박!"
                    : player.ExposurePerSecond > 0f ? "위험 균열 근처: 오염도 증가 중"
                    : status.HighestStage == 2 ? "균열이 2단계로 성장했습니다." : "";
            }
            if (failurePanel != null && failurePanel != gameObject)
                failurePanel.SetActive(status.HasFailed && dayFlow == null);
            if (failureText != null && status.HasFailed)
                failureText.text = dayFlow != null
                    ? $"근무 실패\n{dayFlow.CurrentDay}일차\n{status.FailureReason}\n플레이 모드를 종료한 뒤 다시 시작하세요."
                    : $"게임 실패\n{status.FailureReason}\n플레이 모드를 종료한 뒤 다시 시작하세요.";
        }

        public void ConfigureZoneTracker(FacilityZoneTracker tracker)
        {
            zoneTracker = tracker;
        }
    }
}
