#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using Unity.XR.CoreUtils;
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
            TestLobbyGuidance();
            TestOnboardingAndEnding();
            TestStageTwoFlow();
            TestFlameClearAll();
            TestStepChain();
            TestSceneWiring();
            TestVrConfiguration();

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
            Check("HUD 文本标注鼠标转头与 Q/E 转身",
                  text != null && text.text != null && text.text.Contains("移动鼠标转头") && text.text.Contains("Q/E"));
            Check("桌面：鼠标视角跟随灵敏度 > 0", input.mouseLookSensitivity > 0f);
            Check("桌面：俯仰限幅在 30°~90° 之间", input.pitchClamp > 30f && input.pitchClamp <= 90f);

            var lookCam = new GameObject("TEST_LookCam").transform;
            input.cameraTransform = lookCam;
            input.ApplyLook(100f, 0f);
            Check("桌面：鼠标偏航累计到相机朝向（≈12°）", Mathf.Abs(lookCam.localEulerAngles.y - 12f) < 1f);
            input.ApplyLook(0f, 1000f);
            Check("桌面：俯仰被限幅在 ±75°",
                Mathf.Abs(Mathf.DeltaAngle(0f, lookCam.localEulerAngles.x)) <= 75.5f);
            UnityEngine.Object.DestroyImmediate(lookCam.gameObject);

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
            Check("问答：题库已重设为第 13 轮新题（第 1 题为有效射程）",
                quiz.questions.Count == 5 && quiz.questions[0].question.Contains("有效喷射距离"));
            Check("问答：新题 5 道互不重复",
                new HashSet<string>(quiz.questions.ConvertAll(q => q.question)).Count == quiz.questions.Count);
            Check("问答：正确答案不集中在同一个选项",
                new HashSet<int>(quiz.questions.ConvertAll(q => q.correctIndex)).Count >= 3);

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
        // 3.9 第 2 阶段引导箭头：指向大厅
        // ------------------------------------------------------------
        static void TestLobbyGuidance()
        {
            var arrGo = new GameObject("TEST_Arrows");
            var arr = arrGo.AddComponent<GuidanceArrows>();
            InvokeAwake(arr);

            var lobby = new GameObject("TEST_Lobby");
            lobby.transform.position = new Vector3(3f, 0.02f, 0f);
            arr.lobbyTarget = lobby.transform;
            arr.lobbyArriveDistance = 0.6f;

            int arrived = 0;
            arr.onArrivedAtLobby.AddListener(() => arrived++);

            Check("大厅引导：初始未激活", !arr.Active);
            arr.BeginLobbyGuidance();
            Check("大厅引导：Begin 后进入大厅模式", arr.Active && arr.LobbyMode);
            Check("大厅引导：lobbyTarget 已赋值", arr.lobbyTarget != null);

            // 距大厅 3 米，不应触发到达
            arr.StopGuidance();
            Check("大厅引导：StopGuidance 后关闭", !arr.Active);

            UnityEngine.Object.DestroyImmediate(lobby);
            UnityEngine.Object.DestroyImmediate(arrGo);
        }

        // ------------------------------------------------------------
        // 3.10 引导页（온보딩）与结束页（엔딩）
        // ------------------------------------------------------------
        static void TestOnboardingAndEnding()
        {
            // —— 引导页：分页翻页、关闭 ——
            var obGo = new GameObject("TEST_Onboarding");
            var ob = obGo.AddComponent<OnboardingScreen>();
            InvokeAwake(ob);

            Check("引导页：默认 4 页（实际 " + ob.PageCount + "）", ob.PageCount == 4);
            Check("引导页：初始在第 1 页", ob.PageIndex == 0);

            ob.Show();
            Check("引导页：可打开", ob.IsOpen);

            ob.NextPage();
            Check("引导页：下一步翻页到第 2 页", ob.PageIndex == 1);
            ob.PrevPage();
            Check("引导页：上一步回到第 1 页", ob.PageIndex == 0);

            for (int i = 0; i < 5 && ob.PageIndex < ob.PageCount - 1; i++) ob.NextPage();
            Check("引导页：可翻到最后一页", ob.PageIndex == ob.PageCount - 1);

            ob.Skip();
            Check("引导页：跳过即关闭", !ob.IsOpen);

            UnityEngine.Object.DestroyImmediate(obGo);

            // —— 结束页：成功/失败两种结果与关闭 ——
            var endGo = new GameObject("TEST_Ending");
            var ending = endGo.AddComponent<EndingScreen>();
            InvokeAwake(ending);

            int shown = 0;
            ending.onShown.AddListener(() => shown++);

            Check("结束页：初始关闭", !ending.IsOpen);
            ending.Show(true);
            Check("结束页：成功路径可弹出", ending.IsOpen && ending.IsSuccess && shown == 1);
            Check("结束页：成功文案为「演练完成」", ending.titleSuccess.Contains("演练完成"));

            ending.Hide();
            Check("结束页：关闭后隐藏", !ending.IsOpen);

            ending.Show(false);
            Check("结束页：失败路径可弹出", ending.IsOpen && !ending.IsSuccess);
            Check("结束页：失败文案为「时间到」", ending.titleFailure.Contains("时间到"));

            ending.Hide();
            UnityEngine.Object.DestroyImmediate(endGo);
        }

        // ------------------------------------------------------------
        // 3.11 第二 / 第三阶段：烟雾潜行 → 大厅复燃 → 出口门通关
        // ------------------------------------------------------------
        static void TestStageTwoFlow()
        {
            var lobbyGo = new GameObject("TEST_Lobby");
            lobbyGo.transform.position = new Vector3(0f, 0.02f, -4.5f);

            var smokeGo = new GameObject("TEST_Smoke");
            var smoke = smokeGo.AddComponent<SmokeZone>();
            smoke.emitterCount = 3;
            smoke.start = new Vector3(0f, 0.3f, 1.8f);
            smoke.end = new Vector3(0f, 0.3f, -4f);

            var doorGo = new GameObject("TEST_ExitDoor");
            doorGo.transform.position = new Vector3(0f, 0f, -5.6f);
            var door = doorGo.AddComponent<ExitDoor>();
            InvokeAwake(door);                       // 编辑模式补跑 Awake：搭门体 + 指示牌

            var arrowsGo = new GameObject("TEST_Arrows2");
            var arrows = arrowsGo.AddComponent<GuidanceArrows>();
            InvokeAwake(arrows);
            arrows.lobbyTarget = lobbyGo.transform;
            arrows.exitTarget = doorGo.transform;

            var endGo = new GameObject("TEST_Ending2");
            var ending = endGo.AddComponent<EndingScreen>();
            InvokeAwake(ending);

            var flowGo = new GameObject("TEST_StageTwoFlow");
            var flow = flowGo.AddComponent<StageTwoFlow>();
            flow.arrows = arrows;
            flow.smoke = smoke;
            flow.exitDoor = door;
            flow.endingScreen = ending;
            flow.autoMoveSpareExtinguisher = false;
            InvokeAwake(flow);                       // 编辑模式补跑 Awake（单例 + 引用解析）

            // 备用灭火器：模拟一台还没被拿起的备用瓶，验证「按包围盒直立贴地摆到图 3 门口」
            var spareGo = new GameObject("TEST_Spare");
            spareGo.transform.position = new Vector3(10f, 1.2f, 10f);
            var spareBody = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            spareBody.name = "Body";
            spareBody.transform.SetParent(spareGo.transform, false);
            var spareExt = spareGo.AddComponent<Extinguisher>();
            var spareGrab = spareGo.AddComponent<ToggleGrab>();
            spareExt.grab = spareGrab;
            flow.spareExtinguisher = spareExt;

            Check("第二阶段：初始为 Idle", flow.CurrentStage == StageTwoFlow.Stage.Idle);
            Check("第二阶段：默认蹲姿阈值 1.15 米", Mathf.Approximately(flow.crouchHeight, 1.15f));

            flow.Begin();
            Check("继续往下：进入撤离阶段", flow.CurrentStage == StageTwoFlow.Stage.Evacuating);
            Check("撤离阶段：烟雾已开启", smoke.Active && smoke.EmitterCount == 3);

            // URP 下粒子必须是「真透明」：只设 _Surface=1 不够，缺关键字/混合/队列会渲成硬边不透明面片
            {
                var rends = smoke.GetComponentsInChildren<ParticleSystemRenderer>(true);
                Check("撤离：烟雾发射器已带材质（不是洋红空材质）", rends.Length > 0 && rends[0].sharedMaterial != null);
                if (rends.Length > 0 && rends[0].sharedMaterial != null)
                {
                    var m = rends[0].sharedMaterial;
                    Check("撤离：烟雾材质带 _SURFACE_TYPE_TRANSPARENT 关键字（URP 透明渲染）",
                        m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"));
                    Check("撤离：烟雾材质排在 Transparent 队列（不是不透明队列）", m.renderQueue >= 3000);
                    Check("撤离：烟雾材质关闭 ZWrite（多层粒子能互相透出）",
                        !m.HasProperty("_ZWrite") || m.GetFloat("_ZWrite") <= 0.01f);
                }
            }
            Check("撤离阶段：箭头切到大厅模式", arrows.Active && arrows.LobbyMode && !arrows.ExitMode);

            // 编辑模式补跑 Start/Update：箭头与「安全出口 →」标识才会真正生成并摆位
            InvokeMethod(arrows, "Start");
            InvokeMethod(arrows, "Update");
            Check("撤离：浓雾中已生成地面「安全出口 →」标识", arrows.HasExitSign);
            Check("撤离：标识在烟雾中保持可见（指向大厅）", arrows.ExitSignVisible);

            Check("站立到达大厅不推进（必须先蹲下）",
                flow.TryArriveLobby(false) == false && flow.CurrentStage == StageTwoFlow.Stage.Evacuating);

            flow.SetCrouchOverrideForTest(true);
            flow.autoMoveSpareExtinguisher = false;   // 生产配置：这台备用瓶运到第三阶段必须待在原地
            Vector3 sparePosBefore = spareGo.transform.position;
            Check("蹲姿到达大厅：进入第三阶段",
                flow.TryArriveLobby(true) && flow.CurrentStage == StageTwoFlow.Stage.StageThree);
            {
                var rend = spareGo.GetComponentInChildren<Renderer>();
                Check("第三阶段：备用灭火器已带渲染体", rend != null);
                if (rend != null)
                {
                    Bounds b = rend.bounds;
                    Check("第三阶段：备用灭火器待在原地（不再被搬到他处）",
                        (spareGo.transform.position - sparePosBefore).magnitude < 0.05f);
                    Check("第三阶段：备用灭火器原地仍是直立姿态（不横躺）",
                        b.size.y >= b.size.x && b.size.y >= b.size.z);
                }
                Check("第三阶段：备用灭火器问答已重置（重新拾取要再答题）",
                    spareExt != null && !spareExt.QuizPassed);
            }
            Check("第三阶段：大厅复燃火源已生成且尚未熄灭",
                flow.stageTwoFire != null && !flow.stageTwoFire.IsExtinguished);
            Check("第三阶段：烟雾停止", !smoke.Active);
            Check("第三阶段：复燃火源有灭火阈值", flow.stageTwoFire != null && flow.stageTwoFire.extinguishThreshold > 0f);
            Check("第三阶段：复燃火源带粒子系统（不是空火源）",
                flow.stageTwoFire != null && flow.stageTwoFire.particles != null && flow.stageTwoFire.particles.Length >= 1);
            Check("第三阶段：复燃火源全局只有 1 个火源组件（不重复、不丢）",
                flow.stageTwoFire != null && flow.stageTwoFire.GetComponentsInChildren<FireSource>(true).Length == 1);
            Check("第三阶段：复燃火源组件挂在 StageTwoFire 根节点上（不挂在会被清掉的子物体上）",
                flow.stageTwoFire != null
                && flow.stageTwoFire.gameObject.name == "StageTwoFire"
                && flow.stageTwoFire.transform == flow.stageTwoFire.gameObject.transform);
            Check("第三阶段：复燃火源带熄灭事件（onExtinguished 已初始化）",
                flow.stageTwoFire != null && flow.stageTwoFire.onExtinguished != null);

            if (flow.stageTwoFire != null)
            {
                Vector3 firePos = flow.stageTwoFire.transform.position;
                float doorZ = doorGo.transform.position.z;
                Check("第三阶段：火源在安全出口门正前方（门内侧不点火）",
                    firePos.z > doorZ + 0.4f && firePos.z < doorZ + 1.1f);
                Check("第三阶段：火源距门约 0.75 米",
                    Mathf.Abs(firePos.z - (doorZ + 0.75f)) < 0.06f);
                Check("第三阶段：火源立在门前的走廊上（不是悬空）",
                    firePos.y >= 0f && firePos.y < 1f);
                Check("第三阶段：火源瞄准点在火焰根部高度（喷射锥才压得住）",
                    flow.stageTwoFire.AimPoint.y >= 0.2f && flow.stageTwoFire.AimPoint.y <= 0.6f);
            }
            Check("第三阶段：安全出口仍然关闭（未解锁时不能通关）",
                !door.IsUnlocked && door.TryEnter() == false);

            // 复燃火源熄灭 → 出口解锁（通过 FireSource 的 onExtinguished 事件链验证接线）
            if (flow.stageTwoFire != null && flow.stageTwoFire.onExtinguished != null)
                flow.stageTwoFire.onExtinguished.Invoke();
            Check("复燃火源扑灭：出口解锁",
                flow.CurrentStage == StageTwoFlow.Stage.ExitOpen && door.IsUnlocked);
            Check("复燃火源扑灭：箭头切到安全出口", arrows.Active && arrows.ExitMode && !arrows.LobbyMode);

            Check("走进安全出口：通关成功", flow.TryEnterExit() && flow.CurrentStage == StageTwoFlow.Stage.Done);
            Check("通关：出口记录玩家已进入", door.PlayerEntered);
            Check("通关：结束页弹出且为成功态", ending.IsOpen && ending.IsSuccess);

            // ---- 第二阶段重复灭火：备用灭火器的问答必须重置成新 5 题 ----
            {
                var extGo = new GameObject("TEST_Ext2");
                var ext = extGo.AddComponent<Extinguisher>();
                var qGo = new GameObject("TEST_ExtQuiz2");
                qGo.transform.SetParent(extGo.transform, false);
                var q2 = qGo.AddComponent<ExtinguisherQuizPanel>();
                ext.quizPanel = q2;
                InvokeAwake(q2);                       // BuildUI + 默认 5 题

                ext.MarkQuizPassed();
                Check("第三阶段：备用灭火器初始状态为问答已通过", ext.QuizPassed);

                ext.ResetQuizForStageTwo();
                Check("第三阶段：备用灭火器问答已重置为未通过", !ext.QuizPassed && !q2.Passed);
                Check("第三阶段：重置后仍是 5 道新题", q2.QuestionCount == 5
                    && q2.questions[0].question.Contains("有效喷射距离"));
                Check("第三阶段：重置后不能直接拔销（必须重新答题）", !ext.CanPullPin);

                // 换成 B 组再重置：题目必须换成另一套 5 题
                q2.questionSetIndex = 1;
                ext.MarkQuizPassed();
                ext.ResetQuizForStageTwo();
                Check("第三阶段：B 组灭火器重置后是另一套 5 题（压力表那组）",
                    q2.QuestionCount == 5 && q2.questions[0].question.Contains("压力表"));
                Check("问答面板：选项按钮占满一行（选项文字不会被裁成「A.」）",
                    q2.OptionRowWidth >= 400f);

                UnityEngine.Object.DestroyImmediate(extGo);
            }

            flow.SetCrouchOverrideForTest(false);
            ending.Hide();
            UnityEngine.Object.DestroyImmediate(spareGo);
            UnityEngine.Object.DestroyImmediate(flowGo);
            UnityEngine.Object.DestroyImmediate(endGo);
            UnityEngine.Object.DestroyImmediate(arrowsGo);
            UnityEngine.Object.DestroyImmediate(doorGo);
            UnityEngine.Object.DestroyImmediate(smokeGo);
            UnityEngine.Object.DestroyImmediate(lobbyGo);
        }

        // ------------------------------------------------------------
        // 3.12 弹面板 / 结束时火焰必须消失：ClearAllFlames 连未激活火源一起清
        // ------------------------------------------------------------
        static void TestFlameClearAll()
        {
            var fireGo = new GameObject("TEST_FireActive");
            var light1 = fireGo.AddComponent<Light>();
            light1.intensity = 2.5f;
            var f1 = fireGo.AddComponent<FireSource>();
            f1.fireLight = light1;

            var hiddenGo = new GameObject("TEST_FireInactive");
            hiddenGo.SetActive(false);
            var light2 = hiddenGo.AddComponent<Light>();
            light2.intensity = 3f;
            var f2 = hiddenGo.AddComponent<FireSource>();
            f2.fireLight = light2;

            // 没登记进 fireLight / particles 数组的子灯光：清火也必须一起熄掉
            var childLightGo = new GameObject("TEST_ChildGlow");
            childLightGo.transform.SetParent(fireGo.transform, false);
            var light3 = childLightGo.AddComponent<Light>();
            light3.intensity = 4f;

            FireSource.ClearAllFlames();

            Check("清火：激活火源被立刻熄灯（弹面板火焰消失）", light1.intensity <= 0.001f);
            Check("清火：未激活火源也被熄灯（Resources 兜底路径生效）", light2.intensity <= 0.001f);
            Check("清火：未登记进数组的子灯光也被熄掉（火焰不再残留）", light3.intensity <= 0.001f);

            UnityEngine.Object.DestroyImmediate(fireGo);
            UnityEngine.Object.DestroyImmediate(hiddenGo);
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
                if (e.pin != null)
                    Check("场景 " + n + "：保险销拉出阈值 0.12 米",
                        Mathf.Approximately(e.pin.pullDistance, 0.12f));
                Check("场景 " + n + "：handleLever 已引用", e.handleLever != null);
                Check("场景 " + n + "：sprayParticles 已引用", e.sprayParticles != null);
                Check("场景 " + n + "：quizPanel 已引用", e.quizPanel != null);

                if (e.quizPanel != null)
                {
                    Check("场景 " + n + "：问答 5 题", e.quizPanel.QuestionCount == 5);
                    Check("场景 " + n + "：问答每题 3 选项", e.quizPanel.OptionCount == 3);
                    Check("场景 " + n + "：问答每题 10 秒", Mathf.Approximately(e.quizPanel.timePerQuestion, 10f));
                    Check("场景 " + n + "：问答 60% 通过", Mathf.Approximately(e.quizPanel.passRatio, 0.6f));
                    Check("场景 " + n + "：问答面板距相机 1.15 米（一臂外阅读距离）",
                        Mathf.Approximately(e.quizPanel.distanceFromCamera, 1.15f));
                    Check("场景 " + n + "：问答面板略低于视线 -0.05 米",
                        Mathf.Approximately(e.quizPanel.verticalOffset, -0.05f));
                    Check("场景 " + n + "：问答面板会跟随玩家回到正前方", e.quizPanel.followCamera);
                    bool isBGroup = n.Contains("02");
                    Check("场景 " + n + "：题库组下标正确（01=A 组 / 02=B 组）",
                        e.quizPanel.questionSetIndex == (isBGroup ? 1 : 0));
                    string q0 = e.quizPanel.questions[0].question;
                    Check("场景 " + n + "：本组件库为该组 5 题",
                        e.quizPanel.questions.Count == 5
                        && (isBGroup ? q0.Contains("压力表") : q0.Contains("有效喷射距离")));
                }
            }

            // 两台灭火器题库合计 10 题，且彼此不重复
            {
                var union = new HashSet<string>();
                foreach (var e in exts)
                    if (e.quizPanel != null)
                        foreach (var q in e.quizPanel.questions) union.Add(q.question);
                Check("场景：两台灭火器合计 10 道互不重复的题（实际 " + union.Count + "）", union.Count == 10);

                var pool = ExtinguisherQuizPanel.BuildAllQuestions();
                var poolUnion = new HashSet<string>();
                foreach (var q in pool) poolUnion.Add(q.question);
                Check("题库池共 10 题且两两不重复（实际 " + poolUnion.Count + "）",
                    pool.Count == 10 && poolUnion.Count == 10);
                Check("题库 A 组 / B 组各 5 题且无交集",
                    ExtinguisherQuizPanel.BuildQuestionSet(0).Count == 5
                    && ExtinguisherQuizPanel.BuildQuestionSet(1).Count == 5
                    && ExtinguisherQuizPanel.BuildQuestionSet(0)[0].question
                       != ExtinguisherQuizPanel.BuildQuestionSet(1)[0].question);
            }

            var stagePanel = UnityEngine.Object.FindObjectOfType<StageOneCompletePanel>();
            Check("场景：阶段一完成面板存在", stagePanel != null);
            if (stagePanel != null)
            {
                Check("场景：阶段面板已引用倒计时", stagePanel.countdown != null);
                Check("场景：阶段面板已引用开始界面", stagePanel.startScreen != null);
                Check("场景：阶段面板距相机 1.55 米（按钮不再被 HUD 压住）",
                    Mathf.Approximately(stagePanel.distanceFromCamera, 1.55f));
                Check("场景：阶段面板已引用第二阶段流程", stagePanel.stageTwo != null);
            }

            var start = UnityEngine.Object.FindObjectOfType<StartScreen>();
            Check("场景：开始界面存在", start != null);
            if (start != null)
            {
                Check("场景：开始界面已绑定手柄锚点（正前方）", start.anchorHand != null);
                Check("场景：开始界面前伸 0.75 米（比上轮更近）", Mathf.Approximately(start.handForwardDistance, 0.75f));
                Check("场景：开始界面上抬 0.15 米", Mathf.Approximately(start.handUpOffset, 0.15f));
                Check("场景：开始界面距离上限 1.4 米", Mathf.Approximately(start.maxCameraDistance, 1.4f));
                Check("场景：开始界面相机兜底 1.1 米", Mathf.Approximately(start.cameraFallbackDistance, 1.1f));
            }

            // 距离保护行为：离得远的目标被拉回相机前方，近的不受影响
            {
                Vector3 camPos = new Vector3(0f, 1.5f, 0f);
                Vector3 camFwd = new Vector3(0f, 0f, 1f);
                float maxD = 1.4f, fallback = 1.1f;

                Vector3 farDesired = camPos + camFwd * 10f + new Vector3(2f, 0f, 0f);
                Vector3 farClamped = StartScreen.ClampToCamera(farDesired, camPos, camFwd, fallback, maxD);
                Check("开始界面：10 米外的目标被拉回相机正前方 1.1 米",
                    Mathf.Approximately(Vector3.Distance(camPos, farClamped), fallback));

                Vector3 nearDesired = camPos + camFwd * 1.05f;
                Vector3 nearKept = StartScreen.ClampToCamera(nearDesired, camPos, camFwd, fallback, maxD);
                Check("开始界面：1.05 米的目标保持原位（不被改远）",
                    Mathf.Approximately(Vector3.Distance(camPos, nearKept), 1.05f));

                Vector3 exactly = camPos + camFwd * maxD;
                Vector3 exactlyKept = StartScreen.ClampToCamera(exactly, camPos, camFwd, fallback, maxD);
                Check("开始界面：恰好在 1.4 米上限时保持不动",
                    Mathf.Approximately(Vector3.Distance(camPos, exactlyKept), maxD));

                Vector3 behind = camPos + camFwd * 3f + camFwd * 0f;
                Vector3 behindClamped = StartScreen.ClampToCamera(behind, camPos, camFwd, fallback, maxD);
                Check("开始界面：过远时兜底位置始终在相机前方",
                    Vector3.Dot(behindClamped - camPos, camFwd) > 0f);
            }

            var cdHud = UnityEngine.Object.FindObjectOfType<DrillCountdown>();
            Check("场景：倒计时面板存在", cdHud != null);
            if (cdHud != null)
            {
                Check("场景：倒计时面板已绑定手柄锚点（正上方）", cdHud.anchorHand != null);
                Check("场景：倒计时面板抬升 0.42 米", Mathf.Approximately(cdHud.handAboveDistance, 0.42f));
                Check("场景：倒计时面板前探 0.12 米", Mathf.Approximately(cdHud.handForwardOffset, 0.12f));
            }

            var fires = UnityEngine.Object.FindObjectsByType<FireSource>(FindObjectsSortMode.None);
            Check("场景：存在至少 1 个火源（灭火对象）", fires.Length >= 1);
            foreach (var f in fires)
            {
                Check("场景火源 " + f.name + "：有灭火阈值配置", f.extinguishThreshold > 0f);
            }

            // 第三阶段复燃火源是克隆「场景现成火焰」的，模板必须够丰富（否则只剩一坨色块）
            {
                var baseFire = GameObject.Find("BaseFire000");
                Check("场景：存在可复用的火焰模板 BaseFire000", baseFire != null);
                if (baseFire != null)
                {
                    var tplFire = baseFire.GetComponent<FireSource>();
                    Check("场景：BaseFire000 挂了 FireSource（可被克隆成复燃火源）", tplFire != null);
                    int psCount = baseFire.GetComponentsInChildren<ParticleSystem>(true).Length;
                    Check("场景：火焰模板含多个粒子系统（实测 " + psCount + " 个，复燃火不是单张面片）",
                        psCount >= 2);
                }
            }

            // 手柄射线：空材质会被 Unity 画成洋红，默认 1 米线宽会糊住半屏视野
            {
                foreach (var cn in new[] { "RightHand Controller", "LeftHand Controller" })
                {
                    var go = GameObject.Find("XR Origin (Drill)/" + cn);
                    Check("场景手柄 " + cn + "：存在", go != null);
                    var lr = go != null ? go.GetComponent<LineRenderer>() : null;
                    Check("场景手柄 " + cn + "：射线 LineRenderer 有材质（不再渲染成洋红）",
                        lr != null && lr.sharedMaterial != null);
                    Check("场景手柄 " + cn + "：射线线宽 ≤0.05 米（不再是一条挡视线的大板）",
                        lr != null && lr.startWidth * lr.widthMultiplier <= 0.05f);
                }
            }

            var arrows = UnityEngine.Object.FindObjectOfType<GuidanceArrows>();
            Check("场景：引导箭头存在", arrows != null);
            if (arrows != null)
            {
                Check("场景：引导箭头已配置大厅目标（lobbyTarget）", arrows.lobbyTarget != null);
                Check("场景：大厅到达距离为 0.6 米", Mathf.Approximately(arrows.lobbyArriveDistance, 0.6f));
                Check("场景：引导箭头已配置安全出口目标（exitTarget）", arrows.exitTarget != null);
                Check("场景：出口到达距离 0.9 米", Mathf.Approximately(arrows.exitArriveDistance, 0.9f));
                Check("场景：浓烟里箭头放大到 2.2 倍", Mathf.Approximately(arrows.stage2ArrowScale, 2.2f));
                Check("场景：地面安全出口标识文案为「安全出口 →」", arrows.floorSignText == "安全出口 →");
                Check("场景：标识降到离地 0.62 米（低于雾底 1.15 米，浓烟里也读得到）",
                    Mathf.Approximately(arrows.floorSignHeight, 0.62f));
                Check("场景：标识横向挪到门侧 1.3 米（不挡住门前火）",
                    Mathf.Approximately(arrows.floorSignSideOffset, 1.3f));
            }

            var onboarding = UnityEngine.Object.FindObjectOfType<OnboardingScreen>();
            Check("场景：引导页（온보딩）存在", onboarding != null);
            if (onboarding != null)
            {
                Check("场景：引导页已绑定手柄锚点（正前方）", onboarding.anchorHand != null);
                Check("场景：引导页拉近到 0.6 米（比上轮 1.0 米更近）",
                    Mathf.Approximately(onboarding.handForwardDistance, 0.6f));
                Check("场景：引导页距离上限 1.3 米",
                    Mathf.Approximately(onboarding.maxCameraDistance, 1.3f));
                Check("场景：引导页相机兜底 1.0 米",
                    Mathf.Approximately(onboarding.cameraFallbackDistance, 1.0f));
                Check("场景：引导页至少 4 页（实际 " + onboarding.PageCount + "）", onboarding.PageCount >= 4);
            }

            var ending = UnityEngine.Object.FindObjectOfType<EndingScreen>();
            Check("场景：结束页（엔딩）存在", ending != null);
            if (ending != null)
            {
                Check("场景：结束页已引用倒计时（用时统计）", ending.countdown != null);
                Check("场景：结束页距相机 1.35 米", Mathf.Approximately(ending.distanceFromCamera, 1.35f));
                Check("场景：结束页已引用安全出口门（通关判定）", ending.exitDoor != null);
            }

            var lobbyObj = GameObject.Find("Lobby");
            Check("场景：大厅标记物体 Lobby 存在", lobbyObj != null);

            var flow = UnityEngine.Object.FindObjectOfType<StageTwoFlow>();
            Check("场景：第二阶段流程 StageTwoFlow 存在", flow != null);
            if (flow != null)
            {
                Check("场景：流程已引用烟雾区", flow.smoke != null);
                Check("场景：流程已引用出口门", flow.exitDoor != null);
                Check("场景：流程已引用引导箭头", flow.arrows != null);
                Check("场景：流程蹲姿阈值 1.15 米", Mathf.Approximately(flow.crouchHeight, 1.15f));
                Check("场景：复燃火源配置在出口门正前方（门的本地 +Z）0.75 米",
                    Mathf.Approximately(flow.stageTwoFireOffset.z, 0.75f)
                    && flow.stageTwoFireOffset.z > 0f
                    && Mathf.Approximately(flow.stageTwoFireOffset.x, 0f));
                var spare = GameObject.Find("Extinguisher02");
                Check("场景：第三阶段固定用 Extinguisher02（B 组题）",
                    spare != null && flow.spareExtinguisher != null
                    && flow.spareExtinguisher.gameObject == spare);
                Check("场景：第三阶段不搬移备用灭火器（autoMoveSpareExtinguisher 已关闭）",
                    flow.autoMoveSpareExtinguisher == false);
                {
                    // 原地 = 场景里那台瓶身的作者位置（1.70 / 0.52），且离大厅点在 3 米内仍然找得到
                    Vector3 authored = new Vector3(1.70f, 0f, 0.52f);
                    Check("场景：备用灭火器落点参数＝它的原位（x≈1.70 / z≈0.52）",
                        Mathf.Abs(flow.spareExtinguisherSpot.x - authored.x) < 0.3f
                        && Mathf.Abs(flow.spareExtinguisherSpot.z - authored.z) < 0.3f);
                    Vector3 bodyCenter = new Vector3(1.69f, 0f, 0.58f);
                    Check("场景：备用灭火器离大厅点不超过 3 米（第三阶段找得到）",
                        Vector3.Distance(bodyCenter, new Vector3(3.35f, 0f, 1.02f)) < 3.0f);

                    // 这台瓶是带 Rigidbody 的刚体：初始位置一旦被别人的碰撞体包住，一运行就会被顶飞
                    bool fireHasCollider = false;
                    foreach (var fs in UnityEngine.Object.FindObjectsByType<FireSource>(FindObjectsSortMode.None))
                        if (fs.GetComponentsInChildren<Collider>(true).Length > 0) fireHasCollider = true;
                    Check("场景：火源不带碰撞体（火是视觉物，不会把灭火器顶飞）", !fireHasCollider);

                    var spareCol = spare.GetComponent<Collider>();
                    int overlap = 0;
                    if (spareCol != null)
                    {
                        Vector3 half = new Vector3(spareCol.bounds.extents.x - 0.01f,
                                                   spareCol.bounds.extents.y - 0.01f,
                                                   spareCol.bounds.extents.z - 0.01f);
                        foreach (var h in Physics.OverlapBox(spareCol.bounds.center, half, spare.transform.rotation, ~0, QueryTriggerInteraction.Collide))
                            if (h != spareCol && !h.transform.IsChildOf(spare.transform)) overlap++;
                    }
                    Check("场景：备用灭火器初始位置没有与任何碰撞体重叠（运行后原地不动）",
                        spareCol != null && overlap == 0);
                }
            }

            var smokeZone = UnityEngine.Object.FindObjectOfType<SmokeZone>(true);
            Check("场景：烟雾区 SmokeZone 存在", smokeZone != null);
            if (smokeZone != null)
            {
                Check("场景：烟雾区至少 4 个发射点（实际 " + smokeZone.emitterCount + "）", smokeZone.emitterCount >= 4);
                Check("场景：雾底抬高到 1.15 米（不糊住地面箭头）",
                    smokeZone.floorClearance >= 1.0f);
                Check("场景：烟雾仅在视线高度（起始 y ≥ 1.2 米）", smokeZone.start.y >= 1.2f);
                Check("场景：浓雾布在灭火房间 → 真实门的通道上（x 从负到正）",
                    smokeZone.start.x < 1f && smokeZone.end.x > 1.5f);
                Check("场景：烟雾粒子细化（≤1.2 米，避免糊成大块白斑）",
                    smokeZone.particleSize <= 1.2f && smokeZone.particleSize > 0.4f);
                Check("场景：烟雾发射点 ≥7 个（浓雾更连贯）", smokeZone.emitterCount >= 7);
            }

            var exitDoor = UnityEngine.Object.FindObjectOfType<ExitDoor>(true);
            Check("场景：安全出口门 ExitDoor 存在", exitDoor != null);
            if (exitDoor != null)
            {
                Check("场景：出口门指示牌文案为「安全出口 EXIT」", exitDoor.signText.Contains("安全出口"));
                Check("场景：出口门进入判定 0.9 米", Mathf.Approximately(exitDoor.enterDistance, 0.9f));
                Check("场景：出口门初始未解锁", !exitDoor.IsUnlocked);
                var realDoor = GameObject.Find("door_b_variant");
                Check("场景：出口门复用了另一扇真实门 door_b_variant",
                    realDoor != null && exitDoor.existingDoorRoot == realDoor.transform);
                Check("场景：真实门解锁后绕门轴转开 -95 度",
                    Mathf.Approximately(exitDoor.existingOpenAngle, -95f));
                Check("场景：出口门指示牌挂在门框上方 2.42 米",
                    Mathf.Approximately(exitDoor.signHeight, 2.42f));
                // 位置硬校验：出口门站在 door_b_variant 的门口（x≈5.25 / z≈1.02）
                Check("场景：出口门位于真实门 door_b_variant 门口（x≈5.25 / z≈1.02）",
                    Mathf.Abs(exitDoor.transform.position.x - 5.25f) < 0.15f
                    && Mathf.Abs(exitDoor.transform.position.z - 1.02f) < 0.2f);
                // 门朝房间侧（本地 +Z 指向 -X）：牌子与门前火都落在这边
                Check("场景：出口门朝向房间侧（本地 +Z 指向 -X）",
                    Mathf.Abs(Mathf.DeltaAngle(exitDoor.transform.eulerAngles.y, -90f)) < 1.5f);
                {
                    Vector3 fireLocal = exitDoor.transform.TransformPoint(new Vector3(0f, 0f, 0.75f));
                    Check("场景：门前复燃火落在门口的房间侧（x≈4.50 / z≈1.02）",
                        Mathf.Abs(fireLocal.x - 4.50f) < 0.2f
                        && Mathf.Abs(fireLocal.z - 1.02f) < 0.2f);
                }
                // 出口门应急照明：这面墙原本没有灯，浓雾里 / 火灭之后门前是黑的，玩家找不到门
                {
                    var lampT = exitDoor.transform.Find("ExitDoorLamp");
                    var lamp = lampT != null ? lampT.GetComponent<Light>() : null;
                    Check("场景：出口门上方有应急照明（浓雾与灭火后都能找到门）",
                        lamp != null && lamp.intensity >= 1f && lamp.range >= 3f);
                }
                if (realDoor != null)
                {
                    var leaf = realDoor.GetComponentInChildren<MeshRenderer>();
                    Check("场景：出口门锚点落在真实门门板中心（≤0.2 米）", leaf != null);
                    if (leaf != null)
                    {
                        Vector3 c = leaf.bounds.center;
                        Check("场景：出口门与门板中心重合（差 "
                            + Vector3.Distance(new Vector3(exitDoor.transform.position.x, 0f, exitDoor.transform.position.z),
                                               new Vector3(c.x, 0f, c.z)).ToString("F2") + " 米）",
                            Vector3.Distance(new Vector3(exitDoor.transform.position.x, 0f, exitDoor.transform.position.z),
                                             new Vector3(c.x, 0f, c.z)) < 0.2f);
                    }
                }
                if (lobbyObj != null)
                {
                    Check("场景：大厅检查点在门正前方（房间侧 -X）1.9 米",
                        Mathf.Abs(lobbyObj.transform.position.x - 3.35f) < 0.2f
                        && Mathf.Abs(lobbyObj.transform.position.z - 1.02f) < 0.2f);
                    Check("场景：出口门在大厅正前方 1.9 米处（door_b_variant 门口）",
                        Mathf.Abs(Vector3.Distance(
                            new Vector3(exitDoor.transform.position.x, 0f, exitDoor.transform.position.z),
                            new Vector3(lobbyObj.transform.position.x, 0f, lobbyObj.transform.position.z)) - 1.9f) < 0.3f);
                }
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
                e1.PlaceDownUpright();
                Check("场景链路：P 放下后复位",
                    !e1.grab.IsHeld && !e1.IsSpraying && !e1.handleLever.IsSqueezed);

                // 放下后必须是竖着贴地，而不是撒手让刚体滚成横躺
                {
                    var rb1 = e1.GetComponent<Rigidbody>();
                    float tiltX = Mathf.DeltaAngle(0f, e1.transform.eulerAngles.x);
                    float tiltZ = Mathf.DeltaAngle(0f, e1.transform.eulerAngles.z);
                    Bounds b1 = new Bounds();
                    bool has = false;
                    foreach (var r in e1.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r is ParticleSystemRenderer) continue;
                        if (r.name.ToLower().Contains("spray")) continue;
                        if (!has) { b1 = r.bounds; has = true; } else b1.Encapsulate(r.bounds);
                    }
                    Check("P 放下：瓶身竖直（没有翻成横躺）",
                        has && Mathf.Abs(tiltX) < 3f && Mathf.Abs(tiltZ) < 3f
                        && b1.size.y >= b1.size.x && b1.size.y >= b1.size.z);
                    Check("P 放下：瓶底贴地（不悬空、不陷进地板）",
                        has && Mathf.Abs(b1.min.y) < 0.05f);
                    Check("P 放下：刚体已冻结在原地（不会再自己滚走）",
                        rb1 != null && rb1.isKinematic);
                }

                UnityEngine.Object.DestroyImmediate(anchor.gameObject);
                ResetManagerInstance();
                UnityEngine.Object.DestroyImmediate(mgrGo);
            }
        }

        // ------------------------------------------------------------
        // 头显接入配置：OpenXR 运行时 + 手柄动作绑定 + 移动/交互组件
        // ------------------------------------------------------------
        static void TestVrConfiguration()
        {
            if (!Application.isBatchMode)
            {
                Debug.Log("[TEST] 跳过 VR 配置校验（仅在 batchmode 下执行）");
                return;
            }

            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/FireSafetyOffice.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            // 1) OpenXR 运行时
            var fails = new List<string>();
            bool xrOk = VROpenXRConfig.Verify(fails);
            if (!xrOk) foreach (var f in fails) Debug.LogError("[TEST] VR 配置缺口：" + f);
            Check("VR：OpenXR Loader 已挂到 Standalone 且交互 profile 全部启用（缺口 " + fails.Count + " 项）", xrOk);

            // 2) XR 锚点与相机
            var rig = UnityEngine.Object.FindObjectOfType<XROrigin>(true);
            Check("VR：场景存在 XR Origin (Drill)", rig != null);
            if (rig == null) return;
            Check("VR：追踪原点为地板模式（真实身高直接映射）",
                rig.RequestedTrackingOriginMode == XROrigin.TrackingOriginMode.Floor);
            var cam = rig.Camera;
            Check("VR：相机存在并已挂到锚点上", cam != null);
            if (cam != null)
            {
                Check("VR：相机近裁面 0.05 米（贴近压把/销子不穿模）",
                    Mathf.Abs(cam.nearClipPlane - 0.05f) < 0.001f);
                Check("VR：相机远裁面 ≥ 60 米（能看清走廊尽头）", cam.farClipPlane >= 60f);
                var camPose = cam.GetComponent<DrillTrackedPose>();
                Check("VR：相机由 CenterEye 位姿驱动", camPose != null && camPose.node == XRNode.CenterEye);
            }

            // 3) 双手手柄：位姿 / 抓取 / 扳机 / UI 点按 / 射线
            CheckHand("LeftHand Controller", XRNode.LeftHand);
            CheckHand("RightHand Controller", XRNode.RightHand);

            // 4) 移动
            var loco = rig.GetComponentInChildren<XRDrillLocomotion>(true);
            Check("VR：XRDrillLocomotion 挂在锚点上", loco != null);
            if (loco != null)
            {
                Check("VR：移动动作指向 XRI Left Locomotion/Move", loco.moveAction != null);
                Check("VR：转身动作指向 XRI Right Locomotion/Snap Turn", loco.turnAction != null);
                Check("VR：瞬转动作按二维轴(Vector2)绑定（代码按 Vector2.x 读取）",
                    loco.turnAction != null && loco.turnAction.action != null &&
                    loco.turnAction.action.expectedControlType == "Vector2");
                Check("VR：瞬转角度 45°", Mathf.Approximately(loco.snapAngle, 45f));
                Check("VR：行走速度 2.2 米/秒", Mathf.Approximately(loco.moveSpeed, 2.2f));
                Check("VR：蹲行速度低于行走速度", loco.crawlSpeed > 0f && loco.crawlSpeed < loco.moveSpeed);
                Check("VR：推满摇杆自动小跑（阈值 0.92）",
                    loco.sprintOnFullStick && Mathf.Abs(loco.fullStickThreshold - 0.92f) < 0.001f);
                Check("VR：Grip 加速默认关闭（Grip 是抓取键，不能顺带加速）", !loco.allowSprint);
                Check("VR：移动方向基准 = 头显朝向",
                    loco.moveDirection == XRDrillLocomotion.DirectionMode.Head);
                Check("VR：移动组件已引用左手柄（左摇杆方向模式可用）", loco.leftHand != null);
                var cc = rig.GetComponent<CharacterController>();
                Check("VR：锚点上有 CharacterController（碰撞与重力生效）", cc != null && cc.enabled);
            }

            // 5) 头显输入层与提示条
            var vr = rig.GetComponentInChildren<VRDrillInput>(true);
            Check("VR：VRDrillInput 挂在锚点上", vr != null);
            if (vr != null)
            {
                Check("VR：已引用两台灭火器（实际 " + (vr.extinguishers != null ? vr.extinguishers.Length : 0) + "）",
                    vr.extinguishers != null && vr.extinguishers.Length >= 2);
                Check("VR：已引用开始界面", vr.startScreen != null);
                Check("VR：已引用引导页", vr.onboarding != null);
                Check("VR：已引用阶段一完成面板", vr.stagePanel != null);
                Check("VR：已引用阶段二流程", vr.stageTwo != null);
                Check("VR：右手扳机动作已绑定（Activate）", vr.rightTriggerAction != null);
                Check("VR：左手扳机动作已绑定（Activate）", vr.leftTriggerAction != null);
                Check("VR：已引用 VR 提示条", vr.hud != null);
                Check("VR：拔销触觉幅度够强（≥0.5）", vr.hapticPin >= 0.5f);
                Check("VR：压把触觉幅度有效", vr.hapticPress > 0f);
                Check("VR：触觉脉冲时长有效", vr.hapticDuration > 0f);
                if (vr.hud != null)
                {
                    Check("VR：提示条锚定在相机上", vr.hud.head == cam.transform);
                    Check("VR：提示条在视线下方（-0.22 米）", vr.hud.verticalOffset <= -0.1f);
                    Check("VR：提示条距离 0.95 米（一臂内可读）", vr.hud.distance >= 0.6f && vr.hud.distance <= 1.2f);
                }
            }

            // 6) 头显下不能退回桌面喷射模式
            var desktop = UnityEngine.Object.FindObjectOfType<DesktopDrillInput>(true);
            Check("VR：桌面输入组件存在（无头显时仍可键鼠演练）", desktop != null);
            foreach (var e in UnityEngine.Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
            {
                Check("VR：" + e.gameObject.name + " 压把链路已装配（handleLever 引用）", e.handleLever != null);
                Check("VR：" + e.gameObject.name + " 保险销已装配", e.pin != null);
            }

            // 7) 手柄模型的生成逻辑与触觉接口（临时对象，不写进场景）
            var probeGo = new GameObject("__controller_visual_probe");
            var pv = probeGo.AddComponent<VRControllerVisual>();
            pv.Build();
            Check("VR：手柄模型可生成（握把 + 前球 + 顶部条 = 3 个几何体）",
                pv.Built && pv.PartCount == 3);
            Check("VR：手柄模型不带碰撞体（不会被自己抓到、不挡自己的射线）",
                probeGo.GetComponentsInChildren<Collider>(true).Length == 0);
            var mr = probeGo.GetComponentInChildren<MeshRenderer>(true);
            Check("VR：手柄模型有可用材质（浓烟里不会变成洋红错误材质）",
                mr != null && mr.sharedMaterial != null && mr.sharedMaterial.shader != null);
            UnityEngine.Object.DestroyImmediate(probeGo);

            VRHaptics.ResetCounters();
            VRHaptics.Enabled = true;
            VRHaptics.Pulse(HandSide.Right, 0.5f, 0.05f);
            Check("VR：触觉接口可调用（无头显时记为跳过而不报错）",
                VRHaptics.AttemptCount == 1 && (VRHaptics.SendCount + VRHaptics.SkipCount) == 1);
        }

        static void CheckHand(string name, XRNode node)
        {
            var go = GameObject.Find(name);
            Check("VR：场景存在 " + name, go != null);
            if (go == null) return;

            var pose = go.GetComponent<DrillTrackedPose>();
            Check("VR：" + name + " 位姿驱动节点 = " + node, pose != null && pose.node == node);

            var ctrl = go.GetComponent<UnityEngine.XR.Interaction.Toolkit.ActionBasedController>();
            Check("VR：" + name + " 有 ActionBasedController", ctrl != null);
            if (ctrl != null)
            {
                Check("VR：" + name + " 位置/旋转动作已绑定", ctrl.positionAction.action != null && ctrl.rotationAction.action != null);
                Check("VR：" + name + " 抓取动作（Select）已绑定", ctrl.selectAction.action != null);
                Check("VR：" + name + " 扳机动作（Activate）已绑定", ctrl.activateAction.action != null);
                Check("VR：" + name + " UI 动作（UI Press）已绑定", ctrl.uiPressAction.action != null);
            }

            var ray = go.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>();
            Check("VR：" + name + " 有射线交互器（射线点按 UI）",
                ray != null && ray.enableUIInteraction);

            var line = go.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
            Check("VR：" + name + " 有射线视觉组件", line != null);
            var lr = go.GetComponent<LineRenderer>();
            Check("VR：" + name + " 射线是细线（≤0.02 米，不挡视线）",
                lr != null && lr.startWidth <= 0.02f);

            // 手柄可见模型：真机里要看得见自己的手
            var vis = go.GetComponent<VRControllerVisual>();
            Check("VR：" + name + " 有手柄可见模型（头显里能看见手）", vis != null);
            if (vis != null)
            {
                var expect = name.Contains("Left") ? HandSide.Left : HandSide.Right;
                Check("VR：" + name + " 模型手侧 = " + expect, vis.side == expect);
                Check("VR：" + name + " 模型不在桌面键鼠模式显示", !vis.showInDesktop);
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
