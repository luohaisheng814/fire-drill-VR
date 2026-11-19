using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 灭火点位：贴着火源的地面站位标记。
    /// 玩家站进圆环、正对火焰根部喷射，喷嘴角与射程才稳定落在有效区间内；
    /// 第 3 阶段（安全出口门前复燃）也复用同一套点位逻辑。
    /// </summary>
    [DisallowMultipleComponent]
    public class ExtinguishPoint : MonoBehaviour
    {
        [Header("关联")]
        [Tooltip("该点位服务的火源（可留空，运行时由流程指定）")]
        public FireSource fire;

        [Header("外观")]
        [Tooltip("地面圆环半径（米）")]
        public float radius = 0.45f;
        [Tooltip("圆环线宽（米）")]
        public float lineWidth = 0.055f;
        public Color ringColor = new Color(0.20f, 0.86f, 1f, 0.95f);
        [Tooltip("圆环离地高度，避免和地板 Z-fighting")]
        public float groundOffset = 0.018f;

        private LineRenderer _ring;
        private LineRenderer _halo;

        /// <summary>玩家脚下点（世界坐标，y 已贴地）。</summary>
        public Vector3 StandPosition => transform.position;

        /// <summary>玩家是否站在点位圆环内。</summary>
        public bool Contains(Vector3 worldPosition, float extraRadius = 0f)
        {
            Vector3 a = worldPosition;
            Vector3 b = transform.position;
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b) <= radius + Mathf.Max(0f, extraRadius);
        }

        /// <summary>火源瞄准点（没有火源时退回点位正前方 1 米、0.8 米高）。</summary>
        public Vector3 AimPoint => fire != null ? fire.AimPoint : transform.position + transform.forward + Vector3.up * 0.8f;

        private void OnEnable()
        {
            BuildRing();
        }

        private void Awake()
        {
            BuildRing();
        }

        /// <summary>运行时生成地面圆环；不写入场景，重进 Play 也能重建。</summary>
        public void BuildRing()
        {
            if (_ring == null)
            {
                var ringGo = new GameObject("Ring");
                ringGo.hideFlags = HideFlags.DontSave;
                ringGo.transform.SetParent(transform, false);
                _ring = ringGo.AddComponent<LineRenderer>();
                _ring.useWorldSpace = false;
                _ring.loop = true;
                _ring.positionCount = 48;
                _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _ring.receiveShadows = false;
                _ring.alignment = LineAlignment.View;
            }

            _ring.startWidth = lineWidth;
            _ring.endWidth = lineWidth;
            _ring.startColor = ringColor;
            _ring.endColor = ringColor;
            _ring.material = MakeRingMaterial();

            for (int i = 0; i < _ring.positionCount; i++)
            {
                float a = i * Mathf.PI * 2f / _ring.positionCount;
                _ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, groundOffset, Mathf.Sin(a) * radius));
            }

            // 第二圈抬到膝盖高度：玩家从远处也能看到「这里有个点位」，站进去时低头也能看到脚下圆环。
            if (_halo == null)
            {
                var haloGo = new GameObject("Halo");
                haloGo.hideFlags = HideFlags.DontSave;
                haloGo.transform.SetParent(transform, false);
                _halo = haloGo.AddComponent<LineRenderer>();
                _halo.useWorldSpace = false;
                _halo.loop = true;
                _halo.positionCount = 32;
                _halo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _halo.receiveShadows = false;
                _halo.alignment = LineAlignment.View;
            }
            _halo.startWidth = lineWidth * 0.7f;
            _halo.endWidth = lineWidth * 0.7f;
            Color haloColor = ringColor;
            haloColor.a *= 0.55f;
            _halo.startColor = haloColor;
            _halo.endColor = haloColor;
            _halo.material = _ring.material;
            for (int i = 0; i < _halo.positionCount; i++)
            {
                float a = i * Mathf.PI * 2f / _halo.positionCount;
                _halo.SetPosition(i, new Vector3(Mathf.Cos(a) * radius * 0.62f, 0.58f, Mathf.Sin(a) * radius * 0.62f));
            }
        }

        private static Material MakeRingMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            mat.hideFlags = HideFlags.DontSave;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            mat.renderQueue = 3000;
            return mat;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = ringColor;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.03f, radius);
        }
    }
}
