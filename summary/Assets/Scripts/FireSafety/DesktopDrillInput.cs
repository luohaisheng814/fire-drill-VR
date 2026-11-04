using UnityEngine;
using UnityEngine.InputSystem;

namespace FireSafety
{
    /// <summary>
    /// 桌面鼠标调试输入（无需头显即可走完阶段一流程）：
    ///   右键单击 → 拿起灭火器
    ///   左键单击 → 拔出保险销
    ///   左键双击 → 按下压把（必须手持灭火器）
    ///   右键双击 → 喷射 / 停止喷射
    ///   P         → 放下灭火器
    ///   回车 / 空格 → 开始演练（无鼠标 UI 时也能开局）
    ///   1~4       → 常识问答作答
    /// 与 XR 手柄输入并存：手持状态由 ToggleGrab 统一维护，压把状态由 HandleLever 统一维护。
    /// </summary>
    public class DesktopDrillInput : MonoBehaviour
    {
        [Header("引用")]
        public Extinguisher extinguisher;
        public ExtinguisherQuizPanel quizPanel;
        public StartScreen startScreen;
        public Transform cameraTransform;

        [Header("桌面手部锚点（留空自动创建在相机下）")]
        public Transform handAnchor;
        public Vector3 handOffset = new Vector3(0.26f, -0.20f, 0.45f);
        public Vector3 handEuler = new Vector3(0f, -12f, 0f);

        [Header("双击判定")]
        [Tooltip("两次点击间隔小于该值算双击（秒）")]
        public float doubleClickWindow = 0.25f;

        [Header("提示")]
        public bool logHints = true;

        float _lastLeftClick = -99f;
        float _lastRightClick = -99f;

        private void Awake()
        {
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

            if (handAnchor == null)
            {
                var h = new GameObject("DesktopHand");
                h.transform.SetParent(cameraTransform != null ? cameraTransform : transform, false);
                handAnchor = h.transform;
            }
        }

        private void Start()
        {
            if (extinguisher == null)
            {
                var all = FindObjectsByType<Extinguisher>(FindObjectsSortMode.None);
                if (all.Length > 0) extinguisher = all[0];
            }
            if (extinguisher != null)
            {
                if (quizPanel == null) quizPanel = extinguisher.quizPanel;
                extinguisher.desktopMode = true;     // 喷射改由右键双击触发
            }
            if (startScreen == null) startScreen = FindObjectOfType<StartScreen>();
        }

        private void LateUpdate()
        {
            // 桌面手部锚点跟随视角
            if (handAnchor != null && cameraTransform != null)
            {
                handAnchor.localPosition = handOffset;
                handAnchor.localRotation = Quaternion.Euler(handEuler);
            }

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null) return;

            // ---------- 开始演练 ----------
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
            {
                if (startScreen != null && !startScreen.Started)
                {
                    startScreen.StartGame();
                    Hint("演练开始：找灭火器 → 右键单击拿起");
                }
            }

            // ---------- 右键：单击拿起 / 双击喷射 ----------
            if (mouse.rightButton.wasPressedThisFrame)
            {
                float now = Time.unscaledTime;
                if (now - _lastRightClick <= doubleClickWindow)
                {
                    _lastRightClick = -99f;      // 消费掉这次双击
                    OnRightDoubleClick();
                }
                else
                {
                    _lastRightClick = now;
                    OnRightClick();
                }
            }

            // ---------- 左键：单击拔销 / 双击压把 ----------
            if (mouse.leftButton.wasPressedThisFrame)
            {
                float now = Time.unscaledTime;
                if (now - _lastLeftClick <= doubleClickWindow)
                {
                    _lastLeftClick = -99f;
                    OnLeftDoubleClick();
                }
                else
                {
                    _lastLeftClick = now;
                    OnLeftClick();
                }
            }

            // ---------- P：放下 ----------
            if (keyboard.pKey.wasPressedThisFrame) DropExtinguisher();

            // ---------- 1~4：问答作答 ----------
            if (quizPanel != null && quizPanel.IsOpen)
            {
                for (int i = 0; i < 4; i++)
                {
                    var key = i switch
                    {
                        0 => keyboard.digit1Key,
                        1 => keyboard.digit2Key,
                        2 => keyboard.digit3Key,
                        _ => keyboard.digit4Key
                    };
                    if (key.wasPressedThisFrame) quizPanel.AnswerOption(i);
                }
            }
        }

        // ---------------- 动作 ----------------

        private void OnRightClick()
        {
            if (extinguisher == null) return;
            if (Held)
            {
                Hint("已手持灭火器：左键单击拔销 / 左键双击压把 / 右键双击喷射 / P 放下");
                return;
            }

            extinguisher.grab.GrabExternal(handAnchor, HandSide.Right);
            Hint("已拿起灭火器 → 左键单击拔出保险销");
        }

        private void OnRightDoubleClick()
        {
            if (extinguisher == null) return;
            if (extinguisher.IsSpraying)
            {
                extinguisher.StopSprayExternal();
                Hint("已停止喷射");
                return;
            }
            if (extinguisher.TryStartSpray())
                Hint("正在喷射 → 对准火焰根部；右键双击可停止");
        }

        private void OnLeftClick()
        {
            if (extinguisher == null) return;
            if (!Held) { Hint("请先右键单击拿起灭火器"); return; }
            if (extinguisher.PinPulled) { Hint("保险销已拔出 → 左键双击按下压把"); return; }

            if (extinguisher.pin != null) extinguisher.pin.PullByInput();
            else Hint("该灭火器没有配置保险销（SafetyPin）");
        }

        private void OnLeftDoubleClick()
        {
            if (extinguisher == null) return;
            if (!Held) { Hint("请先右键单击拿起灭火器"); return; }
            if (!extinguisher.PinPulled) { Hint("请先左键单击拔出保险销"); return; }

            if (extinguisher.handleLever == null) { Hint("该灭火器没有配置压把（HandleLever）"); return; }
            if (extinguisher.handleLever.PressExternal())
                Hint("压把已按下 → 右键双击开始喷射");
        }

        private void DropExtinguisher()
        {
            if (extinguisher == null) return;
            if (!Held) { Hint("当前没有手持灭火器"); return; }

            extinguisher.StopSprayExternal();
            if (extinguisher.handleLever != null) extinguisher.handleLever.ReleaseExternal();
            extinguisher.grab.ReleaseExternal();
            _lastLeftClick = _lastRightClick = -99f;
            Hint("已放下灭火器");
        }

        private bool Held => extinguisher != null && extinguisher.grab != null && extinguisher.grab.IsHeld;

        private void Hint(string msg)
        {
            if (logHints) Debug.Log("[Desktop] " + msg);
        }
    }
}
