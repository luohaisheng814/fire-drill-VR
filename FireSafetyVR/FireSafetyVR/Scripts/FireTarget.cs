using UnityEngine;
using UnityEngine.Events;

namespace FireSafetyVR
{

    /// <summary>
    /// 火焰本体：带生命值，只有"灭火口对准火焰根部并喷射"时才掉血，归零后熄灭。
    /// 挂在场景里的 BaseFire000（或火焰粒子系统的父物体）上。
    /// </summary>
    public class FireTarget : MonoBehaviour
    {
        [Header("生命值")]
        public float maxHealth = 100f;
        [Tooltip("喷射 DPS 为 40 时，100 血约 2.5 秒熄灭")]
        public float health = 100f;

        [Header("根部判定")]
        [Tooltip("火焰根部：贴地的空物体，灭火口必须指向它")]
        public Transform rootPoint;
        public float rootRadius = 0.30f;

        [Header("表现")]
        public ParticleSystem fireParticle;
        public ParticleSystem smokeParticle;   // 熄灭瞬间的余烟
        public AudioSource fireAudio;          // 火焰燃烧循环音
        public Light fireLight;
        [Tooltip("血量归零时缩到多小（0 = 完全消失）")]
        public float minScale = 0f;
        public float smokeAfterExtinguish = 3f;

        [Header("事件")]
        public UnityEvent onExtinguished;

        public bool Extinguished { get; private set; }
        public float Health01 => Mathf.Clamp01(health / Mathf.Max(0.001f, maxHealth));

        Vector3 initialScale;
        float baseEmission;
        float baseLightIntensity;
        float baseAudioVolume;
        float extinguishTimer = -1f;

        void Awake()
        {
            if (rootPoint == null) rootPoint = transform;
            initialScale = transform.localScale;
            health = maxHealth;

            if (fireParticle != null)
            {
                baseEmission = fireParticle.emission.rateOverTime.constant;
                var main = fireParticle.main;
                main.playOnAwake = true;
                if (!fireParticle.isPlaying) fireParticle.Play();
            }
            if (fireLight != null) baseLightIntensity = fireLight.intensity;
            if (fireAudio != null)
            {
                baseAudioVolume = fireAudio.volume;
                if (!fireAudio.isPlaying) fireAudio.Play();
            }
        }

        void Update()
        {
            if (extinguishTimer > 0f)
            {
                extinguishTimer -= Time.deltaTime;
                if (extinguishTimer <= 0f && smokeParticle != null && smokeParticle.isPlaying)
                    smokeParticle.Stop();
            }
        }

        /// <summary>
        /// 灭火口 tip 是否正对火焰根部（距离 + 角度 + 根部半径）。
        /// 由 FireExtinguisher 每帧调用。
        /// </summary>
        public bool IsAimedBy(Transform tip, float maxDistance, float maxAngle)
        {
            if (Extinguished || tip == null) return false;

            Vector3 toRoot = rootPoint.position - tip.position;
            float dist = toRoot.magnitude;
            if (dist > maxDistance) return false;
            if (Vector3.Angle(tip.forward, toRoot.normalized) > maxAngle) return false;

            // 更严格：射线终点与根部的偏差需在 rootRadius 内
            Vector3 hitPoint = tip.position + tip.forward * dist;
            if (Vector3.Distance(hitPoint, rootPoint.position) > rootRadius) return false;

            return true;
        }

        public void ApplyDamage(float amount)
        {
            if (Extinguished || amount <= 0f) return;
            health = Mathf.Max(0f, health - amount);
            ApplyVisual();
            if (health <= 0.0001f) Extinguish();
        }

        void ApplyVisual()
        {
            float k = Health01;

            if (minScale > 0f)
                transform.localScale = Vector3.Lerp(Vector3.one * minScale, initialScale, k);
            else
                transform.localScale = Vector3.Lerp(Vector3.one * 0.05f, initialScale, k);

            if (fireParticle != null)
            {
                var em = fireParticle.emission;
                em.rateOverTime = baseEmission * Mathf.Lerp(0.05f, 1f, k);
            }
            if (fireLight != null) fireLight.intensity = baseLightIntensity * k;
            if (fireAudio != null) fireAudio.volume = baseAudioVolume * k;
        }

        public void Extinguish()
        {
            if (Extinguished) return;
            Extinguished = true;
            health = 0f;

            if (fireParticle != null)
            {
                fireParticle.Stop();
                var em = fireParticle.emission;
                em.rateOverTime = 0f;
            }
            if (fireAudio != null) fireAudio.Stop();
            if (fireLight != null) fireLight.intensity = 0f;

            if (smokeParticle != null)
            {
                smokeParticle.Play();
                extinguishTimer = smokeAfterExtinguish;
            }

            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayAt(AudioManager.Instance.extinguishClip, transform.position, 1f);

            // 延迟一点再消失，让余烟可见；也可直接在 Inspector 里把 minScale 设 0
            if (minScale <= 0f) Destroy(gameObject, smokeAfterExtinguish + 0.2f);

            onExtinguished?.Invoke();
        }

        void OnDrawGizmosSelected()
        {
            if (rootPoint == null) return;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(rootPoint.position, rootRadius);
        }
    }
}
