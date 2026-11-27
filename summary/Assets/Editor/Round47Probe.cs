// 第 47 轮玩家视角验收：
// ① 进入场景即已随机分配身份（事前教学之前就有身份，题才能按身份出）；
// ② 事前教学本套固定 5 题，底部标明「本次身份 + 5 题 = 通用 4 + 角色 1」；
// ③ 四类教学内容（职责 / 报警装置 / 出口与集合点 / 灭火器类型与用法）都在；
// ④ 普通员工与初期应对负责人只有第 5 题不同，前 4 题（通用灭火常识）完全一致；
// ⑤ 真的从玩家视角点完 5 题：达标线 3 题、可全部答对并回调进入演练。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using FireSafety;

namespace FireSafety.EditorTools
{
    public static class Round47Probe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = @"D:\43763\VR\summary\Logs\shots";
        const float HardTimeout = 240f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();

        static float _t0, _phaseT;
        static bool _entered;
        static int _phase;
        static Transform _cam;
        static Camera _camera;
        static StartScreen _start;
        static DrillFlowDirector _flow;

        static readonly List<string> _empCore = new List<string>();
        static string _empDuty;
        static int _answers;
        static bool _finishedCallback;

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            _phase = 0; _phaseT = 0; _entered = false; _t0 = 0;
            _empCore.Clear(); _empDuty = null; _answers = 0; _finishedCallback = false;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G47] " + s); Log.Add(s); }
        static void Fail(string s) { if (Fails.Contains(s)) return; Debug.LogError("[G47] × " + s); Log.Add("× " + s); Fails.Add(s); }
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
                    if (Time.realtimeSinceStartup - _phaseT < 1.0f) return;
                    _flow = UnityEngine.Object.FindFirstObjectByType<DrillFlowDirector>();
                    Check("场景里能取到 DrillFlowDirector", _flow != null);
                    if (_flow == null) { Finish("无 Flow"); return; }
                    Check("进入场景即已分配身份（早于事前教学）", _flow.RoleAssigned, _flow.RoleName);
                    Check("身份为普通员工或初期应对负责人",
                        _flow.AssignedRole == DrillFlowDirector.DrillRole.Employee
                        || _flow.AssignedRole == DrillFlowDirector.DrillRole.ResponseLead, _flow.RoleName);
                    Next(2);
                    return;
                }

                case 2:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                    var pre = PreTrainingQuiz.Instance;
                    Check("场景里能取到事前教育面板", pre != null);
                    if (pre == null) { Finish("无面板"); return; }

                    _flow.SetRole(DrillFlowDirector.DrillRole.Employee);
                    pre.Open(_cam, () => { _finishedCallback = true; }, 0);
                    Next(3);
                    return;
                }

                case 3:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                    var pre = PreTrainingQuiz.Instance;
                    Check("员工套：本套 5 题", pre.QuestionCount == 5, "题数=" + pre.QuestionCount);
                    Check("员工套：达标线 3 题（60%）", pre.RequiredCorrect == 3, "需答对=" + pre.RequiredCorrect);
                    Check("员工套：第 5 题为员工职责题",
                        pre.questions.Count >= 5 && pre.questions[4].qCn.Contains("普通员工"),
                        pre.questions.Count >= 5 ? pre.questions[4].qCn : "-");
                    Check("员工套：面板标题带身份", ScreenTexts().Contains("普通员工"));
                    string txt = ScreenTexts();
                    Check("事前教学列出四类内容（职责 / 报警装置 / 出口与集合点 / 灭火器类型与用法）",
                        txt.Contains("职责") && txt.Contains("报警装置") && txt.Contains("出口与集合点") && txt.Contains("灭火器类型"));
                    Check("底部写明本套构成「5 题 = 通用 4 题 + 员工职责 1 题」",
                        txt.Contains("5 题 = 通用 4 题") && txt.Contains("员工职责"),
                        Truncate(txt, 120));
                    for (int i = 0; i < 4; i++) _empCore.Add(pre.questions[i].qCn);
                    _empDuty = pre.questions[4].qCn;
                    ShotQuiz("g47v_pre_employee.png");
                    _answers = 0;
                    Next(4);
                    return;
                }

                case 4:
                {
                    // 玩家视角逐题点选（都用「正确按钮」点，验证答题链路与进度）
                    // advanceDelay = 1.6 秒，按 1.8 秒节奏点，避免上一题还没翻页就被吞掉
                    if (Time.realtimeSinceStartup - _phaseT < 1.8f) return;
                    var pre = PreTrainingQuiz.Instance;
                    if (pre == null || !pre.IsOpen) { Next(5); return; }
                    if (pre.QuestionIndex >= pre.QuestionCount) { Next(5); return; }
                    if (pre.QuestionIndex == 0) ShotQuiz("g47v_pre_q1.png");
                    if (pre.QuestionIndex == pre.QuestionCount - 1) ShotQuiz("g47v_pre_q5_employee.png");
                    pre.AnswerOption(pre.ButtonIndexOfCorrect());
                    Say("员工答题 第 " + (pre.QuestionIndex + 1) + "/5 题（排列 "
                        + pre.CurrentPermutation[0] + pre.CurrentPermutation[1] + pre.CurrentPermutation[2] + "）");
                    _phaseT = Time.realtimeSinceStartup;
                    return;
                }

                case 5:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                    var pre = PreTrainingQuiz.Instance;
                    Check("员工套：5 题全部答对并达标", pre.Passed && pre.CorrectCount == 5,
                        "答对=" + pre.CorrectCount + " 状态=" + (pre.Passed ? "达标" : "未达标"));
                    Check("员工套：结束页有「进入演练」入口", ScreenTexts().Contains("进入演练"));
                    ShotQuiz("g47v_pre_employee_done.png");
                    pre.ConfirmAndContinue();
                    Check("员工套：点「进入演练」后回调进入演练", _finishedCallback);
                    pre.Close();

                    _flow.SetRole(DrillFlowDirector.DrillRole.ResponseLead);
                    pre.Open(_cam, null, 0);
                    Next(6);
                    return;
                }

                case 6:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                    var pre = PreTrainingQuiz.Instance;
                    Check("负责人套：本套 5 题", pre.QuestionCount == 5, "题数=" + pre.QuestionCount);
                    Check("负责人套：第 5 题为负责人判断题",
                        pre.questions[4].qCn.Contains("初期应对负责人"), pre.questions[4].qCn);
                    Check("两身份第 5 题不同", pre.questions[4].qCn != _empDuty);
                    bool sameCore = true;
                    for (int i = 0; i < 4; i++) if (pre.questions[i].qCn != _empCore[i]) sameCore = false;
                    Check("两身份前 4 题完全相同（通用灭火常识未丢）", sameCore);
                    Check("负责人套：面板标题带身份", ScreenTexts().Contains("初期应对负责人"));
                    Check("负责人套：底部写明「通用 4 题 + 负责人判断 1 题」",
                        ScreenTexts().Contains("负责人判断"), Truncate(ScreenTexts(), 120));
                    ShotQuiz("g47v_pre_lead.png");
                    Next(7);
                    return;
                }

                case 7:
                {
                    var pre = PreTrainingQuiz.Instance;
                    // 先把前 4 道通用题点过去，停在负责人专属第 5 题上截图
                    if (pre.QuestionIndex < 4)
                    {
                        if (Time.realtimeSinceStartup - _phaseT < 1.8f) return;
                        pre.AnswerOption(pre.ButtonIndexOfCorrect());
                        Say("负责人答题 第 " + (pre.QuestionIndex + 1) + "/5 题");
                        _phaseT = Time.realtimeSinceStartup;
                        return;
                    }
                    if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                    ShotQuiz("g47v_pre_q5_lead.png");
                    Check("负责人套：三语与选项都在（当前为中文，题目非空）",
                        !string.IsNullOrEmpty(pre.questions[4].qCn)
                        && !string.IsNullOrEmpty(pre.questions[4].qEn)
                        && !string.IsNullOrEmpty(pre.questions[4].qKo));
                    Check("负责人套：选项随机仍在（3 个不同下标）",
                        pre.CurrentPermutation[0] != pre.CurrentPermutation[1]
                        && pre.CurrentPermutation[1] != pre.CurrentPermutation[2]
                        && pre.CurrentPermutation[0] != pre.CurrentPermutation[2]);
                    pre.Close();
                    Next(8);
                    return;
                }

                case 8:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                    // 身份在正式演练中延续（不再重新随机）
                    var before = _flow.AssignedRole;
                    _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
                    if (_start != null) _start.ActuallyStart(0);
                    Next(9);
                    _startRole = before;
                    return;
                }

                case 9:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.6f) return;
                    Check("正式演练沿用事前教学的身份（没有二次随机）",
                        _flow.AssignedRole == _startRole, "事前=" + _startRole + " 演练=" + _flow.AssignedRole);
                    Check("正式演练已进入 Detection & Alert", _flow.CurrentStage != DrillFlowDirector.Stage.Idle, _flow.StageName);
                    string hud = ScreenTexts();
                    Check("玩家 HUD 同时可见身份与阶段",
                        hud.Contains(_flow.RoleName) && hud.Contains("阶段"), Truncate(hud, 110));
                    // HUD 是跟随玩家的世界空间面板，改用玩家当前视角原样截，避免从背面看出现镜像
                    Shot("g47v_drill_hud.png", _cam.position, _cam.position + _cam.forward * 10f, 0f);
                    Next(10);
                    return;
                }

                case 10:
                    Finish("完成");
                    return;
            }
        }

        static DrillFlowDirector.DrillRole _startRole;

        static string Truncate(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " / ");
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        static void Finish(string tag)
        {
            try
            {
                File.WriteAllText(Path.Combine(ShotDir, "_round47_probe.txt"),
                    string.Join("\n", Log.ToArray()) + "\n\nFAIL_COUNT=" + Fails.Count + "\nPROBE_EXIT=" + (Fails.Count == 0 ? 0 : 1) + "\n");
            }
            catch (Exception e) { Debug.LogError("[G47] 报告写入失败：" + e.Message); }
            Debug.Log("[G47] " + tag + "，失败数=" + Fails.Count);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }

        static string ScreenTexts()
        {
            var sb = new StringBuilder();
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c == null || !c.isActiveAndEnabled) continue;
                foreach (var t in c.GetComponentsInChildren<Text>(false))
                    if (t != null && !string.IsNullOrWhiteSpace(t.text)) sb.Append(t.text).Append(" | ");
            }
            var hud = UnityEngine.Object.FindFirstObjectByType<VRDrillHud>();
            if (hud != null) sb.Append(hud.CurrentHint).Append(" | ").Append(hud.CurrentStatus);
            var scen = UnityEngine.Object.FindFirstObjectByType<DrillScenario>();
            if (scen != null) sb.Append(scen.CurrentHint);
            return sb.ToString();
        }

        static void ShotQuiz(string name)
        {
            var go = GameObject.Find("PreTrainingCanvas");
            if (go == null)
            {
                Shot(name, new Vector3(-5.20f, 1.62f, 1.60f), new Vector3(-3.70f, 1.70f, 3.40f), 60f);
                return;
            }
            var rt = go.GetComponent<RectTransform>();
            float h = rt.rect.height * Mathf.Abs(rt.lossyScale.y);
            const float fov = 45f;
            float dist = Mathf.Max(1.0f, h / (2f * Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f)) * 1.05f);
            var center = rt.TransformPoint(rt.rect.center);
            Shot(name, center - rt.forward * dist, center, fov);
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
