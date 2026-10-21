namespace WardShift
{
    /// <summary>한 번의 시전 결과. None은 아직 판정되지 않은 상태.</summary>
    public enum MagicCastResult { None, Success, Undercharged, Overcharged, Canceled }
}
