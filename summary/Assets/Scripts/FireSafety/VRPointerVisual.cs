using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 第 30 轮：右手射线可视化（激光 + 命中点小圆点 + 命中交互物变绿）。
    /// 头显里"按了没反应"很多时候只是玩家不知道自己指到了哪里，这条线是即时反馈的底线：
    /// 指到电源箱/门/灭火器时线变绿并给一次轻触觉，指到墙则是暖白色。
    /// </summary>
    public class VRPointerVisual : MonoBehaviour
    {
        [Header("引用")]
        public Transform source;                 // 右手柄（空则用自身）
        public VRDrillInput drillInput;          // 用来判断是否头显模式 + 发触觉

        [Header("射线")]
        public float maxDistance = 6f;
        public float lineWidth = 0.012f;
        public Color plainColor = new Color(1f, 0.92f, 0.70f, 0.75f);
        public Color hitColor = new Color(0.35f, 1f, 0.60f, 0.95f);

        LineRenderer _line;
        Transform _dot;
        Renderer _dotRenderer;
        bool _lastHover;
        float _nextHoverPulse;

        void Awake()
        {
            Build();
        }

        void Build()
        {
            var lineGo = new GameObject("VRPointerLine");
            lineGo.transform.SetParent(transform, false);
            _line = lineGo.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.startWidth = lineWidth;
            _line.endWidth = lineWidth * 0.6f;
            var flat = new AnimationCurve();
            flat.AddKey(0f, 1f);
            flat.AddKey(1f, 0.7f);
            _line.widthCurve = flat;
            _line.material = MakeUnlit(plainColor);
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;

            var dotGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dotGo.name = "VRPointerDot";
            var col = dotGo.GetComponent<Collider>();
            if (col != null) Destroy(col);
            dotGo.transform.SetParent(transform, false);
            dotGo.transform.localScale = Vector3.one * 0.022f;
            _dot = dotGo.transform;
            _dotRenderer = dotGo.GetComponent<Renderer>();
            _dotRenderer.material = MakeUnlit(hitColor);
            _dotRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _dotRenderer.receiveShadows = false;
            _dot.gameObject.SetActive(false);
        }

        static Material MakeUnlit(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3100;
            return mat;
        }

        void Update()
        {
            bool active = VRDrillInput.DrillInputActive;
            if (_line != null) _line.enabled = active;
            if (!active)
            {
                if (_dot != null) _dot.gameObject.SetActive(false);
                _lastHover = false;
                return;
            }

            Transform src = source != null ? source : transform;
            Vector3 origin = src.position;
            Vector3 dir = src.forward;

            bool hitSomething = Physics.Raycast(origin, dir, out RaycastHit hit, maxDistance, ~0,
                                                QueryTriggerInteraction.Collide);
            Vector3 end = hitSomething ? hit.point : origin + dir * maxDistance;
            _line.SetPosition(0, origin);
            _line.SetPosition(1, end);

            bool hover = hitSomething && IsInteractable(hit.collider);
            Color c = hover ? hitColor : plainColor;
            _line.startColor = c;
            _line.endColor = new Color(c.r, c.g, c.b, c.a * 0.35f);
            if (_line.material != null)
            {
                if (_line.material.HasProperty("_BaseColor")) _line.material.SetColor("_BaseColor", c);
                if (_line.material.HasProperty("_Color")) _line.material.SetColor("_Color", c);
            }

            if (_dot != null)
            {
                _dot.gameObject.SetActive(hitSomething);
                if (hitSomething)
                {
                    _dot.position = hit.point + hit.normal * 0.006f;
                    _dot.localScale = Vector3.one * (hover ? 0.030f : 0.018f);
                }
            }

            // 进入可交互目标时轻震一下，让玩家"感觉到"瞄准了
            if (hover && !_lastHover && Time.unscaledTime >= _nextHoverPulse)
            {
                _nextHoverPulse = Time.unscaledTime + 0.6f;
                if (drillInput != null) drillInput.PulseRightPublic(0.10f, 0.05f);
            }
            _lastHover = hover;
        }

        static bool IsInteractable(Collider col)
        {
            if (col == null) return false;
            if (col.GetComponentInParent<Extinguisher>() != null) return true;
            if (col.GetComponentInParent<SafetyPin>() != null) return true;
            if (col.GetComponentInParent<HandleLever>() != null) return true;
            if (col.GetComponentInParent<PowerSwitch>() != null) return true;
            if (col.GetComponentInParent<ExitDoor>() != null) return true;
            if (col.GetComponentInParent<RoomDoor>() != null) return true;
            if (col.GetComponentInParent<ExtinguisherQuizPanel>() != null) return true;
            if (col.GetComponentInParent<StartScreen>() != null) return true;
            if (col.GetComponentInParent<OnboardingScreen>() != null) return true;
            if (col.GetComponentInParent<StageOneCompletePanel>() != null) return true;
            if (col.GetComponentInParent<UnityEngine.UI.Selectable>() != null) return true;
            return false;
        }
    }
}
