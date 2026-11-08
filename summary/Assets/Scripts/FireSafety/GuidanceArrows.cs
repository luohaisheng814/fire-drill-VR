using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 地面引导箭头：演练开始（警报响起）后，在地面生成一串沿玩家→灭火器方向排布的箭头；
    /// 一旦灭火器被玩家看到（ExtinguisherHighlight.Spotted）或被拿起，箭头自动隐藏。
    /// 箭头全部放在 Ignore Raycast 层，不参与 XRI 射线交互。
    /// </summary>
    public class GuidanceArrows : MonoBehaviour
    {
        public static GuidanceArrows Instance { get; private set; }

        [Header("引导目标（留空自动收集场景内所有 Extinguisher）")]
        public List<Transform> targets = new List<Transform>();

        [Header("第 2 阶段：大厅撤离目标")]
        public Transform lobbyTarget;
        public float lobbyArriveDistance = 0.6f;
        public UnityEvent onArrivedAtLobby = new UnityEvent();

        [Header("第 3 阶段：安全出口目标")]
        public Transform exitTarget;
        public float exitArriveDistance = 0.9f;

        [Header("箭头参数")]
        public int arrowCount = 6;
        public float spacing = 1.2f;
        public float floorY = 0.02f;
        public Color arrowColor = new Color(1f, 0.82f, 0.15f);
        public float pulseHz = 2.5f;
        public bool autoStartOnAlarm = true;

        [Header("第二阶段：浓烟中的地面安全出口标识")]
        [Tooltip("第二阶段箭头整体放大倍数：烟雾里更醒目")]
        public float stage2ArrowScale = 2.2f;
        [Tooltip("悬浮标识文案（始终朝向玩家，烟雾挡不住）")]
        public string floorSignText = "安全出口 →";
        [Tooltip("标识离地高度（米）：贴近地面，浓烟下部留空处一眼可见")]
        public float floorSignHeight = 0.62f;
        [Tooltip("标识放在目标前方多少米（朝玩家一侧）")]
        public float floorSignBackOffset = 0.9f;

        [Header("事件")]
        public UnityEvent onGuidanceStarted;
        public UnityEvent onGuidanceStopped;

        public bool Active { get; private set; }
        /// <summary>当前是否处于「引导去大厅」模式（true 时箭头指向 lobbyTarget，不再指灭火器）。</summary>
        public bool LobbyMode { get; private set; }
        /// <summary>当前是否处于「引导去安全出口」模式（第三阶段灭火完成后开启）。</summary>
        public bool ExitMode { get; private set; }
        /// <summary>是否已生成「安全出口 →」悬浮标识（自检用）。</summary>
        public bool HasExitSign => _signRoot != null;
        /// <summary>悬浮标识当前是否可见（自检用）。</summary>
        public bool ExitSignVisible => _signRoot != null && _signRoot.activeSelf;

        Camera _cam;
        Transform _player;
        GameObject[] _arrows;
        GameObject _signRoot;
        Text _signText;
        Material _arrowMat;
        bool _collected;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDisable()
        {
            var mgr = FireDrillManager.Instance;
            if (mgr != null) mgr.onStepChanged.RemoveListener(OnStepChanged);
        }

        private void Start()
        {
            _cam = Camera.main;
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) _player = p.transform;
            BuildArrows();
            BuildExitSign();
            SetArrowsVisible(false);
            SetArrowScale(1f);

            var mgr = FireDrillManager.Instance;
            if (mgr != null) mgr.onStepChanged.AddListener(OnStepChanged);
        }

        private void OnStepChanged(DrillStep step)
        {
            if (autoStartOnAlarm && step == DrillStep.AlarmRinging) Begin();
        }

        /// <summary>启动地面引导。</summary>
        public void Begin()
        {
            CollectTargets();
            if (targets == null || targets.Count == 0)
            {
                Debug.LogWarning("[Arrows] 未找到灭火器目标，引导箭头不启动。");
                return;
            }
            Active = true;
            SetArrowsVisible(true);
            SetArrowScale(1f);
            SetSignVisible(false);
            onGuidanceStarted?.Invoke();
            Debug.Log("[Arrows] 地面引导箭头已启动，指向最近灭火器。");
        }

        /// <summary>第 2 阶段：切换为「引导去大厅」并显示箭头。</summary>
        public void BeginLobbyGuidance()
        {
            if (lobbyTarget == null)
            {
                Debug.LogWarning("[Arrows] 未配置 lobbyTarget，大厅引导未启动。");
                return;
            }
            LobbyMode = true;
            ExitMode = false;
            Active = true;
            SetArrowsVisible(true);
            SetArrowScale(stage2ArrowScale);
            onGuidanceStarted?.Invoke();
            Debug.Log("[Arrows] 第 2 阶段引导箭头已启动，指向大厅。");
        }

        /// <summary>第 3 阶段：切换为「引导去安全出口」并显示箭头。</summary>
        public void BeginExitGuidance()
        {
            if (exitTarget == null)
            {
                Debug.LogWarning("[Arrows] 未配置 exitTarget，安全出口引导未启动。");
                return;
            }
            ExitMode = true;
            LobbyMode = false;
            Active = true;
            SetArrowsVisible(true);
            SetArrowScale(stage2ArrowScale);
            onGuidanceStarted?.Invoke();
            Debug.Log("[Arrows] 第 3 阶段引导箭头已启动，指向安全出口。");
        }

        /// <summary>停止并隐藏引导箭头。</summary>
        public void StopGuidance()
        {
            if (!Active) return;
            Active = false;
            SetArrowsVisible(false);
            SetSignVisible(false);
            SetArrowScale(1f);
            onGuidanceStopped?.Invoke();
            Debug.Log("[Arrows] 引导箭头已隐藏。");
        }

        private void Update()
        {
            if (!Active) return;
            if (_cam == null) _cam = Camera.main;
            if (_player == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p) _player = p.transform;
            }

            Transform target = ExitMode && exitTarget != null
                ? exitTarget
                : (LobbyMode ? lobbyTarget : PickNearestActiveTarget());
            if (target == null)
            {
                StopGuidance();
                return;
            }

            Vector3 playerPos = _player ? _player.position
                : (_cam != null ? _cam.transform.position : Vector3.zero);
            Vector3 flatTarget = new Vector3(target.position.x, floorY, target.position.z);
            Vector3 flatPlayer = new Vector3(playerPos.x, floorY, playerPos.z);
            Vector3 dir = flatTarget - flatPlayer;
            dir.y = 0f;
            float dist = dir.magnitude;

            float arriveThreshold = ExitMode ? exitArriveDistance
                : (LobbyMode ? lobbyArriveDistance : 0.4f);
            if (dist < arriveThreshold)
            {
                StopGuidance();
                if (LobbyMode) onArrivedAtLobby?.Invoke();
                return;
            }
            dir.Normalize();

            // 浓烟中：目标前方立一块始终朝玩家的大号「安全出口 →」标识
            if (LobbyMode || ExitMode)
                PlaceSign(target, playerPos);
            else
                SetSignVisible(false);
            Quaternion look = Quaternion.LookRotation(dir, Vector3.up);

            for (int i = 0; i < _arrows.Length; i++)
            {
                var a = _arrows[i];
                if (a == null) continue;
                float d = spacing * (i + 1);
                if (d > dist - 0.3f) { a.SetActive(false); continue; }
                a.SetActive(true);
                a.transform.position = flatPlayer + dir * d;
                a.transform.rotation = look;
            }

            // 箭头颜色呼吸脉动
            float k = 0.6f + 0.4f * (Mathf.Sin(Time.time * Mathf.PI * 2f * pulseHz) * 0.5f + 0.5f);
            if (_arrowMat) _arrowMat.color = arrowColor * k;
        }

        /// <summary>在尚未被看到、尚未被拿起的目标里选距离最近的一个。</summary>
        private Transform PickNearestActiveTarget()
        {
            Vector3 origin = _player != null ? _player.position
                : (_cam != null ? _cam.transform.position : Vector3.zero);

            Transform best = null;
            float bestDist = float.MaxValue;
            foreach (var t in targets)
            {
                if (t == null) continue;
                var ext = t.GetComponent<Extinguisher>();
                if (ext != null && ext.grab != null && ext.grab.IsHeld) continue;
                var hl = t.GetComponent<ExtinguisherHighlight>();
                if (hl != null && hl.IsSpotted) continue;

                float d = Vector3.Distance(origin, t.position);
                if (d < bestDist) { bestDist = d; best = t; }
            }
            return best;
        }

        private void CollectTargets()
        {
            if (_collected) return;
            if (targets == null || targets.Count == 0)
            {
                targets = new List<Transform>();
                foreach (var ext in FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
                    targets.Add(ext.transform);
            }
            _collected = true;
        }

        private void BuildArrows()
        {
            _arrows = new GameObject[arrowCount];

            // 地面箭头必须「自发光」：场景在浓烟里几乎没有环境光，用 Lit 材质会渲成一团黑，
            // 玩家在雾里看不到任何指引。这里用 URP Unlit，颜色不随光照变化。
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            _arrowMat = new Material(shader);
            _arrowMat.name = "GuideArrowMat";
            var lit = new Color(arrowColor.r, arrowColor.g, arrowColor.b, 1f);
            if (_arrowMat.HasProperty("_BaseColor")) _arrowMat.SetColor("_BaseColor", lit);
            if (_arrowMat.HasProperty("_Color")) _arrowMat.SetColor("_Color", lit);
            _arrowMat.color = lit;
            if (_arrowMat.HasProperty("_Surface")) _arrowMat.SetFloat("_Surface", 0f);   // 不透明
            if (_arrowMat.HasProperty("_Cull")) _arrowMat.SetFloat("_Cull", 0f);          // 双面可见
            _arrowMat.renderQueue = 2000;

            Mesh arrowMesh = BuildArrowMesh();

            for (int i = 0; i < arrowCount; i++)
            {
                var go = new GameObject("GuideArrow_" + (i + 1));
                go.layer = 2; // Ignore Raycast

                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = arrowMesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _arrowMat;

                go.transform.SetParent(transform, false);
                go.SetActive(false);
                _arrows[i] = go;
            }
        }

        /// <summary>在 XZ 平面构建朝 +Z 的箭头网格（箭杆矩形 + 三角箭头），双面。</summary>
        private static Mesh BuildArrowMesh()
        {
            var mesh = new Mesh { name = "GuideArrowMesh" };
            float w = 0.06f;          // 箭杆半宽
            float shaftBack = -0.22f;
            float shaftFront = 0.05f;
            float headHalf = 0.16f;
            float headTip = 0.22f;

            var verts = new Vector3[]
            {
                new Vector3(-w, 0f, shaftBack),   // 0 杆后左
                new Vector3( w, 0f, shaftBack),   // 1 杆后右
                new Vector3( w, 0f, shaftFront),   // 2 杆前右
                new Vector3(-w, 0f, shaftFront),  // 3 杆前左
                new Vector3(-headHalf, 0f, shaftFront), // 4 头后左
                new Vector3( headHalf, 0f, shaftFront),// 5 头后右
                new Vector3(0f, 0f, headTip)       // 6 头尖
            };
            var tris = new int[]
            {
                0, 1, 2,  0, 2, 3,   // 箭杆
                4, 5, 6              // 箭头
            };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void SetArrowsVisible(bool visible)
        {
            if (_arrows == null) return;
            foreach (var a in _arrows)
                if (a != null) a.SetActive(visible);
        }

        /// <summary>整体缩放箭头：第二阶段烟雾里放大到 1.6 倍，更容易看见。</summary>
        private void SetArrowScale(float scale)
        {
            if (_arrows == null) return;
            foreach (var a in _arrows)
                if (a != null) a.transform.localScale = Vector3.one * scale;
        }

        /// <summary>
        /// 世界空间「安全出口 →」标识：竖直悬挂、始终朝向玩家，
        /// 不受地面烟雾粒子遮挡，浓雾中也能看清撤离方向。
        /// </summary>
        private void BuildExitSign()
        {
            _signRoot = new GameObject("FloorExitSign");
            _signRoot.transform.SetParent(transform, false);

            var canvas = _signRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 320;
            var rt = _signRoot.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1000, 260);
            rt.localScale = Vector3.one * 0.0016f;   // ≈1.6 米宽 × 0.42 米高，浓烟 3 米外能读清，又不至于糊住门口
            _signRoot.AddComponent<CanvasScaler>();

            var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(_signRoot.transform, false);
            var img = bg.GetComponent<Image>();
            img.color = new Color(0.04f, 0.28f, 0.16f, 0.92f);
            img.raycastTarget = false;
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(_signRoot.transform, false);
            _signText = txtGo.GetComponent<Text>();
            _signText.font = UIFont.Resolve();
            if (_signText.font != null) _signText.material = _signText.font.material;
            _signText.text = floorSignText;
            _signText.fontSize = 108;
            _signText.fontStyle = FontStyle.Bold;
            _signText.color = new Color(0.72f, 1f, 0.82f);
            _signText.alignment = TextAnchor.MiddleCenter;
            _signText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _signText.verticalOverflow = VerticalWrapMode.Overflow;
            _signText.raycastTarget = false;
            var tRt = txtGo.GetComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;
            tRt.offsetMin = Vector2.zero;
            tRt.offsetMax = Vector2.zero;

            _signRoot.SetActive(false);
        }

        private void PlaceSign(Transform target, Vector3 playerPos)
        {
            if (_signRoot == null) return;
            Vector3 toPlayer = new Vector3(playerPos.x - target.position.x, 0f, playerPos.z - target.position.z);
            if (toPlayer.sqrMagnitude < 0.0001f) toPlayer = Vector3.back;
            toPlayer.Normalize();
            _signRoot.transform.position = new Vector3(target.position.x, floorSignHeight, target.position.z)
                                           + toPlayer * floorSignBackOffset;
            if (_cam != null)
            {
                _signRoot.transform.rotation =
                    Quaternion.LookRotation(_signRoot.transform.position - _cam.transform.position, Vector3.up);

                // 玩家走到标识跟前时收起：否则一块 1.6 米宽的牌子贴在脸上，把门口的门和火焰全挡住
                float d = Vector3.Distance(_signRoot.transform.position, _cam.transform.position);
                if (d < 2.4f) { SetSignVisible(false); return; }
            }
            SetSignVisible(true);
        }

        private void SetSignVisible(bool visible)
        {
            if (_signRoot != null && _signRoot.activeSelf != visible) _signRoot.SetActive(visible);
        }
    }
}
