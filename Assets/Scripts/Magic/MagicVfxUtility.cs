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
            if (prefab == null) return null;

            GameObject instance = Object.Instantiate(prefab, position, rotation);
            Prepare(instance);
            PlayParticles(instance);
            Object.Destroy(instance, Mathf.Max(MinimumLifetime, lifetime));
            return instance;
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
