using System.Collections.Generic;
using UnityEngine;

namespace WardShift
{
    /// <summary>이상현상 진압 보상을 한 번만 지급하고 이벤트 구독을 관리한다.</summary>
    [DisallowMultipleComponent]
    public sealed class DayAnomalyRewardController : MonoBehaviour
    {
        private readonly HashSet<int> rewardedAnomalyIds = new HashSet<int>();
        private readonly Dictionary<int, int> cleanupRewardCaps = new Dictionary<int, int>();
        private readonly List<AnomalyBehaviour> subscribedAnomalies = new List<AnomalyBehaviour>();
        private DayFlowManager manager;
        private PlayerInventory inventory;

        public void Configure(DayFlowManager dayFlow, PlayerInventory playerInventory)
        {
            manager = dayFlow;
            inventory = playerInventory;
        }

        public void ResetForDay()
        {
            rewardedAnomalyIds.Clear();
            cleanupRewardCaps.Clear();
            UnsubscribeAll();
        }

        public void FreezeRewardsForCleanup(IReadOnlyList<AnomalyBehaviour> anomalies)
        {
            cleanupRewardCaps.Clear();
            if (anomalies == null) return;
            foreach (AnomalyBehaviour anomaly in anomalies)
                if (anomaly != null && !anomaly.IsEnded)
                    cleanupRewardCaps[anomaly.GetInstanceID()] = anomaly.ResolutionReward;
        }

        public void Register(AnomalyBehaviour anomaly)
        {
            if (anomaly == null || subscribedAnomalies.Contains(anomaly)) return;
            anomaly.ResolutionCompleted += RewardResolution;
            anomaly.Ended += OnAnomalyEnded;
            subscribedAnomalies.Add(anomaly);
        }

        public void RewardResolution(AnomalyBehaviour anomaly)
        {
            if (anomaly == null || manager.State != DayState.Playing) return;
            int id = anomaly.GetInstanceID();
            if (!rewardedAnomalyIds.Add(id)) return;
            int reward = anomaly.ResolutionReward;
            if (reward >= 0 && cleanupRewardCaps.TryGetValue(id, out int cap) && cap >= 0)
                reward = Mathf.Min(reward, cap);
            if (reward < 0)
            {
                int min = Mathf.Max(0, manager.MinimumAnomalyReward);
                int max = Mathf.Max(min, manager.MaximumAnomalyReward);
                reward = UnityEngine.Random.Range(min, max + 1);
            }
            else reward = FloodConditionRules.ResolutionReward(reward, manager.ActiveFloodCondition);
            inventory.AddManaCrystals(Mathf.Max(0, reward));
            anomaly.ResolutionCompleted -= RewardResolution;
        }

        public void UnsubscribeAll()
        {
            foreach (AnomalyBehaviour anomaly in subscribedAnomalies)
            {
                if (anomaly == null) continue;
                anomaly.ResolutionCompleted -= RewardResolution;
                anomaly.Ended -= OnAnomalyEnded;
            }
            subscribedAnomalies.Clear();
        }

        private void OnAnomalyEnded(AnomalyBehaviour anomaly, AnomalyEndReason reason)
        {
            if (anomaly == null) return;
            cleanupRewardCaps.Remove(anomaly.GetInstanceID());
            anomaly.ResolutionCompleted -= RewardResolution;
            anomaly.Ended -= OnAnomalyEnded;
            subscribedAnomalies.Remove(anomaly);
        }
    }
}
