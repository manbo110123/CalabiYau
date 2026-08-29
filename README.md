# CalabiYau 联机 Tank TPS Demo

这是一个基于 Unity 客户端和 .NET C# UDP 服务器实现的第三人称联网射击同步 Demo。项目以 Tank 作为轻量级玩家实体，重点验证服务端权威、固定 Tick、输入上报、状态快照、本地预测、服务端校正、远端插值、网络开火、服务器命中判定、血量死亡重生、网络调试面板和射击延迟补偿。

项目定位是实习作品集级联网 Demo：代码优先清晰可讲，不刻意堆复杂框架；先用 `UdpClient + JSON` 跑通核心同步链路，再保留未来升级到 MessagePack、Protocol Buffers、LiteNetLib 或 KCP 的空间。

## Unity 版本

- 当前唯一开发基线：**Unity 2022.3.62f3c1 LTS**。
- 2026-08-28 已从 Unity 2021.3.45f2c1 完成迁移；后续不要再用 2021 打开主工程，避免资源和项目设置被旧版本反向序列化。
- 迁移记录、验证结果和回退副本位置见 [Unity 2022 迁移记录](docs/unity-2022-migration.md)。
- 编辑器中可运行 `Tools > CalabiYau > Validate Unity 2022 Migration`，检查构建场景、预制体脚本和材质 Shader 引用。

## 快速运行

1. 启动服务器：

```powershell
cd "D:\unity project\CalabiYau\CalabiYau\Server\Server"
dotnet run
```

2. 打开 Unity 项目：

```text
D:\unity project\CalabiYau\CalabiYau
```

在 Unity Hub 中确认该项目使用 `2022.3.62f3c1` 后再打开。首次导入或清理 `Library` 后重新打开耗时较长是正常现象。

3. 启动两个客户端：

- 一个使用 Unity Editor Play。
- 另一个使用打包后的 Windows 客户端，或再开一个可运行客户端环境。

4. 进入同一场景后，两个客户端会连接本地 UDP 服务器，服务器默认端口为 `7777`。

## 已实现功能

- UDP + JSON 网络通信。
- `ClientHello` / `ServerWelcome` 连接与玩家 id 分配。
- 客户端按网络 Tick 发送输入。
- 服务器以 30 Hz Tick 权威模拟玩家位置、车身朝向、炮塔瞄准、血量和死亡状态。
- 服务器广播 `WorldSnapshot`。
- 本地玩家客户端预测。
- 本地玩家收到服务器快照后按 `lastProcessedInputTick` 回滚重放未确认输入。
- 本地预测误差支持死区、平滑修正和硬修正。
- 远端玩家快照缓冲与插值。
- 网络开火事件、命中事件和血量变化事件。
- 服务端权威扣血、死亡、重生。
- 服务器侧射击延迟补偿，基于历史状态回溯目标位置。
- 版本化二维静态地图碰撞：服务器、客户端预测和未确认输入重放共享 Tank 移动规则。
- 静态墙体优先的权威射击遮挡，以及合法出生/重生位置查询。
- Unity 运行时网络调试面板，按 `F3` 显示或隐藏。
- 弱网测试可配合 clumsy 进行延迟、抖动、丢包验证。

## 调试面板

运行时按 `F3` 可以显示或隐藏网络调试面板。面板包含：

- `playerId`
- 连接状态
- RTT
- 估算单向延迟
- 服务器 Tick
- 本地输入 Tick
- 最新快照 Tick
- 远端快照缓冲数量
- 预测修正次数
- 最近一次预测误差
- 快照丢失率估算
- 延迟补偿状态
- 未确认输入数量
- 玩法事件和开火请求统计

## 核心代码位置

- Unity 网络客户端：[UdpNetworkClient.cs](Assets/Code/UdpNetworkClient.cs)
- 网络玩家表现与插值：[NetworkTankAvatar.cs](Assets/Code/NetworkTankAvatar.cs)
- 输入数据结构：[TankInputData.cs](Assets/Code/TankInputData.cs)
- 本地输入读取：[TankInput.cs](Assets/Code/TankInput.cs)
- 坦克移动：[TankMotor.cs](Assets/Code/TankMotor.cs)
- 瞄准与炮塔：[TankAim.cs](Assets/Code/TankAim.cs)
- 武器表现：[TankWeapon.cs](Assets/Code/TankWeapon.cs)
- 共享碰撞数学：[CollisionCore](Assets/Code/CollisionCore)
- Tank 权威地图与移动查询：[TankCollision2D](Assets/Code/TankCollision2D)
- .NET UDP 服务器：[Program.cs](Server/Server/Program.cs)

## 复习文档

面试复习和技术讲解请看：

[项目复习指南](docs/项目复习指南.md)

## 人物 3C 开发入口

Tank 网络基线之后的人物主线采用“一课一次对话”的陪练方式推进。请从下列文档开始，不要直接跳到人物控制器或技能阶段：

1. [3C 开发陪练手册：总目录与协作方式](docs/3C开发陪练手册/00_总目录与协作方式.md)
2. [第一分卷：起步准备、资源、Package 与 CharacterLab](docs/3C开发陪练手册/01_起步准备_资源_Package与实验场.md)
3. [二次元第三人称动作射击开发计划书](docs/CalabiYau二次元第三人称动作射击开发计划书.md)（架构总纲与技术路线，不作为逐步操作教程）

当前入口是 **S00：保存已验收的 Tank 基线**。之后每课由开发者完成 Unity 可视化操作与人工验收，Codex 负责检查工程、实现代码、解释原理、给出精确组件绑定并维护阶段记录。

