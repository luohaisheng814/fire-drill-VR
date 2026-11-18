using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FireSafety
{
    /// <summary>
    /// 第 30 轮：手柄按键的"多路读取"层。
    ///
    /// 背景：本工程 Project Settings 的 Active Input Handling 是「Input System Package (New)」，
    /// 而第 21 轮的 VRDrillInput 用的是 UnityEngine.XR.InputDevices（旧 XR 裸读）。
    /// 在 OpenXR + 新输入系统下这套裸读经常拿不到设备（isValid=false），结果就是
    /// 「左摇杆能走（走的是 XRI 的 InputAction），但 A/B/X、Grip、扳机全无反应」。
    ///
    /// 读取顺序（谁先有值谁生效）：
    ///   1. XRI 的 InputActionReference（扳机，场景里已绑定）；
    ///   2. 新输入系统的 XRController 设备控制（trigger / grip / primaryButton / secondaryButton /
    ///      primary2DAxis / primary2DAxisClick）；
    ///   3. 旧 UnityEngine.XR.InputDevices（兼容老 XR 插件）；
    ///   4. 键盘兜底（仅当"判定为头显、但两只手柄都没识别到"时启用，
    ///      方便 PC 串流用户先用键盘把流程跑通，排查到底卡在哪一层）。
    /// </summary>
    public static class VRInputSource
    {
        public struct Snapshot
        {
            public bool trigger, grip, primary, secondary, leftPrimary, leftSecondary;
            public bool stickClickLeft, stickClickRight;
            public float triggerValue, gripValue;
            public bool leftValid, rightValid, headValid;
            public string leftName, rightName, headName;
            public string source;              // 本次值来自哪一层
            public bool keyboardFallback;      // 是否在用键盘兜底
        }

        const string UsageLeft = "LeftHand";
        const string UsageRight = "RightHand";

        static InputActionReference _rightTrigger;
        static InputActionReference _leftTrigger;

        static bool _leftValid, _rightValid, _headValid;
        static string _leftName = "", _rightName = "", _headName = "";
        static string _lastSource = "无";
        static float _lastTrigger, _lastGrip;

        /// <summary>上一次读取时两只手柄是否被识别到（供 RuntimeDeviceMode / 面板查询）。</summary>
        public static bool AnyControllerValid => _leftValid || _rightValid;
        public static bool LeftControllerValid => _leftValid
            || (UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand).isValid);

        /// <summary>绑定 XRI 的扳机动作（Setup 时调用；未绑定也能靠后面几层读）。</summary>
        public static void BindActions(InputActionReference right, InputActionReference left)
        {
            _rightTrigger = right;
            _leftTrigger = left;
            Enable(_rightTrigger);
            Enable(_leftTrigger);
        }

        public static void Enable(InputActionReference r)
        {
            if (r != null && r.action != null) r.action.Enable();
        }

        /// <summary>读一帧完整状态。allowKeyboard=false 时绝不碰键盘（桌面层自己处理键鼠）。</summary>
        public static Snapshot Read(bool allowKeyboard)
        {
            var s = new Snapshot();
            RefreshDeviceNames(ref s);

            // ---- 1) XRI 动作（扳机）----
            float actionTrigger = 0f;
            if (_rightTrigger != null && _rightTrigger.action != null)
                actionTrigger = Mathf.Max(actionTrigger, _rightTrigger.action.ReadValue<float>());
            if (_leftTrigger != null && _leftTrigger.action != null)
                actionTrigger = Mathf.Max(actionTrigger, _leftTrigger.action.ReadValue<float>());

            // ---- 2) 新输入系统 XRController ----
            var right = FindXR(UsageRight);
            var left = FindXR(UsageLeft);
            string src = "无";

            if (right != null)
            {
                s.triggerValue = Mathf.Max(s.triggerValue, Float(Find(right, "trigger", "triggerButton")));
                s.gripValue = Mathf.Max(s.gripValue, Float(Find(right, "grip", "gripButton")));
                s.primary = Bool(Find(right, "primaryButton"));
                s.secondary = Bool(Find(right, "secondaryButton"));
                s.stickClickRight = Bool(Find(right, "primary2DAxisClick"));
                if (right != null) src = "InputSystem";
            }
            if (left != null)
            {
                s.triggerValue = Mathf.Max(s.triggerValue, Float(Find(left, "trigger", "triggerButton")));
                s.gripValue = Mathf.Max(s.gripValue, Float(Find(left, "grip", "gripButton")));
                s.leftPrimary = Bool(Find(left, "primaryButton"));
                s.leftSecondary = Bool(Find(left, "secondaryButton"));
                s.stickClickLeft = Bool(Find(left, "primary2DAxisClick"));
                src = "InputSystem";
            }
            s.triggerValue = Mathf.Max(s.triggerValue, actionTrigger);

            // ---- 3) 旧 XR 裸读（Input System 层拿不到时兜底）----
            if (!s.trigger && !s.grip && !s.primary && !s.leftPrimary
                && (src == "无" || !AnyControllerValid))
            {
                var r = LegacyButtonFeatures(UnityEngine.XR.XRNode.RightHand);
                if (r.valid)
                {
                    s.trigger |= r.trigger;
                    s.grip |= r.grip;
                    s.primary |= r.primary;
                    s.secondary |= r.secondary;
                    s.stickClickRight |= r.stick;
                    s.triggerValue = Mathf.Max(s.triggerValue, r.triggerValue);
                    s.gripValue = Mathf.Max(s.gripValue, r.gripValue);
                    src = "XR.Input";
                }
                var l = LegacyButtonFeatures(UnityEngine.XR.XRNode.LeftHand);
                if (l.valid)
                {
                    s.trigger |= l.trigger;
                    s.grip |= l.grip;
                    s.leftPrimary |= l.primary;
                    s.leftSecondary |= l.secondary;
                    s.stickClickLeft |= l.stick;
                    s.triggerValue = Mathf.Max(s.triggerValue, l.triggerValue);
                    s.gripValue = Mathf.Max(s.gripValue, l.gripValue);
                    if (src == "无") src = "XR.Input";
                }
            }

            s.trigger |= s.triggerValue > 0.6f;
            s.grip |= s.gripValue > 0.6f;

            // ---- 4) 键盘兜底：只在"有头显、但两只手柄都没识别"时启用 ----
            if (allowKeyboard && !s.leftValid && !s.rightValid)
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    s.trigger |= kb.spaceKey.isPressed;
                    s.grip |= kb.gKey.isPressed;
                    s.primary |= kb.enterKey.isPressed;
                    s.secondary |= kb.bKey.isPressed;
                    s.leftPrimary |= kb.xKey.isPressed;
                    s.leftSecondary |= kb.yKey.isPressed;
                    s.stickClickRight |= kb.rKey.isPressed;
                    s.keyboardFallback = true;
                    if (src == "无") src = "键盘兜底";
                }
            }

            _lastSource = src;
            _lastTrigger = s.triggerValue;
            _lastGrip = s.gripValue;
            s.source = src;
            s.leftValid = _leftValid;
            s.rightValid = _rightValid;
            s.headValid = _headValid;
            s.leftName = _leftName;
            s.rightName = _rightName;
            s.headName = _headName;
            return s;
        }

        /// <summary>面板/日志用的一行设备诊断。</summary>
        public static string Diagnostics()
        {
            string l = _leftValid ? Short(_leftName) : "未识别";
            string r = _rightValid ? Short(_rightName) : "未识别";
            string h = _headValid ? Short(_headName) : "—";
            return "设备 左=" + l + " 右=" + r + " 头显=" + h
                 + " · 输入源 " + _lastSource
                 + " · T/G " + _lastTrigger.ToString("F2") + "/" + _lastGrip.ToString("F2");
        }

        /// <summary>给 HUD 的短句：手柄识别状态（不占太多字）。</summary>
        public static string ControllerBrief()
        {
            if (_leftValid && _rightValid) return "双手柄已识别";
            if (_leftValid) return "仅左手柄已识别";
            if (_rightValid) return "仅右手柄已识别";
            return "手柄未识别（按 Y 看诊断）";
        }

        // ---------------- 内部 ----------------

        static void RefreshDeviceNames(ref Snapshot s)
        {
            _headValid = false; _leftValid = false; _rightValid = false;
            _headName = _leftName = _rightName = "";

            var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
            if (head.isValid) { _headValid = true; _headName = head.name; }

            var l = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (l.isValid) { _leftValid = true; _leftName = l.name; }
            var r = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (r.isValid) { _rightValid = true; _rightName = r.name; }

            s.leftValid = _leftValid;
            s.rightValid = _rightValid;
            s.headValid = _headValid;
            s.leftName = _leftName;
            s.rightName = _rightName;
            s.headName = _headName;
        }

        static UnityEngine.InputSystem.XR.XRController FindXR(string usage)
        {
            var devices = UnityEngine.InputSystem.InputSystem.devices;
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                if (!(d is UnityEngine.InputSystem.XR.XRController)) continue;
                bool match = false;
                for (int u = 0; u < d.usages.Count; u++)
                    if (d.usages[u].ToString() == usage) { match = true; break; }
                if (match) return (UnityEngine.InputSystem.XR.XRController)d;
            }
            return null;
        }

        static InputControl Find(InputControl parent, params string[] names)
        {
            if (parent == null) return null;
            for (int i = 0; i < names.Length; i++)
            {
                var c = parent.TryGetChildControl<InputControl>(names[i]);
                if (c != null) return c;
            }
            return null;
        }

        static bool Bool(InputControl c)
        {
            if (c == null) return false;
            object o = c.ReadValueAsObject();
            if (o is bool b) return b;
            if (o is float f) return f > 0.5f;
            return false;
        }

        static float Float(InputControl c)
        {
            if (c == null) return 0f;
            object o = c.ReadValueAsObject();
            if (o is float f) return f;
            if (o is bool b) return b ? 1f : 0f;
            return 0f;
        }

        struct LegacyButtons
        {
            public bool valid;
            public bool trigger, grip, primary, secondary, stick;
            public float triggerValue, gripValue;
        }

        static LegacyButtons LegacyButtonFeatures(UnityEngine.XR.XRNode node)
        {
            var result = new LegacyButtons();
            var d = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (!d.isValid) return result;
            result.valid = true;
            d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out result.trigger);
            d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out result.grip);
            d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out result.primary);
            d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out result.secondary);
            d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out result.stick);
            d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out result.triggerValue);
            d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out result.gripValue);
            return result;
        }

        static string Short(string name)
        {
            if (string.IsNullOrEmpty(name)) return "—";
            return name.Length <= 18 ? name : name.Substring(0, 18) + "…";
        }
    }
}
