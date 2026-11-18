using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace FireSafety
{
    /// <summary>
    /// 运行环境识别与操作模式切换（第 29 轮）。
    ///
    /// 目的：同一个工程要能跑三种"场"——
    ///   · Headset     ：真机头显（OpenXR / SteamVR / Quest Link）→ 手柄摇杆移动 + Grip/Trigger 操作
    ///   · Simulator   ：XR 模拟器（Unity XR Simulation / MockHMD 之类）→ 相机由模拟器驱动，但操作用键鼠
    ///   · Desktop     ：没有任何 XR 输出（普通编辑器播放）→ 键鼠模式
    /// 判定依据（全部是运行时真实读数，不靠猜）：
    ///   1. XRSettings.isDeviceActive：是否有一路 XR 显示在输出；
    ///   2. XRSettings.loadedDeviceName：加载的 XR 插件名（如 "OpenXR Display"）；
    ///   3. XRDisplaySubsystem 的 subsystemDescriptor.id：模拟器会带 "Simulation"；
    ///   4. HMD 设备名（InputDevices.GetDeviceAtXRNode(Head).name）：模拟器常是 "XRSimulationHMD"/"MockHMD"。
    /// 结果落到两种操作模式：XRControllers（手柄）/ KeyboardMouse（键鼠），并切换各输入层的 enabled。
    ///
    /// 手动覆盖：F2 = 切到键鼠（即使戴着头显也能用鼠标调试）；F3 = 强制手柄；F4 = 回到自动检测。
    /// </summary>
    [DisallowMultipleComponent]
    public class RuntimeDeviceMode : MonoBehaviour
    {
        public enum DeviceKind
        {
            Desktop = 0,
            Simulator = 1,
            Headset = 2
        }

        public enum ControlMode
        {
            KeyboardMouse = 0,
            XRControllers = 1
        }

        /// <summary>某种设备的操作方案（纯数据，自检与探针都断言它）。</summary>
        public struct ModePlan
        {
            public DeviceKind kind;
            public ControlMode control;
            public bool vrInputEnabled;
            public bool desktopInputEnabled;
            public bool controllerVisuals;
            public bool keyboardDebug;
            public bool headsetDesktopModeOff;
            public string status;
        }

        public static RuntimeDeviceMode Instance { get; private set; }
        public static bool HasInstance => Instance != null;

        /// <summary>真机头显正在接管（供 VRDrillInput / 手柄视觉 / 桌面层判断）。</summary>
        public static bool RealHeadset { get; private set; }

        /// <summary>
        /// 手柄模型是否该显示：真机与 XR 模拟器都显示（模拟器里它是唯一"手在哪"的参照），桌面模式隐藏。
        /// </summary>
        public static bool VisualsVisible { get; private set; } = false;

        [Header("引用（装配脚本自动填）")]
        public VRDrillInput vrInput;
        public DesktopDrillInput desktopInput;
        public XRDrillLocomotion locomotion;
        public VRDrillHud vrHud;
        public Transform cam;
        public Transform rigRoot;

        [Header("检测")]
        [Tooltip("每秒重新检测一次：中途插上/拔掉头显会自动切换")]
        public bool autoDetect = true;
        [Tooltip("重新检测间隔（秒）")]
        public float pollInterval = 1f;
        [Tooltip("允许用 F2/F3/F4 手动覆盖（仅编辑器与调试构建有意义）")]
        public bool allowHotkeys = true;

        [Header("当前状态（只读，Inspector 里可观察）")]
        public DeviceKind deviceKind = DeviceKind.Desktop;
        public ControlMode controlMode = ControlMode.KeyboardMouse;
        [Tooltip("加载的 XR 插件名")]
        public string loadedDevice = "";
        [Tooltip("HMD 设备名（真机为头显型号）")]
        public string hmdName = "";
        [Tooltip("判定理由（一句话，写进 HUD / 日志）")]
        public string reason = "尚未检测";

        /// <summary>本次运行切换过几次模式（自检断言用）。</summary>
        public int SwitchCount { get; private set; }
        /// <summary>是否处于手动覆盖状态。</summary>
        public bool forced { get; private set; }
        public DeviceKind forcedKind { get; private set; }

        float _nextPoll;
        bool _applied;

        // ---------------- HUD 文案 ----------------

        /// <summary>一行状态：给桌面底部 HUD 与头显提示条共用。</summary>
        public string StatusLine
        {
            get
            {
                if (forced) return "操作模式（手动）：" + KindLabel(deviceKind) + " · " + ControlLabel(controlMode);
                return KindLabel(deviceKind) + " · " + ControlLabel(controlMode);
            }
        }

        /// <summary>带判定依据的完整状态（日志与 HUD 状态行用）。</summary>
        public string DetailLine
        {
            get
            {
                string device = string.IsNullOrEmpty(hmdName) ? (string.IsNullOrEmpty(loadedDevice) ? "无 XR 设备" : loadedDevice) : hmdName;
                return KindLabel(deviceKind) + "（" + device + "）· " + ControlLabel(controlMode) + " · " + reason;
            }
        }

        public static string KindLabel(DeviceKind kind)
        {
            switch (kind)
            {
                case DeviceKind.Headset: return "VR 头显";
                case DeviceKind.Simulator: return "XR 模拟器";
                default: return "桌面模式";
            }
        }

        public static string ControlLabel(ControlMode mode)
        {
            return mode == ControlMode.XRControllers ? "手柄摇杆 / Grip+Trigger" : "键盘鼠标 / WASD+鼠标";
        }

        // ---------------- 生命周期 ----------------

        void Awake()
        {
            Instance = this;
        }

        void OnEnable()
        {
            Instance = this;
            InputDevices.deviceConnected += OnDeviceChanged;
            InputDevices.deviceDisconnected += OnDeviceChanged;
            DetectNow();
            ApplyMode(true);
        }

        void OnDisable()
        {
            InputDevices.deviceConnected -= OnDeviceChanged;
            InputDevices.deviceDisconnected -= OnDeviceChanged;
            if (Instance == this) Instance = null;
            RealHeadset = false;
        }

        void OnDeviceChanged(UnityEngine.XR.InputDevice device)
        {
            if (device != null && device.characteristics.HasFlag(InputDeviceCharacteristics.HeadMounted))
                _nextPoll = 0f;      // 下一帧立刻复检
        }

        void Update()
        {
            if (Application.isPlaying && allowHotkeys && Keyboard.current != null)
            {
                if (Keyboard.current.f2Key.wasPressedThisFrame) ForceKind(DeviceKind.Desktop);
                else if (Keyboard.current.f3Key.wasPressedThisFrame) ForceKind(DeviceKind.Headset);
                else if (Keyboard.current.f4Key.wasPressedThisFrame) ClearForce();
            }

            if (forced || !autoDetect) return;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + Mathf.Max(0.2f, pollInterval);

            var kind = DetectNow();
            if (!_applied || kind != deviceKind)
            {
                deviceKind = kind;
                ApplyMode(true);
            }
        }

        // ---------------- 检测 ----------------

        /// <summary>立即检测一次并写入 loadedDevice / hmdName / reason，返回分类结果。</summary>
        public DeviceKind DetectNow()
        {
            bool active = XRSettings.isDeviceActive;
            string loaded = XRSettings.loadedDeviceName;
            string hmd = HeadDeviceName();
            string displayId = RunningDisplayId();

            DeviceKind kind;
            string why;
            Classify(active, loaded, hmd, displayId, out kind, out why);

            loadedDevice = loaded ?? "";
            hmdName = hmd ?? "";
            reason = why;
            return kind;
        }

        /// <summary>纯函数分类（自检直接喂字符串断言，不需要真的插设备）。</summary>
        public static void Classify(bool xrActive, string loadedDeviceName, string hmdDeviceName,
                                    string displayId, out DeviceKind kind, out string reason)
        {
            string loaded = loadedDeviceName ?? "";
            string hmd = hmdDeviceName ?? "";
            string display = displayId ?? "";

            if (!xrActive)
            {
                kind = DeviceKind.Desktop;
                reason = "XRSettings.isDeviceActive=false，没有任何 XR 显示在输出";
                return;
            }

            string probe = (loaded + " | " + hmd + " | " + display).ToLowerInvariant();
            if (ContainsAny(probe, "simulat", "mock", "xrsimulation", "editor test", "fake"))
            {
                kind = DeviceKind.Simulator;
                reason = "XR 已激活，但设备名/插件名是模拟器（" + FirstNonEmpty(hmd, loaded, display) + "）";
                return;
            }

            kind = DeviceKind.Headset;
            reason = "XR 显示运行中：" + FirstNonEmpty(loaded, display, hmd) + "，HMD=" + FirstNonEmpty(hmd, "未知型号");
        }

        static bool ContainsAny(string text, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
                if (text.Contains(keys[i])) return true;
            return false;
        }

        static string FirstNonEmpty(params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (!string.IsNullOrEmpty(values[i])) return values[i];
            return "未命名";
        }

        static string HeadDeviceName()
        {
            var d = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return d.isValid ? d.name : "";
        }

        static string RunningDisplayId()
        {
            var list = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(list);
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s == null || !s.running || s.subsystemDescriptor == null) continue;
                return s.subsystemDescriptor.id;
            }
            return "";
        }

        // ---------------- 方案与切换 ----------------

        /// <summary>为某种设备算出操作方案：真机=手柄，模拟器/桌面=键鼠（模拟器仍显示手柄模型）。</summary>
        public static ModePlan PlanFor(DeviceKind kind)
        {
            var plan = new ModePlan();
            plan.kind = kind;
            plan.control = kind == DeviceKind.Headset ? ControlMode.XRControllers : ControlMode.KeyboardMouse;
            plan.vrInputEnabled = kind == DeviceKind.Headset;
            plan.desktopInputEnabled = kind != DeviceKind.Headset;
            plan.controllerVisuals = kind != DeviceKind.Desktop;
            plan.keyboardDebug = kind != DeviceKind.Headset;
            plan.headsetDesktopModeOff = kind == DeviceKind.Headset;
            switch (kind)
            {
                case DeviceKind.Headset:
                    plan.status = "VR 头显已连接 · 戴好头显，用左摇杆移动、右摇杆瞬转";
                    break;
                case DeviceKind.Simulator:
                    plan.status = "XR 模拟器 · 相机由模拟器驱动，操作用键盘鼠标";
                    break;
                default:
                    plan.status = "桌面模式 · 未检测到头显，键鼠操作";
                    break;
            }
            return plan;
        }

        /// <summary>手动指定设备类型（调试/探针用）。</summary>
        public void ForceKind(DeviceKind kind)
        {
            forced = true;
            forcedKind = kind;
            deviceKind = kind;
            reason = "手动覆盖（F2=键鼠 / F3=手柄 / F4=自动）";
            ApplyMode(true);
        }

        /// <summary>清掉手动覆盖，回到自动检测。</summary>
        public void ClearForce()
        {
            forced = false;
            DetectNow();
            ApplyMode(true);
        }

        /// <summary>把当前 deviceKind 的操作方案真正应用到各输入层。</summary>
        public void ApplyMode(bool log)
        {
            var plan = PlanFor(deviceKind);
            var previous = controlMode;
            controlMode = plan.control;
            RealHeadset = plan.control == ControlMode.XRControllers;
            VisualsVisible = plan.controllerVisuals;
            _applied = true;
            if (previous != controlMode) SwitchCount++;

            if (vrInput != null) vrInput.enabled = plan.vrInputEnabled;
            if (desktopInput != null)
            {
                desktopInput.enabled = plan.desktopInputEnabled;
                // 组件可能在开局就被 XR 模式禁掉（Awake 没跑过），切回键鼠时补一次初始化
                if (plan.desktopInputEnabled) desktopInput.EnsureReady();
                // 手柄模式下手柄 HUD 必须收起来，否则两块提示条会叠在画面底部
                desktopInput.SetHudVisible(plan.desktopInputEnabled);
            }
            if (locomotion != null) locomotion.ApplyControlMode(plan.control == ControlMode.XRControllers);

            // 手柄视觉：真机与模拟器都显示（模拟器里它是唯一能看见"手在哪"的东西）；桌面模式隐藏
            var visuals = FindObjectsByType<VRControllerVisual>(FindObjectsSortMode.None);
            for (int i = 0; i < visuals.Length; i++)
                if (visuals[i] != null) visuals[i].modeVisible = plan.controllerVisuals;

            // 灭火器的桌面喷射开关：手柄模式下必须关掉 desktopMode，否则压把链路整段失效
            if (plan.headsetDesktopModeOff)
            {
                var ext = FindObjectsByType<Extinguisher>(FindObjectsSortMode.None);
                for (int i = 0; i < ext.Length; i++)
                    if (ext[i] != null) ext[i].desktopMode = false;
            }

            if (vrHud != null)
            {
                vrHud.SetVisible(plan.vrInputEnabled);
                if (plan.vrInputEnabled) vrHud.SetStatus(plan.status);
            }

            if (log)
                Debug.Log("[Mode] " + deviceKind + " → " + controlMode + " · " + DetailLine
                          + "（切换次数 " + SwitchCount + "）");
        }

        /// <summary>当前是否应由手柄层接管（供外部脚本查询，等价于 controlMode==XRControllers）。</summary>
        public static bool XRControlActive => HasInstance ? Instance.controlMode == ControlMode.XRControllers : false;
    }
}
