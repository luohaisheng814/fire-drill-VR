using UnityEngine;
using UnityEngine.Events;

namespace FireSafety
{
    /// <summary>
    /// 灭火器视线高亮：演练开始后，一旦灭火器进入玩家视野（在相机视锥内 + 无遮挡），
    /// 立即触发发光脉冲、地面光环与光柱特效，提示玩家目标位置；被拿起后特效停止。
    /// </summary>
    [RequireComponent(typeof(Extinguisher))]
    public class ExtinguisherHighlight : MonoBehaviour
    {
        [Header("视线判定")]
        public Camera xrCamera;
        [Tooltip("检查间隔（秒），降低开销")]
        public float checkInterval = 0.15f;
        [Tooltip("视线与目标方向最大夹角（度）")]
        public float gazeAngle = 60f;
        public float maxDistance = 20f;
        public bool checkOcclusion = true;
        [Tooltip("遮挡判定层（默认排除 Ignore Raycast 层，避免引导箭头/特效误判为遮挡）")]
        public LayerMask blockingMask = ~0 & ~(1 << 2);

        [Header("高亮表现")]
        public Color highlightColor = new Color(1f, 0.85f, 0.25f);
        public float pulseHz = 3f;
        public float ringRadius = 0.45f;
        public float beamHeight = 1.8f;

        [Header("事件")]
        public UnityEvent onSpotted;

        public bool IsSpotted { get; private set; }

        Extinguisher _ext;
        Renderer[] _renderers;
        MaterialPropertyBlock _mpb;
        GameObject _ring;
        GameObject _beam;
        AudioSource _src;
        AudioClip _spottedClip;
        float _checkTimer;
        bool _visualOn;

        private void Awake()
        {
            _ext = GetComponent<Extinguisher>();
            _renderers = GetComponentsInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();

            _src = GetComponent<AudioSource>();
            if (_src == null) _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;

            _spottedClip = Resources.Load<AudioClip>("Sounds/SFX/ui_click");
        }

        private void Start()
        {
            if (!xrCamera) xrCamera = Camera.main;
            BuildRingAndBeam();
            SetHighlightVisual(false);
        }

        private void Update()
        {
            // 已被看到：维持脉冲；被拿起则熄灭
            if (IsSpotted)
            {
                if (_ext != null && _ext.grab != null && _ext.grab.IsHeld)
                {
                    SetHighlightVisual(false);
                    return;
                }
                PulseHighlight();
                return;
            }

            var mgr = FireDrillManager.Instance;
            if (mgr == null || !mgr.IsStepDone(DrillStep.AlarmRinging)) return;

            _checkTimer -= Time.deltaTime;
            if (_checkTimer > 0f) return;
            _checkTimer = checkInterval;

            if (IsInView()) Spot();
        }

        /// <summary>判定灭火器是否进入玩家当前视野。</summary>
        private bool IsInView()
        {
            if (!xrCamera) return false;
            Vector3 eye = xrCamera.transform.position;
            Vector3 toTarget = transform.position - eye;
            float dist = toTarget.magnitude;
            if (dist > maxDistance) return false;
            if (Vector3.Angle(xrCamera.transform.forward, toTarget.normalized) > gazeAngle) return false;

            // 视锥判定（相机前方且投影在屏幕内，留少量边距）
            Vector3 view = xrCamera.WorldToViewportPoint(transform.position);
            if (view.z <= 0f) return false;
            if (view.x < 0.02f || view.x > 0.98f || view.y < 0.02f || view.y > 0.98f) return false;

            if (checkOcclusion &&
                Physics.Raycast(eye, toTarget.normalized, out var hit, dist, blockingMask))
            {
                bool hitUs = hit.transform == transform || hit.transform.IsChildOf(transform);
                if (!hitUs) return false;
            }

            return true;
        }

        private void Spot()
        {
            IsSpotted = true;
            SetHighlightVisual(true);

            if (_spottedClip && _src) _src.PlayOneShot(_spottedClip, 0.4f);

            onSpotted?.Invoke();
            // 已看到目标，停止地面引导箭头
            GuidanceArrows.Instance?.StopGuidance();

            Debug.Log("[Highlight] 灭火器进入视野，高亮特效已触发：" + gameObject.name);
        }

        private void PulseHighlight()
        {
            if (!_visualOn) return;

            float k = 0.55f + 0.45f * (Mathf.Sin(Time.time * Mathf.PI * 2f * pulseHz) * 0.5f + 0.5f);
            Color emission = highlightColor * k;

            foreach (var r in _renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_EmissionColor"))
                    _mpb.SetColor("_EmissionColor", emission);
                r.SetPropertyBlock(_mpb);
            }

            if (_ring != null)
            {
                _ring.transform.Rotate(Vector3.up, Time.deltaTime * 90f, Space.World);
                var rr = _ring.GetComponent<Renderer>();
                if (rr && rr.sharedMaterial != null)
                    rr.sharedMaterial.color = highlightColor * k;
            }
            if (_beam != null)
            {
                var br = _beam.GetComponent<Renderer>();
                if (br && br.sharedMaterial != null)
                {
                    var c = highlightColor;
                    c.a = 0.18f + 0.14f * k;
                    br.sharedMaterial.color = c;
                }
            }
        }

        private void SetHighlightVisual(bool on)
        {
            _visualOn = on;
            if (_ring != null) _ring.SetActive(on);
            if (_beam != null) _beam.SetActive(on);

            if (!on)
            {
                foreach (var r in _renderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(_mpb);
                    if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_EmissionColor"))
                        _mpb.SetColor("_EmissionColor", Color.black);
                    r.SetPropertyBlock(_mpb);
                }
            }
        }

        private void BuildRingAndBeam()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            // 地面光环：程序化环形网格（XZ 平面，双面）
            _ring = new GameObject("HighlightRing");
            _ring.layer = 2; // Ignore Raycast
            _ring.transform.SetParent(transform, false);
            _ring.transform.localPosition = new Vector3(0f, -0.32f, 0f);
            var ringFilter = _ring.AddComponent<MeshFilter>();
            ringFilter.sharedMesh = BuildRingMesh(ringRadius, ringRadius * 0.72f, 28);
            var ringRenderer = _ring.AddComponent<MeshRenderer>();
            var ringMat = new Material(shader);
            ringMat.color = highlightColor;
            if (ringMat.HasProperty("_EmissionColor")) ringMat.SetColor("_EmissionColor", highlightColor);
            if (ringMat.HasProperty("_Cull")) ringMat.SetFloat("_Cull", 0);
            ringRenderer.sharedMaterial = ringMat;

            // 垂直光柱
            _beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _beam.name = "HighlightBeam";
            var beamCol = _beam.GetComponent<Collider>();
            if (beamCol) Destroy(beamCol);
            _beam.layer = 2;
            _beam.transform.SetParent(transform, false);
            _beam.transform.localPosition = new Vector3(0f, beamHeight * 0.5f, 0f);
            _beam.transform.localScale = new Vector3(0.12f, beamHeight, 0.12f);
            var beamMat = new Material(shader);
            var beamColor = highlightColor;
            beamColor.a = 0.2f;
            beamMat.color = beamColor;
            beamMat.SetFloat("_Surface", 1); // URP Transparent
            beamMat.renderQueue = 3000;
            _beam.GetComponent<Renderer>().sharedMaterial = beamMat;
        }

        /// <summary>在 XZ 平面构建环形（圆环）网格。</summary>
        private static Mesh BuildRingMesh(float outerR, float innerR, int segments)
        {
            var mesh = new Mesh { name = "HighlightRingMesh" };
            var verts = new Vector3[segments * 2];
            var tris = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.PI * 2f * i / segments;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts[i * 2] = new Vector3(c * outerR, 0f, s * outerR);
                verts[i * 2 + 1] = new Vector3(c * innerR, 0f, s * innerR);
            }

            for (int i = 0; i < segments; i++)
            {
                int n = (i + 1) % segments;
                int o0 = i * 2, i0 = i * 2 + 1;
                int o1 = n * 2, i1 = n * 2 + 1;
                int t = i * 6;
                tris[t] = o0; tris[t + 1] = i0; tris[t + 2] = o1;
                tris[t + 3] = o1; tris[t + 4] = i0; tris[t + 5] = i1;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
