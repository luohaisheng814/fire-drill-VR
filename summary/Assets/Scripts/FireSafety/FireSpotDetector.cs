using UnityEngine;
using UnityEngine.Events;

namespace FireSafety
{
    /// <summary>步骤 ③“确认起火位置”：注视火焰累计一定时间（或走近）即判定已确认。</summary>
    public class FireSpotDetector : MonoBehaviour
    {
        [Header("引用")]
        public Camera xrCamera;
        public FireSource fire;

        [Header("注视判定")]
        [Tooltip("需要持续注视的秒数")]
        public float gazeSeconds = 1.0f;
        [Tooltip("视线与起火点方向的最大夹角（度）")]
        public float gazeAngle = 25f;
        public float maxGazeDistance = 25f;
        [Tooltip("检查是否被墙体/家具遮挡")]
        public bool checkOcclusion = true;
        public LayerMask blockingMask = ~0;
        public float decayMultiplier = 1.5f;

        [Header("靠近也算确认")]
        public bool allowProximity = true;
        public float proximityRadius = 2.5f;

        [Header("事件")]
        public UnityEvent onLocated;
        public UnityEvent<float> onGazeProgress;   // 0~1，可接进度条

        public bool IsLocated { get; private set; }
        public float GazeProgress01 => Mathf.Clamp01(_gazeTimer / Mathf.Max(0.01f, gazeSeconds));

        private float _gazeTimer;

        private void Reset() { xrCamera = GetComponent<Camera>(); if (!xrCamera) xrCamera = Camera.main; }
        private void Awake()
        {
            if (!xrCamera) xrCamera = GetComponent<Camera>();
            if (!xrCamera) xrCamera = Camera.main;
        }

        private void Update()
        {
            if (IsLocated) return;

            var mgr = FireDrillManager.Instance;
            if (mgr == null || !mgr.IsStepDone(DrillStep.AlarmRinging)) return;
            if (!xrCamera || !fire) return;

            bool ok = IsLookingAtFire();
            if (allowProximity && !ok)
                ok = Vector3.Distance(xrCamera.transform.position, fire.AimPoint) <= proximityRadius;

            if (ok)
            {
                _gazeTimer += Time.deltaTime;
                onGazeProgress?.Invoke(GazeProgress01);
                if (_gazeTimer >= gazeSeconds) Locate();
            }
            else
            {
                _gazeTimer = Mathf.Max(0f, _gazeTimer - Time.deltaTime * decayMultiplier);
                onGazeProgress?.Invoke(GazeProgress01);
            }
        }

        private bool IsLookingAtFire()
        {
            Vector3 origin = xrCamera.transform.position;
            Vector3 toFire = fire.AimPoint - origin;
            float dist = toFire.magnitude;
            if (dist > maxGazeDistance) return false;
            if (Vector3.Angle(xrCamera.transform.forward, toFire.normalized) > gazeAngle) return false;

            if (checkOcclusion &&
                Physics.Raycast(origin, toFire.normalized, out var hit, dist, blockingMask))
                return hit.transform == fire.transform || hit.transform.IsChildOf(fire.transform);

            return true;
        }

        private void Locate()
        {
            IsLocated = true;
            onGazeProgress?.Invoke(1f);
            onLocated?.Invoke();
            FireDrillManager.Instance?.CompleteStep(DrillStep.FireLocated);
            Debug.Log("[Drill] 已确认起火位置");
        }
    }
}