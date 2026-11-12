using UnityEngine;
using UnityEngine.XR;

namespace FireSafety
{
    /// <summary>
    /// 手柄触觉反馈：抓取、拔销、压把、答题等关键动作给一次短脉冲，
    /// 让玩家在头显里「手上能感到」动作已被接收（VR 里没有键盘回弹，触觉是唯一的确认感）。
    ///
    /// 只用 UnityEngine.XR 裸 API（InputDevice.SendHapticImpulse），不依赖 XRI 的 Haptic 动作资产，
    /// 因此不论头显品牌、是否走 XRI 动作，都能发出去；设备不支持时静默跳过（无头显的探针模式下属于正常情况）。
    /// </summary>
    public static class VRHaptics
    {
        /// <summary>总开关（false 时代码路径仍走，但不下发，用于「静音」对比测试）。</summary>
        public static bool Enabled = true;

        /// <summary>调用次数（无论设备是否存在都计数），供自检与探针断言使用。</summary>
        public static int AttemptCount { get; private set; }

        /// <summary>真正下发成功的次数。</summary>
        public static int SendCount { get; private set; }

        /// <summary>没有可用设备 / 设备不支持脉冲的次数。</summary>
        public static int SkipCount { get; private set; }

        /// <summary>最近一次脉冲的可读描述（写在 HUD/日志里便于排查）。</summary>
        public static string LastInfo { get; private set; } = "（尚未触发）";

        public static void ResetCounters()
        {
            AttemptCount = 0;
            SendCount = 0;
            SkipCount = 0;
            LastInfo = "（尚未触发）";
        }

        /// <summary>按手侧发脉冲（amplitude 0~1，duration 秒）。</summary>
        public static bool Pulse(HandSide side, float amplitude, float duration)
        {
            return PulseNode(side == HandSide.Left ? XRNode.LeftHand : XRNode.RightHand, amplitude, duration);
        }

        /// <summary>双手同时脉冲（阶段完成、灭火成功这类「大事」用）。</summary>
        public static void PulseBoth(float amplitude, float duration)
        {
            PulseNode(XRNode.LeftHand, amplitude, duration);
            PulseNode(XRNode.RightHand, amplitude, duration);
        }

        /// <summary>按 XR 节点发脉冲。</summary>
        public static bool PulseNode(XRNode node, float amplitude, float duration)
        {
            AttemptCount++;
            if (!Enabled) { SkipCount++; LastInfo = "已关闭（Enabled=false）"; return false; }

            amplitude = Mathf.Clamp01(amplitude);
            duration = Mathf.Max(0.01f, duration);

            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
            {
                // 无头显（探针/桌面）时属正常：不报警，只记一次跳过
                SkipCount++;
                LastInfo = node + " 无设备（未连接头显）";
                return false;
            }

            HapticCapabilities caps;
            if (device.TryGetHapticCapabilities(out caps) && caps.supportsImpulse)
            {
                if (device.SendHapticImpulse(0u, amplitude, duration))
                {
                    SendCount++;
                    LastInfo = node + " 脉冲 " + amplitude.ToString("0.00") + " / " + duration.ToString("0.00") + " 秒";
                    return true;
                }
            }

            SkipCount++;
            LastInfo = node + " 设备不支持脉冲";
            return false;
        }
    }
}
