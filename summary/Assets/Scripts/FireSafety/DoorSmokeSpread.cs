using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 第 40 轮：安全出口打开后的「门内 → 门外」烟雾扩散。
    /// 门解锁（门扇开始转开）时，在门洞内侧到门外 6 米之间铺若干发射点，
    /// 每个点都带一个朝门外方向的定向漂移速度，视觉上就是烟被抽出去、在门外摊开变淡。
    /// 门外那几层不参与生命值判定（countsForDamage=false），避免玩家刚出门就被判扣血。
    /// </summary>
    public class DoorSmokeSpread : MonoBehaviour
    {
        public static DoorSmokeSpread Instance { get; private set; }

        [Header("绑定")]
        public ExitDoor door;

        [Header("范围")]
        [Tooltip("门内侧起始距离（米，负数表示门内）")]
        public float insideStart = -1.6f;
        [Tooltip("门外扩散到多远（米）")]
        public float outsideEnd = 7.0f;
        [Tooltip("沿途布置几个发射点")]
        public int emitterCount = 9;
        [Tooltip("烟雾层高度（米）")]
        public float layerHeight = 1.05f;

        [Header("表现")]
        public float driftSpeed = 1.60f;      // 门外方向漂移（m/s）
        public float ratePerSecond = 18f;
        public float particleSize = 2.60f;
        public float lifetime = 7.5f;
        public Color smokeColor = new Color(0.70f, 0.71f, 0.73f, 0.55f);

        public bool Active { get; private set; }
        public int EmitterCount { get; private set; }

        SmokeZone[] _zones;
        bool _built;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (door == null) door = ExitDoor.Instance;
            if (door == null) return;
            bool open = door.IsUnlocked;
            if (open && !Active) Begin();
            else if (!open && Active) Stop();
        }

        /// <summary>门开了：铺开发射点并开始冒烟（可重复调用）。</summary>
        public void Begin()
        {
            if (door == null) door = ExitDoor.Instance;
            if (door == null) return;
            Build();
            foreach (var z in _zones)
            {
                if (z != null) z.Begin();
            }
            Active = true;
            Debug.Log("[DoorSmoke] 门口烟雾已开启，" + EmitterCount + " 个发射点（门内 "
                + insideStart.ToString("F1") + "m → 门外 " + outsideEnd.ToString("F1") + "m）。");
        }

        /// <summary>门重新锁上 / 演练结束：停止发射。</summary>
        public void Stop()
        {
            if (_zones != null)
            {
                foreach (var z in _zones)
                {
                    if (z != null) z.Stop();
                }
            }
            Active = false;
        }

        void Build()
        {
            if (_built && _zones != null && _zones.Length > 0) return;
            _built = true;

            // 门本地 forward = 室内侧（ExitDoor.DetectPlayerEnter 同口径），门外方向 = -forward
            Vector3 doorPos = door.transform.position;
            Vector3 inward = door.transform.forward;
            Vector3 outward = -inward;
            inward.y = 0f; outward.y = 0f;
            if (inward.sqrMagnitude < 0.01f) inward = Vector3.forward;
            inward.Normalize();
            outward = -inward;

            int n = Mathf.Max(2, emitterCount);
            _zones = new SmokeZone[n];
            for (int i = 0; i < n; i++)
            {
                float k = (float)i / (n - 1);
                float dist = Mathf.Lerp(insideStart, outsideEnd, k);
                Vector3 pos = doorPos + outward * dist;
                pos.y = Mathf.Max(layerHeight, 0.6f);

                var go = new GameObject("DoorSmoke_" + (i + 1));
                go.transform.SetParent(transform, false);
                go.transform.position = pos;

                var z = go.AddComponent<SmokeZone>();
                z.start = pos;
                z.end = pos;
                z.emitterCount = 1;
                z.floorClearance = pos.y;
                z.particleSize = particleSize * Mathf.Lerp(0.85f, 1.25f, k);
                z.ratePerSecond = ratePerSecond * Mathf.Lerp(1.15f, 0.85f, k);
                z.lifetime = lifetime;
                z.smokeColor = smokeColor;
                z.emitterShape = new Vector3(1.7f, 0.35f, 1.7f);
                z.particleCap = 150;
                z.driftPerSecond = outward * driftSpeed * Mathf.Lerp(0.75f, 1.25f, k) + Vector3.up * 0.10f;
                z.driftJitter = 0.22f;
                z.scatterSpeed = 0.30f;
                z.alphaScale = Mathf.Lerp(2.2f, 1.0f, k);   // 门口最浓，越往门外越淡
                z.sizeGrowth = 2.3f;
                z.countsForDamage = false;
                z.autoFollow = false;
                _zones[i] = z;
            }
            EmitterCount = n;
        }
    }
}
