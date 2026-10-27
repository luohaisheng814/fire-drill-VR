using UnityEngine;
using UnityEngine.XR;

namespace FireSafety
{
    /// <summary>
    /// 用内置 XR 设备数据驱动相机/手柄位姿（无需 TrackedPoseDriver 或 Input Actions）。
    /// 用于一键搭建生成的锚点：相机挂 CenterEye，左右手柄挂 LeftHand/RightHand。
    /// 设备无效时保持原位，不报错。
    /// </summary>
    public class DrillTrackedPose : MonoBehaviour
    {
        public XRNode node = XRNode.CenterEye;

        void Update()
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return;

            if (node == XRNode.CenterEye)
            {
                if (device.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 p))
                    transform.localPosition = p;
                if (device.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion r))
                    transform.localRotation = r;
            }
            else
            {
                if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 p))
                    transform.localPosition = p;
                if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion r))
                    transform.localRotation = r;
            }
        }
    }
}
