#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
            TestLobbyGuidance();
            TestOnboardingAndEnding();
            TestStageTwoFlow();
            TestFlameClearAll();
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

            Check("第二阶段：初始为 Idle", flow.CurrentStage == StageTwoFlow.Stage.Idle);
            Check("第二阶段：默认蹲姿阈值 1.15 米", Mathf.Approximately(flow.crouchHeight, 1.15f));

            flow.Begin();
            Check("继续往下：进入撤离阶段", flow.CurrentStage == StageTwoFlow.Stage.Evacuating);
            Check("撤离阶段：烟雾已开启", smoke.Active && smoke.EmitterCount == 3);
            Check("撤离阶段：箭头切到大厅模式", arrows.Active && arrows.LobbyMode && !arrows.ExitMode);

            // 编辑模式补跑 Start/Update：箭头与「安全出口 →」标识才会真正生成并摆位
            InvokeMethod(arrows, "Start");
            InvokeMethod(arrows, "Update");
            Check("撤离：浓雾中已生成地面「安全出口 →」标识", arrows.HasExitSign);
            Check("撤离：标识在烟雾中保持可见（指向大厅）", arrows.ExitSignVisible);

            Check("站立到达大厅不推进（必须先蹲下）",
                flow.TryArriveLobby(false) == false && flow.CurrentStage == StageTwoFlow.Stage.Evacuating);

            flow.SetCrouchOverrideForTest(true);
            Check("蹲姿到达大厅：进入第三阶段",
                flow.TryArriveLobby(true) && flow.CurrentStage == StageTwoFlow.Stage.StageThree);
            Check("第三阶段：大厅复燃火源已生成且尚未熄灭",
                flow.stageTwoFire != null && !flow.stageTwoFire.IsExtinguished);
            Check("第三阶段：烟雾停止", !smoke.Active);
            Check("第三阶段：复燃火源有灭火阈值", flow.stageTwoFire != null && flow.stageTwoFire.extinguishThreshold > 0f);

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

                UnityEngine.Object.DestroyImmediate(extGo);
            }

            flow.SetCrouchOverrideForTest(false);
            ending.Hide();
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

            FireSource.ClearAllFlames();

            Check("清火：激活火源被立刻熄灯（弹面板火焰消失）", light1.intensity <= 0.001f);
            Check("清火：未激活火源也被熄灯（Resources 兜底路径生效）", light2.intensity <= 0.001f);

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
                    Check("场景 " + n + "：题库已换成本轮新 5 题",
                        e.quizPanel.questions.Count == 5 && e.quizPanel.questions[0].question.Contains("有效喷射距离"));
                }
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

            var arrows = UnityEngine.Object.FindObjectOfType<GuidanceArrows>();
            Check("场景：引导箭头存在", arrows != null);
            if (arrows != null)
            {
                Check("场景：引导箭头已配置大厅目标（lobbyTarget）", arrows.lobbyTarget != null);
                Check("场景：大厅到达距离为 0.6 米", Mathf.Approximately(arrows.lobbyArriveDistance, 0.6f));
                Check("场景：引导箭头已配置安全出口目标（exitTarget）", arrows.exitTarget != null);
                Check("场景：出口到达距离 0.9 米", Mathf.Approximately(arrows.exitArriveDistance, 0.9f));
                Check("场景：浓烟里箭头放大到 1.6 倍", Mathf.Approximately(arrows.stage2ArrowScale, 1.6f));
                Check("场景：地面安全出口标识文案为「安全出口 →」", arrows.floorSignText == "安全出口 →");
                Check("场景：标识离地 1.05 米（高于雾底 1.15 米视线带）",
                    Mathf.Approximately(arrows.floorSignHeight, 1.05f));
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
                Check("场景：复燃火源配置在出口门前方 0.75 米",
                    Mathf.Approximately(flow.stageTwoFireOffset.z, 0.75f)
                    && flow.stageTwoFireOffset.z > 0f);
            }

            var smokeZone = UnityEngine.Object.FindObjectOfType<SmokeZone>(true);
            Check("场景：烟雾区 SmokeZone 存在", smokeZone != null);
            if (smokeZone != null)
            {
                Check("场景：烟雾区至少 4 个发射点（实际 " + smokeZone.emitterCount + "）", smokeZone.emitterCount >= 4);
                Check("场景：雾底抬高到 1.15 米（不糊住地面箭头）",
                    smokeZone.floorClearance >= 1.0f);
                Check("场景：烟雾仅在视线高度（起始 y ≥ 1.2 米）", smokeZone.start.y >= 1.2f);
            }

            var exitDoor = UnityEngine.Object.FindObjectOfType<ExitDoor>(true);
            Check("场景：安全出口门 ExitDoor 存在", exitDoor != null);
            if (exitDoor != null)
            {
                Check("场景：出口门指示牌文案为「安全出口 EXIT」", exitDoor.signText.Contains("安全出口"));
                Check("场景：出口门进入判定 0.9 米", Mathf.Approximately(exitDoor.enterDistance, 0.9f));
                Check("场景：出口门初始未解锁", !exitDoor.IsUnlocked);
                if (lobbyObj != null)
                    Check("场景：出口门在大厅正后方 1.1 米处",
                        Mathf.Abs(Vector3.Distance(
                            new Vector3(exitDoor.transform.position.x, 0f, exitDoor.transform.position.z),
                            new Vector3(lobbyObj.transform.position.x, 0f, lobbyObj.transform.position.z)) - 1.1f) < 0.25f);
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
