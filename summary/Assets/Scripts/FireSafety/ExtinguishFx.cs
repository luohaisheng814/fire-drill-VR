using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 第 28 轮：火被扑灭那一刻的一次性「收尾特效」——
    /// CO₂ 泄出一团白色气雾、干粉落下粉尘云、水基腾起蒸汽。
    /// 由 Extinguisher 在火源熄灭时调用，粒子与音效都是运行时构建，用完即毁。
    /// </summary>
    public static class ExtinguishFx
    {
        /// <summary>探针/自检用的计数器：确认「熄灭确实播了收尾特效」。</summary>
        public static int SpawnCount { get; private set; }
        public static ExtinguisherKind LastKind { get; private set; }

        public static GameObject Play(Vector3 position, ExtinguisherKind kind)
        {
            var go = new GameObject("ExtinguishFx_" + ExtinguisherSfx.KindToken(kind));
            go.transform.position = position;

            var ps = go.AddComponent<ParticleSystem>();
            Configure(ps, kind);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
                renderer.sharedMaterial = ExtinguisherSpray.CreateSoftMaterial("ExtinguishFxMat");
            }

            ps.Play(true);

            var clip = ExtinguisherSfx.Burst(kind);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1.5f;
            src.maxDistance = 14f;
            src.pitch = 0.94f + Random.value * 0.12f;
            src.PlayOneShot(clip, 0.9f);

            float life = ps.main.startLifetime.constantMax + 1.2f;
            SpawnCount++;
            LastKind = kind;

            if (Application.isPlaying) Object.Destroy(go, life);
            else Object.DestroyImmediate(go);

            return go;
        }

        static void Configure(ParticleSystem ps, ExtinguisherKind kind)
        {
            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.18f;
            shape.radiusThickness = 1f;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            var grow = new AnimationCurve(
                new Keyframe(0f, 0.55f),
                new Keyframe(0.35f, 1f),
                new Keyframe(1f, 1.45f));
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, grow);

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            Color c = ExtinguisherKindInfo.SprayColor(kind);
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.Lerp(c, Color.white, 0.1f), 0f),
                    new GradientColorKey(c, 0.6f),
                    new GradientColorKey(c, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.85f, 0.12f),
                    new GradientAlphaKey(0.35f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

            var burst = new ParticleSystem.Burst(0f, (short)BurstCount(kind));
            emission.SetBursts(new[] { burst });

            switch (kind)
            {
                case ExtinguisherKind.CO2:
                    // 冷气雾：量大、飘得快、几乎不下落，2 秒散尽
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.86f, 0.93f, 1f, 0.55f));
                    main.gravityModifier = -0.05f;
                    break;

                case ExtinguisherKind.WetChemical:
                    // 蒸汽：上升、拖得久
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.30f);
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.90f, 0.95f, 1f, 0.60f));
                    main.gravityModifier = -0.32f;
                    break;

                default:
                    // 干粉：粉尘云滞留，缓慢下沉
                    main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.6f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.20f, 0.38f);
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.96f, 0.94f, 0.88f, 0.70f));
                    main.gravityModifier = 0.18f;
                    break;
            }

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.1f;
            noise.damping = true;
        }

        static int BurstCount(ExtinguisherKind kind)
        {
            switch (kind)
            {
                case ExtinguisherKind.CO2: return 70;
                case ExtinguisherKind.WetChemical: return 80;
                default: return 100;
            }
        }
    }
}
