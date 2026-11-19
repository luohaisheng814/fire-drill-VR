using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 第 35 轮：灭火器指导标识（替换原 ExtinguisherHighlight 的光环/光柱与 ExtinguisherKindTag 的大标签）。
    /// 由三部分组成：
    ///   · 瓶顶向下的青色箭头：一眼看出“这个瓶子可以拿”；
    ///   · 世界空间信息牌（TextMesh + 背景板，不依赖 Canvas，避免首帧空白）；
    ///   · 地面断续双环：站到瓶旁时脚下有明确落点，不再用一整根光柱挡视线。
    /// 被拿起后整体隐藏，放下且仍需要灭火时重新出现。
    /// </summary>
    [RequireComponent(typeof(Extinguisher))]
    public class ExtinguisherGuideMarker : MonoBehaviour
    {
        [Header("关联")]
        public ExtinguisherKind kind = ExtinguisherKind.PowderABC;

        [Header("摆放")]
        [Tooltip("信息牌在瓶顶之上的高度（米）")]
        public float labelRise = 0.34f;
        [Tooltip("信息牌世界尺寸（米）")]
        public Vector2 sizeMeters = new Vector2(0.90f, 0.34f);
        [Tooltip("地面圆环半径（米）")]
        public float ringRadius = 0.40f;
        [Tooltip("朝相机让位距离（米），避免插进墙里")]
        public float pushFromWall = 0.28f;

        [Header("配色")]
        public Color guideColor = new Color(0.18f, 0.92f, 0.98f, 1f);

        public Transform LabelTransform => _labelRoot;

        TextMesh _title;
        TextMesh _kindLine;
        TextMesh _actionLine;
        Transform _labelRoot;
        GameObject _arrow;
        GameObject _ringRoot;
        ToggleGrab _grab;
        Camera _cam;
        float _topY = 0.6f;
        bool _builtRuntime;

        void Awake()
        {
            _grab = GetComponentInParent<ToggleGrab>();
            if (_grab == null) _grab = GetComponent<ToggleGrab>();
            MeasureTop();
            Build();
        }

        System.Collections.IEnumerator Start()
        {
            // TextMesh 在 Awake 里赋值时可能还没生成网格，下一帧再刷新一次，保证中文首帧就出字。
            yield return null;
            RefreshText();
        }

        void OnEnable()
        {
            DrillLocale.Changed += RefreshText;
        }

        void OnDisable()
        {
            DrillLocale.Changed -= RefreshText;
        }

        void OnDestroy()
        {
            DrillLocale.Changed -= RefreshText;
            if (_labelRoot != null)
            {
                if (Application.isPlaying) Destroy(_labelRoot.gameObject);
                else DestroyImmediate(_labelRoot.gameObject);
            }
            if (_ringRoot != null)
            {
                if (Application.isPlaying) Destroy(_ringRoot);
                else DestroyImmediate(_ringRoot);
            }
        }

        public void ApplyKind(ExtinguisherKind newKind)
        {
            kind = newKind;
            RefreshText();
        }

        void MeasureTop()
        {
            bool has = false;
            Bounds b = new Bounds(transform.position, Vector3.zero);
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (r.name.ToLower().Contains("spray")) continue;
                if (r.name.StartsWith("Guide")) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            if (has) _topY = Mathf.Max(0.35f, b.max.y - transform.position.y);
        }

        void Build()
        {
            if (_builtRuntime) return;
            _builtRuntime = true;
            BuildLabel();
            BuildArrow();
            BuildFloorRing();
            RefreshText();
        }

        // ---------------- 信息牌：TextMesh（无 Canvas） ----------------

        void BuildLabel()
        {
            var go = new GameObject("ExtinguisherGuideLabel");
            go.transform.SetParent(null, false);
            _labelRoot = go.transform;

            float w = sizeMeters.x, h = sizeMeters.y;

            // 底板 + 左侧色条
            AddQuad(go.transform, "Bg", new Vector3(0f, 0f, 0f), new Vector3(w, h, 1f),
                new Color(0.03f, 0.09f, 0.13f, 1f));
            AddQuad(go.transform, "Strip", new Vector3(-w * 0.5f + 0.008f, 0f, -0.004f),
                new Vector3(0.016f, h, 1f), guideColor);

            _title = AddText(go.transform, "", new Vector3(-w * 0.5f + 0.045f, h * 0.30f, -0.008f),
                64, 0.019f, new Color(0.94f, 1f, 1f), FontStyle.Bold);
            _kindLine = AddText(go.transform, "", new Vector3(-w * 0.5f + 0.045f, h * -0.02f, -0.008f),
                48, 0.0105f, new Color(0.80f, 0.95f, 1f), FontStyle.Normal);
            _actionLine = AddText(go.transform, "", new Vector3(-w * 0.5f + 0.045f, h * -0.29f, -0.008f),
                40, 0.008f, new Color(0.62f, 0.88f, 0.82f), FontStyle.Normal);
        }

        void AddQuad(Transform parent, string name, Vector3 localPos, Vector3 localScale, Color color)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            var col = q.GetComponent<Collider>();
            if (col != null) Destroy(col);
            q.transform.SetParent(parent, false);
            q.transform.localPosition = localPos;
            q.transform.localRotation = Quaternion.identity;
            q.transform.localScale = localScale;

            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(sh);
            mat.hideFlags = HideFlags.DontSave;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", Texture2D.whiteTexture);
            mat.renderQueue = 2000;
            q.GetComponent<MeshRenderer>().sharedMaterial = mat;
            q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.GetComponent<MeshRenderer>().receiveShadows = false;
        }

        TextMesh AddText(Transform parent, string content, Vector3 localPos, int fontSize,
                         float characterSize, Color color, FontStyle style)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            // TextMesh 的可见面与 Quad 相反：绕 Y 翻 180° 让它朝向玩家；
            // 再对 X 取反抵消这次翻转造成的镜像，字序仍然从左到右。
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = new Vector3(-1f, 1f, 1f);

            var tm = go.AddComponent<TextMesh>();
            tm.font = UIFont.Resolve();
            if (tm.font != null) tm.font.material = tm.font.material;
            tm.text = content;
            tm.fontSize = fontSize;
            tm.characterSize = characterSize;
            tm.anchor = TextAnchor.MiddleLeft;
            tm.alignment = TextAlignment.Left;
            tm.color = color;
            tm.fontStyle = style;
            tm.richText = false;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (tm.font != null) mr.sharedMaterial = tm.font.material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            return tm;
        }

        // ---------------- 箭头 / 地面圆环 ----------------

        void BuildArrow()
        {
            _arrow = new GameObject("GuideArrow");
            _arrow.layer = 2;
            _arrow.AddComponent<MeshFilter>().sharedMesh = BuildConeMesh(0.5f, 1.0f, 16);
            var mr = _arrow.AddComponent<MeshRenderer>();
            _arrow.transform.SetParent(transform, false);
            _arrow.transform.localPosition = new Vector3(0f, _topY + 0.16f, 0f);
            _arrow.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            _arrow.transform.localScale = new Vector3(0.18f, 0.16f, 0.18f);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", guideColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", guideColor);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>自建圆锥网格：顶点朝上（+Y），底面在 -Y，避免依赖不存在的 Cone 图元。</summary>
        static Mesh BuildConeMesh(float radius, float height, int segments)
        {
            var verts = new Vector3[segments + 2];
            var tris = new int[segments * 3 + segments * 3];
            verts[0] = new Vector3(0f, height * 0.5f, 0f);
            int baseCenter = segments + 1;
            verts[baseCenter] = new Vector3(0f, -height * 0.5f, 0f);
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.PI * 2f * i / segments;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, -height * 0.5f, Mathf.Sin(a) * radius);
            }
            for (int i = 0; i < segments; i++)
            {
                int cur = i + 1;
                int next = ((i + 1) % segments) + 1;
                int t = i * 3;
                tris[t] = 0; tris[t + 1] = next; tris[t + 2] = cur;
                int b = segments * 3 + i * 3;
                tris[b] = baseCenter; tris[b + 1] = cur; tris[b + 2] = next;
            }
            var mesh = new Mesh();
            mesh.name = "GuideCone";
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void BuildFloorRing()
        {
            _ringRoot = new GameObject("GuideFloorRing");
            _ringRoot.layer = 2;
            _ringRoot.transform.SetParent(transform, false);
            _ringRoot.transform.localPosition = Vector3.zero;

            for (int ring = 0; ring < 2; ring++)
            {
                float radius = ring == 0 ? ringRadius : ringRadius * 0.72f;
                int arcs = 8;
                float gap = 9f;
                float arcDeg = 360f / arcs - gap;
                for (int a = 0; a < arcs; a++)
                {
                    var go = new GameObject("Arc");
                    go.layer = 2;
                    go.transform.SetParent(_ringRoot.transform, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.useWorldSpace = false;
                    lr.loop = false;
                    lr.positionCount = 7;
                    lr.alignment = LineAlignment.View;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lr.receiveShadows = false;
                    lr.startWidth = lr.endWidth = ring == 0 ? 0.035f : 0.022f;
                    Color c = guideColor;
                    c.a = ring == 0 ? 0.95f : 0.55f;
                    lr.startColor = lr.endColor = c;
                    lr.material = MakeLineMaterial();

                    float start = a * (360f / arcs) + gap * 0.5f;
                    for (int i = 0; i < lr.positionCount; i++)
                    {
                        float deg = start + arcDeg * i / (lr.positionCount - 1);
                        float rad = deg * Mathf.Deg2Rad;
                        lr.SetPosition(i, new Vector3(Mathf.Cos(rad) * radius, 0.018f, Mathf.Sin(rad) * radius));
                    }
                }
            }
        }

        Material MakeLineMaterial()
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

        void RefreshText()
        {
            if (_title == null) return;
            // 先清空再写：TextMesh 只有检测到文本变化才重建网格，
            // 否则“显示瞬间文本没变”会留下一块空白牌（中文首帧问题的根因）。
            _title.text = string.Empty;
            _kindLine.text = string.Empty;
            _actionLine.text = string.Empty;
            _title.text = DrillLocale.T("灭火器", "FIRE EXTINGUISHER");
            _kindLine.text = DrillLocale.KindName(kind) + " · " + DrillLocale.KindSuitable(kind);
            _actionLine.text = DrillLocale.T("靠近后按 Grip / 右键取用", "Grip or right-click to pick up");

            // 动态字体在首次生成网格后会重配材质，这里再挂一次，防止首帧拿到空贴图。
            RebindFontMaterial(_title);
            RebindFontMaterial(_kindLine);
            RebindFontMaterial(_actionLine);
        }

        static void RebindFontMaterial(TextMesh tm)
        {
            if (tm == null || tm.font == null) return;
            var mr = tm.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = tm.font.material;
        }

        System.Collections.IEnumerator CoRefreshTextNextFrame()
        {
            yield return null;          // 等 Canvas/激活完成
            RefreshText();
            yield return null;          // 再等一帧，确保 TextMesh 网格重建
            RefreshText();
        }

        void LateUpdate()
        {
            if (!_builtRuntime) { Build(); return; }

            bool started = FireDrillManager.Instance == null
                           || FireDrillManager.Instance.IsStepDone(DrillStep.AlarmRinging);
            if (StartScreen.Instance != null && StartScreen.Instance.DrillStarted) started = true;

            bool held = _grab != null && _grab.IsHeld;
            var ext = GetComponent<Extinguisher>();
            bool quizOpen = ext != null && ext.quizPanel != null && ext.quizPanel.IsOpen;
            bool show = started && !held && !quizOpen;

            bool wasShown = _labelRoot != null && _labelRoot.gameObject.activeSelf;
            if (_labelRoot != null && wasShown != show) _labelRoot.gameObject.SetActive(show);
            if (_arrow != null && _arrow.activeSelf != show) _arrow.SetActive(show);
            if (_ringRoot != null && _ringRoot.activeSelf != show) _ringRoot.SetActive(show);
            if (show && !wasShown)
            {
                DrillLocale.NotifyChanged();     // 首帧强制走一次和“切换语言”相同的刷新链路
                StartCoroutine(CoRefreshTextNextFrame());
            }
            if (!show) return;

            // 不能缓存 Camera.main：批处理/场景加载早期可能指向另一台相机，
            // 缓存后牌子会一直朝旧相机方向（侧对玩家）。每帧重新解析。
            var mainCam = Camera.main;
            if (mainCam != null) _cam = mainCam;
            if (_cam == null || _labelRoot == null) return;

            Vector3 basePos = transform.position + Vector3.up * (_topY + labelRise);
            Vector3 toCam = _cam.transform.position - basePos;
            toCam.y = 0f;
            if (toCam.sqrMagnitude <= 0.0001f) return;

            if (_labelRoot.parent != null) _labelRoot.SetParent(null, true);   // 防止被当成瓶身子物体跟着墙面转
            _labelRoot.position = basePos + toCam.normalized * pushFromWall;
            // TextMesh / Quad 的标准 billboard：本地 +Z 背向相机，正面（-Z）朝向玩家才可读。
            _labelRoot.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);

            float pulse = 0.78f + 0.22f * (Mathf.Sin(Time.time * 4.2f) * 0.5f + 0.5f);
            var c = guideColor;
            c.a = 0.55f + 0.45f * pulse;
            if (_arrow != null)
            {
                var ar = _arrow.GetComponent<Renderer>();
                if (ar != null && ar.material.HasProperty("_BaseColor")) ar.material.SetColor("_BaseColor", c);
                _arrow.transform.localPosition = new Vector3(0f, _topY + 0.10f + 0.05f * pulse, 0f);
            }
        }
    }
}

