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
        public int arrowCount = 8;
        public float spacing = 1.1f;
        public float floorY = 0.02f;
        [Tooltip("第一枚箭头离玩家多远：太近会踩在脚下，太远又看不出起点")]
        public float arrowStartDistance = 0.9f;
        public Color arrowColor = new Color(1f, 0.82f, 0.15f);
        public float pulseHz = 2.5f;
        public bool autoStartOnAlarm = true;

        [Header("第 36 轮：不同方向/绕障路径")]
        [Tooltip("路径探测高度（米）：贴着地面走，只关心桌腿、墙、门框等低矮遮挡")]
        public float pathProbeHeight = 0.35f;
        [Tooltip("路径通畅判定的身体半径（米）：0.3 米能避开椅子、桌腿和窄缝")]
        public float pathClearRadius = 0.30f;
        [Tooltip("直线被挡时，依次尝试的横向绕行距离（米）")]
        public float[] pathSideOffsets = { 0.8f, 1.6f, 2.4f };
        [Tooltip("安全出口牌显示实时距离与相对方向（↑ ↗ → ↘ ↓ ↙ ← ↖）")]
        public bool adaptiveExitSign = true;

        public int VisibleArrowCount { get; private set; }
        public int PathPointCount => _pathCount;
        public bool UsingDetour { get; private set; }
        public string CurrentSignText => _signText != null ? _signText.text : string.Empty;
        public Vector3 FirstArrowPosition
        {
            get
            {
                if (_arrows == null) return Vector3.zero;
                foreach (var a in _arrows)
                    if (a != null && a.activeSelf) return a.transform.position;
                return Vector3.zero;
            }
        }
        public Vector3 FirstArrowForward
        {
            get
            {
                if (_arrows == null) return Vector3.forward;
                foreach (var a in _arrows)
                    if (a != null && a.activeSelf) return a.transform.forward;
                return Vector3.forward;
            }
        }

        [Header("第二阶段：浓烟中的地面安全出口标识")]
        [Tooltip("第二阶段箭头整体放大倍数：烟雾里更醒目")]
        public float stage2ArrowScale = 2.2f;
        [Tooltip("悬浮标识文案（始终朝向玩家，烟雾挡不住）")]
        public string floorSignText = "安全出口 →";
        [Tooltip("标识离地高度（米）：贴近地面，浓烟下部留空处一眼可见")]
        public float floorSignHeight = 0.62f;
        [Tooltip("标识放在目标前方多少米（朝玩家一侧）")]
        public float floorSignBackOffset = 0.9f;
        [Tooltip("标识沿门横向再挪开多少米：避免正对火源、把门和火焰挡住（0 = 正对门口）")]
        public float floorSignSideOffset = 0f;

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
        readonly Vector3[] _path = new Vector3[3];
        readonly Collider[] _probeHits = new Collider[32];
        int _pathCount;

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

            // 第 36 轮：先算一条从玩家到目标的可行路径，直线被桌/墙挡住时自动绕行，
            // 再把箭头沿折线等距铺开；玩家从大厅、茶水间或门口侧面来都能得到正确朝向。
            BuildPath(flatPlayer, flatTarget);
            PlaceArrowsAlongPath();
        }

        void BuildPath(Vector3 start, Vector3 end)
        {
            _pathCount = 0;
            UsingDetour = false;
            _path[0] = start;
            _path[1] = end;
            _pathCount = 2;

            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length < 0.5f) return;
            Vector3 dir = delta / length;
            if (IsPathClear(start, end)) return;

            Vector3 side = Vector3.Cross(Vector3.up, dir);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
            side.Normalize();

            // 直线走不通：在中点附近向左/右各试几个绕行点，选第一条两段都通畅的折线。
            for (int i = 0; i < pathSideOffsets.Length; i++)
            {
                float offset = Mathf.Max(0.2f, pathSideOffsets[i]);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 way = start + dir * (length * 0.55f) + side * (offset * s);
                    way.y = start.y;
                    if (!IsPathClear(start, way)) continue;
                    if (!IsPathClear(way, end)) continue;
                    _path[1] = way;
                    _path[2] = end;
                    _pathCount = 3;
                    UsingDetour = true;
                    return;
                }
            }
        }

        bool IsPathClear(Vector3 a, Vector3 b)
        {
            Vector3 start = new Vector3(a.x, floorY, a.z);
            Vector3 end = new Vector3(b.x, floorY, b.z);
            Vector3 seg = end - start;
            float dist = seg.magnitude;
            if (dist < 0.4f) return true;
            Vector3 dir = seg / dist;

            // 两端各留 0.35 米余量：玩家自己站的位置、门框/出口目标本身不算挡路。
            Vector3 from = new Vector3(a.x, floorY + pathProbeHeight + 0.10f, a.z) + dir * 0.35f;
            Vector3 to = new Vector3(b.x, floorY + pathProbeHeight + 0.10f, b.z) - dir * 0.35f;
            if ((to - from).sqrMagnitude < 0.01f) return true;
            int count = Physics.OverlapCapsuleNonAlloc(from, to, pathClearRadius, _probeHits,
                ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = _probeHits[i];
                if (c == null) continue;
                if (c.transform.IsChildOf(transform)) continue;
                if (_player != null && c.transform.IsChildOf(_player)) continue;
                if (c.GetComponentInParent<ExitDoor>() != null) continue;
                if (c.GetComponentInParent<FireSource>() != null) continue;
                string n = c.name ?? string.Empty;
                if (n.IndexOf("Guide", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (n.IndexOf("Arrow", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (n.IndexOf("Lobby", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                return false;
            }
            return true;
        }

        void PlaceArrowsAlongPath()
        {
            VisibleArrowCount = 0;
            if (_arrows == null || _pathCount < 2) return;

            float total = 0f;
            for (int i = 0; i < _pathCount - 1; i++) total += Vector3.Distance(_path[i], _path[i + 1]);
            if (total < 0.2f) { SetArrowsVisible(false); return; }

            int seg = 0;
            for (int i = 0; i < _arrows.Length; i++)
            {
                float travel = arrowStartDistance + spacing * i;
                if (travel > total - 0.35f)
                {
                    if (_arrows[i] != null) _arrows[i].SetActive(false);
                    continue;
                }

                seg = 0;
                float acc = 0f;
                while (seg < _pathCount - 2)
                {
                    float walkLen = Vector3.Distance(_path[seg], _path[seg + 1]);
                    if (travel <= acc + walkLen) break;
                    acc += walkLen;
                    seg++;
                }
                float segLen = Vector3.Distance(_path[seg], _path[seg + 1]);
                float local = Mathf.Clamp(travel - acc, 0f, segLen);
                Vector3 dir = (_path[seg + 1] - _path[seg]).normalized;
                Vector3 pos = _path[seg] + dir * local;
                pos.y = floorY;
                _arrows[i].SetActive(true);
                _arrows[i].transform.position = pos;
                _arrows[i].transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                VisibleArrowCount++;
            }

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

            // 第 36 轮：牌面按玩家实际来向更新「距离 + 相对方向」，不再永远写死 →。
            if (ExitMode && adaptiveExitSign && _signText != null)
            {
                Vector3 playerToExit = -toPlayer;
                float planarDistance = Vector2.Distance(
                    new Vector2(playerPos.x, playerPos.z),
                    new Vector2(target.position.x, target.position.z));
                string glyph = DirectionGlyph(playerToExit);
                _signText.fontSize = 84;
                _signText.text = DrillLocale.English
                    ? "EXIT " + planarDistance.ToString("0.0") + " m " + glyph
                    : "安全出口 " + planarDistance.ToString("0.0") + " 米 " + glyph;
            }
            else if (_signText != null)
            {
                _signText.fontSize = 108;
                _signText.text = floorSignText;
            }

            // 横向挪开的基准向量：与「门→玩家」垂直，保证牌子挪到门侧、不挡住火源和门口
            Vector3 side = Vector3.Cross(Vector3.up, toPlayer);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
            side.Normalize();
            _signRoot.transform.position = new Vector3(target.position.x, floorSignHeight, target.position.z)
                                           + toPlayer * floorSignBackOffset
                                           + side * floorSignSideOffset;
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

        /// <summary>按玩家视线方向给出 ↑ ↗ → ↘ ↓ ↙ ← ↖，保证箭头符号和实际门的方向一致。</summary>
        string DirectionGlyph(Vector3 playerToExit)
        {
            Vector3 forward = _cam != null ? _cam.transform.forward : playerToExit;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = playerToExit;
            forward.Normalize();
            float angle = Vector3.SignedAngle(forward, playerToExit, Vector3.up); // 右转为正
            float a = Mathf.Abs(angle);
            if (a < 22.5f) return "↑";
            if (a < 67.5f) return angle > 0f ? "↗" : "↖";
            if (a < 112.5f) return angle > 0f ? "→" : "←";
            if (a < 157.5f) return angle > 0f ? "↘" : "↙";
            return "↓";
        }

        private void SetSignVisible(bool visible)
        {
            if (_signRoot != null && _signRoot.activeSelf != visible) _signRoot.SetActive(visible);
        }
    }
}
