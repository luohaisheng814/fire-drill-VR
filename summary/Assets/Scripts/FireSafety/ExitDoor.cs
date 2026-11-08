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

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Build();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>解锁出口：指示牌变绿、门板滑开。</summary>
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
        }

        private void Update()
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
            if (_panel != null) return;
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

            // 指示牌（世界空间画布）
            var sign = new GameObject("ExitSign");
            sign.transform.SetParent(transform, false);
            sign.transform.localPosition = new Vector3(0f, h + ft * 3.4f, -t * 2f);
            var canvas = sign.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 310;
            var rt = sign.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(420f, 110f);
            rt.localScale = Vector3.one * 0.0016f;
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
            _signText.fontSize = 46;
            _signText.alignment = TextAnchor.MiddleCenter;
            _signText.color = signLockedColor;
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
