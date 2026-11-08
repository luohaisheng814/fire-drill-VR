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

        [Header("布点（世界坐标，两点之间等距排布）")]
        public Vector3 start = new Vector3(0f, 1.60f, 1.8f);
        public Vector3 end = new Vector3(0f, 1.60f, -4.0f);
        public int emitterCount = 6;
        [Tooltip("雾底离地高度（米）：地板上的安全出口箭头必须露在雾下面")]
        public float floorClearance = 1.15f;

        [Header("烟雾表现")]
        public Color smokeColor = new Color(0.64f, 0.66f, 0.68f, 0.55f);
        public float particleSize = 2.0f;
        public float lifetime = 5f;
        [Tooltip("每秒冒出多少团烟雾")]
        public float ratePerSecond = 12f;

        public bool Active { get; private set; }
        public int EmitterCount => _systems != null ? _systems.Length : 0;

        private ParticleSystem[] _systems;
        private Material _material;
        private Texture2D _softTex;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
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
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Standard");
            _material = shader != null ? new Material(shader) : null;
            if (_material != null)
            {
                _material.mainTexture = _softTex;
                if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", _softTex);
                if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", Color.white);
                if (_material.HasProperty("_Surface")) _material.SetFloat("_Surface", 1f);
            }

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
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(particleSize * 0.7f, particleSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 120;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.startColor = smokeColor;

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = ratePerSecond;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(2.2f, 0.15f, 0.6f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.72f, 0.74f, 0.76f), 0f),
                    new GradientColorKey(new Color(0.52f, 0.54f, 0.57f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.42f, 0.25f),
                    new GradientAlphaKey(0.22f, 0.75f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.35f));

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            vel.y = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.material = _material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
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
