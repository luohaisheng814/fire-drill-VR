using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace FireSafety
{
    /// <summary>
    /// 头显功能输入层：把手柄按键映射到演练动作，全部逻辑只在头显连接（或探针虚拟模式）时生效，
    /// 桌面键鼠路径（DesktopDrillInput）不受影响。
    ///
    /// 键位：
    ///   · Grip（Select）       ：抓取 / 松开灭火器与保险销（由 ToggleGrab 直接读取）
    ///   · Trigger（Activate）  ：第一次按下拔保险销；再按一次压下压把并持续喷射，松开即停
    ///   · A（右手 Primary）    ：开始演练 / 引导页翻页 / 阶段一「继续往下」/ 问答选 A
    ///   · B（右手 Secondary）  ：问答选 B
    ///   · X（左手 Primary）    ：问答选 C / 蹲下与站起兜底
    ///   · 左摇杆按下（Stick Click）：蹲下 / 站起兜底（真人蹲下也直接生效）
    ///   · 右手射线             ：点按世界空间 UI（EventSystem + XRUIInputModule 已装配）
    ///   · 触觉                 ：抓起灭火器 / 拔销 / 压把 / 答题成功各给一次脉冲（幅度可在 Inspector 调）
    /// </summary>
    [DisallowMultipleComponent]
    public class VRDrillInput : MonoBehaviour
    {
        /// <summary>头显是否已连接并初始化（真实设备）。</summary>
        public static bool HeadsetPresent
        {
            get
            {
                // Unity 6 起 XRDevice.isPresent 已移除，XRSettings.isDeviceActive 即「头显正在输出」
                return XRSettings.isDeviceActive;
            }
        }

        static int _virtualDrivers;

        /// <summary>当前是否有实例处于探针虚拟模式（无真机时的测试通道）。</summary>
        public static bool VirtualDriverActive => _virtualDrivers > 0;

        /// <summary>是否该由头显输入层接管（真实头显 或 探针虚拟模式）。</summary>
        public static bool DrillInputActive => HeadsetPresent || _virtualDrivers > 0;

        [Header("场景引用")]
        public Extinguisher[] extinguishers;
        public StartScreen startScreen;
        public OnboardingScreen onboarding;
        public StageOneCompletePanel stagePanel;
        public StageTwoFlow stageTwo;
        public VRDrillHud hud;
        public Transform leftHand;
        public Transform rightHand;

        [Header("扳机动作（XRI Right/Left Interaction · Activate）")]
        public InputActionReference rightTriggerAction;
        public InputActionReference leftTriggerAction;

        [Header("提示")]
        [Tooltip("秒数：同一条提示重复推送的间隔上限")]
        public float hintRefresh = 8f;

        [Header("触觉反馈（0 = 关闭该动作的脉冲）")]
        [Tooltip("抓起灭火器时的脉冲幅度")]
        public float hapticGrab = 0.22f;
        [Tooltip("保险销拔出的脉冲幅度（动作最关键，给最强）")]
        public float hapticPin = 0.75f;
        [Tooltip("压下压把的脉冲幅度")]
        public float hapticPress = 0.42f;
        [Tooltip("答题成功的脉冲幅度")]
        public float hapticAnswer = 0.30f;
        [Tooltip("单次脉冲时长（秒）")]
        public float hapticDuration = 0.07f;

        /// <summary>虚拟输入（播放态探针用）：完全绕过真实设备。</summary>
        public bool virtualMode = false;
        public bool vPrimary;
        public bool vSecondary;
        public bool vLeftPrimary;
        public bool vTrigger;
        public bool vStickClick;

        bool _prevTrigger, _prevPrimary, _prevSecondary, _prevLeftPrimary, _prevStickClick;
        bool _triggerConsumed;                 // 本次按压已用于拔销，不再顺带压把
        bool _virtualRegistered;
        float _lastHint = -999f;
        Extinguisher _held;
        Extinguisher _prevHeld;

        void OnDisable()
        {
            if (_virtualRegistered) { _virtualDrivers--; _virtualRegistered = false; }
        }

        void OnEnable()
        {
            if (rightTriggerAction != null && rightTriggerAction.action != null) rightTriggerAction.action.Enable();
            if (leftTriggerAction != null && leftTriggerAction.action != null) leftTriggerAction.action.Enable();
        }

        void Update()
        {
            if (virtualMode) { if (_virtualRegistered == false) { _virtualDrivers++; _virtualRegistered = true; } }
            else if (_virtualRegistered) { _virtualDrivers--; _virtualRegistered = false; }

            bool active = virtualMode || HeadsetPresent;
            if (hud != null)
            {
                hud.head = hud.head != null ? hud.head : (Camera.main != null ? Camera.main.transform : null);
                hud.SetVisible(active);
                if (active) hud.SetStatus(virtualMode && !HeadsetPresent ? "VR 探针模式（模拟头显）" : "VR 头显已连接");
            }
            if (!active) return;

            // 头显接管时关掉桌面喷射模式，压把/扳机链路才有效
            if (extinguishers != null)
                foreach (var e in extinguishers)
                    if (e != null && e.desktopMode) e.desktopMode = false;

            bool trigger = virtualMode ? vTrigger : ReadTrigger();
            bool primary = virtualMode ? vPrimary : Button(XRNode.RightHand, UnityEngine.XR.CommonUsages.primaryButton);
            bool secondary = virtualMode ? vSecondary : Button(XRNode.RightHand, UnityEngine.XR.CommonUsages.secondaryButton);
            bool leftPrimary = virtualMode ? vLeftPrimary : Button(XRNode.LeftHand, UnityEngine.XR.CommonUsages.primaryButton);
            bool stickClick = virtualMode ? vStickClick : Button(XRNode.LeftHand, UnityEngine.XR.CommonUsages.primary2DAxisClick);

            _held = FindHeld();

            // 抓取成功 → 一次轻脉冲（VR 里没有「拿住了」的视觉确认，靠手上震一下）
            if (_held != null && _prevHeld != _held) PulseRight(hapticGrab, hapticDuration);
            _prevHeld = _held;

            // ---- Trigger：拔销 → 压把 → 松开 ----
            if (trigger && !_prevTrigger)
            {
                if (_held != null)
                {
                    if (!_held.PinPulled && !_triggerConsumed)
                    {
                        if (_held.pin != null) _held.pin.PullByInput();
                        else _held.PullPin();
                        _triggerConsumed = true;
                        if (_held.PinPulled) PulseRight(hapticPin, 0.10f);
                        Hint(_held.PinPulled ? "保险销已拔出 · 再按一次扳机压下压把" : "请先完成常识问答再拔销");
                    }
                    else if (_held.PinPulled && !_triggerConsumed)
                    {
                        if (_held.handleLever != null) _held.handleLever.PressExternal();
                        PulseRight(hapticPress, hapticDuration);
                        Hint("压把已按下 · 对准火焰根部持续喷射，松开扳机停止");
                    }
                }
            }
            else if (!trigger && _prevTrigger)
            {
                _triggerConsumed = false;
                if (_held != null && _held.handleLever != null) _held.handleLever.ReleaseExternal();
            }

            // ---- A：开始 / 翻页 / 继续往下 / 选 A ----
            if (primary && !_prevPrimary) OnPrimary();

            // ---- B：选 B / 换下一题（未答题时） ----
            if (secondary && !_prevSecondary) OnSecondary();

            // ---- X：选 C ----
            if (leftPrimary && !_prevLeftPrimary)
            {
                if (!AnswerQuiz(2)) Hint("X 键：问答时选 C");
            }

            // ---- 左摇杆按下：蹲下 / 站起兜底 ----
            if (stickClick && !_prevStickClick) ToggleCrouch();

            _prevTrigger = trigger;
            _prevPrimary = primary;
            _prevSecondary = secondary;
            _prevLeftPrimary = leftPrimary;
            _prevStickClick = stickClick;

            UpdateIdleHint();
        }

        void OnPrimary()
        {
            if (onboarding != null && onboarding.IsOpen)
            {
                onboarding.NextPage();
                Hint("A 键：引导页下一页（共 " + onboarding.PageCount + " 页）");
                return;
            }
            if (startScreen != null && !startScreen.Started)
            {
                startScreen.StartGame();
                Hint("演练开始 · 先用 Grip 抓起红色灭火器");
                return;
            }
            if (stagePanel != null && stagePanel.IsOpen)
            {
                stagePanel.Continue();
                Hint("继续往下 · 蹲下潜行到大厅（左摇杆按下可切换蹲姿）");
                return;
            }
            if (AnswerQuiz(0)) return;
            Hint("A 键暂无可用动作：靠近灭火器按 Grip 抓取");
        }

        void OnSecondary()
        {
            if (AnswerQuiz(1)) return;
            Hint("B 键只在常识问答中用于选 B");
        }

        bool AnswerQuiz(int option)
        {
            if (extinguishers == null) return false;
            foreach (var e in extinguishers)
            {
                if (e == null || e.quizPanel == null) continue;
                if (!e.quizPanel.IsOpen) continue;
                if (e.quizPanel.IsAnsweredThisQuestion) return false;
                bool ok = e.quizPanel.AnswerOption(option);
                if (ok) { PulseRight(hapticAnswer, hapticDuration); Hint("已选择 " + (char)('A' + option) + " · 答对 3/5 题即可拿起灭火器"); }
                return ok;
            }
            return false;
        }

        void ToggleCrouch()
        {
            if (stageTwo == null) return;
            stageTwo.ToggleDesktopCrouch();
            Hint(stageTwo.DesktopCrouch ? "已切换为蹲姿潜行" : "已站起");
        }

        void UpdateIdleHint()
        {
            if (Time.time - _lastHint < hintRefresh) return;

            string hint;
            if (onboarding != null && onboarding.IsOpen) hint = "A 键翻页 · 右手射线可直接点「下一页 / 开始演练」";
            else if (startScreen != null && !startScreen.Started) hint = "按 A 开始演练 · 右手射线点「开始演练」也可以";
            else if (AnyQuizOpen()) hint = "问答中：A / B / X 选 A / B / C（也可用右手射线点选项）";
            else if (_held != null && !_held.PinPulled) hint = "已拿起灭火器 · 按 Trigger 拔保险销";
            else if (_held != null && _held.IsSpraying) hint = "正在喷射 · 对准火焰根部，松开扳机即停";
            else if (_held != null) hint = "按 Trigger 压下压把喷射 · Grip 松开可放下灭火器";
            else hint = "左摇杆移动 · 右摇杆瞬转 · 靠近红色灭火器后按 Grip 抓取";
            Hint(hint);
        }

        bool AnyQuizOpen()
        {
            if (extinguishers == null) return false;
            foreach (var e in extinguishers)
                if (e != null && e.quizPanel != null && e.quizPanel.IsOpen) return true;
            return false;
        }

        Extinguisher FindHeld()
        {
            if (extinguishers == null) return null;
            foreach (var e in extinguishers)
                if (e != null && e.grab != null && e.grab.IsHeld) return e;
            return null;
        }

        void Hint(string text)
        {
            _lastHint = Time.time;
            if (hud != null) hud.SetHint(text);
        }

        /// <summary>右手柄脉冲（幅度为 0 时跳过，便于逐个关掉）。</summary>
        void PulseRight(float amplitude, float duration)
        {
            if (amplitude <= 0f) return;
            VRHaptics.Pulse(HandSide.Right, amplitude, duration);
        }

        bool ReadTrigger()
        {
            if (rightTriggerAction != null && rightTriggerAction.action != null &&
                rightTriggerAction.action.ReadValue<float>() > 0.5f) return true;
            if (leftTriggerAction != null && leftTriggerAction.action != null &&
                leftTriggerAction.action.ReadValue<float>() > 0.5f) return true;
            if (Button(XRNode.RightHand, UnityEngine.XR.CommonUsages.triggerButton)) return true;
            return Button(XRNode.LeftHand, UnityEngine.XR.CommonUsages.triggerButton);
        }

        static bool Button(XRNode node, InputFeatureUsage<bool> usage)
        {
            var d = InputDevices.GetDeviceAtXRNode(node);
            return d.isValid && d.TryGetFeatureValue(usage, out bool v) && v;
        }
    }
}
