// 第 49 轮玩家视角验收：独立情形 1/2、四种墙面报警按钮、操作教学返回主面板、三台灭火器竖挂
// 统一 1600×900 截图，相机高度 1.62 米（玩家眼高）。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using FireSafety;

namespace FireSafety.EditorTools
{
    public static class Round49Probe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = @"D:\43763\VR\summary\Logs\shots";
        const float HardTimeout = 160f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static float _t0, _phaseT;
        static bool _entered;
        static int _phase, _tutPage;
        static Transform _cam;
        static Camera _camera;
        static StartScreen _start;
        static DrillScenario _scenario;
        static DrillFlowDirector _flow;
        static StageTwoFlow _stage;
        static ExitDoor _door;
        static OnboardingScreen _ob;
        static string _tag = "probe";

        public static void RunTutorial() => Run("tutorial");
        public static void RunInspect() => Run("inspect");
        public static void RunCase1() => Run("case1");
        public static void RunCase2() => Run("case2");

        static void Run(string tag)
        {
            _tag = tag;
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            _phase = 0; _phaseT = 0; _entered = false; _t0 = 0; _tutPage = 0;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G49] " + s); Log.Add(s); }
        static void Fail(string s) { if (Fails.Contains(s)) return; Debug.LogError("[G49] × " + s); Log.Add("× " + s); Fails.Add(s); }
        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) Say("√ " + name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
            else Fail(name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
        }
        static void Next(int p) { _phase = p; _phaseT = Time.realtimeSinceStartup; Say("→ 阶段 " + p); }

        static void Tick()
        {
            if (!Application.isPlaying)
            {
                if (_t0 > 0f && Time.realtimeSinceStartup - _t0 > HardTimeout) EditorApplication.Exit(2);
                return;
            }
            if (!_entered) { _entered = true; _t0 = Time.realtimeSinceStartup; Say("进入 Play 模式（玩家视角 1.62 米）"); }
            if (Time.realtimeSinceStartup - _t0 > HardTimeout) { Finish("TIMEOUT"); return; }

            var cam = Camera.main;
            if (cam == null) return;
            if (_cam == null) { _cam = cam.transform; _camera = cam; }

            switch (_phase)
            {
                case 0:
                    if (Time.realtimeSinceStartup - _t0 < 3f) return;
                    var lang = UnityEngine.Object.FindFirstObjectByType<LanguageSelectScreen>();
                    if (lang != null) lang.Choose(DrillLocale.Lang.Chinese);
                    Next(1);
                    return;

                case 1:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.2f) return;
                    _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
                    _scenario = UnityEngine.Object.FindFirstObjectByType<DrillScenario>();
                    _flow = UnityEngine.Object.FindFirstObjectByType<DrillFlowDirector>();
                    _ob = UnityEngine.Object.FindFirstObjectByType<OnboardingScreen>();
                    Check("场景组件齐全（StartScreen / DrillScenario / Onboarding）",
                          _start != null && _scenario != null && _ob != null);
                    if (_start == null || _scenario == null || _ob == null) { Finish("缺组件"); return; }
                    Check("开始界面有「决策训练」入口", _start.buttonLabel.Contains("决策"), _start.buttonLabel);
                    Check("开始界面有两个独立情形入口",
                          FindButton("情形 1") != null && FindButton("情形 2") != null);
                    Check("开始界面有「操作教学」入口", _start.showTutorialButton && _start.tutorialLabel.Contains("操作教学"),
                          _start.tutorialLabel);
                    ShotHere("g49v_start_panel.png");
                    if (_tag == "inspect") { HidePanels(); Next(11); return; }
                    if (_tag == "tutorial")
                    {
                        bool clicked = ClickButton("操作教学");
                        Check("点击「操作教学」打开教学面板", clicked && _ob.IsOpen);
                        if (!clicked) { Finish("找不到教学入口"); return; }
                        Next(2);
                    }
                    else
                    {
                        _flow.SetRole(DrillFlowDirector.DrillRole.Employee);
                        Say("身份固定为普通员工（本轮只验证独立火情与报警流程）");
                        string label = _tag == "case1" ? "情形 1" : "情形 2";
                        bool clicked = ClickButton(label);
                        Check("点开独立演练入口「" + label + "」", clicked);
                        if (!clicked) { Finish("找不到情形按钮"); return; }
                        Next(3);
                    }
                    return;
                }

                // ---------------- 操作教学：6 页 → 返回主面板 ----------------
                case 2:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.55f) return;
                    Check("教学面板共 6 页", _ob.PageCount == 6, "pages=" + _ob.PageCount);
                    Check("教学第 " + (_ob.PageIndex + 1) + " 页有正文", !string.IsNullOrEmpty(_ob.pages[_ob.PageIndex]));
                    ShotHere("g49v_tutorial_" + (_ob.PageIndex + 1) + ".png");
                    if (_ob.PageIndex < _ob.PageCount - 1)
                    {
                        _ob.NextPage();
                        _phaseT = Time.realtimeSinceStartup;
                        return;
                    }
                    Check("末页按钮文案为「返回演练面板」", _ob.lastLabel == "返回演练面板", _ob.lastLabel);
                    _ob.NextPage();   // 末页 → FinishTutorial → Close(true)，只回主面板
                    Next(3);
                    return;
                }

                case 3:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.0f) return;
                    if (_tag == "tutorial")
                    {
                        Check("读完教学后教学面板已关闭", !_ob.IsOpen);
                        Check("回到「办公室消防知识演练」主面板", _start.gameObject.activeInHierarchy);
                        Check("没有直接开始演练（仍停在主面板）",
                              _scenario.CurrentMode != DrillScenario.Mode.Formal && !_start.Started,
                              "mode=" + _scenario.CurrentMode + " started=" + _start.Started);
                        ShotHere("g49v_tutorial_back_panel.png");
                        Finish("DONE");
                        return;
                    }

                    // ---------------- 独立情形：检查场景装配 ----------------
                    var want = _tag == "case1" ? DrillScenario.OperationCase.Incipient : DrillScenario.OperationCase.Developed;
                    Check("进入正式演练", _scenario.CurrentMode == DrillScenario.Mode.Formal);
                    Check("本轮是独立火情演练", _scenario.StandaloneCase);
                    Check("不加载任何起火情景（CurrentScenarioIndex=-1）", _scenario.CurrentScenarioIndex == -1,
                          "index=" + _scenario.CurrentScenarioIndex);
                    Check("标题不出现电气/纸张/茶水间", _scenario.CurrentDisplayTitle == "独立火情演练",
                          _scenario.CurrentDisplayTitle);
                    Check("情形正确", _scenario.operationCase == want, _scenario.operationCase.ToString());
                    Check("普通员工不能灭主火", _scenario.Stage1AttackForbidden);
                    bool anyScenarioFire = false;
                    if (_scenario.scenarios != null)
                        foreach (var fs in _scenario.scenarios)
                            if (fs != null && fs.fire != null && fs.fire.IsBurning) anyScenarioFire = true;
                    Check("三套起火情景的火全部关闭", !anyScenarioFire);
                    var preAlarm = UnityEngine.Object.FindFirstObjectByType<FireAlarm>();
                    Check("开始时不自动拉响警报", preAlarm != null && !preAlarm.IsRinging,
                          "alarmOnBegin=" + _start.startAlarmOnBegin + " autoDiscover=" + _flow.autoAlarmOnDiscover);
                    ShotHere("g49v_" + _tag + "_start.png");

                    _door = UnityEngine.Object.FindFirstObjectByType<ExitDoor>();
                    _stage = UnityEngine.Object.FindFirstObjectByType<StageTwoFlow>();
                    Check("出口门与第 2 阶段流程存在", _door != null && _stage != null);
                    Check("探针门与流程门是同一扇", _stage != null && _stage.exitDoor == _door);
                    Check("第 2 阶段出口火情随情形", _stage != null && _stage.exitFirePresent == (want == DrillScenario.OperationCase.Developed));

                    // 四个墙面按钮逐一以玩家视角查看
                    var n = GameObject.Find("AlarmCallButton_North");
                    var w = GameObject.Find("AlarmCallButton_West");
                    var s = GameObject.Find("AlarmCallButton_South");
                    var e = GameObject.Find("AlarmCallButton_East");
                    Check("北墙报警按钮存在", n != null);
                    Check("西墙报警按钮存在", w != null);
                    Check("南墙报警按钮存在", s != null);
                    Check("东墙报警按钮存在", e != null);
                    if (n != null)
                    {
                        PlacePlayer(new Vector3(2.40f, 0f, 2.20f), new Vector3(2.40f, 1.30f, 3.76f));
                        Next(4); return;
                    }
                    Next(5); return;
                }

                // ---------------- 四面墙报警按钮：玩家接近并截图 ----------------
                case 4:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.55f) return;
                    Shot("g49v_alarm_north.png", new Vector3(2.40f, 1.62f, 2.20f), new Vector3(2.40f, 1.30f, 3.76f), 58f);
                    PlacePlayer(new Vector3(-5.10f, 0f, 2.20f), new Vector3(-6.54f, 1.30f, 2.20f));
                    Next(5);
                    return;
                }
                case 5:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.55f) return;
                    Shot("g49v_alarm_west.png", new Vector3(-5.10f, 1.62f, 2.20f), new Vector3(-6.54f, 1.30f, 2.20f), 58f);
                    PlacePlayer(new Vector3(2.55f, 0f, -2.55f), new Vector3(2.55f, 1.30f, -3.96f));
                    Next(6);
                    return;
                }
                case 6:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.55f) return;
                    Shot("g49v_alarm_south.png", new Vector3(2.55f, 1.62f, -2.55f), new Vector3(2.55f, 1.30f, -3.96f), 58f);
                    PlacePlayer(new Vector3(4.05f, 0f, -2.00f), new Vector3(5.12f, 1.30f, -2.00f));
                    Next(7);
                    return;
                }
                case 7:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.55f) return;
                    Shot("g49v_alarm_east.png", new Vector3(4.05f, 1.62f, -2.00f), new Vector3(5.12f, 1.30f, -2.00f), 58f);

                    var south = GameObject.Find("AlarmCallButton_South");
                    var alarm = south != null ? south.GetComponent<AlarmCallButton>() : null;
                    var fireAlarm = UnityEngine.Object.FindFirstObjectByType<FireAlarm>();
                    Check("按下前警报没有响", fireAlarm != null && !fireAlarm.IsRinging);
                    if (alarm == null) { Finish("南墙按钮缺 AlarmCallButton"); return; }
                    alarm.PressByScript();
                    Check("按下墙面按钮后警报开始响", fireAlarm != null && fireAlarm.IsRinging);
                    Next(8);
                    return;
                }

                // ---------------- 报警后：疏散 → 出口门 ----------------
                case 8:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.3f) return;
                    Check("普通员工报警后立即进入第 2 阶段", _scenario.CurrentPhase == DrillScenario.Phase.Stage2,
                          _scenario.CurrentPhase.ToString());
                    Check("出口门前火情仍与情形一致",
                          _stage.exitFirePresent == (_scenario.operationCase == DrillScenario.OperationCase.Developed),
                          "exitFirePresent=" + _stage.exitFirePresent);
                    PlacePlayer(new Vector3(5.25f, 0f, 0.05f), new Vector3(5.25f, 1.20f, 1.02f));
                    Next(9);
                    return;
                }
                case 9:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.9f) return;
                    _stage.TryArriveLobby(false);   // 两种情形都要先“到达大厅”，第 2 阶段才会推进到出口
                    if (_scenario.operationCase == DrillScenario.OperationCase.Incipient)
                    {
                        Check("情形 1 门前无火，出口解锁", _door.IsUnlocked);
                        Check("情形 1 门仍需玩家右击打开", !_door.IsOpen);
                        Shot("g49v_case1_exit_closed.png", new Vector3(5.25f, 1.62f, 0.05f), new Vector3(5.25f, 1.20f, 1.02f), 60f);
                        Check("右击门扇后开始开门", _door.TryOpenByPlayer(6f));
                    }
                    else
                    {
                        Check("情形 2 出口未解锁", !_door.IsUnlocked);
                        Check("情形 2 门前生成初期火焰", _stage.stageTwoFire != null && _stage.stageTwoFire.IsBurning);
                        Shot("g49v_case2_exit_fire.png", new Vector3(5.25f, 1.62f, 0.05f), new Vector3(5.25f, 1.20f, 1.02f), 60f);
                        Check("门前有火时不能直接判定出门", !_stage.TryEnterExit());
                        if (_stage.stageTwoFire != null) _stage.stageTwoFire.Extinguish();
                    }
                    Next(10);
                    return;
                }
                case 10:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.0f) return;
                    if (_tag == "case2")
                    {
                        Check("灭掉门前火焰后出口解锁", _door.IsUnlocked);
                        Shot("g49v_case2_exit_out.png", new Vector3(5.25f, 1.62f, 0.05f), new Vector3(5.25f, 1.20f, 1.02f), 60f);
                    }
                    else Check("情形 1 右击后门扇已打开", _door.IsOpen);
                    bool done = _stage.TryEnterExit();
                    Check("走出安全出口判定成功", done);
                    Shot("g49v_" + _tag + "_outside.png", new Vector3(7.20f, 1.62f, 1.02f), new Vector3(10.40f, 0.60f, 1.35f), 70f);
                    Finish("DONE");
                    return;
                }

                // ---------------- 静态外观：三台竖挂灭火器 ----------------
                case 11: // 仅供 inspect 使用
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                    HidePanels();
                    CheckExt("Extinguisher01", new Vector3(-4.85f, 1.62f, 1.80f), new Vector3(-4.85f, 0.83f, 3.42f), "g49v_ext01_wall.png");
                    PlacePlayer(new Vector3(-4.60f, 0f, -1.20f), new Vector3(-6.26f, 0.83f, -1.20f));
                    Next(12);
                    return;
                }
                case 12:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.55f) return;
                    CheckExt("Extinguisher02", new Vector3(-4.60f, 1.62f, -1.15f), new Vector3(-6.26f, 0.83f, -1.15f), "g49v_ext02_wall.png");
                    PlacePlayer(new Vector3(5.10f, 0f, -3.87f), new Vector3(6.75f, 0.83f, -3.87f));
                    Next(13);
                    return;
                }
                case 13:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.55f) return;
                    CheckExt("Extinguisher03", new Vector3(5.10f, 1.62f, -3.87f), new Vector3(6.75f, 0.83f, -3.87f), "g49v_ext03_wall.png");
                    PlacePlayer(new Vector3(2.40f, 0f, 2.20f), new Vector3(2.40f, 1.30f, 3.76f));
                    Next(14);
                    return;
                }
                case 14:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.55f) return;
                    Shot("g49v_alarm_north.png", new Vector3(2.40f, 1.62f, 2.20f), new Vector3(2.40f, 1.30f, 3.76f), 58f);
                    PlacePlayer(new Vector3(-5.10f, 0f, 2.20f), new Vector3(-6.54f, 1.30f, 2.20f));
                    Next(15);
                    return;
                }
                case 15:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.55f) return;
                    Shot("g49v_alarm_west.png", new Vector3(-5.10f, 1.62f, 2.20f), new Vector3(-6.54f, 1.30f, 2.20f), 58f);
                    PlacePlayer(new Vector3(2.55f, 0f, -2.55f), new Vector3(2.55f, 1.30f, -3.96f));
                    Next(16);
                    return;
                }
                case 16:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.55f) return;
                    Shot("g49v_alarm_south.png", new Vector3(2.55f, 1.62f, -2.55f), new Vector3(2.55f, 1.30f, -3.96f), 58f);
                    PlacePlayer(new Vector3(4.05f, 0f, -2.00f), new Vector3(5.12f, 1.30f, -2.00f));
                    Next(17);
                    return;
                }
                case 17:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.55f) return;
                    Shot("g49v_alarm_east.png", new Vector3(4.05f, 1.62f, -2.00f), new Vector3(5.12f, 1.30f, -2.00f), 58f);
                    var n = GameObject.Find("AlarmCallButton_North");
                    var w = GameObject.Find("AlarmCallButton_West");
                    var s = GameObject.Find("AlarmCallButton_South");
                    var e = GameObject.Find("AlarmCallButton_East");
                    Check("四面墙各有一个报警按钮", n != null && w != null && s != null && e != null);
                    Check("所有按钮都是壁挂（无 Column）",
                          n != null && w != null && s != null && e != null
                          && n.transform.Find("Column") == null && w.transform.Find("Column") == null
                          && s.transform.Find("Column") == null && e.transform.Find("Column") == null);
                    Finish("DONE");
                    return;
                }
            }
        }

        static readonly List<Vector3> _homes = new List<Vector3>();
        static void HidePanels()
        {
            if (_start != null) _start.SetVisible(false);
            if (_ob != null && _ob.IsOpen) _ob.Close(false);
            ShotHere("g49v_inspect_room.png");
        }

        static void CheckExt(string name, Vector3 camPos, Vector3 lookAt, string shotName)
        {
            var go = GameObject.Find(name);
            Check(name + " 存在", go != null);
            if (go == null) return;
            Check(name + " 竖挂（pivot y≥0.40）", go.transform.position.y >= 0.40f,
                  "y=" + go.transform.position.y.ToString("F2"));
            var rb = go.GetComponent<Rigidbody>();
            Check(name + " 已冻结物理", rb != null && rb.isKinematic && !rb.useGravity);
            var r = go.GetComponentInChildren<Renderer>();
            Check(name + " 长轴竖直", r != null && r.bounds.size.y >= Mathf.Max(r.bounds.size.x, r.bounds.size.z) * 0.95f,
                  r != null ? r.bounds.size.ToString("F2") : "null");
            Shot(shotName, camPos, lookAt, 55f);
        }

        static Button FindButton(string contains)
        {
            foreach (var b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b == null) continue;
                foreach (var t in b.GetComponentsInChildren<Text>(true))
                    if (t != null && t.text != null && t.text.Contains(contains)) return b;
            }
            return null;
        }

        static bool ClickButton(string contains)
        {
            var b = FindButton(contains);
            if (b == null) return false;
            b.onClick.Invoke();
            return true;
        }

        static void PlacePlayer(Vector3 feet, Vector3 lookAt)
        {
            var origin = GameObject.Find("XR Origin (Drill)");
            if (origin != null)
            {
                var t = origin.transform;
                t.position = new Vector3(feet.x, t.position.y, feet.z);
            }
            if (_cam != null)
            {
                var dir = (lookAt - new Vector3(feet.x, 1.62f, feet.z)).normalized;
                _cam.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        static void Finish(string tag)
        {
            try
            {
                File.WriteAllText(Path.Combine(ShotDir, "_round49_" + _tag + ".txt"),
                    string.Join("\n", Log.ToArray()) + "\n\nFAIL_COUNT=" + Fails.Count
                    + "\nPROBE_EXIT=" + (Fails.Count == 0 ? 0 : 1) + "\n");
            }
            catch (Exception e) { Debug.LogError("[G49] 报告写入失败：" + e.Message); }
            Debug.Log("[G49] " + tag + "，失败数=" + Fails.Count);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }

        static void ShotHere(string name)
        {
            try
            {
                var cam = _camera != null ? _camera : (_cam != null ? _cam.GetComponent<Camera>() : null);
                if (cam == null) return;
                const int w = 1600, h = 900;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var oldTarget = cam.targetTexture; var oldEye = cam.stereoTargetEye;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(ShotDir, name), tex.EncodeToPNG());
                Say("截图 " + name);
                UnityEngine.Object.Destroy(tex);
                cam.targetTexture = oldTarget; cam.stereoTargetEye = oldEye;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
            catch (Exception e) { Fail("截图失败 " + name + "：" + e.Message); }
        }

        static void Shot(string name, Vector3 pos, Vector3 lookAt, float fov = 0f)
        {
            try
            {
                var tr = _cam;
                var oldPos = tr.position; var oldRot = tr.rotation; var oldParent = tr.parent;
                tr.SetParent(null, true);
                tr.position = pos; tr.rotation = Quaternion.LookRotation((lookAt - pos).normalized, Vector3.up);

                var cam = _camera != null ? _camera : tr.GetComponent<Camera>();
                const int w = 1600, h = 900;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var oldTarget = cam.targetTexture; var oldEye = cam.stereoTargetEye; float oldFov = cam.fieldOfView;
                if (fov > 1f) cam.fieldOfView = fov;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(ShotDir, name), tex.EncodeToPNG());
                Say("截图 " + name);
                UnityEngine.Object.Destroy(tex);
                cam.targetTexture = oldTarget; cam.stereoTargetEye = oldEye;
                if (fov > 1f) cam.fieldOfView = oldFov;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                tr.position = oldPos; tr.rotation = oldRot;
                if (oldParent != null) tr.SetParent(oldParent, true);
            }
            catch (Exception e) { Fail("截图失败 " + name + "：" + e.Message); }
        }
    }
}


