#if UNITY_EDITOR
using System.Collections.Generic;
using FireSafety;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 头显手柄的「看得见的手 + 摸得着的反馈 + 不打架的移动」配置（幂等，可反复执行）：
    ///   1. 左右手各挂一个 VRControllerVisual —— 运行时生成手柄轮廓，避免真机里「看不见自己的手」；
    ///   2. 移动选项写进场景：推满摇杆自动小跑、Grip 加速默认关闭（Grip 是抓取键，抓瓶移动不该自动加速）；
    ///   3. 触觉反馈参数写进 VRDrillInput（抓取/拔销/压把/答题各一次脉冲）。
    ///
    /// 与 FireSafetySetup 的区别：本入口只动上述几处，不重建场景，不会覆盖已调好的
    /// VRDrillHud（distance / verticalOffset / canvasScale）与 UI 版式。
    ///
    /// 菜单：FireSafety → 配置手柄模型与移动选项
    /// 批处理：-executeMethod FireSafety.EditorTools.VRHandsConfig.ApplyFromBatch
    /// 校验  ：-executeMethod FireSafety.EditorTools.VRHandsConfig.VerifyFromBatch
    /// </summary>
    public static class VRHandsConfig
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string RigName = "XR Origin (Drill)";
        const string LeftHandName = "LeftHand Controller";
        const string RightHandName = "RightHand Controller";

        [MenuItem("FireSafety/配置手柄模型与移动选项")]
        public static void RunFromMenu()
        {
            bool ok = Apply();
            EditorUtility.DisplayDialog("手柄配置", ok ? "已写入场景并保存。" : "配置失败，详见 Console。", "好");
        }

        public static void ApplyFromBatch()
        {
            bool ok = Apply();
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static void VerifyFromBatch()
        {
            EnsureScene();
            var fails = new List<string>();
            bool ok = Verify(fails);
            Debug.Log("[VRHands] 校验结果：" + (ok ? "全部通过" : "失败 " + fails.Count + " 项"));
            foreach (var f in fails) Debug.LogError("[VRHands] 缺口：" + f);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>把配置写进场景并保存。</summary>
        public static bool Apply()
        {
            Debug.Log("[VRHands] ==== 配置手柄模型与移动选项 ====");

            EnsureScene();

            var rigGo = FindDeep("XR Origin (Drill)");
            if (rigGo == null) { Debug.LogError("[VRHands] 场景里找不到 " + RigName); return false; }

            var left = FindDeep(LeftHandName);
            var right = FindDeep(RightHandName);
            if (left == null || right == null) { Debug.LogError("[VRHands] 找不到左右手柄节点"); return false; }

            // 1) 手柄模型
            ConfigureVisual(left.transform, HandSide.Left);
            ConfigureVisual(right.transform, HandSide.Right);

            // 2) 移动选项
            var loco = rigGo.GetComponent<XRDrillLocomotion>();
            if (loco == null) loco = rigGo.GetComponentInChildren<XRDrillLocomotion>(true);
            if (loco == null) { Debug.LogError("[VRHands] 找不到 XRDrillLocomotion"); return false; }

            loco.moveDirection = XRDrillLocomotion.DirectionMode.Head;
            loco.sprintOnFullStick = true;
            loco.fullStickThreshold = 0.92f;
            loco.allowSprint = false;                 // Grip 是抓取键，不再兼任加速
            loco.leftHand = left.transform;
            loco.rightHand = right.transform;
            if (loco.head == null)
            {
                var xrRig = rigGo.GetComponent<XROrigin>();
                if (xrRig != null && xrRig.Camera != null) loco.head = xrRig.Camera.transform;
            }

            // 3) 触觉参数
            var vr = rigGo.GetComponent<VRDrillInput>();
            if (vr == null) vr = rigGo.GetComponentInChildren<VRDrillInput>(true);
            if (vr != null)
            {
                vr.hapticGrab = 0.22f;
                vr.hapticPin = 0.75f;
                vr.hapticPress = 0.42f;
                vr.hapticAnswer = 0.30f;
                vr.hapticDuration = 0.07f;
                if (vr.leftHand == null) vr.leftHand = left.transform;
                if (vr.rightHand == null) vr.rightHand = right.transform;
            }
            else
            {
                Debug.LogWarning("[VRHands] 场景里没有 VRDrillInput，跳过触觉参数");
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

            Debug.Log("[VRHands] ✔ 已写入并保存：" + ScenePath
                      + "（双手模型 + 推满摇杆小跑 + Grip 加速关闭 + 触觉参数）");
            return true;
        }

        /// <summary>批处理启动时默认是一个空场景，先确保打开的是主场景。</summary>
        static void EnsureScene()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path == ScenePath) return;
            if (!Application.isBatchMode && active.isDirty) EditorSceneManager.SaveScene(active);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        static void ConfigureVisual(Transform hand, HandSide side)
        {
            var vis = hand.GetComponent<VRControllerVisual>();
            if (vis == null) vis = hand.gameObject.AddComponent<VRControllerVisual>();
            vis.side = side;
            vis.showInDesktop = false;
            vis.bodyColor = new Color(0.15f, 0.16f, 0.19f);
            vis.accentColor = side == HandSide.Left
                ? new Color(0.28f, 0.82f, 1f)      // 左手青
                : new Color(1f, 0.55f, 0.2f);      // 右手橙
            vis.emission = 0.55f;
            Debug.Log("[VRHands] ✔ 手柄模型：" + hand.name + "（" + (side == HandSide.Left ? "左手" : "右手") + "）");
        }

        /// <summary>校验场景里的配置是否齐全。</summary>
        public static bool Verify(List<string> fails)
        {
            int before = fails.Count;

            var rigGo = FindDeep(RigName);
            if (rigGo == null) { fails.Add("场景缺少 " + RigName); return false; }

            var xrRig = rigGo.GetComponent<XROrigin>();
            if (xrRig == null) fails.Add("锚点上缺少 XROrigin 组件");
            else if (xrRig.RequestedTrackingOriginMode != XROrigin.TrackingOriginMode.Floor)
                fails.Add("追踪原点不是地板模式（真实身高无法映射）");

            foreach (var hand in new[] { LeftHandName, RightHandName })
            {
                var t = FindDeep(hand);
                if (t == null) { fails.Add("缺少手柄节点 " + hand); continue; }
                var vis = t.GetComponent<VRControllerVisual>();
                if (vis == null) fails.Add(hand + " 未挂 VRControllerVisual（头显里看不见手）");
                else if (vis.side != (hand == LeftHandName ? HandSide.Left : HandSide.Right))
                    fails.Add(hand + " 手柄模型手侧配置错误");
            }

            var loco = rigGo.GetComponent<XRDrillLocomotion>();
            if (loco == null) fails.Add("锚点缺少 XRDrillLocomotion");
            else
            {
                if (!loco.sprintOnFullStick) fails.Add("推满摇杆小跑未启用");
                if (loco.fullStickThreshold < 0.8f || loco.fullStickThreshold > 1f) fails.Add("小跑阈值不在 0.8~1 之间");
                if (loco.allowSprint) fails.Add("Grip 加速仍开启（会和抓取抢键）");
                if (loco.leftHand == null) fails.Add("移动组件未引用左手柄（LeftHand 方向模式会退化）");
                if (loco.moveDirection != XRDrillLocomotion.DirectionMode.Head) fails.Add("移动方向基准不是头显");
            }

            var vr = rigGo.GetComponent<VRDrillInput>();
            if (vr == null) fails.Add("锚点缺少 VRDrillInput");
            else
            {
                if (vr.hapticPin <= 0f) fails.Add("拔销触觉幅度为 0");
                if (vr.hapticPress <= 0f) fails.Add("压把触觉幅度为 0");
                if (vr.hapticDuration <= 0f) fails.Add("触觉时长无效");
                if (vr.leftHand == null || vr.rightHand == null) fails.Add("输入层未引用双手");
            }

            if (!VRHaptics.Enabled) fails.Add("VRHaptics 总开关被关闭");

            Debug.Log("[VRHands] 校验明细：新增缺口 " + (fails.Count - before) + " 项");
            return fails.Count == before;
        }

        // ---------------- 工具 ----------------

        static GameObject FindDeep(string name)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var t = FindRecursive(root.transform, name);
                if (t != null) return t.gameObject;
            }
            return null;
        }

        static Transform FindRecursive(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var hit = FindRecursive(t.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
#endif
