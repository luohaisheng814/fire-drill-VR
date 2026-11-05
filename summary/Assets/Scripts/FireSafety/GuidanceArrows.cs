using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

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

        [Header("箭头参数")]
        public int arrowCount = 6;
        public float spacing = 1.2f;
        public float floorY = 0.02f;
        public Color arrowColor = new Color(1f, 0.82f, 0.15f);
        public float pulseHz = 2.5f;
        public bool autoStartOnAlarm = true;

        [Header("事件")]
        public UnityEvent onGuidanceStarted;
        public UnityEvent onGuidanceStopped;

        public bool Active { get; private set; }
        /// <summary>当前是否处于「引导去大厅」模式（true 时箭头指向 lobbyTarget，不再指灭火器）。</summary>
        public bool LobbyMode { get; private set; }

        Camera _cam;
        Transform _player;
        GameObject[] _arrows;
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
            SetArrowsVisible(false);

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
            Active = true;
            SetArrowsVisible(true);
            onGuidanceStarted?.Invoke();
            Debug.Log("[Arrows] 第 2 阶段引导箭头已启动，指向大厅。");
        }

        /// <summary>停止并隐藏引导箭头。</summary>
        public void StopGuidance()
        {
            if (!Active) return;
            Active = false;
            SetArrowsVisible(false);
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

            Transform target = LobbyMode ? lobbyTarget : PickNearestActiveTarget();
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

            float arriveThreshold = LobbyMode ? lobbyArriveDistance : 0.4f;
            if (dist < arriveThreshold)
            {
                StopGuidance();
                if (LobbyMode) onArrivedAtLobby?.Invoke();
                return;
            }
            dir.Normalize();
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

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _arrowMat = new Material(shader);
            _arrowMat.color = arrowColor;
            if (_arrowMat.HasProperty("_EmissionColor"))
                _arrowMat.SetColor("_EmissionColor", arrowColor);
            // 地面箭头双面可见
            if (_arrowMat.HasProperty("_Cull")) _arrowMat.SetFloat("_Cull", 0);

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
    }
}
