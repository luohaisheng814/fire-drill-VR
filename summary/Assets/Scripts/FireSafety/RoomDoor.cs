using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 情景二/三通往安全出口的那扇房间门（场景里的 door_b，门后有玻璃窗的小房间）。
    ///   · 默认关着，第一阶段的火没灭之前打不开（右键 / 扳机射线打到门只给提示）；
    ///   · 第一阶段火灭后（这两套情景跳过第 2 阶段，直接奔出口）变为可打开：
    ///     门扇绕门轴转开，碰撞体跟着转，玩家可以直接走过去撤离；
    ///   · 门轴就在 door_b 根节点上（x=2.75 一侧），所以旋转自身即可，不需要额外建 pivot。
    /// 只做「开门」这一件事，不参与通关判定（通关仍然只认安全出口门）。
    /// </summary>
    public class RoomDoor : MonoBehaviour
    {
        public static RoomDoor Instance;

        [Header("门扇")]
        [Tooltip("留空 = 用自身（door_b 的轴心已经在门轴一侧）")]
        public Transform leaf;
        [Tooltip("绕 Y 轴转开的角度：负值往房间内开，不会把站在门口的玩家顶出去")]
        public float openAngle = 95f;
        [Tooltip("转开动画时长（秒）")]
        public float openDuration = 0.8f;

        [Header("交互")]
        [Tooltip("玩家与门的水平距离超过这个值就不响应，避免隔墙开门")]
        public float maxInteractDistance = 4.0f;
        [Tooltip("是否要求第一阶段火已灭才可打开（情景二/三的门就是这种）")]
        public bool requireStageOneCleared = true;
        public string lockedHint = "这扇门还打不开：先把第一阶段的火灭掉";
        public string openedHint = "房间门已打开 —— 穿过这扇门去安全出口";

        /// <summary>门扇是否已经转开到位（供探针判断「门真的开了」）。</summary>
        public bool IsOpen { get; private set; }

        Transform _t;
        Quaternion _base = Quaternion.identity;
        bool _baseSaved;
        float _animT = -1f;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _t = leaf != null ? leaf : transform;
            CaptureBase();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            Animate();
            HintWhenNear();
        }

        bool _hintedNear;

        /// <summary>门已可打开、玩家又走近了：主动给一次提示，避免玩家站在门口不知道能开门。</summary>
        void HintWhenNear()
        {
            if (_hintedNear || IsOpen || _animT >= 0f) return;
            if (!CanOpenNow) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 to = transform.position - cam.transform.position;
            to.y = 0f;
            if (to.magnitude > 3.2f) return;
            if (to.sqrMagnitude > 0.01f && Vector3.Dot(cam.transform.forward, to.normalized) < 0.3f) return;
            _hintedNear = true;
            var input = DesktopDrillInput.Instance;
            if (input != null) input.PushHint("这扇门现在可以打开了：右键点门（VR 里把手柄射线指到门上按扳机）");
        }

        /// <summary>记录门扇初始旋转，供转开动画使用（也便于重置）。</summary>
        public void CaptureBase()
        {
            if (_t == null || _baseSaved) return;
            _base = _t.localRotation;
            _baseSaved = true;
        }

        /// <summary>现在是否允许打开：情景二/三的第 1 阶段火灭之后才允许。</summary>
        public bool CanOpenNow
        {
            get
            {
                if (!requireStageOneCleared) return true;
                var sc = DrillScenario.Instance != null ? DrillScenario.Instance : FindObjectOfType<DrillScenario>();
                if (sc == null) return true;                       // 没流程脚本（自检里）就不拦
                if (sc.CurrentMode != DrillScenario.Mode.Formal) return true;
                if (sc.CurrentScenario != null && !sc.CurrentScenario.skipEvacuation) return true;
                return sc.Stage1Cleared || sc.CurrentPhase == DrillScenario.Phase.Stage2 || sc.CurrentPhase == DrillScenario.Phase.Done;
            }
        }

        /// <summary>
        /// 玩家主动开门（桌面右键 / 手柄扳机射线打到门扇）。
        /// 返回 true 表示「这次点击确实打到门了」，调用方据此给提示；能不能开看 <see cref="CanOpenNow"/>。
        /// </summary>
        public bool TryOpenByPlayer(out string hint, float maxDistance = -1f)
        {
            hint = null;
            if (maxDistance < 0f) maxDistance = maxInteractDistance;

            Transform p = Camera.main != null ? Camera.main.transform : null;
            if (p != null)
            {
                Vector3 d = p.position - transform.position; d.y = 0f;
                if (d.magnitude > maxDistance) return false;
            }

            if (!CanOpenNow) { hint = lockedHint; return true; }

            StartOpening();
            hint = openedHint;
            return true;
        }

        /// <summary>直接打开（剧情 / 探针用，跳过门禁）。</summary>
        public void Open()
        {
            StartOpening();
        }

        /// <summary>关门复位（重新开始演练时用）。</summary>
        public void ResetDoor()
        {
            if (_t == null) return;
            CaptureBase();
            _t.localRotation = _base;
            _animT = -1f;
            IsOpen = false;
        }

        /// <summary>射线/点击命中的碰撞体是不是这扇门（门扇本身或门上的把手、窗）。</summary>
        public bool IsDoorPart(Collider c)
        {
            if (c == null) return false;
            var root = leaf != null ? leaf : transform;
            var t = c.transform;
            return t == root || t.IsChildOf(root) || t == transform || t.IsChildOf(transform);
        }

        void StartOpening()
        {
            if (IsOpen || _animT >= 0f) return;
            CaptureBase();
            _animT = 0f;
            Debug.Log("[RoomDoor] 房间门打开：" + gameObject.name);
        }

        void Animate()
        {
            if (_animT < 0f || _t == null) return;
            _animT += Time.deltaTime;
            float k = Mathf.Clamp01(_animT / Mathf.Max(0.01f, openDuration));
            k = k * k * (3f - 2f * k);                       // smoothstep，起手与收尾都缓
            _t.localRotation = _base * Quaternion.Euler(0f, openAngle * k, 0f);
            if (k >= 1f) { _animT = -1f; IsOpen = true; }
        }
    }
}
