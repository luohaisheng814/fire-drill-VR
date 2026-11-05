using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace FireSafety
{
    /// <summary>火源：火焰表现 + 灭火进度 + 熄灭状态。挂到 BaseFire000。</summary>
    public class FireSource : MonoBehaviour
    {
        /// <summary>场景中所有活跃火源，供喷射判定使用（无需碰撞体即可命中）。</summary>
        public static readonly List<FireSource> Active = new List<FireSource>();

        [Header("表现")]
        public ParticleSystem[] particles;
        public Light fireLight;
        public AudioSource fireAudio;
        public float flickerAmount = 0.35f;
        public float flickerHz = 8f;

        [Header("瞄准点（注视/喷射判定用，留空则用自身）")]
        public Transform aimTarget;

        [Header("灭火参数")]
        [Tooltip("累计喷射量达到该值即视为扑灭（extinguishRate=1 时约等于秒数）")]
        public float extinguishThreshold = 2.5f;
        [Range(0f, 1f)] public float progress01;

        [Header("事件")]
        public UnityEvent onExtinguished;

        public bool IsExtinguished { get; private set; }
        public Vector3 AimPoint => aimTarget ? aimTarget.position : transform.position;

        private float _baseIntensity;
        private float _baseVolume;

        private void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
            if (fireLight) _baseIntensity = fireLight.intensity;
            if (fireAudio) _baseVolume = fireAudio.volume;
        }

        private void OnDisable() { Active.Remove(this); }

        private void Update()
        {
            if (IsExtinguished) return;

            if (fireLight)
            {
                float f = 1f - flickerAmount * 0.5f + Mathf.PerlinNoise(Time.time * flickerHz, 0f) * flickerAmount;
                fireLight.intensity = _baseIntensity * f;
            }
            if (fireAudio) fireAudio.volume = _baseVolume * (1f - progress01 * 0.9f);
        }

        /// <summary>喷射命中时调用，amount 为有效喷射量（通常 = dt * rate）。</summary>
        public void ApplyExtinguish(float amount)
        {
            if (IsExtinguished || amount <= 0f) return;
            progress01 = Mathf.Clamp01(progress01 + amount / Mathf.Max(0.0001f, extinguishThreshold));
            if (progress01 >= 1f) Extinguish();
        }

        private void Extinguish()
        {
            IsExtinguished = true;
            progress01 = 1f;

            if (particles != null)
                foreach (var p in particles)
                    if (p) p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (fireLight) fireLight.intensity = 0f;
            if (fireAudio) fireAudio.Stop();

            Debug.Log("[Fire] 火源已被扑灭");
            onExtinguished?.Invoke();

            // 全部火源扑灭 → ⑨ 演练完成
            if (AllExtinguished())
            {
                FireDrillManager.Instance?.CompleteStep(DrillStep.FireExtinguished);
                Debug.Log("[Fire] 所有火源已扑灭，演练完成");
            }
        }

        /// <summary>场景内是否已无明火。</summary>
        public static bool AllExtinguished()
        {
            foreach (var f in Active)
                if (f != null && !f.IsExtinguished) return false;
            return true;
        }
    }
}