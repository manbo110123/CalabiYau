# 第一分卷：起步准备、资源、Package 与 CharacterLab

> 包含课次：S00～S06  
> 当前入口：S00  
> 本卷目标：在不破坏 Tank 基线的前提下，得到一个资源来源清楚、Package 配置稳定、层级正确的人物实验场

---

## 使用提醒

不要一口气做完本卷。每个 `Sxx` 都是一轮单独对话。开始某课时，把总目录中的新对话模板发给我，并明确课次编号。

本卷结束时我们只要求“人物项目骨架正确”，不要求联网人物已经能跑。过早同时处理移动、相机、动画和网络，会让新手很难判断一个抖动究竟来自哪里。

---

# S00：保存当前 Tank 基线

## 本课目标

把 2026-08-29 已验收的 Tank 版本保存成真正可以回退和对比的基线。完成后，人物主线即使走错也不会丢失原有网络成果。

## 开始前你要准备

- 关闭不需要的 Unity 实例，只保留当前主工程；
- 确认你愿意把当前已完成内容保存为一个 Git 节点；
- 告诉我当前是否还有不希望提交的私人测试资源或临时改动；
- 准备一个用于保存 Windows Build 的目录名称，例如 `TankBaseline_20260829`。

## 我会先检查

1. `git status`、当前分支和最近提交；
2. 所有修改文件，区分项目成果、临时文件和用户未完成工作；
3. Unity 版本、Package 锁定状态和构建场景；
4. .NET 服务器测试；
5. 现有 Build 是否和当前源码一致。

我不会在没有得到你明确同意时自动提交、打标签或删除文件。

## 我负责完成

- 运行服务器自动测试并保存结果摘要；
- 检查文档与实际版本是否一致；
- 给出建议的提交范围、提交信息和标签名；
- 必要时补一个简短的基线记录文档；
- 在你同意后执行 Git 提交/标签，或把精确命令交给你执行。

## 你在 Unity 中操作

1. 打开 `Assets/Scenes/SampleScene.unity`。
2. 观察 Console，确认没有新的红色错误。
3. 运行 `Tools > CalabiYau > Validate Unity 2022 Migration`。
4. 启动服务器并进入 Play，做一次本地移动、开火、撞墙和重生。
5. 如果当前 Build 已过期，打开 `File > Build Settings`：
   - Platform 选择 `Windows, Mac, Linux`；
   - Target Platform 选择 `Windows`；
   - Architecture 选择 `x86_64`；
   - 确认 `SampleScene` 在 Scenes In Build 中；
   - 使用 `Development Build` 生成基线包。
6. 用 Editor + Build 做双客户端冒烟，确认它们进入同一服务器。

## 本课要理解的概念

- **基线**：已经证明可用、后续能回来的版本，不只是“某个文件夹备份”。
- **提交**：保存源文件状态；**标签**：给某个提交一个容易识别的里程碑名字；**Build**：证明当时二进制能运行。
- 自动测试保证规则没有明显回归，人工测试保证场景引用、渲染和真实交互仍然工作。

## 完成标准

- [ ] 工作区每项改动的归属都已说明；
- [ ] 服务器自动测试通过；
- [ ] Unity 迁移检查通过且 Console 无新增红错；
- [ ] Editor + Windows Build 双客户端冒烟通过；
- [ ] 有明确的 Git 提交或你确认采用的替代保存方式；
- [ ] 基线标签、Build 路径和测试日期被记录。

## 本课不要做

- 不安装新 Package；
- 不导入人物模型；
- 不删除 Tank 代码或 SampleScene；
- 不顺手重构网络客户端。

## 本课新对话补充语句

```text
当前工程中我希望保留但暂不提交的文件有：……
我是否授权创建 Git 提交和标签：是/否（先检查后再决定也可以）
```

## 完成记录

```text
状态：进行中
完成日期：
基线提交：
基线标签：
Build 路径：Builds/Unity2022Migration（2026-08-28 的迁移验证包；尚未生成 S00 专用基线包）
测试结果：2026-08-29，Server.Tests 测试程序通过；开发者已完成服务器 + 两客户端人工联机测试，确认功能均正常生效。
已知问题：当前工作区含既有文档改动、删除、新增 3C 文档和测试生成物，尚未区分并保存为 Git 基线；需要在授权后创建提交和标签。
```

---

# S01：锁定第一个垂直切片

## 本课目标

把“二次元第三人称动作射击”收缩为一个能完成的第一版。我们会决定第一名角色、第一把枪、第一张训练场和第一种动作能力，不在开发过程中不断换目标。

## 开始前你要准备

你不需要先找到最终素材，只需要回答偏好：

- 更偏写实比例、日系赛璐璐还是 Q 版；
- 角色使用步枪、手枪还是其他 Hitscan 武器；
- 镜头希望偏近肩、标准 TPS 还是略远；
- 冲刺希望是滑步、短闪、翻滚还是普通加速；
- 你最想展示的一项技能是什么。

如果没有明确答案，我会默认：日系赛璐璐人形、步枪、标准右肩 TPS、地面短冲刺、一个直线投射物技能。

## 我负责完成

- 根据现有网络底座约束提出一套最小垂直切片；
- 明确 P0 必做、可选和禁止扩张项；
- 建立一页 `VerticalSliceBrief`，记录体验目标和验收画面；
- 把需要寻找的素材转换为可搜索的规格，而不是只给宽泛关键词。

## 我们共同确认的第一版范围

默认范围如下，除非本课明确修改：

- 1 个可操作 Humanoid 二次元角色；
- 1 把 Hitscan 步枪；
- Idle、八方向移动、跳跃、落地、瞄准、开火、换弹、受击、死亡、冲刺动画；
- 探索相机、越肩瞄准、相机防穿墙、左右肩切换；
- 1 个短冲刺和 1 个投射物技能；
- 1 张小型训练竞技场；
- 两个客户端、服务器权威、本地预测、远端插值和 100/200 ms 弱网演示；
- 画面风格先做到轮廓、颜色和反馈统一，不在第一版自研复杂卡通渲染器。

## 完成标准

- [ ] 一页垂直切片说明已经写入项目；
- [ ] 角色、枪械、动作、VFX、音效分别有明确最低规格；
- [ ] 明确哪些资源允许先用占位；
- [ ] 明确第一版不做联机大厅、皮肤系统、复杂连招和多英雄；
- [ ] 你能用两三句话描述最终演示过程。

## 完成记录

```text
状态：未开始
角色风格：
主武器：
相机风格：
第一动作能力：
第一技能：
不做清单：
```

---

# S02：安装 Input System 与 Cinemachine

## 本课目标

只安装人物主线马上需要的两个 Package，并确保 Tank 场景没有因此损坏。Animation Rigging 留到 S26。

## 开始前你要准备

- S00 已有可回退基线；
- Unity Console 没有红色错误；
- 保存所有打开场景；
- 如果 Unity 提示重启编辑器，你可以接受重启。

## 当前工程事实

截至本手册建立时：

- 已安装 URP 14.0.12；
- 尚未安装 Input System；
- 尚未安装 Cinemachine；
- 尚未安装 Animation Rigging。

开始本课时我会重新读取 `Packages/manifest.json` 和 `packages-lock.json`，不能只相信这段历史记录。

## 你在 Unity 中安装 Input System

1. 打开 `Window > Package Manager`。
2. 左上角 Packages 下拉框选择 `Unity Registry`。
3. 搜索 `Input System`。
4. 选择 Unity 2022.3 提供的兼容稳定版本，点击 `Install`。
5. 若出现是否启用新输入后端并重启的提示，先截图告诉我；通常选择启用并重启。
6. 重启后打开 `Edit > Project Settings > Player`。
7. 在 `Other Settings > Configuration > Active Input Handling` 中，迁移阶段使用 `Both`，保证旧 Tank 输入暂时仍可工作。

`Both` 是过渡方案。人物输入稳定后再单独讨论是否切为 `Input System Package (New)`，这一课不删除旧输入代码。

## 你在 Unity 中安装 Cinemachine

1. 再次打开 `Window > Package Manager`。
2. 选择 `Unity Registry`，搜索 `Cinemachine`。
3. 安装 Unity 2022.3 提供的兼容稳定版本。
4. 安装后只确认菜单与组件可用，不创建最终相机。
5. 保存项目并等待所有脚本重新编译。

## 我负责完成

- 检查实际解析版本并记录；
- 检查 `manifest.json` 和 `packages-lock.json` 的变化是否合理；
- 搜索旧 Tank 输入是否依赖 Legacy Input；
- 如 Package 导致编译错误，先定位兼容问题，不让你随机点设置；
- 运行现有自动测试和最小场景冒烟。

## 人工验收

1. 打开 `SampleScene` 并 Play。
2. 确认 Tank 仍可移动、瞄准和开火。
3. 确认 Console 没有新增红错或持续刷警告。
4. 退出 Play 后，在 `Add Component` 中能够搜索到新输入和 Cinemachine 相关组件。

## 完成标准

- [ ] Input System 安装成功，实际版本已记录；
- [ ] Cinemachine 安装成功，实际版本已记录；
- [ ] `Active Input Handling` 的当前值已记录；
- [ ] SampleScene 的旧 Tank 操作未回归；
- [ ] Console 无新增红错；
- [ ] Package 变更已纳入版本控制检查。

## 完成记录

```text
状态：未开始
Input System 版本：
Cinemachine 版本：
Active Input Handling：
Tank 回归结果：
```

---

# S03：寻找角色、枪械和动作资源

## 本课目标

先在工程外准备候选素材，不急着导入。我们要避免模型骨骼不兼容、动作只有 Root Motion、枪械方向错误或许可证无法展示等后期返工。

## 你要寻找的角色模型

最低要求：

- 人形双足，能够配置为 Unity Humanoid；
- 有完整手指更好，但第一版至少肩、肘、腕、髋、膝、踝骨骼合理；
- T-Pose 或 A-Pose 清楚；
- 面数和材质数量适合实时游戏，不是一导入就带几十套 4K 材质；
- 允许用于个人作品集视频和公开演示；
- 最好可获得 FBX。若只有 VRM、Blender 或其他格式，先告诉我，不要直接装转换插件；
- 如果角色自带长裙、长发或复杂飘带，要接受第一版可能先关闭 Cloth，避免网络角色表现被物理抖动干扰。

## 你要寻找的枪械

- 一把视觉方向清楚的步枪；
- 模型原点、前方和缩放可以调整；
- 许可证可用于作品集；
- 有独立弹匣更好，但第一版不强制真实拆装；
- 不要求自带脚本、伤害系统或武器框架，我们只使用美术资源。

## 你要寻找的动作

第一批必要动作：

| 类别 | 最低动作 | 备注 |
|---|---|---|
| Locomotion | Idle、Walk/Run Forward、Backward、Strafe Left/Right | 优先 In-Place |
| Air | Jump Start、Fall、Land | 可先合并为较少状态 |
| Weapon | Rifle Aim Idle、Fire、Reload | 上半身动作可单独使用 |
| Reaction | Hit、Death | 第一版每类一个即可 |
| Action | Dash 或短闪 | 与 S01 选择一致 |

可选动作：起步、急停、左右转身、蹲伏、多个受击方向。可选项不要阻塞第一版。

## 判断 In-Place 与 Root Motion

- 预览时角色向前跑但模型根节点不离开原点，通常是 In-Place；
- 动画让根节点真实向前移动，通常带 Root Motion；
- 网络权威移动第一版优先 In-Place；
- 如果喜欢的动作只有 Root Motion，不代表不能用，但要先记录，后续由代码位移驱动并处理动画根运动，而不是直接勾选 `Apply Root Motion`。

## 你要交给我的信息

每个候选资源都用下面格式，不需要先购买或导入全部：

```text
资源名称：
来源页面或本地路径：
许可证/用途说明：
文件格式：
是否 Humanoid：
动作是否 In-Place：
包含的动作：
我喜欢它的原因：
截图：
```

## 我负责完成

- 帮你判断资源是否适合本项目，而不是只看画面好不好；
- 检查动作覆盖是否存在明显缺口；
- 给出正式资源与临时占位资源的组合方案；
- 为选定资源制定导入目录和命名表；
- 标记许可证需要保留的文本、链接或署名。

## 资源暂存方式

下载文件先放在 Unity 工程外，例如：

```text
CalabiYau_AssetStaging/
  CharacterCandidates/
  AnimationCandidates/
  WeaponCandidates/
  Licenses/
```

这一课不要直接把完整示例工程复制进 `Assets`，也不要覆盖现有 `ProjectSettings` 或 `Packages`。

## 完成标准

- [ ] 至少选定一个可用角色，占位或正式均可；
- [ ] 至少选定一把枪械或确认先用灰盒枪；
- [ ] 必要动作已有来源，缺失项有明确占位策略；
- [ ] 每项资源的许可和来源有记录；
- [ ] 文件格式、Humanoid 和 In-Place 风险已判断；
- [ ] 只有选中的最小资源集进入下一课。

## 完成记录

```text
状态：未开始
选定角色：
选定枪械：
选定动作包：
缺失动作：
许可记录位置：
```

---

# S04：导入并检查 Humanoid 资源

## 本课目标

把 S03 选中的最小资源集安全导入工程，并在不写角色控制代码前确认缩放、骨骼、Avatar、材质和动画本身正常。

## 我会先做

- 检查当前 `Assets` 目录，设计不会和旧 Tank 代码混在一起的新目录；
- 判断资源是否需要格式转换或额外 Package；
- 对导入前后的文件列表做检查，避免资源包写入无关设置；
- 根据模型格式给你对应的 Import Settings，而不是套用固定参数。

## 建议目录

```text
Assets/
  Art/
    Characters/<CharacterName>/
      Models/
      Materials/
      Textures/
    Weapons/<WeaponName>/
    Animations/<CharacterOrCommon>/
    VFX/
    Audio/
    Licenses/
  Game/
    Characters/
    Camera/
    Combat/
    Input/
  Scenes/
    CharacterLab.unity
```

美术源文件与游戏逻辑分开。第三方原包如果必须原样保留，放在 `Assets/Thirdparty/<Publisher>/<Package>`，不要把我们自己的 Prefab 写回第三方目录。

## 你在 Unity 中检查模型

1. 将选定文件复制到约定目录，等待 Unity 导入完成。
2. 在 Project 窗口选中角色 FBX，打开 Inspector 的 `Model` 页：
   - 先查看 Scale Factor，不急着修改；
   - 确认角色在预览中面向合理方向；
   - 检查材质和网格是否完整。
3. 打开 `Rig` 页：
   - Animation Type 选择 `Humanoid`；
   - Avatar Definition 第一份角色模型用 `Create From This Model`；
   - 点击 `Apply`；
   - 点击 `Configure...`，确认必要骨骼为绿色；
   - 使用 Pose 菜单检查或执行 `Enforce T-Pose`，保存后返回。
4. 打开 `Materials` 页，根据资源情况决定提取或保留嵌入材质。不要在不清楚来源时批量覆盖所有材质。

## 你在 Unity 中检查动画

1. 选中动画 FBX，打开 `Rig` 页。
2. Animation Type 选择 `Humanoid`。
3. Avatar Definition 选择 `Copy From Other Avatar`，拖入角色 Avatar。
4. 点击 `Apply`。
5. 打开 `Animation` 页逐个预览 Clip：
   - 名称是否正确；
   - 循环动作是否勾选 `Loop Time`；
   - 循环首尾是否明显跳变；
   - 根节点是否产生位移；
   - 脚是否明显滑动或陷地。
6. 只记录问题，本课不建立最终 Animator Controller。

## 临时预览场景

你可以把模型拖入空场景原点进行观察，但不要在导入的 FBX 内直接添加游戏脚本。正式组件放在我们自己的 Prefab 上。

## 我负责完成

- 检查 `.meta` 和目录是否完整；
- 分析导入警告和 Avatar 骨骼映射问题；
- 帮你确定统一单位、朝向和材质处理方案；
- 建立资源清单与许可证记录；
- 必要时提供小型检查脚本，但不会在本课写完整角色控制器。

## 人工验收

- Scene 中角色脚底接近地面，尺寸接近正常人形；
- 材质没有整身粉色或明显丢贴图；
- Avatar 配置有效；
- Idle 和至少四个移动方向能在 Inspector 预览；
- 动画循环、位移方式和明显瑕疵均有记录；
- Console 无新增红错。

## 完成记录

```text
状态：未开始
模型路径：
Avatar 路径：
统一缩放：
模型朝向：
动作列表与问题：
材质处理：
```

---

# S05：创建 CharacterLab 灰盒场景

## 本课目标

建立一张专门验证人物 3C 的实验场。以后每种移动、镜头和战斗问题先在这里复现，不直接在最终地图中猜原因。

## 为什么需要实验场

复杂美术地图会同时带来碰撞体、材质、灯光、遮挡和性能噪声。CharacterLab 只放能够回答问题的几何体，并给每种测试一个固定区域。

## 我负责完成

- 根据现有 URP 设置和项目层级设计实验场；
- 如适合用 Editor 脚本批量生成灰盒，我会直接编写；
- 建立场景标记、Gizmo 和测试说明；
- 检查新场景是否错误引用旧 Tank 逻辑；
- 保证 SampleScene 仍保留为 Tank 基线。

## 你在 Unity 中创建场景

1. 打开 `File > New Scene`。
2. 选择适合当前 URP 项目的 Basic 场景模板。
3. 立刻使用 `File > Save As` 保存为：

```text
Assets/Scenes/CharacterLab.unity
```

4. 在 Hierarchy 创建空对象 `CharacterLab`，再创建以下子节点：

```text
CharacterLab
  Environment
    Ground
    WallTests
    SlopeTests
    StepTests
    CameraTests
    CombatTests
  SpawnPoints
    LocalSpawn
    RemoteSpawn
  Targets
  Lighting
  Debug
```

5. Ground 使用 Cube 或 Plane。第一版建议 Cube，位置 `(0, -0.5, 0)`，缩放到足够大的测试平台。
6. 保存场景，不把 CharacterLab 替换成 SampleScene。

## 场地最低内容

- 一块水平地面；
- 一条直线加速和急停跑道；
- 内角、外角和窄通道墙体；
- 多种角度斜坡，建议先准备约 15°、30°、45°；
- 多种高度台阶；
- 一面用于相机贴墙和狭窄空间测试的墙；
- 近、中、远三组靶位；
- 两个清楚分开的出生点；
- 所有区域有名字或世界空间文字标记。

实际尺寸会在人物 Capsule 出现后校准。本课先保证层级和用途清楚，不宣称斜坡/台阶逻辑已实现。

## Layer 与碰撞约定

本课会先讨论再创建，建议至少预留：

- `Environment`：权威静态地图；
- `Character`：人物逻辑碰撞体；
- `HitBox`：后续射击命中盒；
- `CameraObstacle`：相机防穿墙查询；
- `IgnoreCamera`：相机不应撞到的视觉对象。

不要在没有记录的情况下随意改 Physics Collision Matrix。我会根据实现给出具体勾选表。

## 人工验收

1. 在 Scene 视图逐一定位所有测试区。
2. 确认对象命名能说明用途，没有几十个无意义的 `Cube (12)`。
3. Play 后场景无红错。
4. SampleScene 仍能单独打开运行。
5. CharacterLab 保存后重新打开，引用不丢失。

## 完成记录

```text
状态：未开始
场景路径：
测试区列表：
新增 Layer：
待人物尺寸确定后重调的对象：
```

---

# S06：建立 LogicRoot / VisualRoot 角色 Prefab 骨架

## 本课目标

建立一个人物预制体的正确骨架：逻辑位置、碰撞、网络状态和视觉模型分离。即使暂时只有胶囊和简单模型，也不把所有组件堆在同一对象上。

## 目标层级

本课开始时我会根据实际代码复核，默认结构为：

```text
NetworkCharacter
  LogicRoot
    CollisionRoot
    GroundProbe
    AimOrigin
  VisualRoot
    ModelRoot
    WeaponSocket
  CameraTargets
    CameraFollow
    CameraLookAt
  Presentation
    VfxRoot
    AudioRoot
    WorldUiRoot
  Debug
```

这里的名字表达职责，不要求所有节点第一天都挂脚本。

## 各节点意义

- `NetworkCharacter`：网络实体入口和稳定身份；
- `LogicRoot`：权威/预测位置和朝向，不依赖动画骨骼；
- `CollisionRoot`：人物 Capsule 及查询数据；
- `GroundProbe`：后续地面检测的可视化参考；
- `AimOrigin`：逻辑瞄准起点，不等同于枪口；
- `VisualRoot`：只负责看起来在哪里，可做远端平滑追赶；
- `ModelRoot`：正式模型替换点；
- `WeaponSocket`：武器视觉挂点，之后会绑定手骨骼；
- `CameraFollow / CameraLookAt`：Cinemachine 目标，不把相机直接挂人物骨骼；
- `Presentation`：特效、声音和世界 UI，预测重放时可抑制重复副作用。

## 我负责完成

- 创建必要的最小组件脚本和命名空间；
- 明确哪些组件属于本地玩家、远端玩家或二者共有；
- 如果适合，提供 Editor 菜单自动建立层级，减少手工命名错误；
- 建立 Prefab 并检查引用；
- 解释每个脚本的输入、输出和生命周期，但不会提前写 S07 的完整输入系统。

## 你在 Unity 中操作

具体组件名以本课实际代码为准，流程会类似：

1. 在 CharacterLab Hierarchy 创建根对象 `NetworkCharacter`。
2. 按目标层级创建子对象，并把本地坐标清零。
3. 在 `LogicRoot` 添加我本课实现的角色逻辑入口组件。
4. 在 `VisualRoot/ModelRoot` 暂时放 Capsule 或 S04 的模型副本。
5. 把 `CameraFollow` 放在角色上半身附近，把 `CameraLookAt` 放在瞄准参考高度；具体数值待相机课调节。
6. 按我给出的 Inspector 表把各 Transform 拖入对应字段。
7. 将根对象拖到 `Assets/Game/Characters/Prefabs/` 生成我们自己的 Prefab。
8. 删除场景临时对象，再从 Prefab 拖回，确认引用没有丢失。

## Inspector 绑定表的写法

本课实施时我会给你类似下表的精确版本，而不会只说“把引用配置好”：

| 挂载对象 | 组件字段 | 拖入对象 | 初始值 |
|---|---|---|---|
| NetworkCharacter | Logic Root | `LogicRoot` | Transform 引用 |
| NetworkCharacter | Visual Root | `VisualRoot` | Transform 引用 |
| NetworkCharacter | Camera Follow | `CameraTargets/CameraFollow` | Transform 引用 |
| NetworkCharacter | Is Local Preview | 不拖对象 | CharacterLab 临时勾选 |

## 人工验收

- 根对象移动时，整个角色一起移动；
- 单独移动 VisualRoot 不会改变逻辑根的调试位置；
- 删除并重新拖入 Prefab 后字段仍有引用；
- 正式模型可通过替换 ModelRoot 内容更换，而不重挂逻辑组件；
- Scene 中没有 Missing Script，Console 无红错；
- SampleScene 的 Tank Prefab 没被修改。

## 完成标准

- [ ] 人物 Prefab 骨架建立；
- [ ] LogicRoot 与 VisualRoot 职责分离；
- [ ] Inspector 拖拽引用有记录；
- [ ] CharacterLab 中能稳定实例化；
- [ ] 你能解释为什么不能让动画骨骼直接拥有权威位置；
- [ ] S07 可以在此 Prefab 上添加输入读取，而不重做层级。

## 完成记录

```text
状态：未开始
Prefab 路径：
新增组件：
Inspector 绑定：
模型当前为：占位/正式
自动检查：
人工验收：
```

---

## 第一分卷完成门

只有以下条件全部满足，才进入第二分卷：

- [ ] Tank 网络基线可回退；
- [ ] 第一版垂直切片范围固定；
- [ ] Input System 和 Cinemachine 安装且旧场景无回归；
- [ ] 模型、枪械和动作至少有可用占位，许可有记录；
- [ ] 角色 Humanoid/Avatar 预览正常；
- [ ] CharacterLab 测试区域齐全；
- [ ] 人物 Prefab 的 LogicRoot / VisualRoot 分离；
- [ ] Console 无新增红错。

进入下一卷时，使用 S07 新对话，不要在 S06 的对话中顺手开始写输入与移动。
