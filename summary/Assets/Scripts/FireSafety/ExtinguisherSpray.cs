using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 灭火器喷射粒子效果（干粉雾柱）：挂在喷嘴下方，沿喷嘴 +Z 方向喷出锥形干粉流。
    /// 全部模块由代码构建，无需预制体；粒子贴图在运行时生成柔和圆点，避免出现方块。
    /// 与 Extinguisher.sprayParticles 自动联动，按压压把时启停。
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class ExtinguisherSpray : MonoBehaviour
    {
        [Header("引用")]
        public Extinguisher owner;
        [Tooltip("留空则使用自身 Transform 的 +Z 方向")]
        public Transform nozzle;

        [Header("喷射形态")]
        [Tooltip("每秒喷出粒子数")]
        public float rate = 280f;
        [Tooltip("初速度（米/秒），越大喷得越远")]
        public float startSpeed = 3.4f;
        [Tooltip("喷射锥半角（度）")]
        public float coneAngle = 9f;
        [Tooltip("喷口半径")]
        public float nozzleRadius = 0.02f;
        [Tooltip("粒子存活时间（秒）")]
        public float lifetime = 1.2f;
        [Tooltip("起始粒径（米）")]
        public float startSize = 0.05f;
        [Tooltip("最大粒径倍数（相对起始粒径）")]
        public float sizeGrowth = 5.5f;
        [Tooltip("重力影响，干粉会缓慢下沉")]
        public float gravity = 0.45f;
        [Tooltip("湍流扰动强度，让粉尘自然扩散")]
        public float noiseStrength = 0.45f;

        [Header("颜色")]
        public Color sprayColor = new Color(0.94f, 0.96f, 0.98f, 0.75f);

        [Header("启停")]
        [Tooltip("起喷时压力爬升时间（秒），0 = 立刻满压")]
        public float spinUpTime = 0.12f;

        ParticleSystem _ps;
        ParticleSystemRenderer _renderer;
        float _pressure;

        public bool IsPlaying => _ps != null && _ps.isPlaying;

        private void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            _renderer = GetComponent<ParticleSystemRenderer>();

            if (!owner) owner = GetComponentInParent<Extinguisher>();
            if (!nozzle) nozzle = transform;

            Configure();
            BindOwner();

            // 场景里默认粒子可能已预播放，这里清空，等按压再喷
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _pressure = 0f;
        }

        /// <summary>完整配置粒子系统的各个模块（幂等，可重复调用）。</summary>
        public void Configure()
        {
            if (_ps == null) _ps = GetComponent<ParticleSystem>();

            var main = _ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.8f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(startSpeed * 0.85f, startSpeed * 1.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(startSize, startSize * 1.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(sprayColor.r, sprayColor.g, sprayColor.b, sprayColor.a),
                new Color(sprayColor.r, sprayColor.g, sprayColor.b, sprayColor.a * 0.55f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 2400;
            main.prewarm = false;

            var emission = _ps.emission;
            emission.enabled = true;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(rate);

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = nozzleRadius;
            shape.radiusThickness = 1f;          // 从整个锥体体积喷出
            shape.rotation = Vector3.zero;       // 沿本地 +Z 喷射
            shape.position = Vector3.zero;

            // 粒径随时间膨胀，模拟干粉云扩散
            var sizeOverLife = _ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.35f),
                new Keyframe(0.18f, 1f),
                new Keyframe(0.55f, 1.35f),
                new Keyframe(1f, sizeGrowth));
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // 透明度从浓到淡
            var colorOverLife = _ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.97f, 0.98f, 1f), 0f),
                    new GradientColorKey(new Color(0.88f, 0.91f, 0.95f), 0.5f),
                    new GradientColorKey(new Color(0.82f, 0.86f, 0.9f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(0.55f, 0.5f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

            // 湍流：粉尘自然翻涌
            var noise = _ps.noise;
            noise.enabled = noiseStrength > 0f;
            noise.strength = noiseStrength;
            noise.frequency = 0.42f;
            noise.scrollSpeed = 0.18f;
            noise.damping = true;
            noise.octaveCount = 2;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            // 轻微阻尼，喷远后逐渐减速
            var limit = _ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.12f;
            limit.limit = new ParticleSystem.MinMaxCurve(4.5f);
            limit.limitMultiplier = 1f;
            limit.space = ParticleSystemSimulationSpace.World;

            // 粒子随距离略微自转
            var rot = _ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);

            // 渲染器与材质
            if (_renderer != null)
            {
                _renderer.renderMode = ParticleSystemRenderMode.Billboard;
                _renderer.alignment = ParticleSystemRenderSpace.View;
                _renderer.sortingFudge = 0f;
                _renderer.maxParticleSize = 0.6f;
                _renderer.sharedMaterial = BuildMaterial();
            }
        }

        private void BindOwner()
        {
            if (owner == null) return;
            // 让 Extinguisher 的 Play / Stop 直接驱动这套粒子
            if (owner.sprayParticles == null || owner.sprayParticles == _ps)
                owner.sprayParticles = _ps;
            if (owner.nozzle == null && nozzle != null)
                owner.nozzle = nozzle;
        }

        private void Update()
        {
            if (_ps == null) return;

            // 压力爬升：起喷时由低到满，喷射更自然
            float target = _ps.isPlaying ? 1f : 0f;
            float k = spinUpTime > 0.001f ? Time.deltaTime / spinUpTime : 1f;
            _pressure = Mathf.MoveTowards(_pressure, target, k);

            var emission = _ps.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(rate * Mathf.Max(0.05f, _pressure));
        }

        /// <summary>手动起喷（供外部直接调用，与 Extinguisher 的调用等价）。</summary>
        public void Play()
        {
            if (_ps == null) return;
            EnsureMaterial();   // 材质只在 Start 里建过，若 Start 没跑到（或材质被清掉），粒子会渲染成洋红
            if (!_ps.isPlaying) _ps.Play();
        }

        private void EnsureMaterial()
        {
            if (_ps == null) return;
            var r = _ps.GetComponent<ParticleSystemRenderer>();
            if (r == null) return;
            if (r.sharedMaterial == null || r.sharedMaterial.shader == null)
                r.sharedMaterial = BuildMaterial();
        }

        /// <summary>停止喷出，已喷出的粒子自然消散。</summary>
        public void Stop()
        {
            if (_ps == null) return;
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _pressure = 0f;
        }

        private Material BuildMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var mat = new Material(shader) { name = "SprayPowderMat" };
            var tex = CreateSoftDotTexture();

            mat.SetTexture("_BaseMap", tex);
            mat.SetTexture("_MainTex", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetColor("_Color", Color.white);

            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);                    // Transparent
                mat.SetFloat("_Blend", 0f);                      // Alpha
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return mat;
        }

        /// <summary>运行时生成 64×64 柔和圆点贴图（中心不透明，边缘渐隐）。</summary>
        private static Texture2D CreateSoftDotTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / c;
                    float dy = (y - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a;                        // 二次衰减，边缘更柔
                    byte alpha = (byte)(a * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}
