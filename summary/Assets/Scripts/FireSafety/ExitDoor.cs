using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 安全出口门：大厅尽头的最终出口。
    ///   · 未解锁：门紧闭，上方指示牌为暗红色「安全出口 EXIT」；
    ///   · Unlock() 后：指示牌变绿并微微发光，门板向上滑开；
    ///   · 玩家（相机）走到门口 enterDistance 米内即触发 onEntered，用于判定通关。
    /// 门体全部由运行时基本体拼成，不依赖场景美术资源，便于一键装配与自检。
    /// </summary>
    public class ExitDoor : MonoBehaviour
    {
        public static ExitDoor Instance { get; private set; }

        [Header("尺寸")]
        public float doorWidth = 1.5f;
        public float doorHeight = 2.1f;
        public float doorThickness = 0.1f;
        public float frameThickness = 0.12f;

        [Header("判定")]
        public float enterDistance = 0.9f;
        [Tooltip("留空则用 Camera.main")]
        public Transform player;
        public bool detectByDistance = true;

        [Header("复用场景里的真实门（图 3 那扇 door_b）")]
        [Tooltip("填了就用这扇真实门当出口：不再另建门框门板，解锁时门扇绕门轴转开")]
        public Transform existingDoorRoot;
        [Tooltip("解锁后门扇绕 Y 轴转开的角度（度）")]
        public float existingOpenAngle = -95f;
        [Tooltip("指示牌中心高度（米）")]
        public float signHeight = 2.42f;

        [Header("文案")]
        public string signText = "安全出口 EXIT";
        public Color signLockedColor = new Color(0.72f, 0.22f, 0.18f);
        public Color signOpenColor = new Color(0.25f, 0.85f, 0.42f);

        [Header("事件")]
        public UnityEvent onUnlocked = new UnityEvent();
        public UnityEvent onEntered = new UnityEvent();

        public bool IsUnlocked { get; private set; }
        public bool PlayerEntered { get; private set; }

        Canvas _signCanvas;
        Text _signText;
        Transform _panel;
        Quaternion _existingBaseRot = Quaternion.identity;
        bool _existingRotSaved;
        float _openT = -1f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Build();
            CaptureExistingDoor();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>记录真实门的初始旋转，用于解锁时绕门轴转开。</summary>
        public void CaptureExistingDoor()
        {
            if (existingDoorRoot == null || _existingRotSaved) return;
            _existingBaseRot = existingDoorRoot.localRotation;
            _existingRotSaved = true;
        }

        /// <summary>解锁出口：指示牌变绿、门扇打开。</summary>
        public void Unlock()
        {
            if (IsUnlocked) return;
            IsUnlocked = true;
            if (_signText != null) _signText.color = signOpenColor;
            if (_panel != null)
            {
                var lp = _panel.localPosition;
                _panel.localPosition = new Vector3(lp.x, lp.y + doorHeight * 0.92f, lp.z);
            }
            else if (existingDoorRoot != null)
            {
                CaptureExistingDoor();
                if (_openT < 0f) _openT = 0f;
            }
            onUnlocked?.Invoke();
            Debug.Log("[ExitDoor] 安全出口已解锁，门已打开。");
        }

        public void Lock()
        {
            IsUnlocked = false;
            if (_signText != null) _signText.color = signLockedColor;
            if (_panel != null)
            {
                var lp = _panel.localPosition;
                float baseY = doorHeight * 0.5f + frameThickness;
                _panel.localPosition = new Vector3(lp.x, baseY, lp.z);
            }
            if (existingDoorRoot != null && _existingRotSaved)
            {
                existingDoorRoot.localRotation = _existingBaseRot;
                _openT = -1f;
            }
        }

        private void Update()
        {
            BillboardSign();
            AnimateExistingDoor();
            DetectPlayerEnter();
        }

        /// <summary>指示牌始终正对相机，避免只从一个角度看得到。</summary>
        private void BillboardSign()
        {
            if (_signCanvas == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            var t = _signCanvas.transform;
            Vector3 toCam = t.position - cam.transform.position;
            if (toCam.sqrMagnitude < 0.0001f) return;
            t.rotation = Quaternion.LookRotation(toCam, Vector3.up);
        }

        /// <summary>复用真实门时，解锁后让门扇绕门轴缓缓转开。</summary>
        private void AnimateExistingDoor()
        {
            if (_openT < 0f) return;
            if (existingDoorRoot == null) return;
            _openT += Time.deltaTime;
            float k = Mathf.Clamp01(_openT / 0.85f);
            k = k * k * (3f - 2f * k);
            existingDoorRoot.localRotation =
                _existingBaseRot * Quaternion.Euler(0f, existingOpenAngle * k, 0f);
            if (k >= 1f) _openT = -1f;
        }

        private void DetectPlayerEnter()
        {
            if (!detectByDistance || PlayerEntered || !IsUnlocked) return;
            Transform p = player != null ? player : (Camera.main != null ? Camera.main.transform : null);
            if (p == null) return;

            Vector3 a = new Vector3(p.position.x, 0f, p.position.z);
            Vector3 b = new Vector3(transform.position.x, 0f, transform.position.z);
            if (Vector3.Distance(a, b) <= enterDistance)
            {
                PlayerEntered = true;
                onEntered?.Invoke();
                Debug.Log("[ExitDoor] 玩家进入安全出口，通关判定触发。");
            }
        }

        /// <summary>供自检/流程图直接调用，等价于玩家走到门口。</summary>
        public bool TryEnter()
        {
            if (!IsUnlocked || PlayerEntered) return false;
            PlayerEntered = true;
            onEntered?.Invoke();
            return true;
        }

        // ---------------- 运行时搭建 ----------------

        private void Build()
        {
            if (_panel != null || (_signCanvas != null && existingDoorRoot != null)) return;

            bool reuse = existingDoorRoot != null;

            if (!reuse)
            {
                var matFrame = MakeMaterial(new Color(0.22f, 0.24f, 0.27f));
                var matDoor = MakeMaterial(new Color(0.34f, 0.38f, 0.42f));

                float w = doorWidth, h = doorHeight, t = doorThickness, ft = frameThickness;

                // 门框：左右立柱 + 上横梁
                MakeBox("FrameLeft", new Vector3(-(w * 0.5f + ft * 0.5f), h * 0.5f + ft, 0f),
                    new Vector3(ft, h + ft, ft), matFrame);
                MakeBox("FrameRight", new Vector3(w * 0.5f + ft * 0.5f, h * 0.5f + ft, 0f),
                    new Vector3(ft, h + ft, ft), matFrame);
                MakeBox("FrameTop", new Vector3(0f, h + ft * 1.5f, 0f),
                    new Vector3(w + ft * 3f, ft, ft), matFrame);

                // 门板
                var panelGo = MakeBox("DoorPanel", new Vector3(0f, h * 0.5f + ft, 0f),
                    new Vector3(w, h, t), matDoor);
                _panel = panelGo.transform;

                // 门把手
                MakeBox("DoorHandle", new Vector3(w * 0.32f, h * 0.45f + ft, -t * 0.6f),
                    new Vector3(0.08f, 0.08f, 0.16f), matFrame, _panel);
            }

            // 指示牌（世界空间画布）：复用真实门时挂在门框上方，尺寸放大到一眼可读
            var sign = new GameObject("ExitSign");
            sign.transform.SetParent(transform, false);
            if (reuse)
            {
                // 真实门在 z≈0 的墙面上，牌子朝玩家一侧（+Z）偏出 0.12 米，避免与墙面重叠
                sign.transform.localPosition = new Vector3(0f, signHeight, 0.12f);
            }
            else
            {
                sign.transform.localPosition = new Vector3(0f, doorHeight + frameThickness * 3.4f, -doorThickness * 2f);
            }
            var canvas = sign.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 310;
            var rt = sign.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(560f, 150f);
            rt.localScale = Vector3.one * 0.0016f;   // ≈0.90 米宽 × 0.24 米高
            sign.AddComponent<CanvasScaler>();
            sign.AddComponent<GraphicRaycaster>();

            var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(sign.transform, false);
            var brt = bg.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.08f, 0.92f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(sign.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8f, 6f);
            trt.offsetMax = new Vector2(-8f, -6f);
            _signText = textGo.GetComponent<Text>();
            _signText.text = signText;
            _signText.font = UIFont.Resolve();
            if (_signText.font != null) _signText.material = _signText.font.material;
            _signText.fontSize = 62;
            _signText.fontStyle = FontStyle.Bold;
            _signText.alignment = TextAnchor.MiddleCenter;
            _signText.color = signLockedColor;
            _signText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _signText.raycastTarget = false;

            _signCanvas = canvas;
            sign.transform.rotation = Quaternion.LookRotation(-transform.forward, Vector3.up);
        }

        private GameObject MakeBox(string name, Vector3 localPos, Vector3 size, Material mat)
            => MakeBox(name, localPos, size, mat, transform);

        private GameObject MakeBox(string name, Vector3 localPos, Vector3 size, Material mat, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var rend = go.GetComponent<Renderer>();
            if (rend != null && mat != null) rend.sharedMaterial = mat;
            return go;
        }

        private static Material MakeMaterial(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            var mat = new Material(shader);
            mat.color = c;
            return mat;
        }
    }
}
