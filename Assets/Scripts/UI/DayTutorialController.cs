using System;
using System.Collections.Generic;
using UnityEngine;

namespace WardShift
{
    /// <summary>튜토리얼 진행, 완료 기록과 이상현상 이벤트를 관리합니다. 화면은 Day5HUD가 그립니다.</summary>
    internal sealed class DayTutorialController : IDisposable
    {
        private const string SaveKey = "WardShift.Tutorial.Completed.v1";
        internal const int Intro = 1 << 0, Seal = 1 << 1, Clear = 1 << 2,
            Supply = 1 << 3, Curse = 1 << 4, Inventory = 1 << 5,
            Map = 1 << 6, Device = 1 << 7, Radial = 1 << 8,
            Projectile = 1 << 10, FirstStageTwo = 1 << 12, FirstStageThree = 1 << 13,
            FirstRupture = 1 << 14;

        internal enum Hint
        {
            None, SecondSeal, StageTwo, StageThree, Rupture, SealComplete,
            Sense, SelectSeal, FirstSeal, CurseSelect, CurseCast,
            DeviceSelect, DeviceCast, Inventory, Map
        }

        private int flags;
        private bool resetPending;
        private bool previousSupplyOpen;
        private bool previousRadialWarning;
        private bool previousProjectileWarning;
        private readonly List<ManaRift> watchedRifts = new List<ManaRift>();
        private readonly List<AnomalyBehaviour> watchedOtherAnomalies = new List<AnomalyBehaviour>();
        private readonly List<AnomalyBehaviour> watchedGrowthAnomalies = new List<AnomalyBehaviour>();
        private readonly Queue<int> growthNoticeQueue = new Queue<int>();
        private int queuedGrowthFlags;
        private int activeGrowthNotice;
        private float growthNoticeRemaining;
        private bool showRuptureOnFailure;
        private bool wasFailedLastFrame;
        private float completionUntil;

        internal int IntroPage { get; private set; } = -1;
        internal bool ResetPending => resetPending;
        internal bool ShowRuptureOnFailure => showRuptureOnFailure;

        internal DayTutorialController() => flags = PlayerPrefs.GetInt(SaveKey, 0);

        internal bool Learned(int flag) => (flags & flag) != 0;

        private void Learn(int flag)
        {
            if (Learned(flag)) return;
            flags |= flag;
            PlayerPrefs.SetInt(SaveKey, flags);
            PlayerPrefs.Save();
        }

        internal bool ShouldBeginIntroduction(DayFlowManager dayFlow) =>
            dayFlow.State == DayState.Playing && dayFlow.CurrentDay == 1
            && !dayFlow.HasStartedDayBriefingThisRun && !Learned(Intro) && IntroPage < 0
            && AnomalyBehaviour.ActiveAnomalies.Count == 0
            && !dayFlow.SupplyPanelOpen && !dayFlow.InventoryOpen && !dayFlow.IsMapOpen;

        internal void BeginIntroduction() => IntroPage = 0;

        internal int AdvanceIntroduction()
        {
            if (IntroPage < 0) return IntroPage;
            if (IntroPage < 4) return IntroPage = IntroPage + 1;
            IntroPage = -2;
            Learn(Intro);
            return IntroPage;
        }

        internal Hint Tick(DayFlowManager dayFlow, MagicCaster caster, MagicDetector detector)
        {
            // DAY 1 브리핑의 근무 시작으로 기존 인트로 모달을 대체한 경우에도
            // 같은 완료 기록을 남겨 이후 TAB 지도 안내 조건을 충족시킵니다.
            if (dayFlow.CurrentDay == 1 && dayFlow.State == DayState.Playing
                && dayFlow.HasStartedDayBriefingThisRun && IntroPage < 0)
                Learn(Intro);
            if (dayFlow.CurrentDay > 1) Learn(Clear);
            if (dayFlow.State == DayState.DayFailed)
            {
                if (showRuptureOnFailure) Learn(FirstRupture);
                wasFailedLastFrame = true;
            }
            else if (wasFailedLastFrame)
            {
                showRuptureOnFailure = false;
                wasFailedLastFrame = false;
            }

            if (dayFlow.InventoryOpen) Learn(Inventory);
            if (dayFlow.IsMapOpen) Learn(Map);
            if (previousSupplyOpen && !dayFlow.SupplyPanelOpen) Learn(Supply);
            previousSupplyOpen = dayFlow.SupplyPanelOpen;

            if (IntroPage >= 0 || dayFlow.State != DayState.Playing || dayFlow.InventoryOpen
                || dayFlow.IsMapOpen || dayFlow.SupplyPanelOpen || Time.timeScale <= 0f)
                return Hint.None;

            ManaRift rift = null;
            CurseAnomaly curse = null;
            ManaDeviceAnomaly device = null;
            foreach (AnomalyBehaviour anomaly in AnomalyBehaviour.ActiveAnomalies)
            {
                if (rift == null && anomaly is ManaRift activeRift && activeRift.IsHazardActive) rift = activeRift;
                if (curse == null && anomaly is CurseAnomaly activeCurse && activeCurse.IsHazardActive) curse = activeCurse;
                if (device == null && anomaly is ManaDeviceAnomaly activeDevice && activeDevice.IsHazardActive) device = activeDevice;
            }
            if (!Learned(Seal))
                foreach (AnomalyBehaviour anomaly in AnomalyBehaviour.ActiveAnomalies)
                    if (anomaly is ManaRift activeRift && !watchedRifts.Contains(activeRift))
                    {
                        activeRift.Resolved += OnRiftResolved;
                        watchedRifts.Add(activeRift);
                    }
            foreach (AnomalyBehaviour anomaly in AnomalyBehaviour.ActiveAnomalies)
                if (((anomaly is CurseAnomaly && !Learned(Curse))
                    || (anomaly is ManaDeviceAnomaly && !Learned(Device)))
                    && !watchedOtherAnomalies.Contains(anomaly))
                {
                    anomaly.ResolutionCompleted += OnAnomalyResolved;
                    watchedOtherAnomalies.Add(anomaly);
                }
            ObserveGrowth();

            bool radialWarning = device != null && device.IsBurstWarningActive
                && device.NextAttack == ManaDeviceAnomaly.AttackKind.RadialBurst;
            bool projectileWarning = device != null && device.IsBurstWarningActive
                && device.NextAttack == ManaDeviceAnomaly.AttackKind.TrackingBolt;
            bool sealChainUrgent = !Learned(Seal) && rift != null && rift.IsSealChainInProgress;
            Hint growthHint = NextGrowthNotice((!Learned(Projectile) && projectileWarning)
                || (!Learned(Radial) && radialWarning) || sealChainUrgent);
            Hint hint = Hint.None;
            if (radialWarning || projectileWarning)
                hint = Hint.None; // 상단의 즉시 회피 경고가 장치 공격 안내를 맡습니다.
            else if (sealChainUrgent) hint = Hint.SecondSeal;
            else if (growthHint != Hint.None) hint = growthHint;
            else if (Time.unscaledTime < completionUntil) hint = Hint.SealComplete;
            else if (!Learned(Seal) && rift != null)
            {
                if (detector == null || !detector.IsSensing
                    || rift.FirstSealPoint == null || !rift.FirstSealPoint.IsDetected)
                    hint = Hint.Sense;
                else if (caster != null && caster.SelectedMagic != MagicType.Seal) hint = Hint.SelectSeal;
                else hint = Hint.FirstSeal;
            }
            else if (!Learned(Curse) && curse != null)
                hint = caster != null && caster.SelectedMagic != MagicType.Purify
                    ? Hint.CurseSelect : Hint.CurseCast;
            else if (!Learned(Device) && device != null)
                hint = caster != null && caster.SelectedMagic != MagicType.Stabilize
                    ? Hint.DeviceSelect : Hint.DeviceCast;

            if (previousRadialWarning && !radialWarning) Learn(Radial);
            if (previousProjectileWarning && !projectileWarning) Learn(Projectile);
            previousRadialWarning = radialWarning;
            previousProjectileWarning = projectileWarning;
            if (hint == Hint.None && rift == null && curse == null && device == null)
            {
                if (!Learned(Inventory) && dayFlow.CurrentDay >= 2 && !dayFlow.DaySupplyPending)
                    hint = Hint.Inventory;
                else if (!Learned(Map) && Learned(Intro) && dayFlow.CurrentDay == 1)
                    hint = Hint.Map;
            }
            return hint;
        }

        private void OnRiftResolved(ManaRift rift)
        {
            Learn(Seal);
            completionUntil = Time.unscaledTime + 2.2f;
            foreach (ManaRift watched in watchedRifts)
                if (watched != null) watched.Resolved -= OnRiftResolved;
            watchedRifts.Clear();
        }

        private void OnAnomalyResolved(AnomalyBehaviour anomaly)
        {
            if (anomaly is CurseAnomaly) Learn(Curse);
            else if (anomaly is ManaDeviceAnomaly) Learn(Device);
            if (anomaly != null) anomaly.ResolutionCompleted -= OnAnomalyResolved;
            watchedOtherAnomalies.Remove(anomaly);
        }

        private void ObserveGrowth()
        {
            foreach (AnomalyBehaviour anomaly in AnomalyBehaviour.ActiveAnomalies)
            {
                if (anomaly == null || !anomaly.IsHazardActive) continue;
                if (anomaly.DangerStage >= 2) Learn(FirstStageTwo);
                if (anomaly.DangerStage >= 3) Learn(FirstStageThree);
                if (!Learned(FirstRupture) && !watchedGrowthAnomalies.Contains(anomaly))
                {
                    anomaly.Ended += OnGrowthAnomalyEnded;
                    watchedGrowthAnomalies.Add(anomaly);
                }
            }
        }

        private void OnGrowthAnomalyEnded(AnomalyBehaviour anomaly, AnomalyEndReason reason)
        {
            if (reason == AnomalyEndReason.Ruptured && !Learned(FirstRupture))
            {
                showRuptureOnFailure = true;
                QueueGrowthNotice(FirstRupture);
            }
            if (anomaly != null) anomaly.Ended -= OnGrowthAnomalyEnded;
            watchedGrowthAnomalies.Remove(anomaly);
        }

        private void QueueGrowthNotice(int flag)
        {
            if (Learned(flag) || activeGrowthNotice == flag || (queuedGrowthFlags & flag) != 0) return;
            growthNoticeQueue.Enqueue(flag);
            queuedGrowthFlags |= flag;
        }

        private Hint NextGrowthNotice(bool urgentHintVisible)
        {
            if (urgentHintVisible) return Hint.None;
            while (activeGrowthNotice == 0 && growthNoticeQueue.Count > 0)
            {
                int flag = growthNoticeQueue.Dequeue();
                queuedGrowthFlags &= ~flag;
                if (Learned(flag)) continue;
                activeGrowthNotice = flag;
                growthNoticeRemaining = 3f;
                Learn(flag);
                if (flag == FirstRupture) showRuptureOnFailure = false;
            }
            if (activeGrowthNotice == 0) return Hint.None;
            Hint hint = activeGrowthNotice == FirstStageTwo ? Hint.StageTwo
                : activeGrowthNotice == FirstStageThree ? Hint.StageThree : Hint.Rupture;
            growthNoticeRemaining -= Time.unscaledDeltaTime;
            if (growthNoticeRemaining <= 0f) activeGrowthNotice = 0;
            return hint;
        }

        internal void ResetProgress()
        {
            ClearSavedProgress();
            if (Application.isPlaying) resetPending = true;
        }

        internal static void ClearSavedProgress()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
        }

        public void Dispose()
        {
            foreach (ManaRift watched in watchedRifts)
                if (watched != null) watched.Resolved -= OnRiftResolved;
            foreach (AnomalyBehaviour watched in watchedOtherAnomalies)
                if (watched != null) watched.ResolutionCompleted -= OnAnomalyResolved;
            foreach (AnomalyBehaviour watched in watchedGrowthAnomalies)
                if (watched != null) watched.Ended -= OnGrowthAnomalyEnded;
            watchedRifts.Clear();
            watchedOtherAnomalies.Clear();
            watchedGrowthAnomalies.Clear();
        }
    }
}
