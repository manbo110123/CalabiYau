# 阶段 12B：可靠关键结果事件账本

本阶段只为少量“发生过一次”的战斗结果补充轻量 UDP 可靠语义，不改变快照和开火意图的职责。

## 消息分级

- `WorldSnapshot`：仍然不可靠、可覆盖，不等待任何 ACK。
- `FireEvent`、`HitEvent`、`HealthChangedEvent`：仍然不可靠。特别是 `HitEvent` 只播放受击闪红/命中特效；`HealthChangedEvent` 只保留为可观察的战斗提示，不直接修改客户端生命状态。血量和 `isAlive` 以权威 `WorldSnapshot` 为准。
- `DeathEvent`、`RespawnEvent`、`KillEvent`、`MatchEndEvent`：带全局递增的 `eventId` 与 `serverTick`，由可靠事件账本投递；它们是死亡、重生、击杀和结算这些“发生过一次”的结果记录。
- `FireReceipt`：仍只确认 `FireRequest` 是否被服务器接收并做出入队决定，不承担命中、死亡或事件可靠确认。

当前 Demo 尚未定义对局胜负规则，因此 `MatchEndEvent` 的 DTO、客户端 ACK/去重与服务端账本路由已经就绪；未来产生 `MatchEndWorldEvent` 时会自动使用同一账本，不在本阶段凭空引入比赛规则。

## 服务端规则

每个 `ConnectedClient` 都有独立的 `ReliableEventLedger`。账本保存原始 JSON、关联实体、首次/上次发送 UTC 时间、发送次数和重发次数。默认每 250 ms 检查一次，最多重发 4 次；到达上限后删除待处理项并记录指标，不会无限发送。

客户端发送 `EventAck { eventId }` 后，服务器只从该客户端自己的账本删除对应项，并累计 ACK 延迟。离开复制范围的 Avatar 依赖事件会从该客户端账本取消；之后重新进入范围时以最新 `WorldSnapshot` 的生命状态为准。

## 客户端规则

客户端维护容量为 128 的最近 `eventId` 集合。首次收到事件才应用死亡/重生状态或记录击杀；重复包不重复播放或应用，只再次发送 `EventAck`。F3 面板显示最近已处理数与重复丢弃数；服务端每秒按客户端输出待确认数、重发数、ACK 延迟和超限数。

这个集合是**有限窗口内至多一次处理**，不是永久幂等日志：当第 129 个不同事件到来时，最旧的 ID 会被移出集合；一个极旧的重复包理论上可能再次被当作新事件。实际设计依赖服务端有限重发次数、客户端有限存活会话和快照兜底，使这个窗口覆盖正常重发范围；不能把它写成无限期 exactly-once。

## 阶段 12B.1：时序与延迟应用修正

### 为什么需要修正

`eventId` 只能回答“这一个网络事件是否见过”，不能回答“同一玩家的生命状态哪个更新”。UDP 可能先送达重生包、后送达旧死亡包；若客户端只按 `eventId` 去重，就会把已重生的 Avatar 再次切回死亡。另一个竞态是：可靠事件先到、该玩家的 `WorldSnapshot` 后到。旧实现会 ACK 并记下 `eventId`，但由于 Avatar 尚未创建而没有实际应用；重发包又会被去重，事件便永久丢失。

### 问题复盘：它是什么问题、属于哪个模块

这不是服务器权威结算错误，也不是单纯的“UDP 丢包”。它是**客户端把可靠传输语义、事件去重语义和 Unity Avatar 生命周期混在一起后产生的时序应用错误**。服务器给出的死亡/重生结果是正确的，但客户端收到这些结果的顺序和 Avatar 可用时间不受 UDP 保证。

| 现象 | 根因 | 主要归属模块 | 修复责任 |
| --- | --- | --- | --- |
| 玩家已经重生，却又显示死亡 | 旧 `DeathEvent` 与新 `RespawnEvent` 的 `eventId` 不相同；去重只会拦截同一个包的重发，无法判断两个不同事件哪个生命状态更新 | Unity 客户端 `UdpNetworkClient` 的生命周期事件应用逻辑；协议缺少状态新旧依据 | 协议增加 `lifeStateVersion`；服务端维护它；客户端按玩家比较它 |
| Avatar 未创建时收到死亡/重生，之后永远没应用 | 客户端先把 `eventId` 标为已处理并 ACK；处理函数找不到 Avatar 后直接返回；服务器收到 ACK 不再重发，后续包又被客户端去重 | Unity 客户端 `ProcessReliableEvent()`、`HandleDeathEvent()`、`HandleRespawnEvent()` 与 `GetOrCreateRemoteAvatar()` 的衔接 | 客户端 ACK 后缓存最新待应用生命周期结果，Avatar 创建后再落地 |
| 旧 `HealthChangedEvent` 也可能把状态拉回去 | 该事件不可靠，但旧代码直接用其中的 `health/isAlive` 改 Avatar 和本地输入状态 | Unity 客户端 `HandleHealthChangedEvent()` 的职责越界 | 该事件降为观察日志；血量、存活、倒计时只读权威快照 |
| 客户端配置 TickRate 与服务器不同，时间估算偏差 | 客户端把 `inputTickRate` 同时用于“发送输入”和“换算服务器 Tick”，两个时钟职责不同 | 协议握手与 Unity 客户端插值/调试估算逻辑 | `ServerWelcome` 下发真实 `serverTickRate`，客户端只在服务器时间轴计算中使用它 |

服务端 `GameWorld` 仍是死亡、重生和血量的唯一裁决者；`ReliableEventLedger` 的职责仍是“对每个接收者有限重发直到 ACK”。它们不负责替 Unity 客户端排序，也不应知道客户端 Avatar 是否已经实例化。换言之，问题的最终落点在客户端应用层，但必须用协议字段让客户端有可比较的权威依据。

### 故障时序一：不同可靠事件乱序，去重无法阻止状态倒退

假设 B 初始生命版本为 1。B 死亡后，服务器产生 `DeathEvent(eventId=100, lifeStateVersion=2)`；重生后产生 `RespawnEvent(eventId=101, lifeStateVersion=3)`。它们都是正确结果，但 UDP 不保证到达顺序：

```text
服务器权威时间：  死亡 v2 / event 100  ----------------> 重生 v3 / event 101
UDP 到达客户端：                         Respawn v3  ---> Death v2（迟到）
旧客户端行为：                           应用重生          eventId 不重复，所以又应用死亡
旧客户端结果：                           活着              错误地退回死亡
```

这里的关键点是：`100` 和 `101` 本来就应该各自只处理一次，因此“最近 eventId 集合”正确地允许了二者；错误在于它没有第二把尺子判断 `v2 < v3`。不能通过把 `eventId` 当作玩家生命状态版本解决，因为它是跨所有玩家的全局事件编号，且 `KillEvent` 等其他事件也会占用它。

### 故障时序二：ACK 成功，但游戏状态没有成功应用

```text
1. 客户端收到 DeathEvent(eventId=100)。
2. ProcessReliableEvent 把 100 写入最近 ID 集合。
3. HandleDeathEvent 查询 target Avatar；此时对应 WorldSnapshot 还未到，Avatar 为 null。
4. 旧逻辑只输出“no avatar”并返回。
5. 客户端仍发送 EventAck(100)，服务端账本删除 100，不再重发。
6. 后续 WorldSnapshot 到达并创建 Avatar；DeathEvent 已被 ACK 且重发包会被去重。
7. 结果：网络层认为事件可靠送达，表现层却从未应用事件。
```

这是“**传输确认成功**”与“**业务状态已落地**”不是同一件事的问题。若改成“不创建 Avatar 就不 ACK”，服务器会在客户端看不见实体、Prefab 缺失或实体离开复制范围时持续重发；因此本阶段不延迟 ACK，而是在客户端保存足以稍后落地的权威结果。

### 修复后的职责划分

| 层级/模块 | 修改内容 | 为什么在这里修 |
| --- | --- | --- |
| `GameWorld` / `PlayerState` | 每玩家从 1 开始维护 `LifeStateVersion`，每次死亡、重生加 1 | 生命状态变化只允许由服务端权威世界产生 |
| `SnapshotBuilder` | `PlayerSnapshot` 带 `lifeStateVersion` | 快照是状态恢复基线，必须能说明它对应哪一代生命状态 |
| `UdpGameServer` / DTO | `DeathEvent`、`RespawnEvent`、`ServerWelcome` 序列化版本号或真实 TickRate | 协议把权威事实明确传给客户端 |
| `ReliableEventLedger` | 保持有限 ACK/重发，不强行改造成有序通道 | 账本解决丢包补偿；它不承担跨事件排序 |
| `UdpNetworkClient` 生命周期事件处理 | 按 `playerId + lifeStateVersion` 只应用更新状态；无 Avatar 时缓存最新一条 | Unity 表现对象何时存在，只有客户端知道 |
| `UdpNetworkClient` 快照处理 | 记录快照版本，拒绝较旧生命状态覆盖；创建 Avatar 后尝试消费缓存 | 让快照和可靠结果使用同一套“新旧”判断 |
| `UdpNetworkClient` 的 `HealthChangedEvent` | 只记录，不再写血量或 `isAlive` | 避免不可靠的旧提示越权覆盖权威状态 |

### 修复后的处理规则

客户端处理死亡或重生可靠事件时，顺序是：

```text
收到可靠事件
-> eventId 是否在有限去重窗口内？是：仅 ACK，结束
-> 记录 eventId，并发送 ACK
-> 事件的 lifeStateVersion 是否大于该玩家已知版本？否：这是旧状态，丢弃
-> Avatar 是否存在？否：按 playerId 缓存最新一条生命周期结果，结束
-> 应用 Avatar 状态，并把该玩家已知版本更新为事件版本
```

收到快照时，先比较快照中的 `lifeStateVersion`：旧于已知版本的快照不能覆盖生命状态；同版本或更新版本可以作为权威状态恢复。随后若该快照创建了 Avatar，客户端尝试消费该玩家缓存的生命周期事件。缓存事件的版本若已经被同版本或更新快照覆盖，则安全丢弃；若更高，则立即应用。

缓存按玩家只保留一条“最新的未落地死亡或重生结果”，而不是保存无限事件历史。例如尚未创建 Avatar 时先收到死亡 v2、再收到重生 v3，v3 覆盖 v2 即可，因为服务器已经以 v3 覆盖了 v2 的最终状态。

### 采用的方案

每位玩家从初始值 1 开始维护 `lifeStateVersion`，每次死亡和每次重生各加 1。`DeathEvent`、`RespawnEvent` 和 `PlayerSnapshot` 都携带该值。客户端按玩家保存已知最大版本，只接受更大的生命周期事件；所以“重生版本 3 已应用后才到的死亡版本 2”会被直接丢弃。快照也携带版本，既可作为状态恢复，也能阻止旧快照覆盖已知的新生命状态。

可靠事件先到但 Avatar 不存在时，客户端仍 ACK（避免服务器无限重发）并把每个玩家最新的一条死亡/重生结果缓存起来。该玩家首次由快照创建 Avatar 后再尝试应用缓存；如果同版本或更新的快照已经恢复了状态，缓存自然被版本比较淘汰。每位玩家只需保留最新状态，因为较新生命版本已在权威服务器上覆盖较旧版本。

选择这个方案是因为它把“网络包是否见过”（`eventId`）和“玩家现在处于哪一代生命状态”（`lifeStateVersion`）分开，代码只用两个字典即可验证。备选方案包括：延迟 ACK 到 Avatar 出现后（会让无 Avatar 或复制范围变化时不断重发）、完整有序可靠通道（会有队头阻塞和更多超时规则）、或只依赖快照（会丢失一次性结果的即时表现）。本阶段不需要这些复杂度。

### TickRate 协议

`ServerWelcome.serverTickRate` 下发真实的服务端 TickRate。客户端保留自己的输入发送频率配置，但所有“服务器 Tick 换算”的时间估算使用服务端下发值：远端插值延迟秒数转 Tick、远端快照缓冲播放速率、以及“最后快照之后估算的服务器 Tick”。因此客户端 Inspector 的输入频率即使与服务器不同，也不会扭曲服务器时间轴。

## 自动验证

`Server.Tests` 覆盖：

- 未 ACK 的关键事件到达重发间隔后重发；
- ACK 后不再重发；
- 达到重发上限后停止；
- 同一 `eventId` 只会首次处理；
- 去重集合超过容量后会淘汰最旧 ID，明确验证其有限窗口语义；
- 死亡、重生为同一玩家产生严格递增的 `lifeStateVersion`，且快照带回最新版本；
- 待确认事件不会阻塞连续 `WorldSnapshot`。

## 手动验收

1. 启动服务器与两个 Unity 客户端，确认连接日志打印实际 `serverTickRate`。
2. 让 A 连续击杀 B 并等待 B 重生；在 clumsy 对 UDP 7777 加入 150--250 ms 延迟、5--10% 丢包和 50--100 ms 乱序（两个方向都覆盖）。
3. 重复至少十次死亡/重生。B 在重生后不得因迟到的旧 `DeathEvent` 再次进入死亡状态；F3 的 `stale life drops` 可以增加，这是预期的旧包拦截。
4. 在 B 刚连接或刚进入复制范围时制造同样弱网条件。控制台可见 `Cached reliable ... Avatar is not available yet.` 时，后续快照创建 Avatar 后状态仍应与最新快照/最新生命周期事件一致，而不是因 ACK 后永久漏状态。
5. 观察命中闪红偶尔缺失是允许的；血量、死亡、重生和本地输入禁用/恢复必须最终以权威快照与可靠结果一致。
