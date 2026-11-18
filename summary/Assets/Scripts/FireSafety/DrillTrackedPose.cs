using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace FireSafety
{
    /// <summary>
    /// 用 XR 设备数据驱动相机/手柄位姿。
    /// 第 31 轮：本工程是 Input System only（activeInputHandler=1），旧的
    /// UnityEngine.XR.InputDevices.GetDeviceAtXRNode 拿不到位姿数据，所以优先读
    /// Input System 的 XRHMD / XRController（TrackedDevice.devicePosition/deviceRotation），
    /// 只有它拿不到时才回落到旧接口。
    /// </summary>
    public class DrillTrackedPose : MonoBehaviour
    {
        public XRNode node = XRNode.CenterEye;

        [Tooltip("优先用 Input System（XRHMD / XRController）读取位姿；关掉则退回 Unity 旧 XR 接口")]
        public bool preferInputSystem = true;

        /// <summary>最近一帧是否真的写入了位姿（诊断与自检用）。</summary>
        public bool HasPose { get; private set; }

        void Update()
        {
            HasPose = false;
            if (preferInputSystem && ReadInputSystem())
            {
                HasPose = true;
                return;
            }
            HasPose = ReadLegacy();
        }

        bool ReadInputSystem()
        {
            if (node == XRNode.CenterEye)
            {
                var hmd = InputSystem.GetDevice<XRHMD>();
                if (hmd == null) return false;
                if (hmd.isTracked != null && hmd.isTracked.ReadValue() < 0.5f) return false;

                Vector3 hp = hmd.centerEyePosition.ReadValue();
                Quaternion hr = hmd.centerEyeRotation.ReadValue();
                if (!IsFinite(hp) || !IsFinite(hr)) return false;

                transform.localPosition = hp;
                transform.localRotation = hr;
                return true;
            }

            XRController pad = null;
            if (node == XRNode.LeftHand) pad = XRController.leftHand;
            else if (node == XRNode.RightHand) pad = XRController.rightHand;
            if (pad == null) return false;
            if (pad.isTracked != null && pad.isTracked.ReadValue() < 0.5f) return false;

            Vector3 p = pad.devicePosition.ReadValue();
            Quaternion r = pad.deviceRotation.ReadValue();
            if (!IsFinite(p) || !IsFinite(r)) return false;

            transform.localPosition = p;
            transform.localRotation = r;
            return true;
        }

        bool ReadLegacy()
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return false;

            if (node == XRNode.CenterEye)
            {
                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyePosition, out Vector3 p) &&
                    device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyeRotation, out Quaternion r))
                {
                    transform.localPosition = p;
                    transform.localRotation = r;
                    return true;
                }
                return false;
            }

            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 dp) &&
                device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion dr))
            {
                transform.localPosition = dp;
                transform.localRotation = dr;
                return true;
            }
            return false;
        }

        static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                     float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }

        static bool IsFinite(Quaternion q)
        {
            return !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w) ||
                     float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));
        }
    }
}
