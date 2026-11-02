using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WardShift
{
    /// <summary>중앙 클립 조회와 AudioMixer 라우팅만 담당하는 가벼운 재생 서비스입니다.</summary>
    [DisallowMultipleComponent]
    public sealed class AudioService : MonoBehaviour
    {
        private sealed class Voice
        {
            public AudioSource Source;
            public double BusyUntilDspTime;
        }

        private static readonly AnimationCurve SilentAtMaxDistance = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        private static readonly List<Voice> Voices = new List<Voice>();
        private static readonly Dictionary<long, float> LastPlayedAt = new Dictionary<long, float>();
        private static AudioService instance;
        private static bool bootstrapAttempted;

        [SerializeField] private WardShiftAudioLibrary library;
        private AudioSource loopSource;
        private WardShiftSoundId currentLoopSoundId;
        private bool hasLoopSound;

        public static AudioService Instance => instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            bootstrapAttempted = false;
            Voices.Clear();
            LastPlayedAt.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureInstance();
        }

        public static bool Play2D(WardShiftSoundId id)
        {
            return Play2D(id, 1f);
        }

        /// <summary>특정 UI/입력 재생에만 적용할 볼륨 배수를 전달합니다.</summary>
        public static bool Play2D(WardShiftSoundId id, float volumeMultiplier)
        {
            AudioService service = EnsureInstance();
            return service != null && service.Play(id, Vector3.zero, false, 0, volumeMultiplier);
        }

        public static bool Play3DAt(WardShiftSoundId id, Vector3 position, int emitterInstanceId = 0)
        {
            AudioService service = EnsureInstance();
            return service != null && service.Play(id, position, true, emitterInstanceId, 1f);
        }

        public static bool PlayLoop(WardShiftSoundId id)
        {
            AudioService service = EnsureInstance();
            return service != null && service.PlayLoopInternal(id);
        }

        public static bool StopLoop(WardShiftSoundId id)
        {
            return instance != null && instance.StopLoopInternal(id);
        }

        private static AudioService EnsureInstance()
        {
            if (instance != null) return instance;
            if (bootstrapAttempted && !Application.isPlaying) return null;
            bootstrapAttempted = true;

            AudioService existing = FindFirstObjectByType<AudioService>(FindObjectsInactive.Include);
            if (existing != null)
            {
                existing.Initialize();
                return instance;
            }

            if (!Application.isPlaying) return null;
            GameObject serviceObject = new GameObject("WardShiftAudioService");
            return serviceObject.AddComponent<AudioService>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Initialize();
        }

        private void Initialize()
        {
            instance = this;
            if (library == null) library = Resources.Load<WardShiftAudioLibrary>("WardShiftAudioLibrary");
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }

        private bool Play(WardShiftSoundId id, Vector3 position, bool spatial, int emitterInstanceId,
            float volumeMultiplier)
        {
            if (library == null) library = Resources.Load<WardShiftAudioLibrary>("WardShiftAudioLibrary");
            if (library == null) return false;

            WardShiftAudioEntry entry = library.Find(id);
            if (entry == null || entry.Clip == null || entry.Volume <= 0f) return false;
            if (!PassRepeatSuppression(entry, emitterInstanceId)) return false;

            Voice voice = GetVoice();
            AudioSource source = voice.Source;
            source.Stop();
            source.transform.position = spatial ? position : transform.position;
            source.outputAudioMixerGroup = library.OutputFor(id);
            source.clip = entry.Clip;
            source.loop = false;
            source.playOnAwake = false;
            source.spatialBlend = spatial ? entry.SpatialBlend : 0f;
            source.minDistance = entry.MinDistance;
            source.maxDistance = entry.MaxDistance;
            source.rolloffMode = AudioRolloffMode.Custom;
            AnimationCurve curve = entry.DistanceAttenuation;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff,
                curve != null && curve.length > 0 ? curve : SilentAtMaxDistance);
            source.dopplerLevel = 0f;
            source.volume = entry.Volume * Mathf.Clamp01(volumeMultiplier);
            double playbackStartedAt = AudioSettings.dspTime;
            float fullDuration = Mathf.Max(0.02f,
                entry.Clip.length / Mathf.Max(0.01f, Mathf.Abs(source.pitch)));
            float playbackDuration = entry.MaxPlaybackDurationSeconds > 0f
                ? Mathf.Min(fullDuration, Mathf.Max(0.02f, entry.MaxPlaybackDurationSeconds))
                : fullDuration;
            source.Play();
            if (playbackDuration < fullDuration)
                StartCoroutine(StopPlaybackAfter(source, entry.Clip, playbackDuration));
            voice.BusyUntilDspTime = playbackStartedAt + playbackDuration;
            return true;
        }

        private IEnumerator StopPlaybackAfter(AudioSource source, AudioClip clip, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (source != null && source.clip == clip && source.isPlaying)
                source.Stop();
        }

        private bool PlayLoopInternal(WardShiftSoundId id)
        {
            if (library == null) library = Resources.Load<WardShiftAudioLibrary>("WardShiftAudioLibrary");
            if (library == null) return false;

            WardShiftAudioEntry entry = library.Find(id);
            if (entry == null || entry.Clip == null || entry.Volume <= 0f) return false;
            if (loopSource != null && loopSource.isPlaying && hasLoopSound
                && currentLoopSoundId == id && loopSource.clip == entry.Clip)
                return true;

            if (loopSource == null)
            {
                GameObject sourceObject = new GameObject("WardShiftLoopSource");
                sourceObject.transform.SetParent(transform, false);
                loopSource = sourceObject.AddComponent<AudioSource>();
                loopSource.playOnAwake = false;
                loopSource.priority = 64;
            }

            loopSource.Stop();
            loopSource.clip = entry.Clip;
            loopSource.outputAudioMixerGroup = library.OutputFor(id);
            loopSource.loop = true;
            loopSource.spatialBlend = 0f;
            loopSource.volume = entry.Volume;
            loopSource.Play();
            currentLoopSoundId = id;
            hasLoopSound = true;
            return true;
        }

        private bool StopLoopInternal(WardShiftSoundId id)
        {
            if (loopSource == null || !hasLoopSound || currentLoopSoundId != id) return false;
            loopSource.Stop();
            loopSource.clip = null;
            loopSource.loop = false;
            currentLoopSoundId = default;
            hasLoopSound = false;
            return true;
        }

        private static bool PassRepeatSuppression(WardShiftAudioEntry entry, int emitterInstanceId)
        {
            float interval = entry.DuplicateSuppressionSeconds;
            if (interval <= 0f) return true;

            long key = ((long)(int)entry.Id << 32) ^ (uint)emitterInstanceId;
            float now = Time.unscaledTime;
            if (LastPlayedAt.TryGetValue(key, out float lastTime) && now - lastTime < interval)
                return false;

            LastPlayedAt[key] = now;
            return true;
        }

        private Voice GetVoice()
        {
            double now = AudioSettings.dspTime;
            for (int i = 0; i < Voices.Count; i++)
            {
                Voice candidate = Voices[i];
                if (candidate.Source == null)
                {
                    Voices.RemoveAt(i--);
                    continue;
                }
                if (candidate.BusyUntilDspTime <= now && !candidate.Source.isPlaying)
                    return candidate;
            }

            GameObject sourceObject = new GameObject("WardShiftAudioVoice");
            sourceObject.transform.SetParent(transform, false);
            AudioSource audioSource = sourceObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.priority = 128;
            Voice voice = new Voice { Source = audioSource };
            Voices.Add(voice);
            return voice;
        }
    }
}
