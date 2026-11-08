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

        /// <summary>
        /// 只清掉视觉上的明火（粒子 + 灯光 + 声音），不改变灭火进度、不触发事件。
        /// 用于「弹面板/结束时火焰必须立刻消失」这类表现诉求。
        /// </summary>
        public void ClearFlame()
        {
            if (particles != null)
                foreach (var p in particles)
                    if (p) p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // 不依赖序列化数组：把自身和所有子物体的粒子系统一并停掉，
            // 否则 Smoke001 / Fire002 这类「没登记进数组」的子系统会继续烧，面板弹出后火焰还在
            foreach (var p in GetComponentsInChildren<ParticleSystem>(true))
                p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var l in GetComponentsInChildren<Light>(true))
                l.intensity = 0f;

            if (fireLight) fireLight.intensity = 0f;
            if (fireAudio) fireAudio.Stop();
        }

        /// <summary>清掉场景里所有火源的明火表现（含未登记在 Active 的粒子系统）。</summary>
        public static void ClearAllFlames()
        {
            var all = FindObjectsByType<FireSource>(FindObjectsSortMode.None);
            foreach (var f in all)
                if (f != null) f.ClearFlame();

            var allIncludingInactive = Resources.FindObjectsOfTypeAll<FireSource>();
            foreach (var f in allIncludingInactive)
                if (f != null && f.gameObject.scene.IsValid()) f.ClearFlame();
        }
    }
}