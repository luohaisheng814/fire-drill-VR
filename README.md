# fire-drill-VR

办公室消防知识演练 VR 项目（Unity）

## 项目简介

基于 Unity（6000.6.0f1）+ XR Interaction Toolkit（3.6.1）开发的办公室消防知识 VR 演练项目。
玩家在办公室场景中完成「警报响起 → 寻找灭火器 → 常识考核 → 拔销 → 压把 → 喷射灭火 → 沿箭头撤离到大厅」的完整流程，
结束后弹出成绩单。同一份工程支持 **VR 双手柄** 与 **桌面鼠标键盘** 两条完整输入链。

## 玩法流程

1. **开始界面**：世界空间面板锚定在右手手柄正前方 1.0 米、上抬 0.15 米；可点「操作教学」看分页引导，或直接「开始演练」。
2. **操作教学（引导页）**：4 页（演练目标 / 桌面操作 / VR 手柄操作 / 安全考核规则），末页按钮变为「开始演练」。
3. **警报与倒计时**：开始后警笛响起、背景音乐切换为警报、火源点燃，倒计时面板出现在手柄正上方 0.42 米处（上限 3 分钟）。
4. **寻找灭火器**：地面引导箭头指向灭火器，视线进入会高亮。
5. **常识考核（门控）**：右键单击灭火器弹出 5 题 ABC 问答，每题 10 秒、超时算错，答对 60%（3/5）才能拿起灭火器。
6. **规范操作**：拿起 → 左键单击拔销（保险销可拔时会轻微脉动提示）→ 左键双击压把 → 右键双击喷射，持续对准火焰根部约 2.5 秒火焰熄灭。
7. **阶段一结算**：火焰熄灭后立即弹出完成面板，倒计时暂停，可选择「立刻结算」或「继续往下」。
8. **撤离到大厅**：选择「继续往下」后地面箭头切换指向大厅，走到 0.6 米内自动判定到达。
9. **结束页**：成功（到达大厅）或失败（倒计时耗尽）都会弹出结束页，显示总用时/剩余时间、答对题数、火源处理数、完成步骤，可「重新演练」或「关闭」。

## 主要功能

- **手柄锚定 UI**：开始界面（前 1.0 m / 上 0.15 m）、倒计时（上 0.42 m / 前 0.12 m）、引导页（前 1.0 m / 上 0.14 m）都跟随右手手柄并朝向玩家（`StartScreen.cs` / `DrillCountdown.cs` / `OnboardingScreen.cs`）
- **引导页与结束页**：分页教学 + 成功/失败成绩单（`OnboardingScreen.cs` / `EndingScreen.cs`）
- **9 步顺序状态机**：①身处火场 → ②警报 → ③确认火点 → ④移动到灭火器 → ⑤拿起 → ⑥拔销 → ⑦压把 → ⑧喷射 → ⑨扑灭，`enforceOrder` 禁止跳步（`FireDrillManager.cs`）
- **限时问答门控**：5 题 × 10 秒、60% 通过才允许拔销与喷射，答错给知识点讲解（`ExtinguisherQuizPanel.cs`）
- **粒子累积灭火**：喷射锥内逐帧累积，达阈值 2.5 秒立即清空火焰粒子、灯光归零并上报 ⑨（`Extinguisher.cs` / `FireSource.cs`）
- **两阶段地面箭头**：警报后指向灭火器；阶段一完成后指向大厅，到达 0.6 m 触发结束页（`GuidanceArrows.cs`）
- **桌面鼠标完整操作链**：右键单击灭火器=答题/拿起、左键单击=拔销、左键双击=压把、右键双击=喷射/停止、P=放下、回车/空格=开始、问答 1/2/3（`DesktopDrillInput.cs`）
- **背景音乐与音效**：办公环境音 → 火灾警笛交叉淡入，拾取/拔销/喷射/答对/答错/通关等全套音效（`BackgroundMusic.cs`）
- **抓取手型匹配**：抓取时手部姿态与物体外形匹配（`GrabPoseMatcher.cs`）

## VR 头显接入（第 21 轮）

真机头显接上后无需改场景，直接按下面的输入映射操作；无头显时同一套逻辑走「虚拟驱动」通道，可批处理自测。

| 操作 | 桌面（键鼠） | VR（手柄） |
|---|---|---|
| 移动 | W/A/S/D | 左摇杆推杆（`moveSpeed 2.2 m/s`，蹲姿 1.05 m/s） |
| 转向 | 鼠标移动 | 右摇杆瞬转（`snapAngle 45°`，冷却 0.35 s） |
| 抓取灭火器 | 右键单击 | Grip（XRI Select） |
| 拔销 / 压把 | 左键单击 / 双击 | Trigger 第一下拔销、第二下压把并保持 |
| 停止喷射 | 右键双击 | 松开 Trigger |
| 开始 / 翻页 / 继续 | 回车 / 空格 | A 键 |
| 答题选项 | 数字 1 / 2 / 3 | B / X 键切换，A 确认 |
| 蹲下 / 站起 | Ctrl | 左摇杆按下 |

- **OpenXR 运行时**：`Assets/Editor/VROpenXRConfig.cs` 装配 OpenXRLoader 与 11 项手柄 profile（Oculus Touch / Meta Quest Touch Plus / Vive / Index / 微软 MR 等），批处理入口 `FireSafety.EditorTools.VROpenXRConfig.RunFromBatch`，校验入口 `...VROpenXRConfig.Verify`
- **输入层**：`Assets/Scripts/FireSafety/VRDrillInput.cs`（按键上升沿 + `_triggerConsumed` 锁，避免一按到底连拔销带压把）
- **位移与瞬转**：`XRDrillLocomotion.cs`（CharacterController 位移/重力/蹲姿，`ResolveReferences()` 自动补组件）
- **世界空间 HUD**：`VRDrillHud.cs`（视线下方提示条，模态面板打开时自动让位）+ `HudPlacement.cs`（把面板夹进视野的纯函数）
- **相机层级**：`FireSafetySetup.FindOrCreateRig()` 只认 `XR Origin (Drill)` 直接子物体下的 Camera Offset，并自建 Main Camera，避免移动逻辑挪的是另一套 rig


## 目录结构

```
Assets/
├── Scenes/FireSafetyOffice.unity   # 主场景（一键搭建脚本已装配好全部 VR 设施）
├── Scripts/FireSafety/             # 玩法脚本（开始界面/引导页/结束页/倒计时/箭头/问答/灭火/输入）
├── Editor/FireSafetySetup.cs       # 一键搭建（菜单 FireSafety/一键搭建并保存场景）
├── Editor/FireSafetySelfTest.cs    # 自动化自检（298 项断言，菜单 FireSafety/自检）
├── Editor/VROpenXRConfig.cs        # OpenXR 运行时与手柄 profile 装配/校验
├── Editor/VRPlayProbe.cs           # 玩家视角验收探针（播放态离屏渲染 5 张截图 + 全流程断言）
└── Resources/Sounds/               # 背景音乐与音效（BGM/ SFX/）

Docs/
├── 01-先行研究调查-既有消防训练程序.md   # 既有消防训练程序（VR/商用/韩国现状）调查
└── 02-本项目差异化与独创性.md           # 与既有程序的逐维对比、独创性主张与边界
```

## 一键搭建与自检

场景已通过 `FireSafety/一键搭建并保存场景` 菜单（或批处理 `-executeMethod FireSafety.EditorTools.FireSafetySetup.RunSetupFromMenu`）完成装配，包括：

- XR Origin（Drill）+ 左右手柄控制器 + XR 交互管理器
- 用户移动（CharacterController + XRDrillLocomotion）
- StartScreen 开始界面（锚定右手手柄正前方；文案可在 StartScreen 组件的 Instructions 字段修改）
- OnboardingScreen 引导页（4 页教学，锚定右手手柄）
- FireAlarm 火灾警报（警笛 + 红色爆闪灯）
- BackgroundMusic 背景音乐切换
- DrillCountdown 倒计时（锚定右手手柄正上方）
- GuidanceArrows 地面引导箭头（灭火器 → 大厅两阶段）
- StageOneCompletePanel 阶段一完成面板
- EndingScreen 结束页（到大厅=成功 / 倒计时耗尽=失败）
- Extinguisher01 / Extinguisher02（抓取/问答/拔销/压把/粒子喷射/手型匹配）
- EventSystem + XRUIInputModule（手柄射线点按 UI）
- DesktopDrillInput（桌面鼠标完整操作链）

自检（批处理）：`-executeMethod FireSafety.EditorTools.FireSafetySelfTest.RunAll`，成功判据为日志出现 `[TEST] ==== 全部通过 ====`、`error CS` 为 0、退出码为 0。

## 先行研究与差异化

- 既有消防训练程序调查（含 Go Fire、FLAIM、LION BullsEye、ADMS/XVR、国内 VR 仿真等 13 个来源）见
  [`summary/Docs/01-先行研究调查-既有消防训练程序.md`](summary/Docs/01-先行研究调查-既有消防训练程序.md)
- 本项目差异化与独创性论证（含对比表、六项可验证差异、主张边界与待补实验）见
  [`summary/Docs/02-本项目差异化与独创性.md`](summary/Docs/02-本项目差异化与独创性.md)
