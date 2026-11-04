using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 桌面鼠标调试输入（无需头显即可走完阶段一流程）：
    ///   右键单击 → 拿起灭火器（自动拿起离视角最近的一台）
    ///   左键单击 → 拔出保险销
    ///   左键双击 → 按下压把（必须手持灭火器）
    ///   右键双击 → 喷射 / 停止喷射
    ///   P         → 放下灭火器
    ///   回车 / 空格 → 开始演练（也可用鼠标点「开始演练」按钮）
    ///   1~4       → 常识问答作答（也可用鼠标点选项）
    /// 与 XR 手柄输入并存：手持状态由 ToggleGrab 统一维护，压把状态由 HandleLever 统一维护。
    /// </summary>
    public class DesktopDrillInput : MonoBehaviour
    {
        [Header("引用")]
        public Extinguisher extinguisher;
        [Tooltip("场景中的全部灭火器（右键单击时取离视角最近的一台）")]
        public Extinguisher[] extinguishers;
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

        [Header("界面提示")]
        [Tooltip("在屏幕底部显示操作说明与当前状态")]
        public bool showHud = true;
        [Tooltip("临时提示停留秒数")]
        public float hintDuration = 3f;

        [Header("调试")]
        public bool logHints = true;

        Extinguisher _active;
        float _lastLeftClick = -99f;
        float _lastRightClick = -99f;

        Canvas _hudCanvas;
        Text _hudText;
        string _status = "";
        float _statusUntil;
        float _nextHudRefresh;

        private void Awake()
        {
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

            if (handAnchor == null)
            {
                var h = new GameObject("DesktopHand");
                h.transform.SetParent(cameraTransform != null ? cameraTransform : transform, false);
                handAnchor = h.transform;
            }

            BuildHud();
        }

        private void OnDestroy()
        {
            if (_hudCanvas != null) Destroy(_hudCanvas.gameObject);
        }

        private void Start()
        {
            if ((extinguishers == null || extinguishers.Length == 0) && extinguisher == null)
                extinguishers = FindObjectsByType<Extinguisher>(FindObjectsSortMode.None);

            if (extinguisher == null)
            {
                if (extinguishers != null && extinguishers.Length > 0) extinguisher = extinguishers[0];
                else
                {
                    var all = FindObjectsByType<Extinguisher>(FindObjectsSortMode.None);
                    if (all.Length > 0) extinguisher = all[0];
                }
            }
            if (extinguishers == null || extinguishers.Length == 0)
                extinguishers = FindObjectsByType<Extinguisher>(FindObjectsSortMode.None);

            foreach (var e in extinguishers)
            {
                if (e == null) continue;
                e.desktopMode = true;                 // 喷射改由右键双击触发
                if (e.pin == null) e.pin = e.GetComponentInChildren<SafetyPin>(true);   // 兜底：避免"左键单击没反应"
            }

            _active = extinguisher;
            if (quizPanel == null && _active != null) quizPanel = _active.quizPanel;
            if (startScreen == null) startScreen = FindObjectOfType<StartScreen>();

            RefreshHudText(true);
        }

        private void LateUpdate()
        {
            // 桌面手部锚点跟随视角
            if (handAnchor != null && cameraTransform != null)
            {
                handAnchor.localPosition = handOffset;
                handAnchor.localRotation = Quaternion.Euler(handEuler);
            }

            RefreshHudText(false);

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null) return;

            // ---------- 开始演练 ----------
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
                TryStartDrill();

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

            // ---------- 左键：优先点 UI，其次拔销 / 压把 ----------
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (TryClickUI())
                {
                    _lastLeftClick = -99f;       // 点 UI 不参与双击判定
                }
                else if (startScreen != null && !startScreen.Started)
                {
                    Hint("请先用鼠标点击屏幕中的「开始演练」按钮（或按 回车 / 空格）");
                }
                else if (quizPanel != null && quizPanel.IsOpen)
                {
                    Hint("请先用鼠标点击题目选项作答（也可按 1~4）");
                }
                else
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

        private void TryStartDrill()
        {
            if (startScreen != null && !startScreen.Started)
            {
                startScreen.StartGame();
                Hint("演练开始：找灭火器 → 右键单击拿起");
            }
        }

        private void OnRightClick()
        {
            if (Held)
            {
                Hint("已手持灭火器：左键单击拔销 / 左键双击压把 / 右键双击喷射 / P 放下");
                return;
            }
            if (startScreen != null && !startScreen.Started)
            {
                Hint("请先开始演练（点击「开始演练」按钮或按 回车 / 空格）");
                return;
            }

            var target = PickNearest();
            if (target == null || target.grab == null) { Hint("场景中没有可拿取的灭火器"); return; }

            _active = target;
            if (target.pin == null) target.pin = target.GetComponentInChildren<SafetyPin>(true);
            if (target.quizPanel != null) quizPanel = target.quizPanel;

            target.grab.GrabExternal(handAnchor, HandSide.Right);
            Hint("已拿起 " + target.gameObject.name + " → 先完成常识问答，再左键单击拔出保险销");
        }

        private void OnRightDoubleClick()
        {
            var ext = Current;
            if (ext == null) return;

            if (ext.IsSpraying)
            {
                ext.StopSprayExternal();
                Hint("已停止喷射");
                return;
            }
            if (ext.TryStartSpray())
                Hint("正在喷射 → 对准火焰根部；右键双击可停止");
        }

        private void OnLeftClick()
        {
            var ext = Current;
            if (ext == null) return;
            if (!Held) { Hint("请先右键单击拿起灭火器"); return; }
            if (ext.PinPulled) { Hint("保险销已拔出 → 左键双击按下压把"); return; }

            if (ext.pin != null)
            {
                if (ext.pin.PullByInput())
                    Hint("保险销已拔出 → 左键双击按下压把");
            }
            else Hint("该灭火器没有配置保险销（SafetyPin）");
        }

        private void OnLeftDoubleClick()
        {
            var ext = Current;
            if (ext == null) return;
            if (!Held) { Hint("请先右键单击拿起灭火器"); return; }
            if (!ext.PinPulled) { Hint("请先左键单击拔出保险销"); return; }

            if (ext.handleLever == null) { Hint("该灭火器没有配置压把（HandleLever）"); return; }
            if (ext.handleLever.PressExternal())
                Hint("压把已按下 → 右键双击开始喷射");
        }

        private void DropExtinguisher()
        {
            var ext = Current;
            if (ext == null) return;
            if (!Held) { Hint("当前没有手持灭火器"); return; }

            ext.StopSprayExternal();
            if (ext.handleLever != null) ext.handleLever.ReleaseExternal();
            ext.grab.ReleaseExternal();
            _lastLeftClick = _lastRightClick = -99f;
            Hint("已放下灭火器");
        }

        private bool Held => _active != null && _active.grab != null && _active.grab.IsHeld;

        /// <summary>当前操作的灭火器：手持哪台就是哪台，否则取离视角最近的一台。</summary>
        private Extinguisher Current
        {
            get
            {
                if (Held) return _active;
                var near = PickNearest();
                if (near != null) _active = near;
                return _active != null ? _active : extinguisher;
            }
        }

        private Extinguisher PickNearest()
        {
            Extinguisher best = null;
            float bestDist = float.MaxValue;
            Vector3 from = cameraTransform != null ? cameraTransform.position : transform.position;

            if (extinguishers != null)
            {
                foreach (var e in extinguishers)
                {
                    if (e == null) continue;
                    float d = Vector3.Distance(e.transform.position, from);
                    if (d < bestDist) { bestDist = d; best = e; }
                }
            }
            return best;
        }

        /// <summary>用鼠标点世界空间 UI（开始按钮、问答选项等）：不依赖 EventSystem 的 XR 射线。</summary>
        private bool TryClickUI()
        {
            var mouse = Mouse.current;
            if (mouse == null) return false;

            Camera cam = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : Camera.main;
            Vector2 pos = mouse.position.ReadValue();

            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b == null || !b.isActiveAndEnabled || !b.interactable) continue;
                var rt = b.transform as RectTransform;
                if (rt == null) continue;

                var canvas = b.GetComponentInParent<Canvas>();
                bool overlay = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay;
                Camera useCam = overlay ? null : cam;
                if (!overlay && useCam == null) continue;    // 世界空间 UI 必须有相机才能算命中

                if (RectTransformUtility.RectangleContainsScreenPoint(rt, pos, useCam))
                {
                    b.onClick.Invoke();
                    return true;
                }
            }
            return false;
        }

        // ---------------- 屏幕提示 ----------------

        private void Hint(string msg)
        {
            _status = msg;
            _statusUntil = Time.unscaledTime + hintDuration;
            if (logHints) Debug.Log("[Desktop] " + msg);
            RefreshHudText(true);
        }

        private void BuildHud()
        {
            if (!showHud || _hudCanvas != null) return;

            var go = new GameObject("DesktopHintHUD");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 320;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            go.AddComponent<GraphicRaycaster>();
            _hudCanvas = canvas;

            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(go.transform, false);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 16f);
            rt.sizeDelta = new Vector2(1560f, 132f);

            var img = bar.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.62f);
            img.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(bar.transform, false);
            _hudText = textGo.GetComponent<Text>();
            _hudText.font = UIFont.Resolve();
            if (_hudText.font != null) _hudText.material = _hudText.font.material;
            _hudText.fontSize = 22;
            _hudText.color = new Color(1f, 0.95f, 0.82f, 1f);
            _hudText.alignment = TextAnchor.MiddleCenter;
            _hudText.raycastTarget = false;
            _hudText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _hudText.verticalOverflow = VerticalWrapMode.Overflow;

            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(14f, 8f);
            trt.offsetMax = new Vector2(-14f, -8f);

            RefreshHudText(true);      // 首帧就把键位说明显示出来
        }

        private void RefreshHudText(bool force)
        {
            if (!showHud || _hudText == null) return;
            if (!force && Time.unscaledTime < _nextHudRefresh) return;
            _nextHudRefresh = Time.unscaledTime + 0.1f;

            const string controls =
                "【鼠标操作】右键单击：拿起灭火器　｜　左键单击：拔出保险销　｜　左键双击：按下压把　" +
                "｜　右键双击：喷射（再双击停止）　｜　P：放下";

            string status = Time.unscaledTime < _statusUntil ? _status : "";
            string line = controls + "\n" + StateText();
            if (!string.IsNullOrEmpty(status)) line += "　｜　" + status;
            _hudText.text = line;
        }

        private string StateText()
        {
            var mgr = FireDrillManager.Instance;
            if (mgr != null && mgr.IsCompleted) return "状态：演练完成（火焰已扑灭）";
            if (startScreen != null && !startScreen.Started) return "状态：等待开始（点击「开始演练」或按 回车 / 空格）";

            var ext = Current;
            if (ext == null) return "状态：场景中没有找到灭火器";
            if (!Held) return "状态：寻找并拿起灭火器（右键单击）";
            if (quizPanel != null && quizPanel.IsOpen) return "状态：请先完成常识问答（点击选项或按 1~4）";
            if (!ext.PinPulled) return "状态：已手持 → 左键单击拔出保险销";
            if (ext.handleLever != null && !ext.handleLever.PressedOnce)
                return "状态：已拔销 → 左键双击按下压把";
            if (ext.IsSpraying) return "状态：正在喷射 → 对准火焰根部，右键双击停止";
            return "状态：压把已按下 → 右键双击开始喷射";
        }
    }
}
