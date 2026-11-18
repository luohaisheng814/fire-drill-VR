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
        /// <summary>头显是否已连接并初始化（真机）。第 29 轮起由 RuntimeDeviceMode 判定，避免把 XR 模拟器当成真机。</summary>
        public static bool HeadsetPresent
        {
            get
            {
                // Unity 6 起 XRDevice.isPresent 已移除，XRSettings.isDeviceActive 只说明"有 XR 显示在输出"，
                // 它同时包含 XR 模拟器；真机判定必须取 RuntimeDeviceMode 的结果。
                if (RuntimeDeviceMode.HasInstance) return RuntimeDeviceMode.RealHeadset;
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
        public VRHelpPanel helpPanel;
        public XRDrillLocomotion locomotion;
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
        public bool vLeftSecondary;
        public bool vTrigger;
        public bool vGrip;
        public bool vStickClick;
        public bool vStickClickRight;

        bool _prevTrigger, _prevPrimary, _prevSecondary, _prevLeftPrimary, _prevLeftSecondary;
        bool _prevStickClick, _prevStickClickRight, _prevGrip;
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
            // 第 30 轮：把扳机动作交给统一输入层（新输入系统 / 旧 XR / 键盘三级兜底）
            VRInputSource.BindActions(rightTriggerAction, leftTriggerAction);
        }

        void Update()
        {
            if (virtualMode) { if (_virtualRegistered == false) { _virtualDrivers++; _virtualRegistered = true; } }
            else if (_virtualRegistered) { _virtualDrivers--; _virtualRegistered = false; }

            bool active = virtualMode || HeadsetPresent;
            if (hud != null)
            {
                hud.head = hud.head != null ? hud.head : (Camera.main != null ? Camera.main.transform : null);
                // 帮助面板打开时先收起提示条，避免两层文字叠在视线里
                bool hudWant = active && (helpPanel == null || !helpPanel.Visible);
                hud.SetVisible(hudWant);
                if (hudWant)
                {
                    string status = virtualMode && !HeadsetPresent
                        ? "VR 探针模式（模拟头显）"
                        : (RuntimeDeviceMode.HasInstance
                            ? RuntimeDeviceMode.Instance.StatusLine
                            : "VR 头显已连接");
                    // 手柄没识别时直接把原因写在状态行里（短句，避免一行被折成两行压住提示）
                    if (!virtualMode && HeadsetPresent && !VRInputSource.AnyControllerValid)
                        status = "VR 头显 · 手柄未识别（按 Y 看诊断）";
                    hud.SetStatus(status);
                }
            }
            if (!active) return;

            // 头显接管时关掉桌面喷射模式，压把/扳机链路才有效
            if (extinguishers != null)
                foreach (var e in extinguishers)
                    if (e != null && e.desktopMode) e.desktopMode = false;

            // 第 30 轮：所有按键统一走 VRInputSource（Input System XRController → 旧 XR.InputDevices → 键盘兜底）。
            // 之前直接读 UnityEngine.XR.InputDevices，在「新输入系统 + OpenXR」下设备常常拿不到，
            // 表现就是左摇杆能走（走的是 XRI 动作）、其他键全无反应。
            var snap = virtualMode ? default(VRInputSource.Snapshot) : VRInputSource.Read(HeadsetPresent);
            bool trigger = virtualMode ? vTrigger : snap.trigger;
            bool primary = virtualMode ? vPrimary : snap.primary;
            bool secondary = virtualMode ? vSecondary : snap.secondary;
            bool leftPrimary = virtualMode ? vLeftPrimary : snap.leftPrimary;
            bool leftSecondary = virtualMode ? vLeftSecondary : snap.leftSecondary;
            bool stickClick = virtualMode ? vStickClick : snap.stickClickLeft;
            bool stickClickRight = virtualMode ? vStickClickRight : snap.stickClickRight;
            bool gripNow = virtualMode ? vGrip : snap.grip;

            // F5：任何模式下都能开关操作帮助面板（编辑器里排练用；头显里用 Y）
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f5Key.wasPressedThisFrame && helpPanel != null)
            {
                helpPanel.Toggle();
                Hint(helpPanel.Visible ? "已打开操作帮助面板 · Y / F5 关闭" : "已关闭操作帮助面板");
            }

            _held = FindHeld();

            // Grip 按下但手里没东西：直接告诉玩家"按到了、只是没抓住"，不再静默无反应
            if (gripNow && !_prevGrip && _held == null)
                Hint("Grip 已按下：手边没有可抓的灭火器（站到瓶旁 2 米内，Grip 要按到底）");
            _prevGrip = gripNow;

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
                else if (TryCutPowerByRay())
                {
                    // 未持灭火器时，扳机 = 指电源箱切断电源（电气火灾硬规则）
                }
                else if (TryOpenExitDoorByRay())
                {
                    // 扳机同时可开安全出口门（第 25 轮）
                }
                else if (TryOpenRoomDoorByRay())
                {
                    // 扳机也可开情景二/三那扇通往出口的房间门（第 27 轮）
                }
                else
                {
                    // 按到了、但射线没指到任何可交互物 —— 给一句反馈，避免"按了毫无动静"
                    Hint("Trigger 已按下：射线没指到可交互物（电源箱 / 门 / 灭火器）");
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

            // ---- Y：打开 / 关闭操作帮助（输入诊断）面板 ----
            if (leftSecondary && !_prevLeftSecondary)
            {
                if (helpPanel != null)
                {
                    helpPanel.Toggle();
                    Hint(helpPanel.Visible ? "已打开操作帮助面板 · Y 关闭" : "已关闭操作帮助面板");
                }
                else Hint("Y 键：场景里没有挂操作帮助面板（先跑一遍 RunSetup）");
            }

            // ---- 左摇杆按下：蹲下 / 站起兜底 ----
            if (stickClick && !_prevStickClick) ToggleCrouch();

            // ---- 右摇杆按下：原地重定位（现实中走出安全区/相机被挤开时一键回到脚下） ----
            if (stickClickRight && !_prevStickClickRight) Recenter();

            _prevTrigger = trigger;
            _prevPrimary = primary;
            _prevSecondary = secondary;
            _prevLeftPrimary = leftPrimary;
            _prevLeftSecondary = leftSecondary;
            _prevStickClick = stickClick;
            _prevStickClickRight = stickClickRight;

            UpdateIdleHint();
        }

        /// <summary>右摇杆按下：把移动原点挪到当前头部位置，并给一条确认提示。</summary>
        void Recenter()
        {
            var loco = locomotion != null ? locomotion : GetComponent<XRDrillLocomotion>();
            if (loco == null) loco = FindObjectOfType<XRDrillLocomotion>();
            if (loco == null) { Hint("找不到移动层，无法重定位"); return; }
            loco.RecenterOrigin();
            Hint("已重定位：你脚下成为新的移动原点");
        }

        void OnPrimary()
        {
            if (onboarding != null && onboarding.IsOpen)
            {
                onboarding.NextPage();
                Hint("A 键：引导页下一页（共 " + onboarding.PageCount + " 页）");
                return;
            }
            if (startScreen != null && !startScreen.DrillStarted)
            {
                startScreen.StartGame();
                Hint("正式演练开始 · 先切断电源，再拿灭火器答题、拔销、压把、喷射");
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

            var scenario = DrillScenario.Instance;
            string hint;
            if (onboarding != null && onboarding.IsOpen) hint = "A 键翻页 · 右手射线可直接点「下一页 / 开始演练」";
            else if (startScreen != null && !startScreen.DrillStarted) hint = "按 A 开始演练 · 右手射线点「开始演练」也可以";
            else if (AnyQuizOpen()) hint = "问答中：A / B / X 选 A / B / C（也可用右手射线点选项）";
            else if (scenario != null && scenario.CurrentMode == DrillScenario.Mode.Formal
                     && scenario.CurrentPhase == DrillScenario.Phase.Stage1 && !scenario.PowerCut)
                hint = "设备起火：先走到电源箱旁，右手射线指向它按 Trigger 切断电源";
            else if (scenario != null && scenario.CurrentMode == DrillScenario.Mode.Formal
                     && scenario.CurrentPhase == DrillScenario.Phase.Stage2)
                hint = "沿安全出口标识撤离 · 出口前有火情，用出口旁灭火器灭火开门";
            else if (_held != null && !_held.PinPulled) hint = "已拿起灭火器 · 按 Trigger 拔保险销";
            else if (_held != null && _held.IsSpraying) hint = "正在喷射 · 对准火焰根部，松开扳机即停";
            else if (_held != null) hint = "按 Trigger 压下压把喷射 · Grip 松开可放下灭火器";
            else hint = "左摇杆移动 · 右摇杆瞬转 · 靠近红色灭火器按 Grip 抓取 · 电源箱按 Trigger 断电";
            Hint(hint);
        }

        /// <summary>未持灭火器时，右手射线指向配电箱 → 切断电源。</summary>
        bool TryCutPowerByRay()
        {
            var scenario = DrillScenario.Instance;
            if (scenario == null) return false;
            if (scenario.PowerCut) return false;

            Transform src = rightHand != null ? rightHand : (Camera.main != null ? Camera.main.transform : null);
            if (src == null) return false;

            if (!Physics.Raycast(src.position, src.forward, out var hit, 4.5f, ~0, QueryTriggerInteraction.Collide))
                return false;
            if (hit.collider == null) return false;

            var power = hit.collider.GetComponentInParent<PowerSwitch>();
            if (power == null) return false;

            scenario.TryCutPower();
            return true;
        }

        /// <summary>未持灭火器时，右手射线指向安全出口门 → 开门（未解锁则提示先灭火）。</summary>
        bool TryOpenExitDoorByRay()
        {
            var door = ExitDoor.Instance != null ? ExitDoor.Instance : FindObjectOfType<ExitDoor>();
            if (door == null) return false;

            Transform src = rightHand != null ? rightHand : (Camera.main != null ? Camera.main.transform : null);
            if (src == null) return false;

            if (!Physics.Raycast(src.position, src.forward, out var hit, 6f, ~0, QueryTriggerInteraction.Collide))
                return false;
            if (hit.collider == null || !door.IsDoorPart(hit.collider)) return false;

            if (!door.IsUnlocked)
            {
                Hint("这扇安全出口还锁着：先把门前的火灭掉，指示牌变绿后才能开门");
                return true;
            }
            door.TryOpenByPlayer(6f);
            Hint("安全出口已打开 —— 走出这扇门即算撤离成功");
            return true;
        }

        /// <summary>第 27 轮：右手射线指向「房间门」（情景二/三通往出口的那扇）→ 开门。</summary>
        bool TryOpenRoomDoorByRay()
        {
            var door = RoomDoor.Instance != null ? RoomDoor.Instance : FindObjectOfType<RoomDoor>();
            if (door == null) return false;

            Transform src = rightHand != null ? rightHand : (Camera.main != null ? Camera.main.transform : null);
            if (src == null) return false;

            if (!Physics.Raycast(src.position, src.forward, out var hit, 6f, ~0, QueryTriggerInteraction.Collide))
                return false;
            if (hit.collider == null || !door.IsDoorPart(hit.collider)) return false;

            string hint;
            door.TryOpenByPlayer(out hint, 6f);
            if (!string.IsNullOrEmpty(hint)) Hint(hint);
            return true;
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

        /// <summary>给射线/悬停这类外部反馈用的公开脉冲入口。</summary>
        public void PulseRightPublic(float amplitude, float duration)
        {
            PulseRight(amplitude, duration);
        }
    }
}
