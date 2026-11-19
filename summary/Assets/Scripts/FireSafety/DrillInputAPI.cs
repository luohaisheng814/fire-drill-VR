using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 第 33 轮：输入手感的公开 API（桌面视角 / 移动 / 设备模式）。
    ///
    /// 目的：漂移与"过于灵敏"不再只能改 Inspector 后重进 Play，调试脚本、自检、
    /// 外部工具都能在运行时读写同一组参数：
    ///   DrillInputAPI.SetLook(0.018f, 0.012f);         // 度/像素
    ///   DrillInputAPI.SetLookSmoothing(0.05f);         // 秒，0=关闭
    ///   DrillInputAPI.SetMove(2.0f, 100f);             // 米/秒，度/秒
    ///   DrillInputAPI.SetStick(0.2f, 1.55f, 0.06f);    // 死区、曲线、平滑
    ///   DrillInputAPI.SetDeviceMode("desktop");        // auto/desktop/headset/simulator
    ///   DrillInputAPI.Recenter();                      // 把玩家拉回脚下
    ///   DrillInputAPI.Snapshot();                      // 一行诊断
    ///
    /// 说明：参数只影响键鼠/摇杆手感，不改判定与流程；缺少对应组件时返回 false，不抛异常。
    /// </summary>
    public static class DrillInputAPI
    {
        public struct LookConfig
        {
            public float yawSensitivity;
            public float pitchSensitivity;
            public float deadzonePixels;
            public float clampPixels;
            public float smoothingSeconds;
            public float referenceHeight;
            public bool lockCameraPosition;
        }

        public struct MoveConfig
        {
            public float moveSpeed;
            public float turnSpeed;
            public float stickDeadzone;
            public float stickExponent;
            public float stickSmoothing;
            public bool xrControlMode;
        }

        static T Find<T>(T fallback) where T : Component
        {
            if (fallback != null) return fallback;
            var all = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return all != null && all.Length > 0 ? all[0] : null;
        }

        static DesktopDrillInput Desk() => Find(DesktopDrillInput.Instance);

        static XRDrillLocomotion Loco() => Find<XRDrillLocomotion>(null);

        // ---------------- 视角 ----------------

        public static bool GetLook(out LookConfig cfg)
        {
            var d = Desk();
            if (d == null) { cfg = default; return false; }
            cfg = new LookConfig
            {
                yawSensitivity = d.mouseLookSensitivity,
                pitchSensitivity = d.mouseLookPitchSensitivity,
                deadzonePixels = d.mouseDeadzone,
                clampPixels = d.mouseDeltaClamp,
                smoothingSeconds = d.lookSmoothing,
                referenceHeight = d.lookReferenceHeight,
                lockCameraPosition = d.lockCameraPosition
            };
            return true;
        }

        /// <summary>设置水平/垂直灵敏度（度/像素）。传 0 或负数表示保持原值。</summary>
        public static bool SetLook(float yawDegPerPixel, float pitchDegPerPixel)
        {
            var d = Desk();
            if (d == null) return false;
            if (yawDegPerPixel > 0f) d.mouseLookSensitivity = yawDegPerPixel;
            if (pitchDegPerPixel > 0f) d.mouseLookPitchSensitivity = pitchDegPerPixel;
            return true;
        }

        /// <summary>设置死区（像素）与单帧限幅（像素）。传负值表示保持原值。</summary>
        public static bool SetLookFilter(float deadzonePixels, float clampPixels)
        {
            var d = Desk();
            if (d == null) return false;
            if (deadzonePixels >= 0f) d.mouseDeadzone = deadzonePixels;
            if (clampPixels > 0f) d.mouseDeltaClamp = clampPixels;
            return true;
        }

        public static bool SetLookSmoothing(float smoothingSeconds)
        {
            var d = Desk();
            if (d == null) return false;
            d.lookSmoothing = Mathf.Clamp(smoothingSeconds, 0f, 0.3f);
            return true;
        }

        /// <summary>桌面模式是否锁住相机本地位置（防 XR / 角色控制器把相机推走）。</summary>
        public static bool SetLockCameraPosition(bool locked)
        {
            var d = Desk();
            if (d == null) return false;
            d.lockCameraPosition = locked;
            return true;
        }

        // ---------------- 移动 ----------------

        public static bool GetMove(out MoveConfig cfg)
        {
            var l = Loco();
            if (l == null) { cfg = default; return false; }
            cfg = new MoveConfig
            {
                moveSpeed = l.moveSpeed,
                turnSpeed = l.turnSpeed,
                stickDeadzone = l.deadzone,
                stickExponent = l.stickExponent,
                stickSmoothing = l.stickSmoothing,
                xrControlMode = l.xrControlMode
            };
            return true;
        }

        /// <summary>移动速度（米/秒）与连续转身速度（度/秒）。</summary>
        public static bool SetMove(float metersPerSecond, float turnDegreesPerSecond)
        {
            var l = Loco();
            if (l == null) return false;
            l.SetMoveTuning(metersPerSecond, turnDegreesPerSecond);
            return true;
        }

        /// <summary>摇杆死区 / 响应曲线指数 / 平滑时间常数。</summary>
        public static bool SetStick(float deadzone, float exponent, float smoothingSeconds)
        {
            var l = Loco();
            if (l == null) return false;
            l.SetStickTuning(deadzone, exponent, smoothingSeconds);
            return true;
        }

        // ---------------- 设备与重置 ----------------

        /// <summary>auto / desktop / headset / simulator；返回是否识别了该名称。</summary>
        public static bool SetDeviceMode(string mode)
        {
            if (!RuntimeDeviceMode.HasInstance) return false;
            switch ((mode ?? "").Trim().ToLowerInvariant())
            {
                case "auto":
                case "自动":
                    RuntimeDeviceMode.Instance.ClearForce();
                    return true;
                case "desktop":
                case "键鼠":
                case "桌面":
                    RuntimeDeviceMode.Instance.ForceKind(RuntimeDeviceMode.DeviceKind.Desktop);
                    return true;
                case "headset":
                case "vr":
                case "头显":
                    RuntimeDeviceMode.Instance.ForceKind(RuntimeDeviceMode.DeviceKind.Headset);
                    return true;
                case "simulator":
                case "模拟器":
                    RuntimeDeviceMode.Instance.ForceKind(RuntimeDeviceMode.DeviceKind.Simulator);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>把玩家整体挪回当前头部所在位置（桌面用于纠偏）。</summary>
        public static bool Recenter()
        {
            var l = Loco();
            if (l == null) return false;
            l.RecenterOrigin();
            return true;
        }

        /// <summary>一行诊断，写日志或 HUD 都够用。</summary>
        public static string Snapshot()
        {
            var sb = new System.Text.StringBuilder("[InputAPI] ");
            if (RuntimeDeviceMode.HasInstance)
                sb.Append(RuntimeDeviceMode.Instance.DetailLine).Append(" · ");
            LookConfig look;
            if (GetLook(out look))
                sb.Append("视角=").Append(look.yawSensitivity.ToString("F3")).Append("/")
                  .Append(look.pitchSensitivity.ToString("F3")).Append(" 度/px · 死区=")
                  .Append(look.deadzonePixels.ToString("F1")).Append("px · 平滑=")
                  .Append(look.smoothingSeconds.ToString("F2")).Append("s · 锁位=")
                  .Append(look.lockCameraPosition ? "开" : "关").Append(" · ");
            MoveConfig move;
            if (GetMove(out move))
                sb.Append("移动=").Append(move.moveSpeed.ToString("F2")).Append("m/s · 摇杆死区=")
                  .Append(move.stickDeadzone.ToString("F2")).Append(" · 曲线=")
                  .Append(move.stickExponent.ToString("F2"));
            return sb.ToString();
        }
    }
}
