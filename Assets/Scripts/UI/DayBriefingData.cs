using System;
using TMPro;
using UnityEngine;

namespace WardShift
{
    /// <summary>DAY별 운영 기록과 브리핑 UI에서 사용할 폰트를 보관합니다.</summary>
    [CreateAssetMenu(fileName = "DayBriefingData", menuName = "WARD SHIFT/Day Briefing Data")]
    public sealed class DayBriefingData : ScriptableObject
    {
        [Serializable]
        public sealed class DayRecord
        {
            [SerializeField, Range(1, 7)] private int day = 1;
            [SerializeField] private string recordType;
            [SerializeField] private string title;
            [SerializeField, TextArea(6, 14)] private string body;
            [SerializeField, TextArea(2, 4)] private string caution;
            [SerializeField, TextArea(4, 10)] private string sidePanelText;

            public int Day => Mathf.Clamp(day, 1, 7);
            public string RecordType => recordType ?? string.Empty;
            public string Title => title ?? string.Empty;
            public string Body => body ?? string.Empty;
            public string Caution => caution ?? string.Empty;
            public string SidePanelText => sidePanelText ?? string.Empty;
        }

        [Header("브리핑 UI 폰트")]
        [SerializeField] private TMP_FontAsset koreanFont;
        [SerializeField] private TMP_FontAsset englishFont;
        [Header("DAY별 운영 기록")]
        [SerializeField] private DayRecord[] records = Array.Empty<DayRecord>();

        public TMP_FontAsset KoreanFont => koreanFont;
        public TMP_FontAsset EnglishFont => englishFont;

        public DayRecord GetRecord(int day)
        {
            if (records == null) return null;
            foreach (DayRecord record in records)
                if (record != null && record.Day == day) return record;
            return null;
        }
    }
}
