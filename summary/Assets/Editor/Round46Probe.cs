// 第 46 轮玩家视角验收：
// ① 北窗（电源总闸旁）已整扇删除、墙洞封成实墙，从玩家站位看过去是一整面墙、射线命中实墙；
// ② 本次身份随机（普通员工 / 初期应对负责人）且显示在 HUD 阶段行；
// ③ 七阶段课程口径：Pre-briefing → Detection & Alert → Decision → A/B → Assembly & Reporting → Debriefing；
// ④ 普通员工被强制走 B（不尝试灭火），初期应对负责人才允许 A；
// ⑤ 事后反馈（Debriefing）记录身份 / 判断 / 操作 / 路线。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using FireSafety;

namespace FireSafety.EditorTools
{
    public static class Round46Probe
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

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            _phase = 0; _phaseT = 0; _entered = false; _t0 = 0;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G46] " + s); Log.Add(s); }
        static void Fail(string s) { if (Fails.Contains(s)) return; Debug.LogError("[G46] × " + s); Log.Add("× " + s); Fails.Add(s); }
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
                    if (Time.realtimeSinceStartup - _phaseT < 1.4f) return;
                    _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
                    if (_start != null) _start.ActuallyStart(0);
                    Next(2);
                    return;
                }

                case 2:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 3.0f) return;
                    // ① 北窗删除 + 实墙
                    Check("北窗整扇删除（Window_North / WindowWall_North / Daylight_North 都不存在）",
                        GameObject.Find("Window_North") == null
                        && GameObject.Find("WindowWall_North") == null
                        && GameObject.Find("Daylight_North") == null);
                    var solid = GameObject.Find("WindowWall_North_Solid");
                    var r = solid != null ? solid.GetComponentInChildren<MeshRenderer>() : null;
                    Check("北墙封成整块实墙（x −4.75~−2.75、y 0~3）",
                        r != null && r.bounds.min.x <= -4.70f && r.bounds.max.x >= -2.80f
                        && r.bounds.min.y <= 0.05f && r.bounds.max.y >= 2.95f,
                        r != null ? string.Format("x[{0:F2},{1:F2}] y[{2:F2},{3:F2}] z{4:F2}",
                            r.bounds.min.x, r.bounds.max.x, r.bounds.min.y, r.bounds.max.y, r.bounds.center.z) : "null");
                    bool hit = Physics.Raycast(new Vector3(-3.65f, 1.85f, 2.60f), Vector3.forward,
                        2.0f, ~0, QueryTriggerInteraction.Ignore);
                    Check("朝原北窗位置打射线命中实墙（不再有窗洞/天空）", hit);
                    Shot("g46v_north_wall.png", new Vector3(-4.60f, 1.62f, 2.30f), new Vector3(-3.65f, 1.85f, 3.95f), 60f);
                    Shot("g46v_north_switch.png", new Vector3(-3.10f, 1.62f, 1.60f), new Vector3(-3.30f, 1.60f, 3.90f), 65f);
                    Next(3);
                    return;
                }

                case 3:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                    // ② 身份随机 + ③ 阶段名
                    _flow = UnityEngine.Object.FindFirstObjectByType<DrillFlowDirector>();
                    Check("场景里能取到 DrillFlowDirector", _flow != null);
                    if (_flow == null) { Next(4); return; }
                    Check("本次演练已分配身份", _flow.RoleAssigned, _flow.RoleName);
                    Check("身份为普通员工或初期应对负责人",
                        _flow.AssignedRole == DrillFlowDirector.DrillRole.Employee
                        || _flow.AssignedRole == DrillFlowDirector.DrillRole.ResponseLead);
                    Check("当前阶段已进入 Detection & Alert / Decision",
                        _flow.CurrentStage == DrillFlowDirector.Stage.Discovered
                        || _flow.CurrentStage == DrillFlowDirector.Stage.Alerted
                        || _flow.CurrentStage == DrillFlowDirector.Stage.Assessed, _flow.StageName);
                    string hud = ScreenTexts();
                    var scen = DrillScenario.Instance;
                    string stateLine = scen != null ? scen.StateText() : "";
                    Check("HUD 状态行带上身份与阶段（玩家可见）",
                        (stateLine.Contains(_flow.RoleName) && stateLine.Contains("阶段"))
                        || (hud.Contains(_flow.RoleName) && hud.Contains("阶段")),
                        Truncate(stateLine + " || " + hud, 110));
                    Shot("g46v_hud_role.png", new Vector3(-5.50f, 1.62f, 0.40f), new Vector3(-3.60f, 1.55f, 3.20f), 70f);
                    Next(4);
                    return;
                }

                case 4:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.4f) return;
                    // ④ 预教学四主题 + 选项随机仍在
                    var pre = PreTrainingQuiz.Instance;
                    Check("场景里能取到事前教育面板", pre != null);
                    if (pre == null) { Next(5); return; }
                    pre.Open(_cam, null, 0);
                    string txt = ScreenTexts();
                    Check("Pre-briefing 面板列出四项主题（职责 / 报警装置 / 出口与集合点 / 灭火器类型与用法）",
                        txt.Contains("职责") && txt.Contains("报警装置") && txt.Contains("出口与集合点") && txt.Contains("灭火器类型"));
                    Check("选项随机排列仍在（当前排列为 3 个不同下标）",
                        pre.CurrentPermutation[0] != pre.CurrentPermutation[1]
                        && pre.CurrentPermutation[1] != pre.CurrentPermutation[2]
                        && pre.CurrentPermutation[0] != pre.CurrentPermutation[2],
                        "" + pre.CurrentPermutation[0] + pre.CurrentPermutation[1] + pre.CurrentPermutation[2]);
                    Shot("g46v_prebrief_topics.png", new Vector3(-5.20f, 1.62f, 1.60f), new Vector3(-3.70f, 1.70f, 3.40f), 60f);
                    pre.Close();
                    Next(5);
                    return;
                }

                case 5:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                    // ⑤ 决策分支：普通员工强制 B；负责人按条件
                    ResetAssessment();
                    _flow.SetRole(DrillFlowDirector.DrillRole.Employee);
                    _flow.ForceStage(DrillFlowDirector.Stage.Alerted);
                    Next(6);
                    return;
                }

                case 6:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                    Check("普通员工：判定为不具备初期处置权限（走 B 立即疏散）",
                        !_flow.RoleAllowsAttack && !_flow.CanAttemptInitial,
                        "CanAttemptInitial=" + _flow.CanAttemptInitial);
                    Check("普通员工：判定文案写明「普通员工不尝试灭火」",
                        _flow.LastAssessment.Contains("普通员工"), Truncate(_flow.LastAssessment, 80));
                    Shot("g46v_decision_employee.png", new Vector3(-5.30f, 1.62f, 2.60f), new Vector3(-2.40f, 1.40f, 3.60f), 65f);

                    ResetAssessment();
                    _flow.SetRole(DrillFlowDirector.DrillRole.ResponseLead);
                    _flow.ForceStage(DrillFlowDirector.Stage.Alerted);
                    Next(7);
                    return;
                }

                case 7:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                    Check("初期应对负责人：允许按条件评估（角色门槛已放行）", _flow.RoleAllowsAttack);
                    Say("负责人评估：" + _flow.LastAssessment + "（CanAttemptInitial=" + _flow.CanAttemptInitial + "）");
                    Shot("g46v_decision_lead.png", new Vector3(-5.30f, 1.62f, 2.60f), new Vector3(-2.40f, 1.40f, 3.60f), 65f);

                    // ⑥ 事后反馈（Debriefing）
                    _flow.ForceStage(DrillFlowDirector.Stage.Reported);
                    Next(8);
                    return;
                }

                case 8:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.2f) return;
                    Check("报告后进入 Debriefing（事后反馈）阶段",
                        _flow.CurrentStage == DrillFlowDirector.Stage.Debriefed, _flow.StageName);
                    Check("事后反馈记录包含身份 / 判断 / 操作 / 路线四条",
                        _flow.DebriefRecord.Contains("身份") && _flow.DebriefRecord.Contains("判断")
                        && _flow.DebriefRecord.Contains("操作") && _flow.DebriefRecord.Contains("路线"),
                        Truncate(_flow.DebriefRecord, 120));
                    Check("结束页行为回顾里带 Debriefing 段落",
                        _flow.ReportRecord.Contains("事后反馈") || _flow.ReportRecord.Contains("Debriefing"));
                    Shot("g46v_debrief.png", new Vector3(-3.50f, 1.62f, 0.60f), new Vector3(-2.20f, 1.40f, 1.20f), 65f);
                    Next(9);
                    return;
                }

                case 9:
                    Finish("完成");
                    return;
            }
        }

        static void ResetAssessment()
        {
            try
            {
                var f1 = typeof(DrillFlowDirector).GetField("_assessed", BindingFlags.NonPublic | BindingFlags.Instance);
                var f2 = typeof(DrillFlowDirector).GetField("_assessedToast", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f1 != null) f1.SetValue(_flow, false);
                if (f2 != null) f2.SetValue(_flow, false);
            }
            catch (Exception e) { Fail("重置评估状态失败：" + e.Message); }
        }

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
                File.WriteAllText(Path.Combine(ShotDir, "_round46_probe.txt"),
                    string.Join("\n", Log.ToArray()) + "\n\nFAIL_COUNT=" + Fails.Count + "\nPROBE_EXIT=" + (Fails.Count == 0 ? 0 : 1) + "\n");
            }
            catch (Exception e) { Debug.LogError("[G46] 报告写入失败：" + e.Message); }
            Debug.Log("[G46] " + tag + "，失败数=" + Fails.Count);
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
