#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>阶段一操作流程自检：验证步骤链（拿→拔销→压把→喷射→扑灭）与粒子系统配置。</summary>
    public static class FireSafetySelfTest
    {
        static int _fail;

        [MenuItem("FireSafety/自检（阶段一操作流程）")]
        public static void RunAll()
        {
            _fail = 0;
            Debug.Log("[TEST] ==== 开始自检 ====");

            // 顺序：先测不依赖全局状态的部分，最后测状态链（会建立并销毁单例）
            TestHandleLeverLogic();
            TestSprayParticles();
            TestDesktopMouseFlow();
            TestStepChain();

            if (_fail == 0) Debug.Log("[TEST] ==== 全部通过 ====");
            else Debug.LogError("[TEST] ==== 失败项：" + _fail + " ====");

            if (Application.isBatchMode) EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------
        // 1. 压把逻辑：未拔销时不可用、拔销后可用
        // ------------------------------------------------------------
        static void TestHandleLeverLogic()
        {
            var root = new GameObject("TEST_Ext2");
            var ext = root.AddComponent<Extinguisher>();
            ext.quizRequired = false;

            var pivot = new GameObject("HandlePivot");
            pivot.transform.SetParent(root.transform, false);
            var leverGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leverGo.name = "Handle";
            leverGo.transform.SetParent(pivot.transform, false);
            UnityEngine.Object.DestroyImmediate(leverGo.GetComponent<Collider>());

            var handle = root.AddComponent<HandleLever>();
            handle.owner = ext;
            handle.lever = leverGo.transform;
            handle.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);

            Check("压把初始未按下", handle.IsSqueezed == false);
            Check("压把初始未上报 ⑦", handle.PressedOnce == false);
            Check("未拔销时 PinPulled 为假", ext.PinPulled == false);

            ext.PullPin();
            Check("拔销后 PinPulled 为真", ext.PinPulled);

            UnityEngine.Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------
        // 2. 粒子系统：配置不抛异常、材质可生成、参数合理
        // ------------------------------------------------------------
        static void TestSprayParticles()
        {
            var go = new GameObject("TEST_Spray");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var spray = go.AddComponent<ExtinguisherSpray>();
            spray.rate = 280f;

            // 编辑模式下 Awake 不执行，手动注入渲染器以覆盖材质构建分支
            var fld = typeof(ExtinguisherSpray).GetField("_renderer", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fld != null) fld.SetValue(spray, go.GetComponent<ParticleSystemRenderer>());

            try
            {
                spray.Configure();
                spray.Configure();      // 幂等性：重复配置不应报错
                Check("粒子系统配置不抛异常（且可重复调用）", true);
            }
            catch (Exception e)
            {
                Check("粒子系统配置不抛异常: " + e.Message, false);
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            Check("粒子材质已生成", r.sharedMaterial != null);
            Check("粒子材质贴图非空", r.sharedMaterial != null && r.sharedMaterial.mainTexture != null);

            var main = ps.main;
            Check("播放时不自动启动", main.playOnAwake == false);
            Check("锥形喷射（沿本地 +Z）", ps.shape.shapeType == ParticleSystemShapeType.Cone);
            Check("最大粒子数在 2000-3000", main.maxParticles >= 2000 && main.maxParticles <= 3000);
            Check("粒径随生命周期膨胀", ps.sizeOverLifetime.enabled);
            Check("透明度随生命周期衰减", ps.colorOverLifetime.enabled);

            try
            {
                spray.Play();
                Check("粒子可起喷", ps.isPlaying);
                spray.Stop();
                Check("粒子可停止发射", !ps.isPlaying);
            }
            catch (Exception e)
            {
                Check("粒子启停: " + e.Message, false);
            }

            UnityEngine.Object.DestroyImmediate(go);
        }

        // ------------------------------------------------------------
        // 3. 桌面鼠标流程：右键拿起 → 左键拔销 → 左键双击压把 → 右键双击喷射 → P 放下
        // ------------------------------------------------------------
        static void TestDesktopMouseFlow()
        {
            var mgrGo = new GameObject("TEST_MgrDesktop");
            var mgr = mgrGo.AddComponent<FireDrillManager>();
            InvokeAwake(mgr);
            InvokeMethod(mgr, "Start");
            Check("桌面流程：单例与①初始化", FireDrillManager.Instance == mgr && mgr.CurrentStep == DrillStep.InFireScene);

            // 主灭火器 + 全部交互组件
            var root = new GameObject("TEST_ExtDesktop");
            var ext = root.AddComponent<Extinguisher>();
            ext.quizRequired = false;          // 自检跳过问答（问答按键在真机用手柄/键盘验证）

            var grab = root.AddComponent<ToggleGrab>();
            InvokeAwake(grab);
            ext.grab = grab;
            InvokeMethod(ext, "OnEnable");   // 订阅 onPickedUp / onDropped（编辑模式不自动调用）

            var leverGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leverGo.name = "Handle";
            leverGo.transform.SetParent(root.transform, false);
            UnityEngine.Object.DestroyImmediate(leverGo.GetComponent<Collider>());
            var handle = root.AddComponent<HandleLever>();
            handle.owner = ext;
            handle.lever = leverGo.transform;
            InvokeAwake(handle);
            ext.handleLever = handle;

            // 保险销
            var pinGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pinGo.name = "SafetyPin";
            pinGo.transform.SetParent(root.transform, false);
            UnityEngine.Object.DestroyImmediate(pinGo.GetComponent<Collider>());
            pinGo.AddComponent<Rigidbody>();
            var pinGrab = pinGo.AddComponent<ToggleGrab>();
            InvokeAwake(pinGrab);
            var pin = pinGo.AddComponent<SafetyPin>();
            InvokeAwake(pin);
            pin.owner = ext;
            pin.grab = pinGrab;
            ext.pin = pin;

            // 桌面手部锚点
            var anchor = new GameObject("TEST_DesktopHand").transform;

            // —— 未手持时的门控 ——
            Check("未手持：压把不起作用", handle.PressExternal() == false);
            Check("未手持：不能拔销", pin.PullByInput() == false);
            Check("未手持：不能喷射", ext.TryStartSpray() == false);

            // —— 右键单击：拿起 ——
            grab.GrabExternal(anchor, HandSide.Right);
            Check("右键单击：拿起灭火器", grab.IsHeld);
            Check("拿起即完成 ⑤", mgr.CurrentStep >= DrillStep.ExtinguisherGrabbed);

            // —— 左键单击：拔销 ——
            Check("左键单击：拔出保险销", pin.PullByInput());
            Check("拔销完成 ⑥", mgr.CurrentStep == DrillStep.PinPulled);

            // —— 左键双击：按下压把（逻辑门槛：手持 + 已拔销）——
            Check("左键双击：按下压把", handle.PressExternal());
            Check("压把保持按下状态", handle.IsSqueezed && handle.PressedOnce);
            Check("压把完成 ⑦", mgr.CurrentStep == DrillStep.HandlePressed);

            // —— 右键双击：喷射 ——
            Check("右键双击：开始喷射", ext.TryStartSpray());
            Check("喷射完成 ⑧", mgr.CurrentStep == DrillStep.Spraying);
            Check("喷射状态为真", ext.IsSpraying);

            // —— 火焰扑灭 ——
            var fireGo = new GameObject("TEST_FireDesktop");
            var fire = fireGo.AddComponent<FireSource>();
            fire.extinguishThreshold = 1f;
            InvokeMethod(fire, "OnEnable");
            fire.ApplyExtinguish(5f);
            Check("灭火完成 ⑨", mgr.CurrentStep == DrillStep.FireExtinguished);

            // —— P：放下 ——
            ext.StopSprayExternal();
            handle.ReleaseExternal();
            grab.ReleaseExternal();
            Check("P 放下：手持解除", grab.IsHeld == false);
            Check("P 放下：喷射停止", ext.IsSpraying == false);
            Check("P 放下：压把复位", handle.IsSqueezed == false);

            UnityEngine.Object.DestroyImmediate(fireGo);
            UnityEngine.Object.DestroyImmediate(pinGo);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(anchor.gameObject);
            ResetManagerInstance();
            UnityEngine.Object.DestroyImmediate(mgrGo);
        }

        // ------------------------------------------------------------
        // 4. 步骤链：⑤拿起 → ⑥拔销 → ⑦压把 → ⑧喷射 → ⑨扑灭
        // ------------------------------------------------------------
        static void TestStepChain()
        {
            var mgrGo = new GameObject("TEST_Manager");
            var mgr = mgrGo.AddComponent<FireDrillManager>();

            // 编辑模式下生命周期不会自动执行，显式调用 Awake 建立单例
            var awake = typeof(FireDrillManager).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            if (awake != null) awake.Invoke(mgr, null);
            if (FireDrillManager.Instance == null)
            {
                var setter = typeof(FireDrillManager).GetProperty("Instance").GetSetMethod(true);
                setter.Invoke(null, new object[] { mgr });
            }

            var start = typeof(FireDrillManager).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance);
            if (start != null) start.Invoke(mgr, null);   // 进入 ①

            Check("单例与①初始化", FireDrillManager.Instance == mgr && mgr.CurrentStep == DrillStep.InFireScene);

            int completed = 0;
            mgr.onDrillCompleted.AddListener(() => completed++);

            var extGo = new GameObject("TEST_Extinguisher");
            var ext = extGo.AddComponent<Extinguisher>();
            ext.quizRequired = false;      // 自检跳过问答门控

            mgr.ForceCompleteUpTo(DrillStep.ExtinguisherGrabbed);
            Check("⑤ 拿起灭火器", mgr.CurrentStep >= DrillStep.ExtinguisherGrabbed);

            ext.PullPin();
            Check("⑥ 拔出保险销", mgr.CurrentStep == DrillStep.PinPulled);

            Check("⑦ 按下压把", mgr.CompleteStep(DrillStep.HandlePressed));
            Check("⑧ 开始喷射", mgr.CompleteStep(DrillStep.Spraying));

            var fireGo = new GameObject("TEST_Fire");
            var fire = fireGo.AddComponent<FireSource>();
            fire.extinguishThreshold = 1f;
            fire.SendMessage("OnEnable", SendMessageOptions.DontRequireReceiver); // 加入 Active 列表
            fire.ApplyExtinguish(5f);

            Check("⑨ 火焰扑灭", mgr.CurrentStep == DrillStep.FireExtinguished);
            Check("完成事件只触发一次", completed == 1);
            Check("IsCompleted 为真", mgr.IsCompleted);
            Check("完成后不再推进", mgr.CompleteStep(DrillStep.Spraying) == false);

            UnityEngine.Object.DestroyImmediate(fireGo);
            UnityEngine.Object.DestroyImmediate(extGo);
            UnityEngine.Object.DestroyImmediate(mgrGo);
        }

        static void Check(string name, bool ok)
        {
            if (ok) Debug.Log("[TEST] ✔ " + name);
            else { _fail++; Debug.LogError("[TEST] ✘ " + name); }
        }

        /// <summary>编辑模式下 AddComponent 不会执行 Awake，自检里手动反射调用。</summary>
        static void InvokeAwake(Component c)
        {
            InvokeMethod(c, "Awake");
        }

        static void InvokeMethod(Component c, string methodName)
        {
            if (c == null) return;
            var m = c.GetType().GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (m != null) m.Invoke(c, null);
            else Debug.LogWarning("[TEST] 未找到方法 " + c.GetType().Name + "." + methodName);
        }

        /// <summary>清空 FireDrillManager 单例，便于连续跑多段流程自检。</summary>
        static void ResetManagerInstance()
        {
            var p = typeof(FireDrillManager).GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);
            var setter = p != null ? p.GetSetMethod(true) : null;
            if (setter != null) setter.Invoke(null, new object[] { null });
        }
    }
}
#endif
