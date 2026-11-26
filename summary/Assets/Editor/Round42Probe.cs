using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第 42 轮玩家视角验收（决策训练线）：
    /// 语言页 → 开始界面两条入口 → 纸张情景事前教育（10 题，60% 达标）→ 故意只答对 3 题验证「未达标也能继续」→
    /// 发现火灾（警报背景音）→ 手动报警按钮 → 评估 → 不灭火直接疏散（成功路径）→ 危险增加转疏散 → 集合点报告 + 结束面板 → 三块知识牌。
    /// </summary>
    public static class Round42Probe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = @"D:\43763\VR\summary\Logs\shots";
        const float HardTimeout = 300f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();

        static float _t0;
        static bool _entered;
        static int _phase;
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
        static DrillEndPanel _end;

        static int _q;
        static int _wrongNeeded;
        static bool _secondPass;

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            _phase = 0; _phaseT = 0; _entered = false; _t0 = 0;
            _q = 0; _wrongNeeded = 4; _secondPass = false;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G42] " + s); Log.Add(s); }
        static void Fail(string s)
        {
            if (Fails.Contains(s)) return;
            Debug.LogError("[G42] × " + s);
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
                    _end = Object.FindFirstObjectByType<DrillEndPanel>();

                    Check("语言页在启动时自动弹出", _lang != null && _lang.IsOpen);
                    Check("语言页有三个语言选项", _lang != null && _lang.ButtonCount == 3);
                    Check("场景里有结束状态面板", _end != null);
                    Check("面板层组件齐全", _pre != null && _start != null && _dir != null && _btn != null && _alarm != null);
                    if (_lang != null) _lang.ForcePlace();
                    ShotAt("g42v_00_language_cn.png", new Vector3(0.0f, 1.58f, 3.35f), new Vector3(0.0f, 1.25f, 0.6f), 60f);
                    Next(1);
                    return;

                case 1:
                    _lang.Choose(DrillLocale.Lang.Chinese);
                    Next(2);
                    return;

                case 2:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.3f) return;
                    Check("切回中文生效", DrillLocale.Chinese);
                    Check("开始界面主入口是「决策训练」", _start != null && _start.buttonLabel.Contains("决策"), _start != null ? _start.buttonLabel : "");
                    Check("开始界面有「操作流程训练」入口", _start != null && _start.tutorialLabel.Contains("操作流程"), _start != null ? _start.tutorialLabel : "");
                    Check("开始界面说明写清两条训练线",
                          _start != null && _start.instructions.Contains("决策训练") && _start.instructions.Contains("操作流程训练"));
                    var startCanvas = _start != null ? _start.GetComponentInChildren<Canvas>(true) : null;
                    if (startCanvas != null)
                        ShotAt("g42v_01_start_two_modes.png", _cam.position, startCanvas.transform.position, 65f);
                    Next(3);
                    return;
                }

                case 3:
                    // 打开纸张情景（index 1）的事前教育
                    _pre.Open(_start != null ? _start.transform : _cam, null, 1);
                    Next(4);
                    return;

                case 4:
                    if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                    Check("事前教育按情景加载纸张题库", _pre.QuestionCount == 10 && _pre.questions[7].qCn.Contains("纸张"),
                          "题=" + _pre.QuestionCount + " 第8题=" + (_pre.questions.Count > 7 ? _pre.questions[7].qCn : "-"));
                    Check("事前教育只作教学（不作为门槛）", _pre.educationalOnly);
                    ShotAt("g42v_02_pretraining_paper.png", _cam.position, _cam.position + _cam.forward * 2f, 60f);
                    Next(5);
                    return;

                case 5:
                {
                    if (!_pre.IsOpen) { Fail("事前教育面板未打开"); Next(9); return; }
                    if (_q >= 10) { Next(6); return; }
                    if (Time.realtimeSinceStartup - _phaseT < 1.9f) return;
                    var q = _pre.questions[_q];
                    int answer;
                    if (_wrongNeeded > 0) { answer = (q.correctIndex + 1) % 3; _wrongNeeded--; }
                    else answer = q.correctIndex;
                    _pre.AnswerOption(answer);
                    Say("第 " + (_q + 1) + " 题作答=" + (char)('A' + answer) + "，答对数=" + _pre.CorrectCount);
                    _q++;
                    if (_q == 3 && !_secondPass) ShotAt("g42v_03_pretraining_feedback.png", _cam.position, _cam.position + _cam.forward * 2f, 60f);
                    _phaseT = Time.realtimeSinceStartup;
                    return;
                }

                case 6:
                    if (Time.realtimeSinceStartup - _phaseT < 3.6f) return;
                    Check("第一遍答完 10 题", _pre.CorrectCount == 6, "答对=" + _pre.CorrectCount);
                    Check("6/10（60%）记为教学达标", _pre.Passed, "答对=" + _pre.CorrectCount + "/" + _pre.QuestionCount);
                    ShotAt("g42v_04_pretraining_passed.png", _cam.position, _cam.position + _cam.forward * 2f, 60f);
                    // 第二遍：只答对 3 题，验证「未达标也能继续进入演练」
                    _secondPass = true;
                    _q = 0;
                    _wrongNeeded = 7;
                    _pre.Restart();
                    _phaseT = Time.realtimeSinceStartup;
                    Next(7);
                    return;

                case 7:
                {
                    if (!_pre.IsOpen) return;
                    if (_q >= 10) { Next(8); return; }
                    if (Time.realtimeSinceStartup - _phaseT < 1.9f) return;
                    var q = _pre.questions[_q];
                    int answer = _wrongNeeded > 0 ? (q.correctIndex + 1) % 3 : q.correctIndex;
                    if (_wrongNeeded > 0) _wrongNeeded--;
                    _pre.AnswerOption(answer);
                    _q++;
                    _phaseT = Time.realtimeSinceStartup;
                    return;
                }

                case 8:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 3.6f) return;
                    Check("第二遍只答对 3/10", _pre.CorrectCount == 3, "答对=" + _pre.CorrectCount);
                    Check("未达标被标记为 Failed", _pre.Failed);
                    bool canContinue = false;
                    foreach (var t in _pre.GetComponentsInChildren<Text>(true))
                        if (t != null && (t.text.Contains("继续进入演练") || t.text.Contains("계속"))) canContinue = true;
                    Check("未达标仍给出「继续进入演练」按钮（不是挡住）", canContinue);
                    ShotAt("g42v_04b_pretraining_below_target.png", _cam.position, _cam.position + _cam.forward * 2f, 60f);
                    _pre.Close();
                    _start.ActuallyStart(1);
                    Next(9);
                    return;
                }

                case 9:
                    if (Time.realtimeSinceStartup - _phaseT < 2.6f) return;
                    Check("进入情景后处于「发现火灾」阶段", _dir.CurrentStage == DrillFlowDirector.Stage.Discovered,
                          "阶段=" + _dir.StageName);
                    Check("火灾警报作为背景音已响起", _alarm != null && _alarm.IsRinging);
                    ShotAt("g42v_05_fire_discovered.png", new Vector3(-2.60f, 1.62f, -2.60f), new Vector3(-5.60f, 1.10f, -4.40f), 70f);
                    Next(10);
                    return;

                case 10:
                {
                    if (_btn == null) { Fail("场景里没有手动报警按钮"); Next(12); return; }
                    _cam.SetParent(null, true);
                    _btn.player = _cam;
                    var plate = _btn.transform.position + new Vector3(0f, 1.26f, 0f);
                    var stand = plate + new Vector3(0f, 0.25f, -0.85f);
                    _cam.position = stand;
                    _cam.rotation = Quaternion.LookRotation((plate - stand).normalized, Vector3.up);
                    _standPos = stand;
                    _standRot = _cam.rotation;
                    ShotAt("g42v_06_alarm_button.png", stand, plate, 60f);
                    Next(11);
                    return;
                }

                case 11:
                    _cam.position = _standPos;
                    _cam.rotation = _standRot;
                    if (_btn.player == null) _btn.player = _cam;
                    if (Time.realtimeSinceStartup - _phaseT < 1.8f) return;
                    Check("注视手动报警按钮后按响", _btn.Triggered, "距离=" + _btn.PlayerDistance.ToString("F2"));
                    Check("按下后进入「警示与报告」阶段", (int)_dir.CurrentStage >= (int)DrillFlowDirector.Stage.Alerted,
                          "阶段=" + _dir.StageName);
                    Check("报警完成标记", _dir.AlarmReported);
                    ShotAt("g42v_07_alarm_pressed.png", _cam.position, _cam.position + _cam.forward * 2f, 60f);
                    Next(12);
                    return;

                case 12:
                    if (Time.realtimeSinceStartup - _phaseT < 1.6f) return;
                    Check("评估后进入「初期灭火（可选）」阶段",
                          (int)_dir.CurrentStage >= (int)DrillFlowDirector.Stage.Extinguishing,
                          "阶段=" + _dir.StageName);
                    Check("评估结论有文案", !string.IsNullOrEmpty(_dir.LastAssessment), _dir.LastAssessment);
                    Check("纸张情景判定为可尝试初期处置", _dir.CanAttemptInitial);
                    ShotAt("g42v_08_assess.png", _cam.position, _cam.position + _cam.forward * 2f, 60f);
                    Next(13);
                    return;

                case 13:
                {
                    // 核心新路径：不拿瓶、不灭火，直接走到安全出口 → 应判为成功疏散
                    Check("尚未喷射灭火器", !_dir.HasSprayed);
                    _cam.position = new Vector3(4.35f, 1.62f, 1.05f);      // 门内约 1 米处
                    _cam.rotation = Quaternion.LookRotation(new Vector3(1f, -0.05f, 0f), Vector3.up);
                    ShotAt("g42v_08c_walk_to_exit.png", _cam.position, new Vector3(7.4f, 1.15f, 1.02f), 70f);
                    _phaseT = Time.realtimeSinceStartup;
                    Next(14);
                    return;
                }

                case 14:
                    if (Time.realtimeSinceStartup - _phaseT < 1.6f) return;
                    Check("走到安全出口后自动转入疏散阶段", _dir.CurrentStage == DrillFlowDirector.Stage.Evacuating,
                          "阶段=" + _dir.StageName);
                    Check("记录「未灭火直接疏散」（成功路径）", _dir.EvacuatedWithoutExtinguishing);
                    Check("疏散时安全出口已解锁", _door != null && _door.IsUnlocked);
                    ShotAt("g42v_09_evacuate_direct.png", new Vector3(4.35f, 1.62f, 1.05f), new Vector3(7.4f, 1.15f, 1.02f), 70f);
                    Next(15);
                    return;

                case 15:
                    // 危险增加 → 立即停止并疏散（演示通道，与生产判定同一入口）
                    _dir.RaiseDangerAndEvacuate();
                    Check("危险增加通道已触发", _dir.DangerIncreased);
                    Check("疏散原因写明危险增加", _dir.EvacuationReason.Contains("危险增加"),
                          _dir.EvacuationReason);
                    Check("危险增加后仍处于疏散阶段", _dir.CurrentStage == DrillFlowDirector.Stage.Evacuating);
                    _phaseT = Time.realtimeSinceStartup;
                    Next(151);
                    return;

                case 151:
                    if (Time.realtimeSinceStartup - _phaseT < 0.9f) return;   // 等 Toast 淡入
                    ShotAt("g42v_09b_danger_increasing.png", _cam.position, _cam.position + _cam.forward * 3f, 70f, true);
                    Next(16);
                    return;

                case 16:
                {
                    var ap = _dir.assemblyPoint;
                    _cam.position = new Vector3(ap.x, 1.60f, ap.z);
                    // 背对建筑看向广场空地：结束面板落在空处，不会被门框/知识牌挡住
                    _cam.rotation = Quaternion.LookRotation(new Vector3(1f, -0.05f, -0.15f), Vector3.up);
                    Next(17);
                    return;
                }

                case 17:
                    if (Time.realtimeSinceStartup - _phaseT < 6.5f) return;   // 等 Toast 横幅自然消失，别挡住面板
                    Check("到达集合点后标记为已报告", _dir.ReportedAtAssembly);
                    Check("最终阶段 = 到达安全地点并报告", _dir.CurrentStage == DrillFlowDirector.Stage.Reported,
                          "阶段=" + _dir.StageName);
                    Check("本次应对判定为成功（未灭火直接疏散也算）", _dir.EndingSuccess);
                    Check("结束状态面板已弹出", _end != null && _end.IsOpen);
                    Check("结束面板记录了行为回顾", _end != null && _end.LastRecord.Contains("✓"),
                          _end != null ? _end.LastRecord.Replace("\n", " / ") : "");
                    Check("回顾里写明集合流程依现场计划", _end != null && _end.LastRecord.Contains("现场消防计划"));
                    ShotAt("g42v_10_assembly_report.png", _cam.position, _cam.position + _cam.forward * 3f, 70f, true);
                    Next(18);
                    return;

                case 18:
                    ShotAt("g42v_11_boards_cn_electrical.png",
                           new Vector3(9.30f, 1.55f, -3.10f), new Vector3(11.60f, 1.20f, -3.10f), 65f);
                    ShotAt("g42v_12_boards_cn_paper.png",
                           new Vector3(9.30f, 1.55f, 0.05f), new Vector3(11.60f, 1.20f, 0.05f), 65f);
                    ShotAt("g42v_13_boards_cn_pantry.png",
                           new Vector3(9.30f, 1.55f, 3.20f), new Vector3(11.60f, 1.20f, 3.20f), 65f);
                    Next(19);
                    return;

                case 19:
                    DrillLocale.SetLanguage(DrillLocale.Lang.Korean);
                    UIFont.Invalidate();
                    Next(20);
                    return;

                case 20:
                    if (Time.realtimeSinceStartup - _phaseT < 1.2f) return;
                    ShotAt("g42v_14_boards_korean.png",
                           new Vector3(9.30f, 1.55f, 0.05f), new Vector3(11.60f, 1.20f, 0.05f), 65f);
                    DrillLocale.SetLanguage(DrillLocale.Lang.Chinese);
                    UIFont.Invalidate();
                    Next(21);
                    return;

                case 21:
                    if (Time.realtimeSinceStartup - _phaseT < 1.2f) return;
                    Check("语言切回中文", DrillLocale.Chinese);
                    Check("三情景专属知识牌与通用知识墙并存",
                          Object.FindFirstObjectByType<ScenarioKnowledgeBoards>() != null &&
                          Object.FindFirstObjectByType<PlazaKnowledgeBoard>() != null);
                    Finish("DONE");
                    return;
            }
        }

        static void ShotAt(string name, Vector3 pos, Vector3 lookAt, float fov = 0f, bool hideBoards = false)
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) { Fail("截图无主相机：" + name); return; }
                var tr = cam.transform;
                var oldParent = tr.parent;
                var oldPos = tr.position;
                var oldRot = tr.rotation;
                if (oldParent != null) tr.SetParent(null, true);
                tr.position = pos;
                tr.rotation = Quaternion.LookRotation((lookAt - pos).normalized, Vector3.up);
                SetBoardsVisible(!hideBoards);   // 世界文字是 3D 网格，会穿透 UI 面板，必要时临时隐藏以便看清面板
                foreach (var ls in Object.FindObjectsByType<LanguageSelectScreen>(FindObjectsSortMode.None))
                    if (ls != null && ls.IsOpen) ls.ForcePlace();
                foreach (var ep in Object.FindObjectsByType<DrillEndPanel>(FindObjectsSortMode.None))
                    if (ep != null && ep.IsOpen) ep.ForcePlace();

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
                SetBoardsVisible(true);
                Say("截图 " + name);
            }
            catch (System.Exception e) { Fail("截图失败 " + name + "：" + e.Message); SetBoardsVisible(true); }
        }

        static void SetBoardsVisible(bool visible)
        {
            foreach (var b in Object.FindObjectsByType<PlazaKnowledgeBoard>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b != null && b.gameObject.activeSelf != visible) b.gameObject.SetActive(visible);
            foreach (var b in Object.FindObjectsByType<ScenarioKnowledgeBoards>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b != null && b.gameObject.activeSelf != visible) b.gameObject.SetActive(visible);
        }

        static void Finish(string reason)
        {
            Say("结果 " + reason + "，失败数=" + Fails.Count);
            var sb = new System.Text.StringBuilder();
            foreach (var s in Log) sb.AppendLine(s);
            sb.AppendLine();
            sb.AppendLine("FAIL_COUNT=" + Fails.Count);
            sb.AppendLine("PROBE_EXIT=" + (Fails.Count == 0 ? 0 : 1));
            File.WriteAllText(Path.Combine(ShotDir, "_round42_probe.txt"), sb.ToString(), new System.Text.UTF8Encoding(false));
            EditorApplication.update -= Tick;
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }
    }
}
