#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第 40 轮：约 62 秒玩家视角演示，重点展示本轮四项改动：
    ///   ① 纸张情景的 02 号瓶挂到西墙挂钩上（不再躺/卡在椅子柜子缝里）；
    ///   ② 全局烟雾明显（房间雾层 + 大厅全局雾）；
    ///   ③ 烟雾从西窗向外排（窗内导流 + 窗外排烟）；
    ///   ④ 安全出口打开后，烟从门内扩散到门外。
    /// 批处理：-executeMethod FireSafety.EditorTools.Round40Recorder.RunAll
    /// </summary>
    public static class Round40Recorder
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string FrameDir = "Logs/frames";
        const int W = 1600;
        const int H = 900;
        const float Fps = 24f;
        const float Duration = 62f;

        static DrillScenario _scenario;
        static StartScreen _start;
        static StageTwoFlow _stageTwo;
        static PlayerVitals _vitals;
        static ExitDoor _exitDoor;
        static Extinguisher _ext02;
        static Transform _cam, _handAnchor;
        static bool _diag;
        static RenderTexture _rt;
        static Texture2D _tex;
        static int _frame, _answered;
        static float _t0, _lastCapture = -1f, _answerT;
        static bool _entered, _uiShown, _grab, _quizClosed, _pin, _lever, _spray, _unlocked;
        static float _camFov = 72f;
        static Canvas _cdCanvas;
        static readonly System.Collections.Generic.List<GameObject> _tagRoots = new System.Collections.Generic.List<GameObject>();
        static bool _cleaned;

        public static void RunAll()
        {
            if (Directory.Exists(FrameDir)) Directory.Delete(FrameDir, true);
            Directory.CreateDirectory(FrameDir);
            _frame = 0; _lastCapture = -1f; _answered = 0; _entered = false;
            _uiShown = _grab = _quizClosed = _pin = _lever = _spray = _unlocked = false;
            _answered = 0; _answerT = 0f;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[REC40] " + s); }

        static void Tick()
        {
            if (!Application.isPlaying) return;
            if (!_entered) { _entered = true; _t0 = Time.realtimeSinceStartup; Say("进入 Play，开始录制"); }
            float t = Time.realtimeSinceStartup - _t0;
            if (t > Duration + 5f) { Finish("TIMEOUT"); return; }
            if (t < 0.5f) return;
            if (!Resolve()) return;
            Step(t);
            if (t >= 1.6f)
            {
                float due = Mathf.Floor((t - 1.6f) * Fps) / Fps + 1.6f;
                if (due > _lastCapture + 0.0001f && _frame < (int)(Fps * Duration) + 8)
                {
                    _lastCapture = due;
                    Capture();
                }
            }
            if (t >= Duration) Finish("DONE");
        }

        static bool Resolve()
        {
            if (_scenario == null) _scenario = UnityEngine.Object.FindFirstObjectByType<DrillScenario>();
            if (_start == null) _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
            if (_stageTwo == null) _stageTwo = UnityEngine.Object.FindFirstObjectByType<StageTwoFlow>();
            if (_vitals == null) _vitals = UnityEngine.Object.FindFirstObjectByType<PlayerVitals>();
            if (_exitDoor == null) _exitDoor = UnityEngine.Object.FindFirstObjectByType<ExitDoor>();
            if (_ext02 == null)
            {
                var go = GameObject.Find("Extinguisher02");
                if (go != null) _ext02 = go.GetComponent<Extinguisher>();
            }
            var cam = Camera.main;
            if (cam != null && _cam == null)
            {
                _cam = cam.transform;
                _cam.SetParent(null, true);
                cam.fieldOfView = 72f;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                var rig = GameObject.Find("XR Origin") ?? GameObject.Find("XR Rig");
                if (rig != null) rig.transform.position = new Vector3(0f, -200f, 0f);
            }
            if (_handAnchor == null && _cam != null)
            {
                var go = new GameObject("Rec40Hand");
                go.transform.SetParent(_cam, false);
                go.transform.localPosition = new Vector3(0.26f, -0.28f, 0.62f);
                _handAnchor = go.transform;
            }
            if (!_cleaned)
            {
                _cleaned = true;
                var cd = UnityEngine.Object.FindFirstObjectByType<DrillCountdown>();
                if (cd != null) _cdCanvas = cd.GetComponentInChildren<Canvas>(true);
                KillKindTags();
                DrillToast.HideNow();
                var coach = _ext02.GetComponent<ExtinguisherCoach>();
                if (coach != null) coach.enabled = false;   // 演示视频里彻底压掉「距离火源 xx 米」Toast
                if (_vitals != null)
                    foreach (var c in _vitals.GetComponentsInChildren<Canvas>(true))
                        if (c.transform != _cam) c.gameObject.SetActive(false);
                Say("已隐藏种类牌与生命值 HUD");
            }
            return _cam != null && _scenario != null && _ext02 != null;
        }

        static void KillKindTags()
        {
            var rootField = typeof(ExtinguisherKindTag).GetField("_root",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            foreach (var tag in UnityEngine.Object.FindObjectsByType<ExtinguisherKindTag>(FindObjectsSortMode.None))
            {
                tag.enabled = false;
                var root = rootField != null ? rootField.GetValue(tag) as Transform : null;
                if (root != null && !_tagRoots.Contains(root.gameObject)) _tagRoots.Add(root.gameObject);
            }
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c != null && c.gameObject.name.StartsWith("KindTag") && !_tagRoots.Contains(c.gameObject))
                    _tagRoots.Add(c.gameObject);
            foreach (var go in _tagRoots)
                if (go != null && go.activeSelf) go.SetActive(false);

            // 灭火器世界牌（TextMesh 信息牌 + 箭头 + 地面圈）也不进视频
            var fArrow = typeof(ExtinguisherGuideMarker).GetField("_arrow",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var fRing = typeof(ExtinguisherGuideMarker).GetField("_ringRoot",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            foreach (var m in UnityEngine.Object.FindObjectsByType<ExtinguisherGuideMarker>(FindObjectsSortMode.None))
            {
                m.enabled = false;
                var lt = m.LabelTransform;
                if (lt != null && lt.gameObject.activeSelf) lt.gameObject.SetActive(false);
                var arrow = fArrow != null ? fArrow.GetValue(m) as GameObject : null;
                if (arrow != null && arrow.activeSelf) arrow.SetActive(false);
                var ring = fRing != null ? fRing.GetValue(m) as GameObject : null;
                if (ring != null && ring.activeSelf) ring.SetActive(false);
            }
        }

        static void Cam(Vector3 pos, Vector3 look)
        {
            if (_cam == null) return;
            _cam.position = pos;
            _cam.rotation = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
            AimHand(look);
        }

        static void AimHand(Vector3 look)
        {
            if (_handAnchor == null || _cam == null) return;
            Vector3 dir = (look - _cam.position).normalized;
            // 第 40 轮：原来把手锚点顶到眼前 0.30 米，手持瓶会把整个画面糊死（实测）。
            // 改成相对相机固定摆在右下方 0.72 米，和自检探针的手位一致。
            _handAnchor.localPosition = new Vector3(0.40f, -0.52f, 0.95f);
            _handAnchor.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        static void CamLerp(Vector3 a, Vector3 b, Vector3 la, Vector3 lb, float k)
        {
            k = Mathf.Clamp01(k); k = k * k * (3f - 2f * k);
            Cam(Vector3.Lerp(a, b, k), Vector3.Lerp(la, lb, k));
        }

        static void Step(float t)
        {
            if (t >= 8.6f && _camFov != 72f) { _camFov = 72f; Camera.main.fieldOfView = 72f; }
            KillKindTags();
            DrillToast.HideNow();   // 教练/流程 Toast 会弹「距离火源 xx 米」，演示视频里统一按掉
            if (_cdCanvas != null && _cdCanvas.gameObject.activeSelf != (t >= 8.4f))
                _cdCanvas.gameObject.SetActive(t >= 8.4f);

            // 1.6–8.4 s：开始界面（操作演练按钮下的教学类型标注）
            if (t < 8.4f)
            {
                if (_camFov != 42f) { _camFov = 42f; Camera.main.fieldOfView = 42f; }
                CamLerp(new Vector3(2.95f, 1.58f, 3.05f), new Vector3(2.85f, 1.58f, 3.20f),
                        new Vector3(4.60f, 1.35f, 1.40f), new Vector3(4.55f, 1.35f, 1.55f),
                        (t - 1.6f) / 6.8f);
                if (!_uiShown && t > 2.0f) { _uiShown = true; DrillLocale.SetEnglish(false); _start.SetVisible(true); }
                return;
            }

            // 8.4–9 s：进入纸张情景（情景二）
            if (t < 9.0f)
            {
                _start.SetVisible(false);
                _scenario.StartFormal(-1, 1);
                _vitals.ResetVitals();
                Say("进入纸张情景");
                return;
            }

            // 9–14.5 s：玩家从走道看起火房间 —— 全局烟 + 房间烟
            if (t < 14.5f)
            {
                float k = (t - 9.0f) / 5.5f;
                CamLerp(new Vector3(-3.20f, 1.68f, -2.40f), new Vector3(-5.30f, 1.66f, -1.90f),
                        new Vector3(-5.71f, 1.20f, -4.54f), new Vector3(-5.90f, 1.10f, -4.40f), k);
                return;
            }

            // 14.5–17.5 s：镜头推向挂在西墙上的 02 号瓶（退到房间中部，瓶不全屏）
            if (t < 17.5f)
            {
                float k = (t - 14.5f) / 3.0f;
                CamLerp(new Vector3(-3.20f, 1.62f, -2.90f), new Vector3(-5.15f, 1.55f, -2.05f),
                        new Vector3(-6.33f, 0.85f, -1.20f), new Vector3(-6.33f, 0.85f, -1.20f), k);
                return;
            }

            // 17.5–19 s：伸手取瓶，答题面板弹出
            if (t < 19.0f)
            {
                Cam(new Vector3(-5.25f, 1.55f, -2.15f), new Vector3(-6.33f, 0.95f, -1.20f));
                if (!_grab)
                {
                    _grab = true;
                    _ext02.quizRequired = true;
                    _ext02.grab.GrabExternal(_handAnchor, HandSide.Right);
                    Say("取瓶 IsHeld=" + _ext02.grab.IsHeld + " 面板=" + _ext02.quizPanel.IsOpen
                        + " 距离提示=" + _ext02.quizPanel.DistanceHintVisible);
                }
                return;
            }

            // 19–27 s：答题 5 题（每题约 1.5 秒），全程不出现「距灭火器」
            if (t < 27.0f)
            {
                Cam(new Vector3(-5.20f, 1.56f, -2.30f), new Vector3(-6.10f, 1.20f, -1.30f));
                if (_ext02.quizPanel.IsOpen && _answered < 5 && t - _answerT > 1.5f)
                {
                    _answerT = t;
                    int idx = _ext02.quizPanel.questions != null && _answered < _ext02.quizPanel.questions.Count
                        ? _ext02.quizPanel.questions[_answered].correctIndex : 0;
                    _ext02.quizPanel.AnswerOption(idx);
                    _answered++;
                    Say("答题 " + _answered + "/5，距离提示=" + _ext02.quizPanel.DistanceHintVisible);
                }
                return;
            }

            // 27–30 s：关面板、拔销、压把，走到灭火点位
            if (t < 30.0f)
            {
                float k = (t - 27.0f) / 3.0f;
                CamLerp(new Vector3(-5.20f, 1.56f, -2.30f), new Vector3(-5.80f, 1.50f, -2.85f),
                        new Vector3(-5.71f, 1.05f, -3.10f), new Vector3(-5.71f, 1.00f, -4.54f), k);
                if (!_quizClosed && t > 27.1f) { _quizClosed = true; if (_ext02.quizPanel.IsOpen) _ext02.quizPanel.Close(); }
                if (!_pin && t > 27.4f) { _ext02.PullPin(); _pin = true; Say("拔销=" + _ext02.PinPulled); }
                if (!_lever && t > 27.9f && _ext02.handleLever != null) { _ext02.handleLever.PressExternal(); _lever = true; }
                return;
            }

            // 30–38 s：对准火焰根部喷射，直到火灭
            if (t < 38.0f)
            {
                float k = Mathf.Clamp01((t - 30.0f) / 1.2f);
                CamLerp(new Vector3(-5.80f, 1.50f, -2.85f), new Vector3(-5.85f, 1.46f, -2.95f),
                        new Vector3(-5.71f, 1.00f, -4.54f), new Vector3(-5.71f, 1.05f, -4.54f), k);
                AimNozzle(_ext02, _scenario.deviceFire);
                if (!_spray && t > 30.6f)
                {
                    _spray = _ext02.TryStartSpray();
                    Say("开始喷射=" + _spray + " 火焰进度=" + Left(_scenario.deviceFire) + "%");
                }
                if (!_diag && t > 31.0f)
                {
                    _diag = true;
                    float dn = (_ext02.nozzle && _scenario.deviceFire != null)
                        ? Vector3.Distance(_ext02.nozzle.position, _scenario.deviceFire.AimPoint) : -1f;
                    Say("瓶根=" + _ext02.transform.position.ToString("F2")
                        + " nozzle=" + (_ext02.nozzle ? _ext02.nozzle.position.ToString("F2") : "null")
                        + " 距火=" + dn.ToString("F2"));
                }
                if (_scenario.deviceFire != null && _scenario.deviceFire.IsExtinguished) Say("纸张火已灭（t=" + t.ToString("F1") + "）");
                return;
            }

            // 38–43 s：火灭后烟雾自动收，回看房间
            if (t < 43.0f)
            {
                if (_ext02.IsSpraying) _ext02.StopSprayExternal();
                float k = (t - 38.0f) / 5.0f;
                CamLerp(new Vector3(-5.80f, 1.50f, -2.85f), new Vector3(-3.60f, 1.66f, -5.30f),
                        new Vector3(-5.71f, 1.05f, -4.54f), new Vector3(-5.71f, 1.05f, -4.54f), k);
                return;
            }

            // 43–45 s：处理门前火并解锁安全出口（烟从门内开始往外扩散）
            if (t < 45.5f)
            {
                Cam(new Vector3(2.60f, 1.62f, 1.02f), new Vector3(5.25f, 1.10f, 1.02f));
                if (!_unlocked)
                {
                    _unlocked = true;
                    if (_stageTwo.stageTwoFire != null && _stageTwo.stageTwoFire.gameObject.activeInHierarchy)
                        _stageTwo.stageTwoFire.Extinguish();
                    _exitDoor.Unlock();
                    Say("安全出口解锁 IsOpen=" + _exitDoor.IsOpen);
                }
                return;
            }

            // 45.5–52 s：门内侧看烟从门洞往门外推
            if (t < 52.0f)
            {
                float k = (t - 45.5f) / 6.5f;
                CamLerp(new Vector3(2.60f, 1.62f, 1.02f), new Vector3(3.90f, 1.62f, 1.02f),
                        new Vector3(5.25f, 1.10f, 1.02f), new Vector3(5.25f, 1.10f, 1.02f), k);
                return;
            }

            // 52–58 s：穿过门洞到门外，回看扩散开的烟
            if (t < 58.0f)
            {
                float k = (t - 52.0f) / 6.0f;
                CamLerp(new Vector3(3.90f, 1.62f, 1.02f), new Vector3(7.20f, 1.62f, 1.60f),
                        new Vector3(5.25f, 1.10f, 1.02f), new Vector3(3.60f, 1.10f, 1.02f), k);
                return;
            }

            // 58–62 s：走到门外广场，收尾
            if (t < Duration)
            {
                float k = (t - 58.0f) / 4.0f;
                CamLerp(new Vector3(7.20f, 1.62f, 1.60f), new Vector3(9.40f, 1.62f, 1.20f),
                        new Vector3(3.60f, 1.10f, 1.02f), new Vector3(3.20f, 1.20f, 1.00f), k);
            }
        }

        static void AimNozzle(Extinguisher ext, FireSource fire)
        {
            if (ext == null || fire == null || ext.nozzle == null) return;
            ext.AlignNozzleTo(fire.AimPoint - ext.nozzle.position);
        }

        static float Left(FireSource f) { return f != null ? f.progress01 * 100f : -1f; }

        static void Capture()
        {
            if (_rt == null)
            {
                _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                _rt.Create();
                _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            }
            var cam = Camera.main;
            if (cam == null) return;
            cam.targetTexture = _rt;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.Render();
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            _frame++;
            File.WriteAllBytes(Path.Combine(FrameDir, "f" + _frame.ToString("D5") + ".jpg"), _tex.EncodeToJPG(92));
        }

        static void Finish(string reason)
        {
            Say("录制结束 " + reason + "，帧数=" + _frame);
            try { if (_rt != null) _rt.Release(); } catch { }
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        }
    }
}
#endif

