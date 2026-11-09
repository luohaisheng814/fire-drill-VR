#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 头显接入的运行时配置（可反复执行，幂等）：
    ///   1. 把 OpenXR Loader 指派给 Standalone 的 XRManagerSettings —— 之前 m_Loaders 是空的，
    ///      头显插上也不会初始化 XR，摇杆/手柄位姿全是静止值；
    ///   2. 打开常见头显的交互 profile（Quest Touch Plus / Touch Pro / Oculus Touch / Valve Index /
    ///      HTC Vive / WMR / HP Reverb G2 / KHR Simple），否则 OpenXR 不会把控制器映射成 XRI 能读的动作；
    ///   3. 附带打开 DPad / Palm Pose / Hand Interaction 等可选交互，覆盖 Quest 手柄与手部追踪两种输入。
    /// 菜单：FireSafety → 配置 VR 头显接入（OpenXR）
    /// 批处理：-executeMethod FireSafety.EditorTools.VROpenXRConfig.RunFromBatch
    /// </summary>
    public static class VROpenXRConfig
    {
        const string LoaderTypeName = "UnityEngine.XR.OpenXR.OpenXRLoader";

        [MenuItem("FireSafety/配置 VR 头显接入（OpenXR）")]
        public static void RunFromMenu() => Run();

        /// <summary>批处理入口：配置完成后直接退出。</summary>
        public static void RunFromBatch()
        {
            bool ok = Run();
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Run()
        {
            Debug.Log("[VRConfig] ==== 配置 OpenXR 运行时 ====");
            bool ok = true;

            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (general == null || general.Manager == null)
            {
                Debug.LogError("[VRConfig] 找不到 Standalone 的 XRGeneralSettings / XRManagerSettings");
                return false;
            }

            general.InitManagerOnStart = true;
            var manager = general.Manager;

            bool already = false;
            if (manager.loaders != null)
                foreach (var l in manager.loaders)
                    if (l != null && l.GetType().FullName == LoaderTypeName) { already = true; break; }

            if (!already)
            {
                bool assigned = XRPackageMetadataStore.AssignLoader(manager, LoaderTypeName, BuildTargetGroup.Standalone);
                Debug.Log("[VRConfig] " + (assigned ? "✔ 已挂载" : "✘ 挂载失败") + " OpenXR Loader → Standalone");
                ok &= assigned;
            }
            else
            {
                Debug.Log("[VRConfig] ✔ OpenXR Loader 已在 Standalone 列表中，跳过挂载");
            }

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(general);

            // ---- 交互 profile ----
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (settings == null)
            {
                Debug.LogError("[VRConfig] 找不到 Standalone 的 OpenXRSettings");
                return false;
            }

            ok &= Enable<OculusTouchControllerProfile>(settings, "Oculus Touch（Quest 1/2/3 旧映射）");
            ok &= Enable<MetaQuestTouchPlusControllerProfile>(settings, "Meta Quest Touch Plus（Quest 3）");
            ok &= Enable<MetaQuestTouchProControllerProfile>(settings, "Meta Quest Touch Pro");
            ok &= Enable<ValveIndexControllerProfile>(settings, "Valve Index");
            ok &= Enable<HTCViveControllerProfile>(settings, "HTC Vive");
            ok &= Enable<MicrosoftMotionControllerProfile>(settings, "Windows MR");
            ok &= Enable<HPReverbG2ControllerProfile>(settings, "HP Reverb G2");
            ok &= Enable<KHRSimpleControllerProfile>(settings, "KHR Simple（通用兜底）");
            ok &= Enable<DPadInteraction>(settings, "DPad 扩展");
            ok &= Enable<PalmPoseInteraction>(settings, "Palm Pose 扩展");
            ok &= Enable<HandInteractionProfile>(settings, "手部追踪交互");

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log("[VRConfig] " + (ok ? "==== 配置完成 ====" : "==== 配置存在失败项 ===="));
            return ok;
        }

        static bool Enable<T>(OpenXRSettings settings, string label) where T : UnityEngine.XR.OpenXR.Features.OpenXRFeature
        {
            var feature = settings.GetFeature<T>();
            if (feature == null)
            {
                Debug.LogWarning("[VRConfig] 缺少 feature：" + label + "（" + typeof(T).Name + "）");
                return false;
            }
            if (!feature.enabled)
            {
                feature.enabled = true;
                Debug.Log("[VRConfig] ✔ 启用交互配置：" + label);
            }
            else Debug.Log("[VRConfig] ✔ 交互配置已启用：" + label);
            return feature.enabled;
        }

        /// <summary>自检：把配置缺失项写进 fails 列表，返回是否全部就绪。</summary>
        public static bool Verify(List<string> fails)
        {
            bool ok = true;

            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            bool loader = false;
            if (general != null && general.Manager != null && general.Manager.loaders != null)
                foreach (var l in general.Manager.loaders)
                    if (l != null && l.GetType().FullName == LoaderTypeName) loader = true;

            if (!loader) { fails.Add("Standalone 未挂载 OpenXR Loader（头显接入后不会初始化 XR）"); ok = false; }

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (settings == null) { fails.Add("找不到 Standalone 的 OpenXRSettings"); return false; }

            string[] required =
            {
                "OculusTouchControllerProfile", "MetaQuestTouchPlusControllerProfile",
                "ValveIndexControllerProfile", "HTCViveControllerProfile",
                "MicrosoftMotionControllerProfile", "KHRSimpleControllerProfile"
            };
            foreach (var feature in settings.GetFeatures())
            {
                if (feature == null) continue;
                foreach (var name in required)
                {
                    if (feature.GetType().Name != name) continue;
                    if (!feature.enabled) { fails.Add("交互配置未启用：" + name); ok = false; }
                }
            }
            return ok;
        }
    }
}
#endif
