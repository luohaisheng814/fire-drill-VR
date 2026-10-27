# fire-drill-VR

办公室消防知识演练 VR 项目（Unity）

## 项目简介

基于 Unity（6000.6.0f1）+ XR Interaction Toolkit（3.6.1）开发的办公室消防知识 VR 演练项目。
玩家在办公室场景中完成「警报响起 → 寻找灭火器 → 拾取灭火器 → 灭火器常识问答 → 对准火源灭火」的完整消防演练流程。

## 玩法流程

1. **开始界面**：显示游戏标题与玩法说明，点击「开始演练」按钮后火灾警报响起（背景音切换为警笛声），火源点燃。
2. **观察与寻找**：警报响起后，玩家需要观察四周，寻找办公室中的灭火器。
3. **拾取灭火器**：靠近灭火器后按下手柄抓取键（Grip）拾取。拾取时会弹出 2~3 道灭火器常识问答（上风方向 / 火焰根部 / 压力表绿色区），回答完毕后自动关闭。
4. **拔销与灭火**：对准后方火源，按住喷射键灭火，火焰熄灭即演练完成。

## 主要功能

- **开始界面与动画**：标题脉动、按钮呼吸、面板淡入淡出、火焰点燃动画（`StartScreen.cs` / `UIFX.cs`）
- **用户移动**：左手摇杆平滑移动、右手摇杆平滑/瞬时转身、桌面调试支持 WASD + Q/E（`XRDrillLocomotion.cs`）
- **背景音乐与音效**：办公环境音 → 火灾警报切换（交叉淡入淡出），拾取/放下/拔销/喷射/答对/答错/通关等全套音效（`BackgroundMusic.cs`）
- **灭火器常识问答**：拾取时弹出问答面板，答错显示正确答案与讲解（`ExtinguisherQuizPanel.cs`）
- **抓取手型匹配**：抓取灭火器时手部姿态与物体外形匹配，手指按骨骼自动弯曲（`GrabPoseMatcher.cs`）

## 目录结构

```
Assets/
├── Scenes/FireSafetyOffice.unity   # 主场景（一键搭建脚本已装配好全部 VR 设施）
├── Scripts/FireSafety/             # 玩法脚本（开始界面/移动/问答/手型匹配/音效等）
├── Editor/FireSafetySetup.cs       # 一键搭建编辑器脚本（菜单 FireSafety/一键搭建并保存场景）
└── Resources/Sounds/               # 背景音乐与音效（BGM/ SFX/）
```

## 一键搭建

场景已通过 `FireSafety/一键搭建并保存场景` 菜单（或批处理 `-executeMethod FireSafety.EditorTools.FireSafetySetup.RunSetupFromMenu`）完成装配，包括：

- XR Origin（Drill）+ 左右手柄控制器 + XR 交互管理器
- 用户移动（CharacterController + XRDrillLocomotion）
- StartScreen 开始界面（玩法说明文字可在场景中 StartScreen 组件的 Instructions 字段修改）
- FireAlarm 火灾警报（警笛 + 红色爆闪灯）
- BackgroundMusic 背景音乐切换
- Extinguisher01 / Extinguisher02（抓取/问答/拔销/喷射/手型匹配）
- EventSystem + XRUIInputModule（手柄射线点按 UI）

## 玩法说明文字（待补充）

开始界面的玩法说明文本框内容由项目作者后续补充，当前为占位文案，可在场景中
`StartScreen` 组件的 **Instructions** 字段直接修改。
