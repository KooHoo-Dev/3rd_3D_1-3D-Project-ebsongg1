using System;
using UnityEngine;
using UnityEngine.Audio;

namespace WardShift
{
    public enum WardShiftSoundId
    {
        AnomalyStageTwo,
        AnomalyStageThree,
        SpecialRiftSpawn,
        ManaDeviceRadialWarning,
        ManaDeviceTrackingWarning,
        AnomalyRupture,
        FacilityStabilityDanger,
        RiftSealComplete,
        CursePatchPurified,
        CurseAnomalyResolved,
        ManaDeviceStabilized,
        TrackingBoltLaunch,
        TrackingBoltFlightLoop,
        PlayerContaminationHit,
        PlayerMagicCast,
        PlayerMagicSelect,
        PlayerItemUse,
        PlayerFootstep,
        UiButtonClick,
        UiUpgradePurchase,
        UiSupplyPurchase,
        UiDayStart,
        UiDayClear,
        UiGameOver,
        UiEndingResult,
        TitleMusic,
        FacilityAmbient,
        EndingMusic
    }

    [Serializable]
    public sealed class WardShiftAudioEntry
    {
        [SerializeField] private WardShiftSoundId id;
        [SerializeField] private string displayName;
        [SerializeField] private AudioClip clip;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [Tooltip("2D 호출은 평면 음향으로 재생하고, 3D 호출은 아래 Spatial Blend와 거리 값을 사용합니다.")]
        [SerializeField, Range(0f, 1f)] private float spatialBlend;
        [SerializeField, Min(0.01f)] private float minDistance = 1f;
        [SerializeField, Min(0.1f)] private float maxDistance = 8f;
        [SerializeField, Min(0f)] private float duplicateSuppressionSeconds;
        [Tooltip("3D 소리는 이 곡선이 0에 도달하는 최대 거리 밖에서 무음이 됩니다.")]
        [SerializeField] private AnimationCurve distanceAttenuation = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(1f, 0f));

        public WardShiftSoundId Id => id;
        public string DisplayName => displayName;
        public AudioClip Clip => clip;
        public float Volume => Mathf.Clamp01(volume);
        public float SpatialBlend => Mathf.Clamp01(spatialBlend);
        public float MinDistance => Mathf.Max(0.01f, minDistance);
        public float MaxDistance => Mathf.Max(MinDistance + 0.1f, maxDistance);
        public float DuplicateSuppressionSeconds => Mathf.Max(0f, duplicateSuppressionSeconds);
        public AnimationCurve DistanceAttenuation => distanceAttenuation;

        public WardShiftAudioEntry(WardShiftSoundId soundId, string label, float baseVolume = 1f,
            float blend = 0f, float min = 1f, float max = 8f, float repeatDelay = 0f)
        {
            id = soundId;
            displayName = label;
            volume = baseVolume;
            spatialBlend = blend;
            minDistance = min;
            maxDistance = max;
            duplicateSuppressionSeconds = repeatDelay;
        }

        public void SetClip(AudioClip value) => clip = value;
    }

    [CreateAssetMenu(fileName = "WardShiftAudioLibrary", menuName = "WARD SHIFT/Audio Library")]
    public sealed class WardShiftAudioLibrary : ScriptableObject
    {
        [Header("Mixer 출력 그룹")]
        [SerializeField] private AudioMixerGroup masterGroup;
        [SerializeField] private AudioMixerGroup sfxGroup;
        [SerializeField] private AudioMixerGroup uiGroup;
        [SerializeField] private AudioMixerGroup bgmGroup;
        [SerializeField] private AudioMixerGroup ambientGroup;

        [Header("Warning")]
        [SerializeField] private WardShiftAudioEntry[] warnings =
        {
            new WardShiftAudioEntry(WardShiftSoundId.AnomalyStageTwo, "이상현상 2단계 경고", 0.75f, 0f, 1f, 8f, 0.15f),
            new WardShiftAudioEntry(WardShiftSoundId.AnomalyStageThree, "이상현상 3단계 경고", 0.9f, 0f, 1f, 8f, 0.15f),
            new WardShiftAudioEntry(WardShiftSoundId.SpecialRiftSpawn, "특별 마력 균열 출현", 0.85f, 0f, 1f, 8f, 0.2f),
            new WardShiftAudioEntry(WardShiftSoundId.ManaDeviceRadialWarning, "마력 장치 원형 폭주 예고", 0.8f, 1f, 1f, 8f, 0.2f),
            new WardShiftAudioEntry(WardShiftSoundId.ManaDeviceTrackingWarning, "마력 장치 추적탄 예고", 0.8f, 1f, 1f, 8f, 0.2f),
            new WardShiftAudioEntry(WardShiftSoundId.AnomalyRupture, "이상현상 파열", 0.85f, 1f, 1f, 12f, 0.2f),
            new WardShiftAudioEntry(WardShiftSoundId.FacilityStabilityDanger, "시설 안정도 위험 경고", 1f, 0f, 1f, 8f, 0.4f)
        };

        [Header("Anomaly")]
        [SerializeField] private WardShiftAudioEntry[] anomalies =
        {
            new WardShiftAudioEntry(WardShiftSoundId.RiftSealComplete, "마력 균열 봉인점 완료 (VFX 프리팹 전용)", 0.65f, 1f, 1f, 12f),
            new WardShiftAudioEntry(WardShiftSoundId.CursePatchPurified, "저주 패치 정화", 0.3f, 1f, 1f, 10f),
            new WardShiftAudioEntry(WardShiftSoundId.CurseAnomalyResolved, "저주 이상현상 전체 진압", 0.55f, 1f, 1f, 12f),
            new WardShiftAudioEntry(WardShiftSoundId.ManaDeviceStabilized, "마력 장치 안정화 완료", 0.65f, 1f, 1f, 12f),
            new WardShiftAudioEntry(WardShiftSoundId.TrackingBoltLaunch, "추적탄 발사음 (프리팹 전용)", 1f, 1f, 1f, 20f),
            new WardShiftAudioEntry(WardShiftSoundId.TrackingBoltFlightLoop, "추적탄 비행 루프 (프리팹 전용)", 1f, 1f, 1f, 20f)
        };

        [Header("Player")]
        [SerializeField] private WardShiftAudioEntry[] player =
        {
            new WardShiftAudioEntry(WardShiftSoundId.PlayerContaminationHit, "오염 피해", 1f, 0f, 1f, 8f, 0.2f),
            new WardShiftAudioEntry(WardShiftSoundId.PlayerMagicCast, "마법 시전", 1f, 0f, 1f, 8f),
            new WardShiftAudioEntry(WardShiftSoundId.PlayerMagicSelect, "마법 선택", 1f, 0f, 1f, 8f),
            new WardShiftAudioEntry(WardShiftSoundId.PlayerItemUse, "아이템 사용", 1f, 0f, 1f, 8f),
            new WardShiftAudioEntry(WardShiftSoundId.PlayerFootstep, "발소리", 1f, 1f, 1f, 12f)
        };

        [Header("UI")]
        [SerializeField] private WardShiftAudioEntry[] ui =
        {
            new WardShiftAudioEntry(WardShiftSoundId.UiButtonClick, "버튼 클릭"),
            new WardShiftAudioEntry(WardShiftSoundId.UiUpgradePurchase, "영구 강화 구매"),
            new WardShiftAudioEntry(WardShiftSoundId.UiSupplyPurchase, "보급 상점 구매"),
            new WardShiftAudioEntry(WardShiftSoundId.UiDayStart, "DAY 시작"),
            new WardShiftAudioEntry(WardShiftSoundId.UiDayClear, "DAY 클리어"),
            new WardShiftAudioEntry(WardShiftSoundId.UiGameOver, "게임 오버"),
            new WardShiftAudioEntry(WardShiftSoundId.UiEndingResult, "엔딩 결과음")
        };

        [Header("Music")]
        [SerializeField] private WardShiftAudioEntry[] music =
        {
            new WardShiftAudioEntry(WardShiftSoundId.TitleMusic, "타이틀 BGM", 1f, 0f),
            new WardShiftAudioEntry(WardShiftSoundId.EndingMusic, "엔딩 BGM", 1f, 0f)
        };

        [Header("Ambient")]
        [SerializeField] private WardShiftAudioEntry[] ambient =
        {
            new WardShiftAudioEntry(WardShiftSoundId.FacilityAmbient, "시설 환경음", 1f, 1f, 1f, 25f)
        };

        public WardShiftAudioEntry Find(WardShiftSoundId id)
        {
            return FindIn(warnings, id) ?? FindIn(anomalies, id) ?? FindIn(player, id)
                ?? FindIn(ui, id) ?? FindIn(music, id) ?? FindIn(ambient, id);
        }

        public AudioMixerGroup OutputFor(WardShiftSoundId id)
        {
            if (FindIn(warnings, id) != null || FindIn(anomalies, id) != null || FindIn(player, id) != null)
                return sfxGroup;
            if (FindIn(ui, id) != null) return uiGroup;
            if (FindIn(music, id) != null) return bgmGroup;
            if (FindIn(ambient, id) != null) return ambientGroup;
            return masterGroup;
        }

        public void AssignClip(WardShiftSoundId id, AudioClip clip)
        {
            WardShiftAudioEntry entry = Find(id);
            if (entry != null) entry.SetClip(clip);
        }

        public void AssignMixerGroups(AudioMixerGroup master, AudioMixerGroup sfx,
            AudioMixerGroup uiGroupValue, AudioMixerGroup bgm, AudioMixerGroup ambientValue)
        {
            masterGroup = master;
            sfxGroup = sfx;
            uiGroup = uiGroupValue;
            bgmGroup = bgm;
            ambientGroup = ambientValue;
        }

        private static WardShiftAudioEntry FindIn(WardShiftAudioEntry[] entries, WardShiftSoundId id)
        {
            if (entries == null) return null;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i] != null && entries[i].Id == id) return entries[i];
            return null;
        }
    }
}
