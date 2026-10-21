using UnityEngine;
using UnityEngine.XR;

namespace FireSafetyVR
{

    /// <summary>
    /// 零依赖手柄输入读取：只用 Unity 内置的 UnityEngine.XR，
    /// 不需要 Input System 包、不需要 XR Interaction Toolkit 也能编译运行。
    /// </summary>
    public static class XRHandInput
    {
        public enum Btn { Grip, Trigger, Primary }

        public static bool IsDown(XRNode node, Btn btn)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return false;

            switch (btn)
            {
                case Btn.Grip:
                    if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool g)) return g;
                    // 部分手柄没有 gripButton，退回抓握压力值
                    if (device.TryGetFeatureValue(CommonUsages.grip, out float gv)) return gv > 0.5f;
                    return false;

                case Btn.Trigger:
                    if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool t)) return t;
                    if (device.TryGetFeatureValue(CommonUsages.trigger, out float tv)) return tv > 0.5f;
                    return false;

                case Btn.Primary:
                    if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool p)) return p;
                    if (device.TryGetFeatureValue(CommonUsages.menuButton, out bool m)) return m;
                    return false;
            }
            return false;
        }

        /// <summary>边沿检测：按下瞬间返回 true（需配合调用者保存 prev）。</summary>
        public static bool WasPressed(XRNode node, Btn btn, ref bool prev)
        {
            bool now = IsDown(node, btn);
            bool pressed = now && !prev;
            prev = now;
            return pressed;
        }
    }
}
