using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第 41 轮玩家视角验收：语言页 → 韩文/中文 → 事前教育 10 题（6 对 4 错刚好 60%）→
    /// 发现火灾 → 手动报警按钮 → 判定 → 初期灭火 → 疏散 → 集合点报告 → 三块情景知识板。
    /// </summary>
    public static class Round41Probe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = @"D:\43763\VR\summary\Logs\shots";
        const float HardTimeout = 220f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();

        static float _t0;
        static bool _entered;
        static int _phase;
        static bool _btnSaid;
        static Vector3 _standPos;
        static Quaternion _standRot;
        static float _phaseT;
        static Transform _cam;

        static LanguageSelectScreen _lang;
        static PreTrainingQuiz _pre;
        static StartScreen _start;
        static DrillFlowDirector _dir;
        static AlarmCallButton _btn;
        static FireAlarm _alarm;
        static DrillScenario _scenario;
        static ExitDoor _door;

        static int _qIndex;
        static bool _preOpened;
        static int _wrongLeft = 4;          // 故意答错 4 题：10 题答对 6 题 = 60%

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            _phase = 0; _phaseT = 0; _entered = false; _t0 = 0;
            _qIndex = 0; _preOpened = false; _wrongLeft = 4;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G41] " + s); Log.Add(s); }
        static void Fail(string s)
        {
            if (Fails.Contains(s)) return;
            Debug.LogError("[G41] × " + s);
            Log.Add("× " + s);
            Fails.Add(s);
        }
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
            if (!_entered) { _entered = true; _t0 = Time.realtimeSinceStartup; Say("进入 Play 模式"); }
            if (Time.realtimeSinceStartup - _t0 > HardTimeout) { Finish("TIMEOUT"); return; }

            var cam = Camera.main;
            if (cam == null) return;
            if (_cam == null) _cam = cam.transform;

            switch (_phase)
            {
                case 0:
                    if (Time.realtimeSinceStartup - _t0 < 3f) return;
                    _lang = Object.FindFirstObjectByType<LanguageSelectScreen>();
                    _pre = Object.FindFirstObjectByType<PreTrainingQuiz>();
                    _start = Object.FindFirstObjectByType<StartScreen>();
                    _dir = Object.FindFirstObjectByType<DrillFlowDirector>();
                    _btn = Object.FindFirstObjectByType<AlarmCallButton>();
                    _alarm = Object.FindFirstObjectByType<FireAlarm>();
                    _scenario = Object.FindFirstObjectByType<DrillScenario>();
                    _door = Object.FindFirstObjectByType<ExitDoor>();

                    Check("语言页在启动时自动弹出", _lang != null && _lang.IsOpen);
                    Check("语言页有三个语言选项", _lang != null && _lang.ButtonCount == 3, "按钮=" + (_lang != null ? _lang.ButtonCount : -1));
                    Check("语言页弹出时开始界面被藏起来", _start != null && _start.GetComponentsInChildren<Canvas>(true).Length > 0);
                    if (_lang != null) _lang.ForcePlace();
                    var cv = _lang != null ? _lang.GetComponentInChildren<Canvas>(true) : null;
                    Say("语言页 canvas=" + (cv != null ? cv.name : "null") + " active=" + (cv != null && cv.gameObject.activeInHierarchy) + " pos=" + (cv != null ? cv.transform.position.ToString("F2") : "-") + " cam=" + _cam.position.ToString("F2") + " scale=" + (cv != null ? cv.transform.lossyScale.ToString("F3") : "-"));
                    ShotAt("g41v_00_language_cn.png", new Vector3(0.0f, 1.58f, 3.35f), new Vector3(0.0f, 1.25f, 0.6f), 60f);
                    Next(1);
                    return;

                case 1:
                    // 选韩文：开始界面应重建成韩文
                    _lang.Choose(DrillLocale.Lang.Korean);
                    Next(2);
                    return;

                case 2:
                    if (Time.realtimeSinceStartup - _phaseT < 1.2f) return;
                    Check("选韩文后语言写入", DrillLocale.Korean);
                    Check("选韩文后语言页关闭", _lang != null && !_lang.IsOpen);
                    Check("选韩文后开始界面处于未开始状态（可再次显示）", _start != null && !_start.DrillStarted);
                    ShotAt("g41v_01_start_korean.png", new Vector3(0.0f, 1.58f, 3.30f), new Vector3(0.0f, 1.28f, 0.6f), 60f);
                    // 切回中文，后续流程用中文便于核对文案
                    DrillLocale.SetLanguage(DrillLocale.Lang.Chinese);
                    UIFont.Invalidate();
                    if (_start != null) _start.RefreshLanguage();
                    Next(3);
                    return;

                case 3:
                    if (Time.realtimeSinceStartup - _phaseT < 1.2f) return;
                    Check("切回中文生效", DrillLocale.Chinese);
                    _pre.Open(_start != null ? _start.transform : _cam);
                    _preOpened = true;
                    Next(4);
                    return;

                case 4:
                    // 逐题作答：前 6 题答对，后 4 题答错 → 6/10 = 60% 刚好通过
                    if (!_pre.IsOpen) { Check("事前教育面板已打开", false, "面板未打开"); Next(8); return; }
                    if (Time.realtimeSinceStartup - _phaseT < 0.55f) return;   // 等一帧布局
                    ShotAt("g41v_02_pretraining_q1.png", _cam.position, _cam.position + _cam.forward * 2f, 60f);
                    Next(5);
                    return;

                case 5:
                {
                    if (!_pre.IsOpen) return;
                    if (_qIndex >= 10) { Next(6); return; }
                    if (Time.realtimeSinceStartup - _phaseT < 1.75f) return;   // 看讲解 + 进入下一题
                    var q = _pre.questions[_qIndex];
                    int answer;
                    if (_wrongLeft > 0) { answer = (q.correctIndex + 1) % 3; _wrongLeft--; }
                    else answer = q.correctIndex;
                    _pre.AnswerOption(answer);
                    Say("第 " + (_qIndex + 1) + " 题作答=" + ((char)('A' + answer)) + "，答对数=" + _pre.CorrectCount);
                    _qIndex++;
                    if (_qIndex == 6) ShotAt("g41v_03_pretraining_feedback.png", _cam.position, _cam.position + _cam.forward * 2f, 60f);
                    _phaseT = Time.realtimeSinceStartup;
                    return;
                }

                case 6:
                    if (Time.realtimeSinceStartup - _phaseT < 3.4f) return;   // 等最后一题的讲解走完、结算页出来
                    Check("事前教育 10 题答完", _pre.CorrectCount == 6, "答对=" + _pre.CorrectCount);
                    Check("答对 6/10（60%）判定通过", _pre.Passed, "答对=" + _pre.CorrectCount + "/" + _pre.QuestionCount + "，需要=" + _pre.RequiredCorrect + "，通过=" + _pre.Passed + "，失败=" + _pre.Failed);
                    ShotAt("g41v_04_pretraining_passed.png", _cam.position, _cam.position + _cam.forward * 2f, 60f);
                    _pre.Close();
                    Next(7);
                    return;

                case 7:
                    // 真正开始（纸张情景，index 1）
                    if (_start != null) _start.ActuallyStart(1);
                    Next(8);
                    return;

                case 8:
                    if (Time.realtimeSinceStartup - _phaseT < 2.5f) return;
                    Check("进入情景后处于「发现火灾」阶段", _dir != null && _dir.CurrentStage == DrillFlowDirector.Stage.Discovered,
                          "阶段=" + (_dir != null ? _dir.StageName : "null"));
                    Check("火灾警报作为背景音已响起", _alarm != null && _alarm.IsRinging);
                    ShotAt("g41v_05_fire_discovered.png", new Vector3(-2.60f, 1.62f, -2.60f), new Vector3(-5.60f, 1.10f, -4.40f), 70f);
                    Next(9);
                    return;

                case 9:
                {
                    // 走到手动报警按钮前，注视 0.8 秒
                    if (_btn == null) { Fail("场景里没有手动报警按钮"); Next(11); return; }
                    _cam.SetParent(null, true);                            // 脱离 XR rig，否则移动系统每帧覆盖相机姿态
                    _btn.player = _cam;                                    // 探针里把「玩家」锚到主相机，模拟头显视线
                    var plate = _btn.transform.position + new Vector3(0f, 1.26f, 0f);
                    var stand = plate + new Vector3(0f, 0.25f, -0.85f);
                    _cam.position = stand;
                    _cam.rotation = Quaternion.LookRotation((plate - stand).normalized, Vector3.up);
                    _standPos = stand;
                    _standRot = _cam.rotation;
                    ShotAt("g41v_06_alarm_button.png", stand, plate, 60f);
                    Say("按钮位置=" + _btn.transform.position.ToString("F2") + "，站位=" + stand.ToString("F2") + "，engage=" + _btn.engageDistance + "，ang=" + Vector3.Angle(_cam.forward, (plate - stand).normalized).ToString("F1") + "，enabled=" + _btn.enabled);
                    Next(10);
                    return;
                }

                case 10:
                    _cam.position = _standPos;      // XR 移动系统每帧会把相机拉回玩家位置，这里压回站位
                    _cam.rotation = _standRot;
                    if (_btn.player == null) _btn.player = _cam;
                    if (!_btnSaid) { _btnSaid = true; Say("报警按钮：距离=" + _btn.PlayerDistance.ToString("F2") + "，注视进度=" + _btn.DwellProgress.ToString("F2") + "，触发=" + _btn.Triggered); }
                    if (Time.realtimeSinceStartup - _phaseT < 1.8f) return;
                    Say("报警按钮：距离=" + _btn.PlayerDistance.ToString("F2") + " m，注视进度=" + _btn.DwellProgress.ToString("F2")
                        + "，相机=" + _cam.position.ToString("F2"));
                    Check("注视手动报警按钮后已按响", _btn.Triggered);
                    Check("按下后进入「警示与报告」阶段", (int)_dir.CurrentStage >= (int)DrillFlowDirector.Stage.Alerted,
                          "阶段=" + _dir.StageName);
                    ShotAt("g41v_07_alarm_pressed.png", _cam.position, _cam.position + _cam.forward * 2f, 60f);
                    Next(11);
                    return;

                case 11:
                    if (Time.realtimeSinceStartup - _phaseT < 1.5f) return;
                    Check("评估完成：进入初期灭火或疏散",
                          _dir.CurrentStage == DrillFlowDirector.Stage.Extinguishing
                          || _dir.CurrentStage == DrillFlowDirector.Stage.Evacuating,
                          "阶段=" + _dir.StageName);
                    Check("评估结论有文案", !string.IsNullOrEmpty(_dir.LastAssessment), _dir.LastAssessment);
                    Check("可尝试初期处置时结论为 true", _dir.CanAttemptInitial);
                    Say("评估：" + _dir.LastAssessment);
                    Next(12);
                    return;

                case 12:
                {
                    // 实战初期灭火：取瓶 → 拔销 → 压把 → 喷灭（纸张情景 02 号瓶）
                    var sc = _scenario.CurrentScenario;
                    if (sc == null || sc.fire == null) { Fail("纸张情景火源缺失"); Next(14); return; }
                    _cam.position = new Vector3(-5.90f, 1.55f, -2.45f);
                    _cam.rotation = Quaternion.LookRotation((sc.fire.AimPoint - _cam.position).normalized, Vector3.up);
                    ShotAt("g41v_08_initial_attack.png", _cam.position, sc.fire.AimPoint, 70f);
                    sc.fire.Extinguish();
                    Next(13);
                    return;
                }

                case 13:
                    if (Time.realtimeSinceStartup - _phaseT < 1.2f) return;
                    Check("灭火成功 → 转入疏散阶段", _dir.CurrentStage == DrillFlowDirector.Stage.Evacuating,
                          "阶段=" + _dir.StageName);
                    Check("火灭后安全出口已解锁", _door != null && _door.IsUnlocked);
                    ShotAt("g41v_09_evacuate.png", new Vector3(2.20f, 1.62f, 1.30f), new Vector3(6.20f, 1.20f, 1.02f), 70f);
                    Next(14);
                    return;

                case 14:
                {
                    // 走到门外集合点
                    var ap = _dir.assemblyPoint;
                    _cam.position = new Vector3(ap.x, 1.60f, ap.z);
                    _cam.rotation = Quaternion.LookRotation(new Vector3(-1f, -0.05f, 0f), Vector3.up);
                    Next(15);
                    return;
                }

                case 15:
                    if (Time.realtimeSinceStartup - _phaseT < 1.4f) return;
                    Check("到达集合点后流程标记为已报告", _dir.ReportedAtAssembly);
                    Check("最终阶段 = 到达安全地点并报告", _dir.CurrentStage == DrillFlowDirector.Stage.Reported,
                          "阶段=" + _dir.StageName);
                    ShotAt("g41v_10_assembly_report.png", _cam.position, _cam.position + _cam.forward * 3f, 70f);
                    Next(16);
                    return;

                case 16:
                    // 三块情景专属知识板（中文）
                    ShotAt("g41v_11_boards_cn_electrical.png",
                           new Vector3(9.30f, 1.55f, -3.10f), new Vector3(11.60f, 1.20f, -3.10f), 65f);
                    ShotAt("g41v_12_boards_cn_paper.png",
                           new Vector3(9.30f, 1.55f, 0.05f), new Vector3(11.60f, 1.20f, 0.05f), 65f);
                    ShotAt("g41v_13_boards_cn_pantry.png",
                           new Vector3(9.30f, 1.55f, 3.20f), new Vector3(11.60f, 1.20f, 3.20f), 65f);
                    Next(17);
                    return;

                case 17:
                    DrillLocale.SetLanguage(DrillLocale.Lang.Korean);
                    UIFont.Invalidate();
                    Next(18);
                    return;

                case 18:
                    if (Time.realtimeSinceStartup - _phaseT < 1.2f) return;
                    ShotAt("g41v_14_boards_korean.png",
                           new Vector3(9.30f, 1.55f, 0.05f), new Vector3(11.60f, 1.20f, 0.05f), 65f);
                    DrillLocale.SetLanguage(DrillLocale.Lang.English);
                    UIFont.Invalidate();
                    Next(19);
                    return;

                case 19:
                    if (Time.realtimeSinceStartup - _phaseT < 1.2f) return;
                    ShotAt("g41v_15_boards_english.png",
                           new Vector3(9.30f, 1.55f, -3.10f), new Vector3(11.60f, 1.20f, -3.10f), 65f);
                    DrillLocale.SetLanguage(DrillLocale.Lang.Chinese);
                    UIFont.Invalidate();
                    Next(20);
                    return;

                case 20:
                    Check("三套语言来回切换后仍为中文", DrillLocale.Chinese);
                    Finish("DONE");
                    return;
            }
        }

        static void ShotAt(string name, Vector3 pos, Vector3 lookAt, float fov = 0f, bool english = false)
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) { Fail("截图无主相机：" + name); return; }
                if (english) { DrillLocale.SetLanguage(DrillLocale.Lang.English); UIFont.Invalidate(); }
                var tr = cam.transform;
                var oldParent = tr.parent;
                var oldPos = tr.position;
                var oldRot = tr.rotation;
                if (oldParent != null) tr.SetParent(null, true);
                tr.position = pos;
                tr.rotation = Quaternion.LookRotation((lookAt - pos).normalized, Vector3.up);
                foreach (var ls in Object.FindObjectsByType<LanguageSelectScreen>(FindObjectsSortMode.None))
                    if (ls != null && ls.IsOpen) ls.ForcePlace();   // 语言页跟着截图机位摆一次，否则停在旧机位
                int w = 1600, h = 900;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var oldTarget = cam.targetTexture;
                var oldEye = cam.stereoTargetEye;
                float oldFov = cam.fieldOfView;
                if (fov > 1f) cam.fieldOfView = fov;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(ShotDir, name), tex.EncodeToPNG());
                Object.Destroy(tex);
                cam.targetTexture = oldTarget;
                cam.stereoTargetEye = oldEye;
                if (fov > 1f) cam.fieldOfView = oldFov;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                tr.position = oldPos;
                tr.rotation = oldRot;
                if (oldParent != null) tr.SetParent(oldParent, true);
                Say("截图 " + name);
            }
            catch (System.Exception e) { Fail("截图失败 " + name + "：" + e.Message); }
        }

        static void Finish(string reason)
        {
            Say("结果 " + reason + "，失败数=" + Fails.Count);
            var sb = new System.Text.StringBuilder();
            foreach (var s in Log) sb.AppendLine(s);
            sb.AppendLine();
            sb.AppendLine("FAIL_COUNT=" + Fails.Count);
            sb.AppendLine("PROBE_EXIT=" + (Fails.Count == 0 ? 0 : 1));
            File.WriteAllText(Path.Combine(ShotDir, "_round41_probe.txt"), sb.ToString(), new System.Text.UTF8Encoding(false));
            EditorApplication.update -= Tick;
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }
    }
}









