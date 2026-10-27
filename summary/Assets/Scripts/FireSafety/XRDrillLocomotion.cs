using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

namespace FireSafety
{
    /// <summary>
    /// 用户移动功能（手柄摇杆 + 键盘调试）：
    ///   · 左摇杆：沿头显朝向移动（推杆方向即前进方向），按住 Grip 可加速
    ///   · 右摇杆左右：转身（默认 45° 瞬转，可改连续旋转）
    ///   · 键盘：WASD 移动、Q/E 转身（无头显时桌面调试）
    ///   · 自动挂到 XR 锚点根物体（XRRig / XR Origin / Camera 根），自动添加 CharacterController
    /// 使用 UnityEngine.XR.InputDevices 读取设备，不依赖具体手柄型号与 Input Actions。
    /// </summary>
    public class XRDrillLocomotion : MonoBehaviour
    {
        [Header("速度")]
        [Tooltip("摇杆推满时的移动速度（米/秒）")]
        public float moveSpeed = 2.2f;
        [Tooltip("按住 Grip 时的加速倍率")]
        public float sprintMultiplier = 1.6f;
        [Tooltip("连续转身的角速度（度/秒）")]
        public float turnSpeed = 120f;
        [Tooltip("瞬转身角度（度）")]
        public float snapAngle = 45f;

        [Header("控制")]
        [Tooltip("true=右摇杆瞬转，false=连续转动")]
        public bool useSnapTurn = true;
        [Tooltip("允许按住 Grip 加速")]
        public bool allowSprint = true;
        [Tooltip("启用重力（需要 CharacterController）")]
        public bool useGravity = true;
        public float gravity = -9.81f;

        [Header("锚点（留空自动查找）")]
        [Tooltip("XR 锚点根物体（XRRig/XR Origin）")]
        public Transform rigRoot;
        [Tooltip("头显相机（用于确定移动方向）")]
        public Transform head;

        [Header("调试")]
        [Tooltip("无头显时可用 WASD/Q/E 在桌面预览")]
        public bool keyboardDebug = true;

        Transform _root;
        Transform _head;
        CharacterController _cc;
        Vector3 _fallVelocity;
        Vector2 _lastTurn = Vector2.zero;
        bool _snapped;

        void Start()
        {
            ResolveReferences();
        }

        void ResolveReferences()
        {
            // 本组件由编辑器脚本挂在 XR 锚点根物体上，根物体即移动主体
            if (_root == null) _root = transform;

            if (_head == null)
            {
                var cam = Camera.main;
                if (cam != null) _head = cam.transform;
            }

            if (_cc == null) _cc = GetComponent<CharacterController>();
            if (_cc == null) _cc = gameObject.AddComponent<CharacterController>();
            if (_cc != null)
            {
                _cc.height = 1.6f;
                _cc.center = new Vector3(0f, 0.8f, 0f);
                _cc.radius = 0.3f;
            }
        }

        void Update()
        {
            if (_root == null) { ResolveReferences(); if (_root == null) return; }

            // ---- 输入读取 ----
            Vector2 move = Stick(XRNode.LeftHand, UnityEngine.XR.CommonUsages.primary2DAxis);
            Vector2 turn = Stick(XRNode.RightHand, UnityEngine.XR.CommonUsages.secondary2DAxis);
            bool sprint = allowSprint && (Grip(XRNode.LeftHand) || Grip(XRNode.RightHand));

            if (keyboardDebug && Keyboard.current != null)
            {
                var kb = Keyboard.current;
                float mx = (kb.aKey.isPressed ? -1f : 0f) + (kb.dKey.isPressed ? 1f : 0f);
                float mz = (kb.wKey.isPressed ? 1f : 0f) + (kb.sKey.isPressed ? -1f : 0f);
                if (mx != 0f || mz != 0f) move = new Vector2(mx, mz);
                float kt = (kb.qKey.isPressed ? -1f : 0f) + (kb.eKey.isPressed ? 1f : 0f);
                if (kt != 0f) turn = new Vector2(kt, 0f);
                if (kb.leftShiftKey.isPressed) sprint = true;
            }

            // ---- 转身 ----
            if (turn.x < -0.3f || turn.x > 0.3f)
            {
                if (useSnapTurn)
                {
                    if (!_snapped)
                    {
                        float turnDir = turn.x > 0f ? 1f : -1f;
                        _root.Rotate(0f, turnDir * snapAngle, 0f, Space.World);
                        _snapped = true;
                    }
                }
                else
                {
                    _root.Rotate(0f, turn.x * turnSpeed * Time.deltaTime, 0f, Space.World);
                }
            }
            else _snapped = false;

            // ---- 移动 ----
            Vector3 dir = Vector3.zero;
            if (move.sqrMagnitude > 0.01f)
            {
                float yaw = _head != null ? _head.eulerAngles.y : _root.eulerAngles.y;
                Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                dir = (forward * move.y + right * move.x).normalized;
            }

            float speed = moveSpeed * (sprint ? sprintMultiplier : 1f);

            if (_cc != null && _cc.enabled)
            {
                if (useGravity)
                {
                    _fallVelocity.y = _cc.isGrounded ? -0.5f : _fallVelocity.y + gravity * Time.deltaTime;
                    _cc.Move((dir * speed + _fallVelocity) * Time.deltaTime);
                }
                else
                {
                    _cc.Move(dir * speed * Time.deltaTime);
                }
            }
            else
            {
                _root.position += dir * speed * Time.deltaTime;
            }
        }

        // ---------------- XR 设备读取 ----------------

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
