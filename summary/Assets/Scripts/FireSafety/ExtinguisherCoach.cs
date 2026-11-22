using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 第 35 轮：灭火器交互教练。
    /// 在玩家「点击灭火器 / 拿起灭火器靠近火源 / 喷了一阵没灭」时，给出具体原因：
    /// 太远、太近、喷流偏了、被家具挡住、火还带电未断电、保险销没拔……
    /// 文案同时推给玩家视角提示条和桌面 HUD。
    /// </summary>
    [RequireComponent(typeof(Extinguisher))]
    public class ExtinguisherCoach : MonoBehaviour
    {
        [Header("拾取")]
        [Tooltip("鼠标/射线可以拿取的最远距离（米）")]
        public float pickupRange = 2.2f;
        [Tooltip("离瓶身过近、别说“太远”的阈值（米）")]
        public float pickupTooClose = 0.15f;

        [Header("喷射距离判定")]
        [Tooltip("小于该距离算“太近”，热浪会灼伤")]
        public float safeMinDistance = 1.2f;
        [Tooltip("大于有效射程算“太远”；实际射程仍以 Extinguisher.sprayRange 为准")]
        public float goodMaxDistance = 3.0f;
        [Tooltip("检查遮挡")]
        public bool checkOcclusion = true;

        [Header("节奏")]
        [Tooltip("同一句话最短重复间隔（秒）")]
        public float repeatInterval = 2.6f;
        [Tooltip("进入射程内巡视频率（秒）")]
        public float evaluateInterval = 0.35f;

        public enum FireBand
        {
            None, NoFire, TooFar, TooClose, Misaligned, Blocked, Locked, Good, Extinguished
        }

        public FireBand Band { get; private set; } = FireBand.None;
        public float LastDistance { get; private set; }
        public FireSource LastFire { get; private set; }
        /// <summary>问答面板打开时临时静音：避免距离提示条盖住题目，或与面板里的距离行重复。</summary>
        public bool HintsSuppressed { get; set; }

        Extinguisher _ext;
        Camera _cam;
        float _nextEvaluate;
        string _lastKey;
        float _nextAllowed;
        bool _goodReported;
        bool _extinguishedReported;
        FireSource _sprayFire;
        float _sprayStartedAt;
        float _sprayStartProgress;

        void Awake()
        {
            _ext = GetComponent<Extinguisher>();
            if (_ext.grab != null) _ext.grab.GrabRejected += OnGrabRejected;
        }

        void OnDestroy()
        {
            if (_ext != null && _ext.grab != null) _ext.grab.GrabRejected -= OnGrabRejected;
        }

        void Update()
        {
            if (_ext == null || _ext.grab == null || !_ext.grab.IsHeld) { ResetState(); return; }
            if (Time.unscaledTime < _nextEvaluate) return;
            _nextEvaluate = Time.unscaledTime + evaluateInterval;

            if (_ext.IsSpraying && _sprayFire == null)
            {
                _sprayFire = NearestBurningFire(out _);
                _sprayStartedAt = Time.unscaledTime;
                _sprayStartProgress = _sprayFire != null ? _sprayFire.progress01 : 0f;
            }
            if (!_ext.IsSpraying) _sprayFire = null;

            FireBand band = Evaluate(out string message, out float distance, out FireSource fire);
            Band = band;
            LastDistance = distance;
            LastFire = fire;

            if (band == FireBand.NoFire || band == FireBand.None) return;

            // 只在「进入新状态」或超过重复间隔时提示，避免每 0.35 秒刷屏
            string key = band + "|" + Mathf.RoundToInt(distance * 10f);
            if (key == _lastKey && Time.unscaledTime < _nextAllowed) return;
            _lastKey = key;
            _nextAllowed = Time.unscaledTime + repeatInterval;

            Show(message);
        }

        void ResetState()
        {
            Band = FireBand.None;
            _lastKey = null;
            _goodReported = false;
            _extinguishedReported = false;
            _sprayFire = null;
        }

        /// <summary>给探针/自检调用的即时评估，不依赖 Update 节奏。</summary>
        public FireBand EvaluateNow(out string message, out float distance)
        {
            var band = Evaluate(out message, out distance, out _);
            Band = band;
            LastDistance = distance;
            return band;
        }

        FireBand Evaluate(out string message, out float distance, out FireSource fire)
        {
            message = "";
            distance = 0f;
            fire = NearestBurningFire(out distance);

            if (fire == null)
            {
                bool anyExtinguished = _ext != null && _lastFireWasBurning;
                if (anyExtinguished && !_extinguishedReported)
                {
                    _extinguishedReported = true;
                    message = DrillLocale.T("火已经扑灭了，放下灭火器撤离即可。", "Fire is out. Put the extinguisher down and evacuate.");
                    return FireBand.Extinguished;
                }
                message = DrillLocale.T("附近没有明火：先把灭火器拿到起火点旁。", "No fire nearby. Bring the extinguisher to the fire.");
                return FireBand.NoFire;
            }

            _lastFireWasBurning = true;
            _extinguishedReported = false;

            if (fire.extinguishLocked)
            {
                message = DrillLocale.T(
                    "火还带电，喷了也不会灭：" + fire.lockHint,
                    "The fire is still energized — spraying won't work. Cut the power first.");
                return FireBand.Locked;
            }

            if (!_ext.nozzle)
            {
                message = DrillLocale.T("灭火器没有喷嘴引用，无法判定喷射方向。", "The extinguisher has no nozzle reference.");
                return FireBand.Misaligned;
            }

            Vector3 origin = _ext.nozzle.position;
            Vector3 toFire = fire.AimPoint - origin;
            distance = toFire.magnitude;
            if (distance > _ext.sprayRange)
            {
                message = DrillLocale.T(
                    "距离火源 " + distance.ToString("F1") + " 米，太远了（有效射程 " + _ext.sprayRange.ToString("F1") + " 米）→ 再走近一步。",
                    "Too far: " + distance.ToString("F1") + " m to the fire (effective range " + _ext.sprayRange.ToString("F1") + " m). Step closer.");
                return FireBand.TooFar;
            }

            if (distance < safeMinDistance)
            {
                message = DrillLocale.T(
                    "距离火源 " + distance.ToString("F1") + " 米，太近了 → 后退到约 1.6~2.2 米，避免热浪灼伤。",
                    "Too close: " + distance.ToString("F1") + " m. Back off to about 1.6–2.2 m.");
                return FireBand.TooClose;
            }

            if (checkOcclusion)
            {
                Vector3 dir = toFire.normalized;
                if (Physics.Raycast(origin, dir, out var hit, distance, _ext.blockingMask, QueryTriggerInteraction.Ignore))
                {
                    bool isFire = hit.transform == fire.transform || hit.transform.IsChildOf(fire.transform)
                                  || hit.transform == _ext.transform || hit.transform.IsChildOf(_ext.transform);
                    if (!isFire)
                    {
                        message = DrillLocale.T(
                            "喷流被「" + hit.collider.name + "」挡住了（距喷嘴 " + hit.distance.ToString("F1") + " 米）→ 换个角度。",
                            "Spray is blocked by \"" + hit.collider.name + "\" (" + hit.distance.ToString("F1") + " m). Move to a clear line.");
                        return FireBand.Blocked;
                    }
                }
            }

            float angle = Vector3.Angle(_ext.nozzle.forward, toFire);
            if (angle > _ext.sprayAngle)
            {
                message = DrillLocale.T(
                    "喷流偏了 " + Mathf.RoundToInt(angle) + "°（有效喷锥 ±" + Mathf.RoundToInt(_ext.sprayAngle) + "°）→ 把喷嘴对准火焰根部。",
                    "Spray is off by " + Mathf.RoundToInt(angle) + "° (cone ±" + Mathf.RoundToInt(_ext.sprayAngle) + "°). Aim at the base of the flame.");
                return FireBand.Misaligned;
            }

            // 已经喷了 1 秒但火没掉血：不是距离问题，就是锁定/灭火剂不对
            if (_ext.IsSpraying && _sprayFire == fire &&
                Time.unscaledTime - _sprayStartedAt > 1.0f &&
                fire.progress01 - _sprayStartProgress < 0.03f)
            {
                message = DrillLocale.T(
                    "喷流打到了，但火焰血量没下降：检查是否断电，或换用对路的灭火剂。",
                    "The spray is hitting but the fire HP is not dropping. Check the power or use the correct extinguisher type.");
                return FireBand.Blocked;
            }

            if (!_goodReported)
            {
                _goodReported = true;
                message = DrillLocale.T(
                    "距离合适（" + distance.ToString("F1") + " 米）→ 按住压把持续 2~3 秒。",
                    "Good distance (" + distance.ToString("F1") + " m). Hold the lever for 2–3 seconds.");
            }
            return FireBand.Good;
        }

        bool _lastFireWasBurning;

        FireSource NearestBurningFire(out float distance)
        {
            distance = 0f;
            if (_ext == null || _ext.nozzle == null) return null;

            FireSource best = null;
            float bestDist = float.MaxValue;
            foreach (var f in FireSource.Active)
            {
                if (f == null || !f.IsBurning || f.IsExtinguished) continue;
                float d = Vector3.Distance(_ext.nozzle.position, f.AimPoint);
                if (d < bestDist) { bestDist = d; best = f; }
            }
            distance = best != null ? bestDist : 0f;
            return best;
        }

        /// <summary>鼠标右键 / 手柄扳机射线点中灭火器时调用；返回是否可以继续进入问答/拿起。</summary>
        public bool CanPickup(float distance, out string message)
        {
            message = "";
            if (_ext.grab != null && _ext.grab.IsHeld)
            {
                message = DrillLocale.T("手上已经拿着灭火器了。", "You are already holding an extinguisher.");
                return false;
            }
            if (distance > pickupRange)
            {
                message = DrillLocale.T(
                    "距离灭火器 " + distance.ToString("F1") + " 米，太远了 → 先走到 " + pickupRange.ToString("F1") + " 米内再拿。",
                    "Too far from the extinguisher: " + distance.ToString("F1") + " m. Walk within " + pickupRange.ToString("F1") + " m.");
                return false;
            }
            if (distance < pickupTooClose)
            {
                message = DrillLocale.T(
                    "贴得太近了（" + distance.ToString("F2") + " 米），退后半步再伸手拿。",
                    "Too close (" + distance.ToString("F2") + " m). Step back half a pace.");
                return false;
            }
            return true;
        }

        public void ShowPickupRejected(float distance, string reason = null)
        {
            if (string.IsNullOrEmpty(reason))
            {
                CanPickup(distance, out reason);
            }
            if (!string.IsNullOrEmpty(reason)) Show(reason, 3.0f);
        }

        /// <summary>手柄 Grip 在抓取范围外按下：告诉玩家还差多远。</summary>
        void OnGrabRejected(float handDistance)
        {
            float gap = Mathf.Max(0f, handDistance - (_ext.grab != null ? _ext.grab.grabRange : 0.55f));
            Show(DrillLocale.T(
                "手柄离瓶身还差 " + gap.ToString("F2") + " 米（抓取范围 " + (_ext.grab != null ? _ext.grab.grabRange.ToString("F2") : "0.55") + " 米）→ 再靠近一点。",
                "Hand is " + gap.ToString("F2") + " m short of the extinguisher (grab range " + (_ext.grab != null ? _ext.grab.grabRange.ToString("F2") : "0.55") + " m). Move closer."));
        }

        /// <summary>未拔销/未答题/未压把时，由 Extinguisher 推送原因。</summary>
        public void ShowBlockedReason(string reason)
        {
            if (!string.IsNullOrEmpty(reason)) Show(reason, 3.0f);
        }

        void Show(string message, float seconds = 3.2f)
        {
            if (HintsSuppressed) return;
            DrillToast.ShowRaw(message, seconds);
            var desktop = DesktopDrillInput.Instance;
            if (desktop != null) desktop.PushHint(message);
        }
    }
}
