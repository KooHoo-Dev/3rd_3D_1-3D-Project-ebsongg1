using System;

namespace WardShift
{
    /// <summary>Unity 오브젝트 없이 한 DAY의 진행 상태와 다음 DAY 계획을 보관한다.</summary>
    public sealed class DaySession
    {
        public int CurrentDay { get; private set; }
        public DayState State { get; private set; } = DayState.Preparing;
        public float RemainingSeconds { get; private set; }
        public bool IsCleaningUp { get; private set; }
        public int DayRandomSeed { get; private set; }
        public FloodCondition ActiveFloodCondition { get; private set; }
        public FloodCondition NextDayFloodCondition { get; private set; }
        public FloodCondition PreviousDayFloodCondition { get; private set; }

        private int plannedNextDayNumber;
        private int plannedNextDayRandomSeed;

        public DaySession(int startingDay)
        {
            CurrentDay = Math.Max(1, startingDay);
        }

        public void EnsureDaySeed(int seed)
        {
            if (DayRandomSeed == 0) DayRandomSeed = seed;
        }

        public void SelectConditionForDay(bool isRetry, FloodCondition selectedCondition)
        {
            if (!isRetry) ActiveFloodCondition = selectedCondition;
        }

        public void BeginDay(float durationSeconds)
        {
            RemainingSeconds = Math.Max(1f, durationSeconds);
            IsCleaningUp = false;
            State = DayState.Playing;
        }

        public void AdvanceTime(float deltaSeconds)
        {
            if (State == DayState.Playing && RemainingSeconds > 0f)
                RemainingSeconds = Math.Max(0f, RemainingSeconds - deltaSeconds);
        }

        public void BeginCleanup()
        {
            if (State == DayState.Playing && RemainingSeconds <= 0f)
                IsCleaningUp = true;
        }

        public void CompleteDay(int finalDay)
        {
            State = CurrentDay >= finalDay ? DayState.Ending : DayState.DayClear;
            IsCleaningUp = false;
            PreviousDayFloodCondition = ActiveFloodCondition;
            ActiveFloodCondition = FloodCondition.None;
            RemainingSeconds = 0f;
        }

        public void FailDay()
        {
            State = DayState.DayFailed;
            IsCleaningUp = false;
        }

        public void PrepareRetry()
        {
            State = DayState.Preparing;
            IsCleaningUp = false;
        }

        public void PlanNextDay(int nextDay, int seed, FloodCondition condition)
        {
            plannedNextDayNumber = nextDay;
            plannedNextDayRandomSeed = seed;
            NextDayFloodCondition = condition;
        }

        public void ClearNextDayPlan()
        {
            plannedNextDayNumber = 0;
            plannedNextDayRandomSeed = 0;
            NextDayFloodCondition = FloodCondition.None;
        }

        public void MoveToNextDay(int finalDay, Func<int> createFallbackSeed)
        {
            CurrentDay = Math.Min(CurrentDay + 1, finalDay);
            DayRandomSeed = plannedNextDayNumber == CurrentDay
                ? plannedNextDayRandomSeed : createFallbackSeed();
            ClearNextDayPlan();
            State = DayState.Preparing;
        }

        public void RestoreConditions(FloodCondition active, FloodCondition previous)
        {
            ActiveFloodCondition = active;
            PreviousDayFloodCondition = previous;
        }

#if UNITY_EDITOR
        public void SetDayAndStateForTest(int day, DayState state)
        {
            CurrentDay = Math.Max(1, day);
            State = state;
        }

        public void SetRemainingSecondsForTest(float seconds) => RemainingSeconds = seconds;
        public void SetActiveFloodConditionForTest(FloodCondition condition) => ActiveFloodCondition = condition;
#endif
    }
}
