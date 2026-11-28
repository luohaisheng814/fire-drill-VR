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

        [Header("阶段门控")]
        [Tooltip("为 true 时喷射不推进灭火进度（例：电气设备火必须先切断电源）")]
        public bool extinguishLocked = false;
        [Tooltip("被锁定时给玩家的说明")]
        public string lockHint = "先切断电源，再灭火";
        [Tooltip("开始界面点「开始演练」时是否自动点燃（设备火 / 出口火由流程脚本显式点燃，应设为 false）")]
        public bool igniteFromStartScreen = true;
        [Tooltip("场景初始就是未点燃状态（练习火在「操作演练」时才点燃）")]
        public bool startUnlit = false;
        [Tooltip("喷射被锁定拦截时广播（节流 0.8 秒一次），供流程脚本提示玩家")]
        public UnityEvent onExtinguishBlocked = new UnityEvent();

        [Header("事件")]
        public UnityEvent onExtinguished;

        public bool IsExtinguished { get; private set; }
        /// <summary>当前是否处于燃烧状态。未点燃的火源不参与「全部火源已扑灭」判定。</summary>
        public bool IsBurning { get; private set; } = true;
        public Vector3 AimPoint => aimTarget ? aimTarget.position : transform.position;

        private float _baseIntensity;
        private float _baseVolume;
        private float _lastBlockedLog = -999f;

        private void Awake()
        {
            if (startUnlit)
            {
                IsBurning = false;
                ClearFlame();          // 场景重新加载时粒子会 playOnAwake 复燃，这里强制收掉
            }
        }

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

            // 电气设备火：未断电前喷射只冒白烟不灭火，并给出「先切断电源」的提示
            if (extinguishLocked)
            {
                if (Time.unscaledTime - _lastBlockedLog > 0.8f)
                {
                    _lastBlockedLog = Time.unscaledTime;
                    Debug.Log("[Fire] 火源仍带电，喷射无效：" + lockHint);
                    onExtinguishBlocked?.Invoke();
                }
                return;
            }

            progress01 = Mathf.Clamp01(progress01 + amount / Mathf.Max(0.0001f, extinguishThreshold));
            if (progress01 >= 1f) Extinguish();
        }

        /// <summary>切断电源后解锁灭火（由 PowerSwitch / 流程脚本调用）。</summary>
        public void SetExtinguishLocked(bool locked)
        {
            extinguishLocked = locked;
            if (!locked) Debug.Log("[Fire] 电源已切断，火源解除锁定，可以喷射灭火。");
        }

        /// <summary>显式点燃：把进度清零、粒子 / 灯光重新打开（设备火、出口火用）。</summary>
        public void IgniteNow()
        {
            IsExtinguished = false;
            IsBurning = true;
            progress01 = 0f;

            if (!gameObject.activeSelf) gameObject.SetActive(true);

            if (particles != null)
            {
                foreach (var p in particles)
                {
                    if (p == null) continue;
                    p.Clear();
                    p.Play();
                }
            }
            foreach (var p in GetComponentsInChildren<ParticleSystem>(true))
            {
                if (p != null && !p.isPlaying) p.Play();
            }

            if (fireLight != null)
            {
                fireLight.enabled = true;
                if (_baseIntensity <= 0.01f) _baseIntensity = 2.2f;
                fireLight.intensity = _baseIntensity;
            }

            Debug.Log("[Fire] 已点燃 " + name + "（灭火进度归零）");
        }

        /// <summary>直接判定扑灭（流程切换时收掉旧火源用；正常灭火走 ApplyExtinguish）。</summary>
        public void Extinguish()
        {
            IsExtinguished = true;
            IsBurning = false;
            progress01 = 1f;

            // 第 50 轮：装饰火（FlameDecor_*）不在 particles 数组里，必须统一停掉自身与全部子粒子，
            // 否则主火血量归零后，周围几簇小火还会继续烧。
            ClearFlame();

            Debug.Log("[Fire] 火源已被扑灭");
            onExtinguished?.Invoke();

            // 全部火源扑灭 → ⑨ 演练完成
            if (AllExtinguished())
            {
                FireDrillManager.Instance?.CompleteStep(DrillStep.FireExtinguished);
                Debug.Log("[Fire] 所有火源已扑灭，演练完成");
            }
        }

        /// <summary>场景内是否已无明火（只统计正在燃烧的火源）。</summary>
        public static bool AllExtinguished()
        {
            foreach (var f in Active)
                if (f != null && f.IsBurning && !f.IsExtinguished) return false;
            return true;
        }

        /// <summary>
        /// 装配 / 初始化用：把火源置为「未点燃」——进度清零、明火全停，但不触发任何事件。
        /// 操作演练 / 正式阶段开始时再由 IgniteNow() 点燃。
        /// </summary>
        public void PrepareAsUnlit()
        {
            IsBurning = false;
            IsExtinguished = false;
            progress01 = 0f;
            startUnlit = true;
            ClearFlame();
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