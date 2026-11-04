#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
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
            TestDesktopHud();
            TestQuizLogic();
            TestQuizFailThenRetry();
            TestStageOnePanel();
            TestStepChain();
            TestSceneWiring();

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
        // 3.5 桌面 HUD：屏幕提示能正常生成并包含键位说明
        // ------------------------------------------------------------
        static void TestDesktopHud()
        {
            var go = new GameObject("TEST_DesktopInput");
            var input = go.AddComponent<DesktopDrillInput>();
            InvokeAwake(input);                       // Awake 里会创建 DesktopHand 与 HUD

            var canvasField = typeof(DesktopDrillInput).GetField("_hudCanvas", BindingFlags.NonPublic | BindingFlags.Instance);
            var textField = typeof(DesktopDrillInput).GetField("_hudText", BindingFlags.NonPublic | BindingFlags.Instance);
            var canvas = canvasField != null ? canvasField.GetValue(input) as Canvas : null;
            var text = textField != null ? textField.GetValue(input) as Text : null;

            Check("HUD 画布已创建", canvas != null);
            Check("HUD 文本已创建", text != null);
            Check("HUD 文本包含鼠标键位说明", text != null && text.text != null && text.text.Contains("右键单击"));

            if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            UnityEngine.Object.DestroyImmediate(go);
        }

        // ------------------------------------------------------------
        // 3.6 常识问答：5 题 / 3 选项 / 10 秒限时 / 超时判错 / 60% 通过
        // ------------------------------------------------------------
        static void TestQuizLogic()
        {
            var go = new GameObject("TEST_Quiz");
            var quiz = go.AddComponent<ExtinguisherQuizPanel>();
            InvokeAwake(quiz);                       // BuildUI + BuildDefaultQuestions
            quiz.advanceDelay = 0.01f;

            Check("问答：默认 5 题", quiz.QuestionCount == 5);
            Check("问答：每题 A/B/C 三选项", quiz.OptionCount == 3);
            Check("问答：每题限时 10 秒", Mathf.Approximately(quiz.timePerQuestion, 10f));
            Check("问答：60% 通过 → 至少答对 3 题", quiz.PassCorrectCount == 3);

            foreach (var q in quiz.questions)
            {
                bool ok = q != null && q.options != null && q.options.Length == 3
                          && q.correctIndex >= 0 && q.correctIndex < 3
                          && !string.IsNullOrEmpty(q.question);
                Check("问答：题目与选项完整（" + (q != null ? q.question : "null") + "）", ok);
            }

            int passed = 0;
            quiz.onPassed.AddListener(() => passed++);

            quiz.Open();
            Check("问答：面板打开且从第 1 题开始", quiz.IsOpen && quiz.QuestionIndex == 0);
            Check("问答：开局剩余约 10 秒", quiz.TimeLeft > 9.9f);

            // 第 1 题故意超时 → 判错，并自动进入第 2 题
            quiz.AdvanceTime(10.2f);
            Check("问答：超时判错（答对数仍为 0）", quiz.CorrectCount == 0);
            quiz.AdvanceTime(0.02f);
            Check("问答：超时后进入第 2 题", quiz.QuestionIndex == 1);

            // 第 2~4 题答对
            quiz.AnswerOption(quiz.questions[1].correctIndex);
            quiz.AdvanceTime(0.02f);
            quiz.AnswerOption(quiz.questions[2].correctIndex);
            quiz.AdvanceTime(0.02f);
            quiz.AnswerOption(quiz.questions[3].correctIndex);
            quiz.AdvanceTime(0.02f);
            Check("问答：已答对 3 题", quiz.CorrectCount == 3);

            // 第 5 题答错 → 最终 3/5 = 60%，应判通过
            int wrong = (quiz.questions[4].correctIndex + 1) % 3;
            quiz.AnswerOption(wrong);
            quiz.AdvanceTime(0.02f);

            Check("问答：3/5 达到 60% → 判通过", quiz.Passed && !quiz.Failed);
            Check("问答：通过回调触发一次", passed == 1);

            // 结果停留后自动收起
            quiz.AdvanceTime(1.3f);
            Check("问答：结果展示后自动收起面板", !quiz.IsOpen);

            UnityEngine.Object.DestroyImmediate(go);
        }

        // ------------------------------------------------------------
        // 3.7 问答全错 → 未通过；可重置后重新答题
        // ------------------------------------------------------------
        static void TestQuizFailThenRetry()
        {
            var go = new GameObject("TEST_QuizFail");
            var quiz = go.AddComponent<ExtinguisherQuizPanel>();
            InvokeAwake(quiz);
            quiz.advanceDelay = 0.01f;

            int failed = 0;
            quiz.onFailed.AddListener(() => failed++);

            quiz.Open();
            for (int i = 0; i < quiz.QuestionCount; i++)
            {
                int wrong = (quiz.questions[quiz.QuestionIndex].correctIndex + 1) % 3;
                quiz.AnswerOption(wrong);
                quiz.AdvanceTime(0.02f);
            }

            Check("问答：全错判未通过", quiz.Failed && !quiz.Passed);
            Check("问答：未通过回调触发一次", failed == 1);

            quiz.Close();
            quiz.ResetQuiz();
            Check("问答：重置后状态干净（可重新答题）",
                !quiz.Passed && !quiz.Failed && !quiz.IsOpen && quiz.CorrectCount == 0);

            quiz.Open();
            Check("问答：重开从第 1 题开始", quiz.IsOpen && quiz.QuestionIndex == 0);
            Check("问答：重开计时重置为 10 秒", quiz.TimeLeft > 9.9f);

            quiz.Close();
            UnityEngine.Object.DestroyImmediate(go);
        }

        // ------------------------------------------------------------
        // 3.8 阶段一完成面板：暂停倒计时 / 继续往下 / 立刻结算
        // ------------------------------------------------------------
        static void TestStageOnePanel()
        {
            var cdGo = new GameObject("TEST_Countdown");
            var cd = cdGo.AddComponent<DrillCountdown>();
            InvokeAwake(cd);
            cd.Begin();
            Check("倒计时：开始后运行且未暂停", cd.IsRunning && !cd.IsPaused);
            Check("倒计时：初始剩余上限 180 秒", cd.Remaining <= 180f + 0.01f && cd.Remaining > 179f);

            var panelGo = new GameObject("TEST_StagePanel");
            var panel = panelGo.AddComponent<StageOneCompletePanel>();
            InvokeAwake(panel);
            panel.countdown = cd;
            panel.startScreen = null;              // 结算界面单独验证，这里只查事件与倒计时

            int cont = 0, settle = 0;
            panel.onContinue.AddListener(() => cont++);
            panel.onSettlement.AddListener(() => settle++);

            panel.Show();
            Check("阶段面板：弹出且倒计时暂停", panel.IsOpen && cd.IsPaused);

            panel.Continue();
            Check("继续往下：面板关闭 + 倒计时恢复",
                !panel.IsOpen && !cd.IsPaused && cd.IsRunning && cont == 1);

            panel.Show();
            panel.Settlement();
            Check("立刻结算：面板关闭 + 倒计时复位",
                !panel.IsOpen && !cd.IsRunning && cd.Remaining > 179f && settle == 1);

            UnityEngine.Object.DestroyImmediate(panelGo);
            UnityEngine.Object.DestroyImmediate(cdGo);
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

        // ------------------------------------------------------------
        // 5. 真实场景装配校验：直接打开 FireSafetyOffice.unity 查引用
        // ------------------------------------------------------------
        static void TestSceneWiring()
        {
            if (!Application.isBatchMode)
            {
                Debug.Log("[TEST] 跳过场景装配校验（仅在 batchmode 下执行，避免打断当前编辑）");
                return;
            }
            ResetManagerInstance();

            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/FireSafetyOffice.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            var exts = UnityEngine.Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None);
            Check("场景：找到 2 台灭火器（实际 " + exts.Length + "）", exts.Length == 2);

            // 场景里不允许残留「Missing (Mono Script)」组件
            int missing = 0;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (c == null)
                        {
                            missing++;
                            Debug.LogError("[TEST] 发现缺失脚本组件 @ " + t.name);
                        }
                    }
                }
            }
            Check("场景：没有 Missing (Mono Script) 组件（实际 " + missing + "）", missing == 0);

            foreach (var e in exts)
            {
                string n = e.gameObject.name;
                Check("场景 " + n + "：grab 已引用", e.grab != null);
                Check("场景 " + n + "：pin 已引用（鼠标拔销依赖）", e.pin != null);
                Check("场景 " + n + "：handleLever 已引用", e.handleLever != null);
                Check("场景 " + n + "：sprayParticles 已引用", e.sprayParticles != null);
                Check("场景 " + n + "：quizPanel 已引用", e.quizPanel != null);

                if (e.quizPanel != null)
                {
                    Check("场景 " + n + "：问答 5 题", e.quizPanel.QuestionCount == 5);
                    Check("场景 " + n + "：问答每题 3 选项", e.quizPanel.OptionCount == 3);
                    Check("场景 " + n + "：问答每题 10 秒", Mathf.Approximately(e.quizPanel.timePerQuestion, 10f));
                    Check("场景 " + n + "：问答 60% 通过", Mathf.Approximately(e.quizPanel.passRatio, 0.6f));
                    Check("场景 " + n + "：问答面板距相机 1.2 米", Mathf.Approximately(e.quizPanel.distanceFromCamera, 1.2f));
                }
            }

            var stagePanel = UnityEngine.Object.FindObjectOfType<StageOneCompletePanel>();
            Check("场景：阶段一完成面板存在", stagePanel != null);
            if (stagePanel != null)
            {
                Check("场景：阶段面板已引用倒计时", stagePanel.countdown != null);
                Check("场景：阶段面板已引用开始界面", stagePanel.startScreen != null);
            }

            var input = UnityEngine.Object.FindObjectOfType<DesktopDrillInput>();
            Check("场景：DesktopDrillInput 存在", input != null);
            if (input != null)
            {
                Check("场景：桌面输入 extinguisher 已引用", input.extinguisher != null);
                Check("场景：桌面输入开始界面已引用", input.startScreen != null);
                Check("场景：桌面输入阶段面板已引用", input.stagePanel != null);
                Check("场景：桌面输入 handAnchor 已引用", input.handAnchor != null);
                Check("场景：桌面输入 cameraTransform 已引用", input.cameraTransform != null);
                Check("场景：手持距离改为 0.7 米（不再贴脸）", input.handOffset.z > 0.6f && input.handOffset.z < 0.8f);
                Check("场景：桌面输入覆盖 2 台灭火器",
                    input.extinguishers != null && input.extinguishers.Length == 2);
            }

            // ---- 在真实场景对象上跑一遍鼠标链路（只改内存，不保存场景）----
            Extinguisher e1 = null;
            foreach (var e in exts)
                if (e.gameObject.name == "Extinguisher01") { e1 = e; break; }

            if (e1 != null)
            {
                if (e1.pin != null) e1.pin.hideDelay = 0f;   // 编辑模式不能延迟 Destroy
                InvokeMethod(e1, "OnEnable"); // 订阅 onPickedUp（编辑模式不自动调用）
                InvokeAwake(e1.grab);         // 场景里的 Awake 也不会自动跑，缓存刚体等状态
                if (e1.pin != null) InvokeAwake(e1.pin);
                if (e1.handleLever != null) InvokeAwake(e1.handleLever);

                // 门槛：未通过常识问答时不能拔销/喷射
                e1.quizRequired = true;
                e1.MarkQuizFailed();
                Check("门槛：问答未通过时 CanPullPin 为假", !e1.CanPullPin);
                e1.PullPin();                       // 直接调用拔销，应被问答门控拒绝
                Check("门槛：问答未通过时拔销被拒绝", !e1.PinPulled);
                e1.MarkQuizPassed();
                Check("门槛：问答通过后 CanPullPin 为真", e1.CanPullPin);

                var mgrGo = new GameObject("TEST_SceneManager");
                var mgr = mgrGo.AddComponent<FireDrillManager>();
                InvokeAwake(mgr);
                InvokeMethod(mgr, "Start");

                var anchor = new GameObject("TEST_SceneHand").transform;

                e1.grab.GrabExternal(anchor, HandSide.Right);
                Check("场景链路：右键单击拿起（⑤）",
                    e1.grab.IsHeld && FireDrillManager.Instance.CurrentStep >= DrillStep.ExtinguisherGrabbed);

                Check("场景链路：左键单击拔销（⑥）",
                    e1.pin != null && e1.pin.PullByInput() && e1.PinPulled
                    && FireDrillManager.Instance.CurrentStep == DrillStep.PinPulled);

                Check("场景链路：左键双击压把（⑦）",
                    e1.handleLever != null && e1.handleLever.PressExternal()
                    && FireDrillManager.Instance.CurrentStep == DrillStep.HandlePressed);

                Check("场景链路：右键双击喷射（⑧）",
                    e1.TryStartSpray() && e1.IsSpraying
                    && FireDrillManager.Instance.CurrentStep == DrillStep.Spraying);

                e1.StopSprayExternal();
                e1.handleLever.ReleaseExternal();
                e1.grab.ReleaseExternal();
                Check("场景链路：P 放下后复位",
                    !e1.grab.IsHeld && !e1.IsSpraying && !e1.handleLever.IsSqueezed);

                UnityEngine.Object.DestroyImmediate(anchor.gameObject);
                ResetManagerInstance();
                UnityEngine.Object.DestroyImmediate(mgrGo);
            }
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
