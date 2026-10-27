using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 抓取灭火器时，让手部模型的姿态与物体外形匹配（手指握住瓶身/压把）。
    ///   · 与 ToggleGrab 挂在同一物体上，自动监听拿起/放下事件
    ///   · 自动在抓握手柄（attachPoint）下查找手部模型（名字含 Hand 或首个 SkinnedMeshRenderer）
    ///   · 按骨骼名匹配手指（Thumb/Index/Middle/Ring/Little + Proximal/Intermediate/Distal/Metacarpal/Tip/1/2/3），
    ///     抓取时弯曲手指、微调手掌位姿；放下时平滑还原
    ///   · 找不到骨骼时退化为仅调整手掌位姿（不报错）
    /// 兼容 OpenXR Hand Skeleton 与 Meta（b_l_index1…）命名。
    /// </summary>
    public class GrabPoseMatcher : MonoBehaviour
    {
        [System.Serializable]
        public class PoseSettings
        {
            [Header("手掌位姿（相对手柄，本地空间）")]
            public Vector3 handLocalPosition = Vector3.zero;
            public Vector3 handLocalEuler = Vector3.zero;

            [Header("手指弯曲（度，越大握得越紧）")]
            [Range(0f, 120f)] public float thumbCurl = 30f;
            [Range(0f, 120f)] public float indexCurl = 65f;
            [Range(0f, 120f)] public float middleCurl = 70f;
            [Range(0f, 120f)] public float ringCurl = 70f;
            [Range(0f, 120f)] public float littleCurl = 60f;
        }

        [Header("左右手姿态（可分别微调）")]
        public PoseSettings rightHandPose = new PoseSettings
        {
            handLocalPosition = new Vector3(0f, -0.03f, 0.06f),
            handLocalEuler = new Vector3(-15f, 0f, 5f),
            thumbCurl = 30f, indexCurl = 65f, middleCurl = 70f, ringCurl = 70f, littleCurl = 60f
        };
        public PoseSettings leftHandPose = new PoseSettings
        {
            handLocalPosition = new Vector3(0f, -0.03f, 0.06f),
            handLocalEuler = new Vector3(-15f, 0f, -5f),
            thumbCurl = 30f, indexCurl = 65f, middleCurl = 70f, ringCurl = 70f, littleCurl = 60f
        };

        [Header("骨骼匹配")]
        [Tooltip("手指弯曲的旋转轴（本地空间），OpenXR 手部骨骼通常绕 X 轴弯曲")]
        public Vector3 curlAxis = Vector3.right;
        [Range(0f, 1f)] public float blendInTime = 0.18f;
        [Range(0f, 1f)] public float blendOutTime = 0.28f;
        [Tooltip("含这些词（忽略大小写）的骨骼认为是手指骨")]
        public string[] fingerTokens = { "thumb", "index", "middle", "ring", "little" };
        [Tooltip("含这些词（忽略大小写）的骨骼才参与弯曲，过滤手腕/手掌")]
        public string[] phalangeTokens = { "proximal", "intermediate", "distal", "metacarpal", "tip", "1", "2", "3" };
        [Tooltip("手部模型路径（相对抓握手柄），留空自动搜索")]
        public string handModelPath;

        class BoneRef
        {
            public Transform t;
            public Quaternion homeRotation;
            public float angle;   // 目标弯曲角（度）
        }

        ToggleGrab _grab;
        Transform _hand;
        List<BoneRef> _bones = new List<BoneRef>();
        PoseSettings _pose;
        Coroutine _blend;
        bool _holding;

        void Awake()
        {
            _grab = GetComponent<ToggleGrab>();
        }

        void OnEnable()
        {
            if (_grab != null)
            {
                _grab.onPickedUp.AddListener(OnPickedUp);
                _grab.onDropped.AddListener(OnDropped);
            }
        }

        void OnDisable()
        {
            if (_grab != null)
            {
                _grab.onPickedUp.RemoveListener(OnPickedUp);
                _grab.onDropped.RemoveListener(OnDropped);
            }
        }

        void OnPickedUp()
        {
            if (_grab == null || _grab.CurrentHandSide == HandSide.None) return;
            _pose = _grab.CurrentHandSide == HandSide.Right ? rightHandPose : leftHandPose;
            _hand = FindHandModel();
            _bones = CollectBones(_hand);
            if (_bones.Count == 0)
                Debug.Log("[GrabPose] 未找到可弯曲的手指骨骼，退化为仅调整手掌位姿（可配置 handModelPath）");

            if (_blend != null) StopCoroutine(_blend);
            _blend = StartCoroutine(BlendTo(true));
            _holding = true;
        }

        void OnDropped()
        {
            if (!_holding) return;
            _holding = false;
            if (_blend != null) StopCoroutine(_blend);
            _blend = StartCoroutine(BlendTo(false));
        }

        // ---------------- 查找手部模型 ----------------

        Transform FindHandModel()
        {
            if (_grab == null) return null;

            // 当前抓握手柄
            Transform attach = null;
            foreach (var h in _grab.hands)
            {
                if (h != null && h.attachPoint != null && h.side == _grab.CurrentHandSide)
                {
                    attach = h.attachPoint;
                    break;
                }
            }
            if (attach == null) return null;

            // 显式路径
            if (!string.IsNullOrEmpty(handModelPath))
            {
                var found = attach.Find(handModelPath);
                if (found != null) return found;
            }

            // 递归找名字含 Hand 且不是控制器/射线
            var stack = new Stack<Transform>();
            foreach (Transform c in attach) stack.Push(c);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                string n = t.name.ToLowerInvariant();
                if (n.Contains("hand") && !n.Contains("controller") && !n.Contains("ray"))
                    return t;
                foreach (Transform c in t) stack.Push(c);
            }

            // 退而求其次：第一个蒙皮网格（手部模型）
            var skinned = attach.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinned != null) return skinned.transform;

            return attach;
        }

        // ---------------- 骨骼收集 ----------------

        List<BoneRef> CollectBones(Transform hand)
        {
            var list = new List<BoneRef>();
            if (hand == null) return list;

            var all = new List<Transform>();
            foreach (Transform c in hand) all.Add(c);
            for (int i = 0; i < all.Count; i++)
                foreach (Transform c in all[i]) all.Add(c);

            for (int fi = 0; fi < fingerTokens.Length; fi++)
            {
                string finger = fingerTokens[fi];
                var fingerBones = new List<Transform>();
                foreach (var t in all)
                {
                    string n = t.name.ToLowerInvariant();
                    if (!n.Contains(finger)) continue;
                    bool hasPhalange = false;
                    foreach (var p in phalangeTokens)
                        if (n.Contains(p)) { hasPhalange = true; break; }
                    if (hasPhalange) fingerBones.Add(t);
                }
                if (fingerBones.Count == 0) continue;

                // 按层级深度排序：越靠外的指节弯得越多
                fingerBones.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
                float baseAngle = fi == 0 ? _pose.thumbCurl
                    : fi == 1 ? _pose.indexCurl
                    : fi == 2 ? _pose.middleCurl
                    : fi == 3 ? _pose.ringCurl
                    : _pose.littleCurl;

                for (int j = 0; j < fingerBones.Count; j++)
                {
                    float weight = 0.6f + 0.35f * (j / (float)Mathf.Max(1, fingerBones.Count - 1));
                    // 拇指根部（metacarpal）少弯
                    if (fi == 0 && fingerBones[j].name.ToLowerInvariant().Contains("metacarpal"))
                        weight *= 0.5f;

                    list.Add(new BoneRef
                    {
                        t = fingerBones[j],
                        homeRotation = fingerBones[j].localRotation,
                        angle = baseAngle * weight
                    });
                }
            }
            return list;
        }

        static int Depth(Transform t)
        {
            int d = 0;
            while (t != null) { t = t.parent; d++; }
            return d;
        }

        // ---------------- 混合动画 ----------------

        IEnumerator BlendTo(bool grab)
        {
            var handStartPos = _hand != null ? _hand.localPosition : Vector3.zero;
            var handStartRot = _hand != null ? _hand.localRotation : Quaternion.identity;

            var boneHomes = new Quaternion[_bones.Count];
            for (int i = 0; i < _bones.Count; i++) boneHomes[i] = _bones[i].homeRotation;

            float dur = grab ? blendInTime : blendOutTime;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(0.001f, dur));
                float ease = grab ? 1f - Mathf.Pow(1f - k, 3f) : k * k * (3f - 2f * k);

                if (_hand != null)
                {
                    _hand.localPosition = Vector3.Lerp(handStartPos,
                        grab ? _pose.handLocalPosition : Vector3.zero, ease);
                    _hand.localRotation = Quaternion.Slerp(handStartRot,
                        grab ? Quaternion.Euler(_pose.handLocalEuler) : Quaternion.identity, ease);
                }

                for (int i = 0; i < _bones.Count; i++)
                {
                    var b = _bones[i];
                    if (b.t == null) continue;
                    Quaternion target = grab
                        ? b.homeRotation * Quaternion.Euler(curlAxis * b.angle)
                        : boneHomes[i];
                    b.t.localRotation = Quaternion.Slerp(boneHomes[i], target, ease);
                }
                yield return null;
            }

            // 收尾精确落位
            if (_hand != null)
            {
                _hand.localPosition = grab ? _pose.handLocalPosition : Vector3.zero;
                _hand.localRotation = grab ? Quaternion.Euler(_pose.handLocalEuler) : Quaternion.identity;
            }
            for (int i = 0; i < _bones.Count; i++)
            {
                if (_bones[i].t == null) continue;
                _bones[i].t.localRotation = grab
                    ? _bones[i].homeRotation * Quaternion.Euler(curlAxis * _bones[i].angle)
                    : boneHomes[i];
            }
        }
    }
}
