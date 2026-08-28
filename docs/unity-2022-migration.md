# CalabiYau Unity 2022 迁移记录

日期：2026-08-28  
状态：**迁移与自动验证已完成，等待用户进行一次有画面的手动操作验收。**

## 1. 迁移结果

- 主工程：`D:\unity project\CalabiYau\CalabiYau`
- 原版本：Unity `2021.3.45f2c1`
- 当前版本：Unity `2022.3.62f3c1`，revision `1623fc0bbb97`
- 回退副本：`D:\unity project\CalabiYau_2021_Backup_20260828`
- `Elementwar` 及其他 Unity 项目未被打开、复制或修改。

迁移采用“先建立独立副本，再在原主工程原地升级”的方式。副本排除了可重建的 `Library`、`Temp`、`Logs` 和 .NET `bin/obj`，其余 1001 个文件已逐一计算 SHA-256；迁移前主工程与副本差异为 0。

从现在开始，主工程只用 Unity 2022.3.62f3c1 打开。不要再用 Unity 2021 打开它，否则旧编辑器可能反向改写项目设置和资源序列化格式。需要回退时使用独立的 2021 副本，不要把两个目录混在一起。

## 2. Unity 自动升级内容

Unity 自动更新了项目版本、URP 配置、材质序列化和包依赖。主要包变化：

- Universal Render Pipeline：`12.1.15` -> `14.0.12`
- TextMesh Pro：`3.0.6` -> `3.0.7`
- Timeline：`1.6.5` -> `1.7.7`
- Version Control：`2.5.2` -> `2.7.1`
- Rider Editor：`3.0.31` -> `3.0.36`
- 新增 AI Navigation：`1.1.6`，由 Unity 2022 对原导航模块的包化迁移产生

`ProjectSettings/boot.config` 被 Unity 2022 自动移除；这是编辑器升级结果，不是场景或代码丢失。

## 3. 资源完整性检查

- `SampleScene.unity`、`Tank.prefab`、`RemoteTank.prefab`、`Bullet.prefab` 与迁移前副本逐字节一致。
- `Assets` 下全部 `.meta` 文件与迁移前副本一致，说明 GUID 没有变化。
- 场景和预制体不存在 `m_Script: {fileID: 0}`；材质不存在 `m_Shader: {fileID: 0}`。
- Build Settings 仍启用 `Assets/Scenes/SampleScene.unity`。
- Unity 2022 中已实际打开 `SampleScene`：Tank、障碍训练场和场景层级显示正常，没有粉色材质；Console 为 0 错误、0 警告。

迁移验证工具 `Assets/Editor/Unity2022MigrationVerifier.cs` 会遍历启用的构建场景、所有预制体和材质。本次结果为：1 个场景、28 个 GameObject、60 个材质，丢失脚本和丢失 Shader 均为 0。

素材包自带 20 个 HDRP 专用材质，在当前 URP 工程中会报告“不受当前渲染管线支持”。它们位于 `Assets/Thirdparty/.../Materials/HDRP`，当前场景未引用，不是迁移故障。不要把 HDRP、Standard 和 URP 三套示例材质混用；当前项目应选 URP 版本。

## 4. 兼容性小修正

Unity 2022 会明确警告“给 kinematic Rigidbody 写入 velocity/ angularVelocity 无效”。迁移后的联机冒烟暴露了三处旧写法：

- `Assets/Code/NetworkTankAvatar.cs`
- `Assets/Code/TankMotor.cs`
- `Assets/Code/NetworkStaticMapBody2D.cs`

修正方式是在刚体仍为动态状态时清空速度，再切换为 kinematic；已经是 kinematic 时不再执行无效写入。这不改变权威同步、预测或碰撞结果，只消除无效调用和日志噪声。

## 5. 已完成验证

1. Unity 2022 完整资源重新导入：完成。
2. Unity C# 脚本域重载：无 `CS` 编译错误。
3. 资源引用扫描：无丢失脚本、丢失 Shader、丢失构建场景。
4. Windows 64 位 Development Build：成功，输出到 `Builds/Unity2022Migration/CalabiYau.exe`。
5. .NET 服务器自动测试：碰撞数学、权威 Tank 碰撞、空间查询、地图握手、预测命令重放、命令时间线、可靠开火、按客户端复制和可靠结果事件全部通过。
6. 服务器 + Unity 2022 构建客户端冒烟：收到 `ServerWelcome`，TickRate 为 30 Hz，地图为 `sample-scene-training-ground@4`；服务器接受 206 条输入，拒绝 0 条。
7. 最终 Player 日志：`NullReferenceException`、`MissingReferenceException`、C# 错误和 kinematic 速度警告均为 0。

自动冒烟使用了 `-batchmode -nographics`，因此日志中的无 GPU/Shader 支持提示来自无图形运行条件；正常 DX11 编辑器中已确认场景材质显示正常。

## 6. 用户手动验收

自动验证不能替代手感和画面验收。请完成一次短验收：

1. 在 Unity Hub 中把主工程指定为 Unity 2022.3.62f3c1 并打开。
2. 打开 `Assets/Scenes/SampleScene.unity`，确认 Tank、地面和障碍物都存在且没有粉色材质。
3. 启动 `Server/Server`，再进入 Play Mode；确认客户端显示已连接且可以移动、旋转、瞄准、射击。
4. 再启动一个 Windows 构建客户端，确认双端互相可见、障碍碰撞和射击结果仍一致。
5. 打开 Console，确认没有新的红色错误。若只有未使用 HDRP 示例材质的检查提示，可按第 3 节边界处理。

完成这一步后，迁移即可从“自动验证完成”更新为“人工验收完成”。
