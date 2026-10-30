using UnityEngine;

namespace WardShift
{
    /// <summary>마법 이펙트 인스턴스가 물리 판정에 끼어들지 않도록 준비합니다.</summary>
    internal static class MagicVfxUtility
    {
        private const float MinimumLifetime = 0.1f;

        public static GameObject SpawnAttached(GameObject prefab, Transform anchor)
        {
            if (prefab == null || anchor == null) return null;

            GameObject instance = Object.Instantiate(prefab, anchor, false);
            instance.transform.localPosition = Vector3.zero;
            Prepare(instance);
            PlayParticles(instance);
            return instance;
        }

        public static GameObject SpawnDetached(GameObject prefab, Vector3 position,
            Quaternion rotation, float lifetime)
        {
            return SpawnDetached(prefab, position, rotation, lifetime, false);
        }

        public static GameObject SpawnDetached(GameObject prefab, Vector3 position,
            Quaternion rotation, float lifetime, bool useUnscaledLifetime)
        {
            if (prefab == null) return null;

            GameObject instance = Object.Instantiate(prefab, position, rotation);
            Prepare(instance);
            PlayParticles(instance);
            float safeLifetime = Mathf.Max(MinimumLifetime, lifetime);
            if (useUnscaledLifetime)
                instance.AddComponent<DetachedVfxLifetime>().Begin(safeLifetime);
            else
                Object.Destroy(instance, safeLifetime);
            return instance;
        }

        public static GameObject SpawnPurificationMotesAttached(Transform anchor, Color color,
            float emissionRate, float particleSize, float radius)
        {
            if (anchor == null) return null;

            GameObject instance = new GameObject("PurificationMotes");
            instance.transform.SetParent(anchor, false);
            instance.transform.localPosition = Vector3.up * 0.08f;
            instance.transform.localRotation = Quaternion.identity;

            ParticleSystem particles = instance.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.75f;
            main.startSpeed = 0.16f;
            main.startSize = Mathf.Max(0.005f, particleSize);
            main.startColor = new ParticleSystem.MinMaxGradient(color);
            main.maxParticles = Mathf.Max(24, Mathf.CeilToInt(Mathf.Max(0f, emissionRate) * 2f));

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = Mathf.Max(0f, emissionRate);
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.01f, radius);
            shape.radiusThickness = 0.2f;

            Prepare(instance);
            particles.Play(false);
            return instance;
        }

        public static GameObject SpawnParticleBurst(Vector3 position, Quaternion rotation, Color color,
            int particleCount, float particleSize, float lifetime, float speed, float radius)
        {
            int count = Mathf.Max(1, particleCount);
            float duration = Mathf.Max(MinimumLifetime, lifetime);
            GameObject instance = new GameObject("MagicCompletionBurst");
            instance.transform.SetPositionAndRotation(position, rotation);

            ParticleSystem particles = instance.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.useUnscaledTime = true;
            main.duration = duration;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = duration;
            main.startSpeed = Mathf.Max(0f, speed);
            main.startSize = Mathf.Max(0.005f, particleSize);
            main.startColor = new ParticleSystem.MinMaxGradient(color);
            main.maxParticles = count;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.01f, radius);
            shape.radiusThickness = 1f;

            Gradient fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

            Prepare(instance);
            particles.Emit(count);
            particles.Play(false);
            instance.AddComponent<DetachedVfxLifetime>().Begin(duration + 0.15f);
            return instance;
        }

        public static void UseUnscaledParticleTime(GameObject effect)
        {
            if (effect == null) return;
            foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particles.main;
                main.useUnscaledTime = true;
            }
        }

        public static void PlayDetachedSound(AudioClip clip, Vector3 position, float volume)
        {
            if (clip == null || volume <= 0f) return;
            AudioSource.PlayClipAtPoint(clip, position, Mathf.Clamp01(volume));
        }

        public static void Prepare(GameObject instance)
        {
            if (instance == null) return;

            foreach (Collider effectCollider in instance.GetComponentsInChildren<Collider>(true))
                effectCollider.enabled = false;
            foreach (Collider2D effectCollider in instance.GetComponentsInChildren<Collider2D>(true))
                effectCollider.enabled = false;

            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycastLayer >= 0) SetLayerRecursively(instance.transform, ignoreRaycastLayer);
        }

        public static void StopAndDestroy(GameObject instance)
        {
            if (instance == null) return;

            foreach (ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            instance.SetActive(false);
            Object.Destroy(instance);
        }

        private static void PlayParticles(GameObject instance)
        {
            foreach (ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>(true))
                particles.Play(false);
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }
    }
}
