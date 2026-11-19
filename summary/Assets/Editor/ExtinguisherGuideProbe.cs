#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第35轮玩家第一视角验收：
    /// 中文/英文开始界面 → 新指导标识 → 点击太远提示 → 走近答题/拿取
    /// → 持瓶太远/太近/距离合适 → 实际喷射灭火 → 灭火后提示。
    /// 批处理：-executeMethod FireSafety.EditorTools.ExtinguisherGuideProbe.Run
    /// </summary>
    public static class ExtinguisherGuideProbe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = "Logs/shots";
        const float HardTimeout = 120f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static int _phase;
        static float _t0, _phaseT;
        static bool _shotGuideZh, _shotGuideEn;

        static DrillScenario _sc;
        static Extinguisher _ext;
        static FireSource _fire;
        static DesktopDrillInput _input;
        static StartScreen _start;
        static Transform _rig, _cam, _rightHand;
        static CharacterController[] _ccs;

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            _phase = 0; _phaseT = 0f; _shotGuideZh = _shotGuideEn = false;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G35] " + s); Log.Add(s); }
        static void Fail(string s)
        {
            if (Fails.Contains(s)) return;
            Debug.LogError("[G35] ✘ " + s);
            Log.Add("✘ " + s);
            Fails.Add(s);
        }
        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) Say("✔ " + name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
            else Fail(name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
        }
        static void Shot(string name)
        {
            // 同步渲染截屏：ScreenCapture 是异步的，批处理退出前会一张都来不及落盘。
            try
            {
                var cam = Camera.main;
                if (cam == null) return;
                int w = Mathf.Max(640, Screen.width), h = Mathf.Max(360, Screen.height);
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var oldTarget = cam.targetTexture;
                var oldEye = cam.stereoTargetEye;
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
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                Say("玩家视角截图 " + name + "（" + w + "×" + h + "）");
            }
            catch (System.Exception e) { Fail("截图失败 " + name + "：" + e.Message); }
        }
        static void Next(int p) { _phase = p; _phaseT = Time.realtimeSinceStartup; Say("→ 阶段 " + p); }

        static void Tick()
        {
            if (!Application.isPlaying)
            {
                if (Time.realtimeSinceStartup - _t0 > HardTimeout) EditorApplication.Exit(2);
                return;
            }
            if (_t0 <= 0f) _t0 = Time.realtimeSinceStartup + 2f;

            if (_phase == 0)
            {
                if (!Resolve()) return;
                if (Time.realtimeSinceStartup - _t0 < 1.8f) return;

                DrillLocale.SetEnglish(false);
                Shot("vr35_start_zh.png");
                _start.ToggleLanguage();
                Next(10);
                return;
            }

            if (_phase == 10)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.9f) return;
                Shot("vr35_start_en.png");
                int englishTexts = 0;
                foreach (var t in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))
                    if (t.text != null && (t.text.Contains("Office Fire Safety") || t.text.Contains("START DRILL")
                                          || t.text.Contains("Scenario 1") || t.text.Contains("中文")))
                        englishTexts++;
                Check("开始界面 English 按钮可切换并显示英文标题/按钮", DrillLocale.English, "命中英文文本 " + englishTexts + " 处");
                Check("开始界面出现「中文」回切按钮", englishTexts > 0);
                _start.ToggleLanguage();
                Next(11);
                return;
            }

            if (_phase == 11)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                Check("再次点击回切到中文", !DrillLocale.English);
                _sc.StartFormal(0, 2);       // 茶水间：无断电门控，便于验证距离提示与实喷
                _start.SetVisible(false);
                var countdown = Object.FindObjectOfType<DrillCountdown>();
                if (countdown != null) countdown.StopAndReset();
                var panel = Object.FindObjectOfType<StageOneCompletePanel>();
                if (panel != null && panel.IsOpen) panel.HidePanel();
                _ext = _sc.deviceExtinguisher;
                _fire = _sc.deviceFire;
                if (_ext == null || _fire == null) { Fail("茶水间灭火器/火源为空"); Finish(1); return; }
                Say("茶水间：灭火器=" + _ext.transform.position.ToString("F2")
                    + "，火=" + _fire.transform.position.ToString("F2")
                    + "，距离=" + Vector3.Distance(new Vector3(_ext.transform.position.x, 0, _ext.transform.position.z),
                        new Vector3(_fire.transform.position.x, 0, _fire.transform.position.z)).ToString("F2"));
                Next(1);
                return;
            }

            if (_phase == 1)
            {
                if (Time.realtimeSinceStartup - _phaseT < 6.0f) return;
                // 站在瓶旁玩家视角看新指导标识
                Vector3 extP = _ext.transform.position;
                TeleportTo(new Vector3(extP.x + 0.1f, 0f, extP.z + 1.55f));
                LookAtPoint(new Vector3(extP.x, 1.05f, extP.z));
                Next(15);
                return;
            }

            if (_phase == 15)
            {
                if (Time.realtimeSinceStartup - _phaseT < 1.0f) return;
                Vector3 extP = _ext.transform.position;
                LookAtPoint(new Vector3(extP.x, 1.05f, extP.z));
                if (!_shotGuideZh)
                {
                    Shot("vr35_guide_zh.png");
                    _shotGuideZh = true;
                    var guide = _ext.GetComponent<ExtinguisherGuideMarker>();
                    Check("新指导标识存在且类型匹配", guide != null && guide.kind == _ext.kind);
                    Check("旧类型标签与光环组件已不存在",
                        _ext.GetComponent<ExtinguisherKindTag>() == null && _ext.GetComponent<ExtinguisherHighlight>() == null);
                    if (guide != null && guide.LabelTransform != null)
                    {
                        Vector3 sp = Camera.main.WorldToScreenPoint(guide.LabelTransform.position);
                        Say("中文信息牌：active=" + guide.LabelTransform.gameObject.activeInHierarchy
                            + "，位置=" + guide.LabelTransform.position.ToString("F2")
                            + "，屏幕=" + sp.ToString("F0")
                            + "，缩放=" + guide.LabelTransform.lossyScale.x.ToString("F4")
                            + "，欧拉=" + guide.LabelTransform.eulerAngles.ToString("F0")
                            + "，相机=" + Camera.main.transform.position.ToString("F2"));
                        foreach (var r in guide.LabelTransform.GetComponentsInChildren<Renderer>(true))
                        {
                            Say("  渲染器 " + r.name + "：enabled=" + r.enabled
                                + "，可见=" + r.isVisible
                                + "，层=" + r.gameObject.layer
                                + "，包围盒中心=" + r.bounds.center.ToString("F2")
                                + "，尺寸=" + r.bounds.size.ToString("F2"));
                        }
                    }
                    Next(12);
                }
                return;
            }

            if (_phase == 12)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                DrillLocale.SetEnglish(true);
                Next(13);
                return;
            }

            if (_phase == 13)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.9f) return;
                LookAtPoint(new Vector3(_ext.transform.position.x, 1.05f, _ext.transform.position.z));
                Shot("vr35_guide_en.png");
                _shotGuideEn = true;
                DrillLocale.SetEnglish(false);
                Next(14);
                return;
            }

            if (_phase == 14)
            {
                if (Time.realtimeSinceStartup - _phaseT < 1.0f) return;
                LookAtPoint(new Vector3(_ext.transform.position.x, 1.05f, _ext.transform.position.z));
                Shot("vr35_guide_zh_again.png");
                Next(2);
                return;
            }

            if (_phase == 2)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.7f) return;
                // 远距离点击：站在茶水间门口内，避免隔墙点品牌
                Vector3 extP = _ext.transform.position;
                TeleportTo(new Vector3(5.80f, 0f, -0.20f));
                LookAtPoint(new Vector3(extP.x, 1.0f, extP.z));
                float d = Vector3.Distance(_cam.position, _ext.transform.position);
                bool picked = _input.TryPickupExtinguisher(_ext);
                Check("远距离点击灭火器被拒绝（距离 " + d.ToString("F2") + " 米）", !picked);
                Check("远距离不弹问答", _ext.quizPanel == null || !_ext.quizPanel.IsOpen);
                Check("远距离提示包含「太远」", DrillToast.Instance != null && DrillToast.Instance.CurrentText.Contains("太远"),
                    DrillToast.Instance != null ? DrillToast.Instance.CurrentText : "无提示");
                Next(3);
                return;
            }

            if (_phase == 3)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.35f) return;
                LookAtPoint(new Vector3(_ext.transform.position.x, 1.0f, _ext.transform.position.z));
                Shot("vr35_pick_too_far.png");
                // 走近到 1.7 米：应弹问答
                Vector3 extP = _ext.transform.position;
                TeleportTo(new Vector3(extP.x, 0f, extP.z + 1.70f));
                LookAtPoint(new Vector3(extP.x, 1.0f, extP.z));
                bool opened = _input.TryPickupExtinguisher(_ext);
                Check("走近后点击进入问答（未直接吸附）", !opened);
                Check("问答面板已打开", _ext.quizPanel != null && _ext.quizPanel.IsOpen);
                Next(4);
                return;
            }

            if (_phase == 4)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.45f) return;
                LookAtPoint(new Vector3(_ext.transform.position.x, 1.0f, _ext.transform.position.z));
                Shot("vr35_pick_quiz.png");
                _ext.MarkQuizPassed();
                if (_ext.quizPanel != null) _ext.quizPanel.Close();
                bool grabbed = _input.TryPickupExtinguisher(_ext);
                Check("问答通过后在 2.2 米内成功拿起", grabbed && _ext.grab.IsHeld);
                if (!_ext.PinPulled) _ext.PullPin();
                if (_ext.handleLever != null) _ext.handleLever.PressExternal();
                Check("拔销与压把可操作", _ext.PinPulled && _ext.handleLever != null && _ext.handleLever.PressedOnce);
                Next(5);
                return;
            }

            if (_phase == 5)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                // 持瓶太远：喷嘴到火源约 4 米
                TeleportTo(new Vector3(1.20f, 0f, -0.74f));
                LookAtPoint(_fire.AimPoint);
                Aim();
                Next(51);
                return;
            }

            if (_phase == 51)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;   // 等灭火器跟手一帧
                Aim();
                var band = _ext.coach.EvaluateNow(out string msg, out float dist);
                _ext.coach.ShowBlockedReason(msg);
                Say("太远评估：" + band + " / " + dist.ToString("F2") + " 米 / " + msg);
                Check("持瓶 4 米时判定为太远", band == ExtinguisherCoach.FireBand.TooFar);
                Check("太远提示含实际距离与走近建议", msg.Contains("太远") && msg.Contains("米"));
                Next(6);
                return;
            }

            if (_phase == 6)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                LookAtPoint(_fire.AimPoint);
                Shot("vr35_fire_too_far.png");
                // 太近：0.8 米
                TeleportTo(new Vector3(5.50f, 0f, -0.74f));
                LookAtPoint(_fire.AimPoint);
                Aim();
                Next(61);
                return;
            }

            if (_phase == 61)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                Aim();
                var band = _ext.coach.EvaluateNow(out string msg, out float dist);
                _ext.coach.ShowBlockedReason(msg);
                Say("太近评估：" + band + " / " + dist.ToString("F2") + " 米 / " + msg);
                Check("持瓶 0.8 米时判定为太近", band == ExtinguisherCoach.FireBand.TooClose);
                Check("太近提示建议后退到 1.6~2.2 米", msg.Contains("太近") && msg.Contains("1.6"));
                Next(7);
                return;
            }

            if (_phase == 7)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                LookAtPoint(_fire.AimPoint);
                Shot("vr35_fire_too_close.png");
                // 合适距离：喷嘴到火源约 2 米
                TeleportTo(new Vector3(3.50f, 0f, -0.74f));
                LookAtPoint(_fire.AimPoint);
                Aim();
                Next(71);
                return;
            }

            if (_phase == 71)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                Aim();
                var band = _ext.coach.EvaluateNow(out string msg, out float dist);
                _ext.coach.ShowBlockedReason(msg);
                Say("合适评估：" + band + " / " + dist.ToString("F2") + " 米 / " + msg);
                Check("喷嘴到火源约 2 米时判定为合格距离", band == ExtinguisherCoach.FireBand.Good);
                Shot("vr35_good_distance.png");
                Next(8);
                return;
            }

            if (_phase == 8)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                Aim();
                bool spray = _ext.TryStartSpray();
                Check("距离合适时可以起喷", spray && _ext.IsSpraying);
                Next(9);
                return;
            }

            if (_phase == 9)
            {
                Aim();
                float t = Time.realtimeSinceStartup - _phaseT;
                if (t > 0.5f && !_shotSpray) { Shot("vr35_spraying.png"); _shotSpray = true; }
                if (_fire.IsExtinguished)
                {
                    _ext.StopSprayExternal();
                    Shot("vr35_extinguished.png");
                    Check("实际喷射把火扑灭", _fire.IsExtinguished, "剩余进度 " + _fire.progress01.ToString("F2"));
                    Say("灭火完成：Band=" + _ext.coach.Band + " · 火=" + _fire.IsExtinguished);
                    Finish(Fails.Count == 0 ? 0 : 1);
                    return;
                }
                if (t > 8f) { _ext.StopSprayExternal(); Fail("喷射 8 秒未灭火"); Finish(1); }
                return;
            }
        }

        static bool _shotSpray;

        static bool Resolve()
        {
            _sc = Object.FindObjectOfType<DrillScenario>();
            _input = DesktopDrillInput.Instance != null ? DesktopDrillInput.Instance : Object.FindObjectOfType<DesktopDrillInput>();
            _start = StartScreen.Instance != null ? StartScreen.Instance : Object.FindObjectOfType<StartScreen>();
            var origin = Object.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            _rig = origin != null ? origin.transform : null;
            _cam = Camera.main != null ? Camera.main.transform : null;
            if (_sc == null || _input == null || _start == null || _cam == null || _rig == null) return false;
            foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (tr.name == "RightHand Controller") _rightHand = tr;
            _ccs = _rig.GetComponentsInChildren<CharacterController>(true);
            return _rightHand != null;
        }

        static void TeleportTo(Vector3 p)
        {
            Vector3 cam = _cam.position;
            Vector3 delta = new Vector3(p.x - cam.x, 0f, p.z - cam.z);
            if (_ccs != null) foreach (var c in _ccs) c.enabled = false;
            _rig.position += delta;
            Physics.SyncTransforms();
            if (_ccs != null) foreach (var c in _ccs) c.enabled = true;
            Say("机位 " + p.ToString("F2") + " · 眼高 " + (_cam.position.y - p.y).ToString("F2") + " 米");
        }

        static void LookAtPoint(Vector3 p)
        {
            Vector3 d = p - _cam.position;
            Vector3 flat = d; flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return;
            float wantYaw = Quaternion.LookRotation(flat.normalized, Vector3.up).eulerAngles.y;
            float curYaw = _cam.eulerAngles.y;
            _rig.rotation = Quaternion.Euler(0f, wantYaw - curYaw, 0f) * _rig.rotation;
            float pitch = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            var camOffset = _cam.parent;
            if (camOffset != null && camOffset != _rig)
                camOffset.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            _cam.localRotation = Quaternion.identity;
        }

        static void Aim()
        {
            if (_ext == null || _ext.nozzle == null || _fire == null) return;
            Vector3 dir = _fire.AimPoint - _ext.nozzle.position;
            if (dir.sqrMagnitude < 0.0001f) return;
            // 桌面输入下由视线中心瞄准，等价于玩家用鼠标把准星压到火焰根部。
            _ext.AlignNozzleTo(dir);
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            if (_ext != null) _ext.StopSprayExternal();
            File.WriteAllText(Path.Combine(ShotDir, "_guideprobe_35.txt"),
                string.Join("\n", Log) + "\nFAIL_COUNT=" + Fails.Count + "\nPROBE_EXIT=" + code + "\n",
                new System.Text.UTF8Encoding(false));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
#endif
