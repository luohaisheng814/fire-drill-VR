using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace FireSafety
{
    /// <summary>
    /// 第二阶段烟雾区：在通往大厅的地面走廊上生成一层灰白色烟雾粒子。
    /// 纯运行时构建（不依赖场景美术资源），Begin 开喷、Stop 停止发射，便于自检直接调用。
    /// </summary>
    public class SmokeZone : MonoBehaviour
    {
        public static SmokeZone Instance { get; private set; }

        /// <summary>场景里所有烟雾区（第 38 轮生命值判定用，避免每帧 FindObjectsByType 分配数组）。</summary>
        public static readonly List<SmokeZone> All = new List<SmokeZone>();

        [Header("布点（世界坐标，两点之间等距排布）")]
        public Vector3 start = new Vector3(-1.50f, 1.60f, 0.60f);
        public Vector3 end = new Vector3(2.20f, 1.60f, 1.60f);
        public int emitterCount = 7;
        [Tooltip("雾底离地高度（米）：地板上的安全出口箭头必须露在雾下面")]
        public float floorClearance = 1.15f;

        [Header("烟雾表现")]
        public Color smokeColor = new Color(0.64f, 0.66f, 0.68f, 0.55f);
        public float particleSize = 1.05f;
        public float lifetime = 6f;
        [Tooltip("每秒冒出多少团烟雾")]
        public float ratePerSecond = 20f;

        [Header("第 40 轮：浓度与走向（默认值向后兼容）")]
        [Tooltip("烟团整体漂移速度（世界方向，米/秒）：窗排烟、门口外扩靠它表现「往哪边流」")]
        public Vector3 driftPerSecond = new Vector3(0f, 0.03f, 0f);
        [Tooltip("漂移方向的随机抖动幅度")]
        public float driftJitter = 0.10f;
        [Tooltip("粒子初始四散速度上限")]
        public float scatterSpeed = 0.45f;
        [Tooltip("透明度倍率：>1 更浓，<1 更淡")]
        public float alphaScale = 1f;
        [Tooltip("发射箱尺寸（宽, 高, 厚）")]
        public Vector3 emitterShape = new Vector3(2.2f, 0.15f, 0.6f);
        [Tooltip("单个发射点允许存在的粒子上限；调大更浓但更吃性能")]
        public int particleCap = 240;
        [Tooltip("粒子随寿命的膨胀倍率")]
        public float sizeGrowth = 1.35f;

        [Header("第 40 轮：自动跟随火源")]
        [Tooltip("勾选后不需要外部调用 Begin/Stop：列表里任一火源在燃烧就自动起雾，全灭后自动停")]
        public bool autoFollow = false;
        [Tooltip("跟随的火源（可多个：任一在烧即起雾）")]
        public FireSource[] followFires;
        [Tooltip("参不参与生命值掉血判定：门外扩散烟雾不参与，避免刚出门通关就被判扣血")]
        public bool countsForDamage = true;

        public bool Active { get; private set; }
        public int EmitterCount => _systems != null ? _systems.Length : 0;

        /// <summary>
        /// 玩家是否处在烟雾覆盖带内（第 38 轮生命值判定用）。
        /// 用 start→end 的走廊中心线做水平距离判定，宽度按粒子箱体 2.2 m 的一半再留一点余量；
        /// 高度只取烟雾层附近，蹲下（1.05 m）和站立眼高（1.6 m）都算在烟雾里。
        /// </summary>
        public bool ContainsPlayer(Vector3 worldPos)
        {
            if (!Active || !countsForDamage) return false;

            Vector2 a = new Vector2(start.x, start.z);
            Vector2 b = new Vector2(end.x, end.z);
            Vector2 p = new Vector2(worldPos.x, worldPos.z);
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            Vector2 nearest = a + ab * t;
            float horizontal = Vector2.Distance(p, nearest);
            if (horizontal > 1.25f) return false;

            float layerY = Mathf.Max(start.y, end.y);
            return worldPos.y >= 0.2f && worldPos.y <= layerY + 1.15f;
        }


        private ParticleSystem[] _systems;
        private Material _material;
        private Texture2D _softTex;
        private float _lastBurnTime = -100f;

        /// <summary>
        /// 第 40 轮：自动跟随火源。勾了 autoFollow 的烟雾区（全场景大雾、窗内导流、门外扩散层）
        /// 不用外部调度，火一烧起来就自动起雾，火灭后自动收，避免三套情景的雾同时挂满全场。
        /// </summary>
        private void Update()
        {
            if (!autoFollow) return;
            bool burning = false;
            if (followFires != null)
            {
                for (int i = 0; i < followFires.Length; i++)
                {
                    var f = followFires[i];
                    if (f == null || !f.gameObject.activeInHierarchy) continue;
                    if (f.IsBurning && !f.IsExtinguished) { burning = true; break; }
                }
            }
            if (burning)
            {
                _lastBurnTime = Time.time;
                if (!Active) Begin();
            }
            else if (Active && Time.time - _lastBurnTime > 1.2f)
            {
                Stop();
            }
        }

        private void Awake()
        {
            // 场景里有两条烟雾区（撤离通道 SmokeZone + 第一阶段设备旁 Stage1Smoke）：
            // 这里只登记第一个实例，不能 Destroy 另一个 —— 否则第二个的组件会在运行时被销毁，
            // 流程脚本持有的引用变成 fake-null，Begin() 永远不会执行（开始后看不见烟雾）。
            if (Instance == null) Instance = this;
        }

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        private void OnDisable()
        {
            All.Remove(this);
        }

        private void OnDestroy()
        {
            All.Remove(this);
            if (Instance == this) Instance = null;
        }

        /// <summary>开始冒烟（可重复调用）。</summary>
        public void Begin()
        {
            Build();
            foreach (var ps in _systems)
            {
                if (ps == null) continue;
                ps.Play();
            }
            Active = true;
            Debug.Log("[Smoke] 烟雾已开启，共 " + (EmitterCount) + " 个发射点。");
        }

        /// <summary>停止冒烟（粒子逐渐消散）。</summary>
        public void Stop()
        {
            if (_systems == null) { Active = false; return; }
            foreach (var ps in _systems)
            {
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            Active = false;
            Debug.Log("[Smoke] 烟雾已停止。");
        }

        /// <summary>清空烟雾（立刻消失，用于结束）。</summary>
        public void Clear()
        {
            if (_systems == null) { Active = false; return; }
            foreach (var ps in _systems)
            {
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            Active = false;
        }

        private void Build()
        {
            if (_systems != null && _systems.Length > 0) return;

            _softTex = CreateSoftTexture();
            _material = CreateParticleMaterial(_softTex, false);

            var list = new List<ParticleSystem>();
            for (int i = 0; i < Mathf.Max(1, emitterCount); i++)
            {
                float k = emitterCount <= 1 ? 0f : (float)i / (emitterCount - 1);
                Vector3 pos = Vector3.Lerp(start, end, k);
                pos.y = Mathf.Max(pos.y, floorClearance);
                list.Add(BuildEmitter("Smoke_" + (i + 1), pos));
            }
            _systems = list.ToArray();        }

        private ParticleSystem BuildEmitter(string name, Vector3 worldPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = worldPos;

            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            if (ps == null) ps = go.AddComponent<ParticleSystem>();
            if (ps == null)
            {
                Debug.LogError("[Smoke] 无法为 " + name + " 添加 ParticleSystem");
                return null;
            }
            // 编辑模式下新建的粒子系统必须先 Stop 一次，否则模块访问会抛
            // "Do not create your own module instances" 空引用
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, Mathf.Max(0.1f, scatterSpeed));
            main.startSize = new ParticleSystem.MinMaxCurve(particleSize * 0.7f, particleSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = Mathf.Max(40, particleCap);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.startColor = smokeColor;

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = ratePerSecond;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = emitterShape;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            float a0 = Mathf.Clamp01(0.42f * alphaScale);
            float a1 = Mathf.Clamp01(0.30f * alphaScale);
            float a2 = Mathf.Clamp01(0.18f * alphaScale);
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.72f, 0.74f, 0.76f), 0f),
                    new GradientColorKey(new Color(0.52f, 0.54f, 0.57f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(a0, 0.20f),
                    new GradientAlphaKey(a1, 0.65f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, sizeGrowth));

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(driftPerSecond.x - driftJitter, driftPerSecond.x + driftJitter);
            vel.y = new ParticleSystem.MinMaxCurve(driftPerSecond.y - driftJitter, driftPerSecond.y + driftJitter);
            vel.z = new ParticleSystem.MinMaxCurve(driftPerSecond.z - driftJitter, driftPerSecond.z + driftJitter);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.material = _material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        /// <summary>
        /// 生成一份「真正透明」的粒子材质（烟雾=Alpha 混合，火焰=加色混合）。
        /// 只写 _Surface=1 是不够的：URP Unlit 还依赖 _SURFACE_TYPE_TRANSPARENT 关键字、
        /// 混合因子与渲染队列，缺任何一项粒子都会退化成硬边不透明方块（画面里就是一坨灰/橙色面片）。
        /// </summary>
        public static Material CreateParticleMaterial(Texture2D tex, bool additive)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            var mat = new Material(shader);
            mat.name = additive ? "FX_Additive" : "FX_Alpha";
            if (tex != null)
            {
                mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);   // Transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", additive ? 2f : 0f); // 2=Additive 0=Alpha
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)(additive
                    ? UnityEngine.Rendering.BlendMode.One
                    : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHATEST_OFF");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        /// <summary>运行时生成一张柔边圆形贴图，避免烟雾/火焰变成方块。</summary>
        public static Texture2D CreateSoftTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a;                     // 柔和边缘
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }
    }
}
