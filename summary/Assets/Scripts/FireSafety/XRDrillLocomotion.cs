using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

namespace FireSafety
{
    /// <summary>
    /// 玩家移动（头显摇杆 + 键盘调试）：
    ///   · 左摇杆：沿头显朝向平移（推杆方向即前进方向；可切成沿左手柄朝向）
    ///   · 右摇杆左右：瞬转 45°（可切连续旋转），带冷却避免一次推杆连转多格
    ///   · 推满摇杆自动小跑（sprintMultiplier），不需要额外按键，也不和 Grip 抓取抢键
    ///   · 头顶低于 crouchHeight（真人蹲下）时自动切到蹲行速度
    ///   · 键盘：WASD 平移、Q/E 转身、Shift 加速（无头显时桌面调试）
    /// 输入优先走 XRI 默认动作资产（"XRI Left Locomotion/Move" 与
    /// "XRI Right Locomotion/Snap Turn"），动作缺失时回退到 UnityEngine.XR 裸读设备，
    /// 因此头显、XR 模拟器、桌面三条路径共用同一套移动逻辑。
    /// </summary>
    [DisallowMultipleComponent]
    public class XRDrillLocomotion : MonoBehaviour
    {
        /// <summary>推杆方向的基准：头显朝向 / 左右手柄朝向。</summary>
        public enum DirectionMode { Head, LeftHand, RightHand }

        [Header("输入动作（XRI 默认动作资产）")]
        [Tooltip("XRI Left Locomotion / Move（Vector2）")]
        public InputActionReference moveAction;
        [Tooltip("XRI Right Locomotion / Snap Turn（Axis，右摇杆左右）")]
        public InputActionReference turnAction;

        [Header("速度")]
        [Tooltip("摇杆推满时的移动速度（米/秒）")]
        public float moveSpeed = 2.2f;
        [Tooltip("头顶低于 crouchHeight 时的蹲行速度（米/秒）")]
        public float crawlSpeed = 1.05f;
        [Tooltip("按住 Grip / Shift 时的加速倍率")]
        public float sprintMultiplier = 1.45f;
        [Tooltip("连续转身的角速度（度/秒）")]
        public float turnSpeed = 120f;
        [Tooltip("瞬转身角度（度）")]
        public float snapAngle = 45f;
        [Tooltip("两次瞬转之间的最短间隔（秒）")]
        public float snapCooldown = 0.35f;

        [Header("起步/停步手感")]
        [Tooltip("从静止加速到满速的斜率（米/秒²）：太快会显得「一推就窜」")]
        public float acceleration = 4.5f;
        [Tooltip("松开后的减速斜率（米/秒²）：略大于加速，停得干脆但不瞬停")]
        public float deceleration = 6f;
        [Tooltip("转身死区：低于该值的摇杆偏移不参与转向（含连续转动）")]
        public float turnDeadzone = 0.65f;
        [Tooltip("连续转向的跟随速度（越大越跟手）")]
        public float turnSmoothing = 12f;

        [Header("移动方向")]
        [Tooltip("推杆时的朝向基准：Head=朝头显看的方向走（默认）；LeftHand=朝左手柄指的方向走")]
        public DirectionMode moveDirection = DirectionMode.Head;

        [Header("控制")]
        [Tooltip("true=右摇杆瞬转，false=连续转动")]
        public bool useSnapTurn = true;
        [Tooltip("推满摇杆自动小跑（不需要额外按键，也不会和抓取抢键）")]
        public bool sprintOnFullStick = true;
        [Tooltip("摇杆推杆幅度达到该值视为小跑（0~1）")]
        public float fullStickThreshold = 0.92f;
        [Tooltip("允许按住 Grip 加速。Grip 同时是抓取键，抓着灭火器移动会自动加速，默认关闭")]
        public bool allowSprint = false;
        [Tooltip("启用重力（需要 CharacterController）")]
        public bool useGravity = true;
        public float gravity = -9.81f;
        [Tooltip("摇杆死区，低于该值的推杆视为不动")]
        public float deadzone = 0.2f;
        [Tooltip("摇杆响应曲线指数：1=线性；1.4~1.8 让小推杆更细腻，避免「碰一下就窜」")]
        public float stickExponent = 1.55f;
        [Tooltip("摇杆平滑时间常数（秒）：0=不平滑，0.04~0.08 抑制漂移抖动")]
        public float stickSmoothing = 0.06f;
        [Tooltip("蹲行判定阈値：头显相机世界高度低于该値算蹲下（米）")]
        public float crouchHeight = 1.15f;

        [Header("锚点（留空自动查找）")]
        [Tooltip("XR 锚点根物体（XR Origin）")]
        public Transform rigRoot;
        [Tooltip("头显相机（用于确定移动方向与蹲姿高度）")]
        public Transform head;
        [Tooltip("左手柄（移动方向为 LeftHand 时用来取朝向）")]
        public Transform leftHand;
        [Tooltip("右手柄（移动方向为 RightHand 时用来取朝向）")]
        public Transform rightHand;

        [Header("调试")]
        [Tooltip("无头显时可用 WASD/Q/E 在桌面预览")]
        public bool keyboardDebug = true;

        [Header("运行模式（由 RuntimeDeviceMode 写入）")]
        [Tooltip("true=真机头显接管：摇杆移动生效、键盘调试关闭；false=桌面/模拟器：键盘接管")]
        public bool xrControlMode;

        /// <summary>最近一帧的实际移动速度（米/秒），供测试与调试读取。</summary>
        public float CurrentSpeed { get; private set; }
        /// <summary>最近一帧是否处于蹲行状态。</summary>
        public bool IsCrawling { get; private set; }
        /// <summary>最近一帧是否在小跑（推满摇杆 / 允许时按住 Grip / 桌面 Shift）。</summary>
        public bool IsSprinting { get; private set; }
        /// <summary>虚拟输入模式：播放态探针接管摇杆，完全不读真实设备。</summary>
        public bool virtualMode { get; private set; }

        Transform _root;
        Transform _head;
        CharacterController _cc;
        Vector3 _fallVelocity;
        float _snapTimer;
        float _turnLatch;
        float _turnAmount;
        float _speedScale;
        Vector2 _virtualMove;
        float _virtualTurn;
        Vector2 _stickSmoothed;
        Vector3 _lastDir = Vector3.zero;

        void OnEnable()
        {
            if (moveAction != null && moveAction.action != null) moveAction.action.Enable();
            if (turnAction != null && turnAction.action != null) turnAction.action.Enable();
            ResolveReferences();
        }

        void Start()
        {
            ResolveReferences();
        }

        void ResolveReferences()
        {
            if (_root == null) _root = rigRoot != null ? rigRoot : transform;

            if (_head == null)
            {
                _head = head;
                if (_head == null)
                {
                    var cam = Camera.main;
                    if (cam != null) _head = cam.transform;
                }
            }

            if (_cc == null) _cc = GetComponent<CharacterController>();
            if (_cc == null) _cc = gameObject.AddComponent<CharacterController>();
            if (_cc != null)
            {
                _cc.height = 1.6f;
                _cc.center = new Vector3(0f, 0.8f, 0f);
                _cc.radius = 0.3f;
                _cc.stepOffset = 0.35f;
                _cc.slopeLimit = 50f;
            }
        }

        // ---------------- 探针接口 ----------------

        /// <summary>
        /// 切换操作模式（由 RuntimeDeviceMode 调用）：真机模式关掉键盘调试、锁回瞬转；
        /// 键鼠模式放开 WASD / Q/E。速度与死区保持同一套值，避免两种模式手感差太多。
        /// </summary>
        public void ApplyControlMode(bool xr)
        {
            xrControlMode = xr;
            keyboardDebug = !xr;
            if (xr) useSnapTurn = true;
        }

        /// <summary>
        /// 原地重定位（头显舒适性兜底）：把 XR 锚点挪到"当前头部所处的位置"，
        /// 玩家在现实里走偏了、或相机被家具挤开时，一按就回到脚下。
        /// </summary>
        public void RecenterOrigin()
        {
            ResolveReferences();
            if (_root == null || _head == null) return;
            Vector3 offset = _head.position - _root.position;
            offset.y = 0f;
            MoveRoot(_root.position + offset);
            _fallVelocity = Vector3.zero;
        }

        /// <summary>把玩家整体挪到指定位置（可选朝向），供"回到出生点"与探针使用。</summary>
        public void RecenterTo(Vector3 position, float yawDegrees = 0f, bool applyYaw = false)
        {
            ResolveReferences();
            if (_root == null) return;
            MoveRoot(position);
            if (applyYaw) _root.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            _fallVelocity = Vector3.zero;
        }

        /// <summary>写锚点位置时必须先关 CharacterController，否则它会把相机从体积内顶出去。</summary>
        void MoveRoot(Vector3 position)
        {
            var cc = _cc != null ? _cc : GetComponent<CharacterController>();
            bool was = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            _root.position = position;
            if (cc != null) cc.enabled = was;
        }

        /// <summary>用虚拟摇杆值接管移动（move.y 前后、move.x 左右、turn 左右转身）。</summary>
        public void SetVirtualInput(Vector2 move, float turn)
        {
            virtualMode = true;
            _virtualMove = move;
            _virtualTurn = turn;
        }

        /// <summary>退出虚拟输入，回到真实设备。</summary>
        public void ClearVirtualInput()
        {
            virtualMode = false;
            _virtualMove = Vector2.zero;
            _virtualTurn = 0f;
        }

        /// <summary>运行时调移动手感（供 DrillInputAPI / 外部脚本调用）。</summary>
        public void SetMoveTuning(float speedMetersPerSecond, float turnDegreesPerSecond)
        {
            if (speedMetersPerSecond > 0f) moveSpeed = speedMetersPerSecond;
            if (turnDegreesPerSecond > 0f) turnSpeed = turnDegreesPerSecond;
        }

        /// <summary>运行时调摇杆手感：死区、响应曲线、平滑。</summary>
        public void SetStickTuning(float dz, float exponent, float smoothing)
        {
            deadzone = Mathf.Clamp(dz, 0.02f, 0.9f);
            stickExponent = Mathf.Clamp(exponent, 1f, 3f);
            stickSmoothing = Mathf.Clamp(smoothing, 0f, 0.3f);
            _stickSmoothed = Vector2.zero;
        }

        /// <summary>
        /// 摇杆整形：径向死区 → 归一化 → 指数曲线 → 低通。
        /// 小推杆不再产生"一碰就走"，零漂量级也被死区挡掉；虚拟输入不走这里，探针数值保持不变。
        /// </summary>
        Vector2 ShapeStick(Vector2 raw, float dt)
        {
            float m = raw.magnitude;
            Vector2 shaped = Vector2.zero;
            if (m > deadzone)
            {
                float n = Mathf.Clamp01((m - deadzone) / Mathf.Max(0.0001f, 1f - deadzone));
                n = Mathf.Pow(n, Mathf.Max(1f, stickExponent));
                shaped = raw.normalized * n;
            }

            if (stickSmoothing <= 0.0001f)
            {
                _stickSmoothed = shaped;
            }
            else
            {
                float k = 1f - Mathf.Exp(-Mathf.Max(0.001f, dt) / stickSmoothing);
                _stickSmoothed = Vector2.Lerp(_stickSmoothed, shaped, k);
                if (_stickSmoothed.sqrMagnitude < 0.0001f) _stickSmoothed = Vector2.zero;
            }
            return _stickSmoothed;
        }

        // ---------------- 主循环 ----------------

        void Update()
        {
            if (_root == null) { ResolveReferences(); if (_root == null) return; }

            Vector2 move;
            float turn;

            if (virtualMode)
            {
                move = _virtualMove;
                turn = _virtualTurn;
            }
            else
            {
                // 桌面模式不读摇杆：没有 XR 会话时，某些运行时仍会给出带零漂的假轴值，
                // 以前这里照单全收，表现就是"没碰摇杆，人却慢慢走/慢慢转"。
                if (xrControlMode)
                {
                    move = virtualMode ? ReadMove() : ShapeStick(ReadMove(), Time.deltaTime);
                    turn = ReadTurn();
                }
                else
                {
                    move = Vector2.zero;
                    turn = 0f;
                }

                // 键盘调试：只在没有真实摇杆输入时接管，避免两路互相打架
                if (keyboardDebug && Keyboard.current != null)
                {
                    var kb = Keyboard.current;
                    float mx = (kb.aKey.isPressed ? -1f : 0f) + (kb.dKey.isPressed ? 1f : 0f);
                    float mz = (kb.wKey.isPressed ? 1f : 0f) + (kb.sKey.isPressed ? -1f : 0f);
                    if (mx != 0f || mz != 0f) move = new Vector2(mx, mz);
                    float kt = (kb.qKey.isPressed ? -1f : 0f) + (kb.eKey.isPressed ? 1f : 0f);
                    if (kt != 0f) turn = kt;
                }
            }

            // ---- 转身 ----
            float turnTarget = Mathf.Abs(turn) > turnDeadzone ? Mathf.Sign(turn) : 0f;
            _turnAmount = Mathf.MoveTowards(_turnAmount, turnTarget, turnSmoothing * Time.deltaTime);
            _snapTimer += Time.deltaTime;
            if (turnTarget == 0f)
            {
                _turnLatch = 0f;
            }
            else if (useSnapTurn)
            {
                if (_turnLatch != turnTarget && _snapTimer >= snapCooldown)
                {
                    _root.Rotate(0f, turnTarget * snapAngle, 0f, Space.World);
                    _snapTimer = 0f;
                }
                _turnLatch = turnTarget;
            }
            else
            {
                _root.Rotate(0f, _turnAmount * turnSpeed * Time.deltaTime, 0f, Space.World);
                _turnLatch = turnTarget;
            }

            // ---- 移动 ----
            Vector3 dir = Vector3.zero;
            Vector2 planar = virtualMode
                ? new Vector2(Mathf.Abs(move.x) < deadzone ? 0f : move.x,
                              Mathf.Abs(move.y) < deadzone ? 0f : move.y)
                : move;      // 真实摇杆已在 ShapeStick 里做过径向死区与曲线，不能再按轴二次截断

            // 小跑：仅真机推满摇杆自动触发（键鼠用 Shift，避免桌面"一推就窜"）
            bool sprint = false;
            if (sprintOnFullStick && planar.magnitude >= fullStickThreshold && RuntimeDeviceMode.RealHeadset) sprint = true;
            if (!virtualMode)
            {
                if (allowSprint && (Grip(XRNode.LeftHand) || Grip(XRNode.RightHand))) sprint = true;
                if (keyboardDebug && Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed) sprint = true;
            }

            if (planar.sqrMagnitude > 0.0001f)
            {
                float yaw = MoveBasisYaw();
                Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                dir = (forward * planar.y + right * planar.x);
                if (dir.sqrMagnitude > 1f) dir.Normalize();
                _lastDir = dir;
            }
            else if (_speedScale > 0.0001f && _lastDir.sqrMagnitude > 0.0001f)
            {
                // 松手后仍沿最后方向滑行，速度按 deceleration 收回：不瞬停，也不会一直漂。
                dir = _lastDir;
            }

            IsCrawling = IsHeadLow();
            IsSprinting = sprint && !IsCrawling;
            float baseSpeed = IsCrawling ? crawlSpeed : moveSpeed;
            if (IsSprinting) baseSpeed *= sprintMultiplier;

            // 起步/停步不再瞬间到满速：按加速度斜坡把速度拉上去，松手后按减速度收回，
            // 桌面键鼠和头显摇杆共用同一套，避免"一推就窜、一松就定住"的割裂感。
            float targetScale = planar.sqrMagnitude > 0.0001f ? 1f : 0f;
            float rate = targetScale > _speedScale ? acceleration : deceleration;
            // 卡帧保护：编辑器/加载尖峰时单帧 deltaTime 可能到 0.3 秒，会把速度直接归零，
            // 玩家看到的是"松手瞬停"甚至"位移丢失"。这里按 50 ms 上限积分，保证惯性仍能走完。
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _speedScale = Mathf.MoveTowards(_speedScale, targetScale, rate * dt);

            CurrentSpeed = dir.magnitude * baseSpeed * _speedScale;

            if (_cc != null && _cc.enabled)
            {
                if (useGravity)
                {
                    _fallVelocity.y = _cc.isGrounded ? -1f : _fallVelocity.y + gravity * dt;
                    _cc.Move((dir * baseSpeed * _speedScale + _fallVelocity) * dt);
                }
                else
                {
                    _cc.Move(dir * baseSpeed * _speedScale * dt);
                }
            }
            else
            {
                _root.position += dir * baseSpeed * _speedScale * dt;
            }
        }

        /// <summary>头显相机是否低于蹲姿阈值（真人蹲下即可，无需按键）。</summary>
        public bool IsHeadLow()
        {
            Transform h = _head != null ? _head : (Camera.main != null ? Camera.main.transform : null);
            return h != null && h.position.y <= crouchHeight;
        }

        /// <summary>推杆朝向基准：按 moveDirection 取头显或手柄的偏航角，取不到时退回自身朝向。</summary>
        float MoveBasisYaw()
        {
            Transform basis = _head;
            switch (moveDirection)
            {
                case DirectionMode.LeftHand: basis = leftHand != null ? leftHand : _head; break;
                case DirectionMode.RightHand: basis = rightHand != null ? rightHand : _head; break;
                default: basis = _head; break;
            }
            if (basis == null) basis = _root;
            return basis != null ? basis.eulerAngles.y : 0f;
        }

        // ---------------- 输入读取 ----------------

        Vector2 ReadMove()
        {
            if (moveAction != null && moveAction.action != null)
            {
                var v = moveAction.action.ReadValue<Vector2>();
                if (v.sqrMagnitude > 0.0001f) return v;
            }
            return Stick(XRNode.LeftHand, UnityEngine.XR.CommonUsages.primary2DAxis);
        }

        float ReadTurn()
        {
            // XRI Snap Turn 动作的 expectedControlType 是 Vector2（右摇杆二维轴），
            // 必须按 Vector2 读取再取 x；按 float 读会静默返回 0，动作层瞬转整条失效。
            if (turnAction != null && turnAction.action != null)
            {
                Vector2 v = turnAction.action.ReadValue<Vector2>();
                if (Mathf.Abs(v.x) > 0.01f) return v.x;
            }
            // 回退：右摇杆无法读到 ✓ 就试左手摇杆
            Vector2 r = Stick(XRNode.RightHand, UnityEngine.XR.CommonUsages.primary2DAxis);
            if (Mathf.Abs(r.x) > deadzone) return r.x;
            Vector2 l = Stick(XRNode.LeftHand, UnityEngine.XR.CommonUsages.primary2DAxis);
            return Mathf.Abs(l.x) > deadzone ? l.x : 0f;
        }

        static Vector2 Stick(XRNode node, InputFeatureUsage<Vector2> usage)
        {
            var d = InputDevices.GetDeviceAtXRNode(node);
            if (!d.isValid) return Vector2.zero;
            return d.TryGetFeatureValue(usage, out var v) ? v : Vector2.zero;
        }

        static bool Grip(XRNode node)
        {
            var d = InputDevices.GetDeviceAtXRNode(node);
            if (!d.isValid) return false;
            if (d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool g)) return g;
            return d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float gv) && gv > 0.5f;
        }
    }
}
