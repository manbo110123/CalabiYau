# 模块六：工程架构、协议取舍与 Dedicated Server 演进复习笔记

> 复习重点：本模块不只回答“文件怎么拆”，而是解释职责边界、协议取舍和服务器演进路线。要能说明当前架构为什么可测试、可替换、可扩展，也要能诚实说明它仍是单进程战斗服务器 Demo。

---

## 一、服务端整体职责图

```text
Program
-> 组装配置、启动服务器

UdpGameServer
-> UDP 收发、消息路由、Tick 调度、超时清理、发送快照和事件

ClientRegistry
-> Endpoint 与玩家身份、连接生命周期、每客户端网络状态

GameWorld
-> 权威移动、开火、命中、血量、死亡、重生

SnapshotBuilder
-> 只读 GameWorld，提取复制候选状态

ClientReplicator
-> 为某一个客户端筛选实体和同步频率

ReliableEventLedger
-> 为某一个客户端维护关键事件的 ACK、重发与遥测

Messages
-> UDP JSON 协议 DTO
```

真正的职责拆分不是把一大段代码剪成多个 `.cs` 文件，而是让每个模块只知道自己必须知道的事，并且不能越权。

### 面试话术

> 服务端按职责拆分为启动配置、UDP 传输、会话管理、权威世界、快照构建、按客户端复制、可靠事件账本和协议 DTO。拆分的重点不是文件数量，而是依赖方向：网络层不裁决游戏结果，权威世界不依赖网络和 Unity，复制层只读世界状态并为不同客户端制定发送计划。

---

## 二、各模块的职责边界

### 1. `Program`：组合根

`Program.cs` 创建 `GameWorldSettings`、`UdpGameServerOptions` 和复制配置，然后启动 `UdpGameServer`。例如它配置：

```text
监听端口：7777
服务器 Tick：30Hz
快照频率：30Hz
高优先级复制：30Hz
低优先级复制：5Hz
距离过滤：默认开启
```

它的职责是“把系统组装起来”，不写战斗规则，也不处理具体消息。

### 面试话术

> `Program` 作为组合根，只负责创建世界、服务器和复制配置，并处理启动与关闭；它不承载移动、命中或 Socket 路由逻辑。这样运行参数和业务规则分离，后续切换全量快照对照模式或调整复制频率时不需要修改核心世界逻辑。

---

### 2. `UdpGameServer`：传输、路由与固定 Tick 调度

`UdpGameServer` 负责：

```text
收到 UDP 包
-> 读取 type
-> 路由到 ClientHello、PlayerInput、FireRequest、EventAck 等处理函数

固定 Tick
-> 清理超时客户端
-> 推进 GameWorld
-> 捕获复制候选状态
-> 构建并发送快照与事件
```

它不会直接修改：

```text
PlayerState.X / Z
PlayerState.Health
PlayerState.IsAlive
```

它只把网络消息转换为 `InputCommand`、`FireCommand` 等结构化命令，交给 `GameWorld`。`ReceiveLoopAsync()` 与 `TickLoopAsync()` 同时运行，当前使用 `stateLock` 保护会话状态和权威世界的并发访问；网络发送工作尽量在锁外执行。

### 面试话术

> `UdpGameServer` 负责 Socket 收发、消息路由、连接超时、固定 Tick 调度和结果下发，但不直接修改玩家位置、血量或存活状态。它将 JSON 消息转为结构化命令后交给 `GameWorld`，并用锁保护收包与 Tick 并发访问。这样 UDP 到达时刻不会直接改变游戏规则，权威状态只在固定 Tick 中推进。

---

### 3. `ClientRegistry`：会话与每客户端网络状态

`ClientRegistry` 维护：

```text
Endpoint -> ConnectedClient
ConnectedClient -> playerId
ConnectedClient -> ClientReplicator
ConnectedClient -> ReliableEventLedger
最后收包时间
```

它解决的是：

```text
哪个网络地址对应哪个玩家？
这个客户端是否超时？
这个客户端自己的复制历史是什么？
这个客户端有哪些关键事件仍未 ACK？
```

它不负责移动、命中、血量或死亡。当前 `Endpoint -> playerId` 是本地 Demo 的会话映射，不等于正式账号认证。

### 面试话术

> `ClientRegistry` 将网络 Endpoint 映射到玩家会话，并把每客户端的复制器、可靠事件账本和最后活动时间绑定在同一个 `ConnectedClient` 中。它负责会话生命周期和网络侧状态，不参与战斗规则；当前是本地 Demo 的 Endpoint 身份映射，正式项目还需要账号、Token 和认证机制。

---

### 4. `GameWorld`：无渲染的权威逻辑核心

`GameWorld` 负责：

```text
输入是否合法
如何移动和转向
何时可开火
如何做命中和伤害
何时死亡和重生
产生哪些权威世界事件
```

它不知道：

```text
UDP Socket
JSON 字符串
Unity GameObject
Rigidbody
客户端 Endpoint
```

它接收 `InputCommand`、`FireCommand`，输出权威 `PlayerState`、`GameWorldEvent` 和 `FireReceiptDecision`。因此改变序列化方式、替换网络库或升级客户端表现时，不需要重写移动、命中和死亡规则。

### 面试话术

> `GameWorld` 是无渲染的服务器权威逻辑核心，只接收输入和开火命令，在 Tick 内输出权威状态与世界事件。它不依赖 UDP、JSON 或 Unity，因此可以脱离网络层做单元测试；未来切换 Protobuf、加入房间管理或替换 Tank 表现时，核心战斗规则也不需要被传输层牵动。

---

### 5. `SnapshotBuilder` 与 `ClientReplicator`：读取世界和决定复制分开

```text
SnapshotBuilder
-> 从 GameWorld 读取“全世界当前有什么”
-> 生成 ReplicationCandidate

ClientReplicator
-> 决定“某一个客户端应该收到哪些、多久收到一次”
```

二者都不修改 `GameWorld`。

```text
修改伤害、冷却、死亡规则
-> 应改 GameWorld

修改 18m / 45m、5Hz / 30Hz、AOI 策略
-> 应改 ClientReplicator
```

这避免了“改复制策略时误伤战斗规则”的耦合。

### 面试话术

> 快照构建与复制策略分离：`SnapshotBuilder` 只读取权威世界并形成候选状态，`ClientReplicator` 只为一个接收者决定范围、优先级和发送节奏。复制层没有修改 `GameWorld` 的权限，因此战斗规则与带宽策略可以独立演进，也更容易为复制逻辑写针对性测试。

---

### 6. `ReliableEventLedger` 与 `Messages`

`ReliableEventLedger` 为每个客户端保存未确认关键事件，处理 ACK、有限重发、重试上限和 ACK 延迟遥测。它不需要理解死亡规则本身，只管理“这份 JSON 事件是否被该客户端确认”。

`Messages.cs` 定义服务端 JSON DTO；Unity 客户端在 `UdpNetworkClient.cs` 底部有对应消息类。它们共同构成协议契约。

### 面试话术

> 可靠事件账本和业务事件本身分离：`GameWorld` 决定何时产生死亡或重生，账本只针对每个客户端处理 ACK、重发和遥测。协议 DTO 则单独定义消息字段，让传输格式与权威规则保持边界；这使可靠性策略和业务规则不会互相缠绕。

---

## 三、一次输入跨模块如何流动

```text
UDP 包：PlayerInput JSON
-> UdpGameServer 读取 type
-> ClientRegistry 确认 Endpoint 的 playerId
-> 转为 InputCommand
-> GameWorld.TryQueueInput()
-> 下一个 Tick：GameWorld 消费并推进权威世界
-> SnapshotBuilder 读取新状态
-> ClientReplicator 为各客户端生成快照
-> UdpGameServer 序列化、发送
```

整个链路遵守：

```text
网络层不裁决游戏结果
游戏层不依赖网络细节
复制层不修改权威世界
```

### 面试话术

> 一条输入先在传输层完成 Endpoint 与协议校验，再转为 `InputCommand` 进入 `GameWorld` 的待处理区；固定 Tick 消费后生成权威状态，快照构建与按客户端复制在之后读取结果并发送。这个链路保证网络包不会直接改游戏状态，同时让网络、规则和复制三个层次各自只有单向依赖。

---

## 四、这和帧同步 GameCore 的关系

当前 `GameWorld` 可以理解为一个小型、无渲染的权威逻辑核心：

```text
输入命令
-> Tick 推进
-> 权威状态与事件
```

但它不是严格帧同步中的完整 GameCore。

严格帧同步通常要求：

```text
客户端和服务器运行同一套确定性逻辑
每一帧消费相同输入
两端得到相同结果
```

当前项目是：

```text
服务器 GameWorld：完整权威规则
客户端：只预测自己的移动，远端主要插值
```

因此更准确的定位是：

> 服务端权威状态同步中的无渲染 `GameWorld`，不是客户端和服务器共用的确定性帧同步核心。

以后若做技能、Buff、AI，可以继续把其中可预测、无渲染的纯规则抽为独立逻辑模块；不需要强行把整个项目改成帧同步。

### 面试话术

> `GameWorld` 借鉴了 GameCore 的无渲染逻辑思想：输入驱动 Tick，输出状态和事件，并可独立测试。但当前不是严格帧同步，因为客户端没有运行完整同构逻辑，只对自身移动做预测、对远端做插值；项目仍以服务器状态为最终事实。后续可对技能或 Buff 等局部纯规则进一步抽取逻辑核心，而不必改变主体状态同步架构。

---

## 五、UDP、JSON 与协议 DTO：三个概念不能混淆

当前项目：

```text
传输：UDP
序列化：JSON
协议 DTO：Messages.cs 与客户端对应消息类
```

```text
UDP
-> 包如何在网络上传输

JSON
-> 一条消息如何编码成字节

DTO / 协议
-> 消息有哪些字段、字段分别代表什么
```

### 面试话术

> 我将 UDP、JSON 和协议 DTO 分开理解：UDP 决定传输的实时性与不可靠特性，JSON 决定字段如何编码，DTO 则定义消息语义。替换 JSON 为 Protobuf 是序列化层优化，不会自动解决预测、回滚或可靠事件问题；协议字段的设计和消息语义仍是独立问题。

---

## 六、为什么使用 UDP

位置快照、瞄准方向和移动输入更看重新鲜度：

```text
Tick 100 的位置
Tick 101 的位置
Tick 102 的位置
```

Tick 100 丢失时，客户端通常不需要等它补发，直接使用 101 或 102 更有价值。UDP 不会因为旧包缺失阻塞后续状态，适合实时状态同步。

UDP 不保证送达、顺序和去重，所以项目按消息业务语义补充机制：

```text
位置快照
-> 新 Tick 优先，旧快照丢弃

开火请求
-> fireSequence + 有限重发 + FireReceipt

死亡重生
-> eventId + ACK + 有限重发 + lifeStateVersion
```

### 面试话术

> 我选择 UDP 是因为位置快照和连续输入更看重新鲜度，旧包即使补到也没有同步价值；TCP 的队头阻塞反而会拖慢后续状态。对于不能随意丢失的开火、死亡和重生，我不把整条链路改成 TCP，而是在 UDP 上按消息语义增加序列号、收据、ACK、去重和有限重发。

---

## 七、为什么当前使用 JSON

JSON 的优点：

```text
抓包、日志、Console 输出可直接阅读
字段含义清楚
Unity JsonUtility 与 .NET System.Text.Json 易于使用
调试成本低
```

例如：

```json
{
  "type": "FireRequest",
  "fireSequence": 42,
  "requestTick": 318
}
```

JSON 特别适合当前少量玩家、需要理解协议与时序的学习型 Demo。

JSON 的代价：

```text
字段名占字节
数字文本表达更长
序列化与反序列化存在字符串开销
高频、大量实体时带宽与 CPU 压力更大
```

### 面试话术

> 当前使用 JSON 的原因是协议、Tick 和事件链路能直接观察和调试，适合少量玩家的学习型 UDP Demo。JSON 在大量高频实体下会有字段名、文本数字和字符串解析开销，因此它不是最终性能方案；但在当前规模下，可读性和排错效率的收益大于二进制压缩收益。

---

## 八、为什么不急着换 Protobuf 或 MessagePack

Protobuf、MessagePack 等二进制协议主要优化：

```text
消息体积
序列化开销
反序列化开销
```

它们不会自动解决：

```text
服务端权威
本地预测
延迟补偿
远端插值
可靠事件
复制范围
```

当前网络性能更合理的优化顺序：

```text
先减少不必要实体和发送频率
-> 按客户端复制、范围过滤、高低频同步

再减少每个实体发送的字段
-> 确认基线上的字段差分

最后压缩单个字段的编码
-> 二进制协议、量化、位打包
```

在两人 Tank Demo 一开始改 Protobuf，会提高学习和调试成本，但不一定带来可观察的体验收益。

### 面试话术

> JSON 的性能短板我有明确认识，但没有为了术语而过早替换 Protobuf。网络优化应先减少无关实体和发送频率，再处理确认基线上的差分字段，最后才压缩字段编码。当前项目先完成按客户端复制和高低频同步；当实体量、快照字节数和序列化耗时证明 JSON 成为瓶颈时，可以在 DTO 与序列化层替换二进制协议，而不改动 `GameWorld` 规则。

---

## 九、协议是客户端与服务器的共同契约

同一种消息在两端都有 DTO：

```text
服务器
-> Server/Server/Messages.cs

客户端
-> Assets/Code/UdpNetworkClient.cs 底部消息类
```

例如 `FireRequest` 两端都必须一致理解：

```text
type
playerId
fireSequence
requestTick
aimX / aimZ
estimatedRttSeconds
interpolationDelaySeconds
```

若服务端改字段名、字段语义或默认值，而客户端没有同步改动，可能出现：

```text
反序列化默认值
字段丢失
逻辑判断错误
客户端表现与服务器规则不一致
```

JSON 字段名反序列化相对宽松，但不代表协议可以随意改。当前还没有正式的协议版本协商机制。

协议变更步骤：

```text
1. 明确字段语义和默认值
2. 同时修改客户端与服务端 DTO
3. 更新消息构造和消费逻辑
4. 双客户端验证
5. 对关键时序补服务端测试
```

### 面试话术

> 客户端和服务端的 DTO 共同构成协议契约。JSON 对未知字段相对宽容，但字段改名、删除或改变语义仍可能让一端读到默认值并产生隐蔽同步错误。因此协议改动必须同时更新两端 DTO、消息构造与消费逻辑，并通过双客户端和关键时序测试验证；当前尚未实现正式的协议版本协商，这是后续工程化边界。

---

## 十、当前协议与传输层边界

当前尚未实现：

```text
账号认证、Token、加密
协议版本协商
消息签名或防篡改
真正二进制序列化
快照 ACK 与字段差分基线
拥塞控制
公网 NAT 穿透与部署服务
```

未来若切换 Protobuf，理想改动位置是：

```text
Messages DTO / 序列化与反序列化层
```

而不是：

```text
GameWorld 的移动、命中、死亡规则
```

这体现职责拆分带来的迁移收益。

### 面试话术

> 当前协议层仍是本地 Demo 级别：没有认证、加密、版本协商、快照 ACK 或公网部署能力。我的设计目标是将这些变化限制在传输、会话和序列化边界内，而不污染 `GameWorld` 的权威规则；这也是职责拆分在后续协议升级中的实际价值。

---

## 十一、当前算不算 Dedicated Server

当前 C# 控制台 `UdpGameServer` 是独立运行的专用战斗服务器：

```text
Unity Client A
        \
         -> .NET UdpGameServer
        /
Unity Client B
```

它不渲染、不控制本地角色，只维护权威世界并处理网络请求：

```text
玩家连接
权威 Tick
移动、开火、命中、血量、死亡重生
快照复制
关键事件可靠机制
```

因此它已经符合 Dedicated Server 最本质的定义：

> 服务器是独立进程，只做游戏权威模拟和网络服务，不承担玩家画面或本地控制。

### 面试话术

> 当前项目使用独立运行的 .NET UDP 权威战斗服务器，服务器不依赖 Unity 渲染，也没有本地玩家控制，只维护 `GameWorld` 并向客户端复制状态。从运行形态上，它是单进程、单对局的 Dedicated Server 雏形。

---

## 十二、当前不是什么商业三端服务架构

当前尚未实现：

```text
账号与登录
大厅
匹配
房间列表
多个战斗房间
战斗服实例调度
数据库
服务器部署与扩缩容
```

典型的三端或多服务流程更接近：

```text
客户端
-> 登录 / 大厅 / 匹配服务
-> 分配房间或战斗服务器
-> 客户端连接指定战斗服
-> 对局结束后回大厅
```

当前更准确的表述：

> 实现了单进程、单对局的专用权威战斗服务器；尚未扩展到大厅、匹配和多房间战斗服调度。

### 面试话术

> 我不会把当前 Demo 夸大成完整商业后端。它已经具备独立权威战斗服务器的核心能力，但没有登录、大厅、匹配、多房间和实例调度。理解这一区别很重要：后者是产品规模扩大后的服务拓扑问题，不是简单再拆几个类就能完成的功能。

---

## 十三、向 UE 风格服务器架构演进的正确顺序

不应直接拆出很多空服务，建议按需求递进：

```text
第一步：独立战斗服务器
-> 已完成

第二步：Room / Match 概念
-> 一台战斗服维护多个独立 GameWorld
-> 每个 GameWorld 对应一个房间和玩家集合

第三步：大厅或匹配服务
-> 创建、加入、匹配房间
-> 为房间选择或启动战斗服务器实例

第四步：部署与调度
-> 多个战斗服进程
-> 房间分配、健康检查、异常回收
```

最重要的前提已经具备：

```text
GameWorld
不依赖 UDP
不依赖 Unity
不依赖某个具体客户端
```

因此把“一个 `GameWorld`”升级为“多个房间各自一个 `GameWorld`”，是自然扩展，不需要推翻现有战斗规则。

### 面试话术

> 若后续向 UE 常见的大厅、匹配、战斗服架构演进，我会先在现有独立战斗服上引入多个房间，每个房间持有独立 `GameWorld`；再把大厅匹配和战斗实例调度拆到外层服务。由于权威世界不依赖 UDP 和 Unity，房间化是把世界实例从一个扩展到多个，而不是重写移动、命中和复制逻辑。

---

## 十四、3C 接入后，哪些网络能力可以保留

从 Tank 换为第三人称角色时，以下网络核心可继续保留：

```text
客户端输入上报
服务器 Tick
本地预测与输入重放
远端快照插值
FireRequest / FireReceipt
历史回溯命中
按客户端复制
可靠生命周期事件
调试面板与弱网验证
```

主要替换或加强的是表现与物理规则：

```text
TankController
-> 角色输入、角色移动状态机

TankMotor
-> CharacterController 或角色运动逻辑

TankWeapon
-> 枪械、弹道、后坐力、动画与特效

二维圆形命中 + 已完成的静态墙遮挡
-> 三维人物 Capsule、角色 HitBox 与动态遮挡历史

同一 Tank Rigidbody
-> LogicRoot 与 VisualRoot 分离
```

3C 不是重新做网络项目，而是把现有网络逻辑接到更复杂的角色、相机、动画和地图上。

### 面试话术

> Tank 只是当前用于验证同步链路的表现载体。进入 3C 阶段后，输入上报、服务器 Tick、本地预测、远端插值、开火收据、历史回溯、分级复制和可靠事件都可以保留；主要需要替换角色移动、相机、动画、地图碰撞和命中盒。网络逻辑不需要推倒重来，而是从简单 Tank 表现迁移到角色 3C 表现。

---

## 十五、当前最合理的停止点与后续优先级

当前不应为了“看起来更商业”立刻加入：

```text
多线程分片
完整大厅匹配
数据库
Protobuf
多战斗服部署
完整 AOI
回放系统
```

这些会把重点从“吃透一个能讲清、能验证的网络项目”转为“堆叠很多暂时无法验证的模块”。

当前合理收口：

```text
完成复习
-> 代码链路走读
-> 弱网验收记录
-> 面试演练
-> 开始长期 3C 学习与表现升级
```

等 3C、人物移动和三维地图查询成熟后，再逐步升级：

```text
LogicRoot / VisualRoot
人物 Capsule、斜坡/台阶与动态物体碰撞
Collider 历史回溯
更可信的时钟映射
Room / Match 架构
```

### 面试话术

> 当前项目的目标不是伪造完整商业后端，而是完成一套可验证、能讲清的服务端权威状态同步闭环。我的下一优先级是把现有网络底座接入 3C、地图碰撞和逻辑表现分离，再根据实际性能与产品需求扩展时钟同步、Collider 回溯和房间架构；这比在 Demo 阶段提前堆多线程、数据库或多服务部署更符合工程收益。

---

## 十六、重点疑惑回顾

### 1. 服务器模块拆开，和单纯把 Program 拆成多个文件有什么区别？

区别在职责与依赖边界。单纯分文件后，网络层仍可能直接改血量、复制层仍可能修改世界；当前架构要求 `GameWorld` 不依赖 UDP/JSON/Unity，`UdpGameServer` 不直接改 `PlayerState`，`ClientReplicator` 不修改世界，因此模块可独立测试和替换。

### 2. `GameWorld` 是不是帧同步的 GameCore？

它具备无渲染、输入驱动 Tick 的逻辑核心思想，但不是严格帧同步 GameCore。当前只有服务器运行完整权威规则，客户端仅预测自身移动、插值远端状态，两端不追求每帧完全确定性一致。

### 3. 现在算 Dedicated Server 吗？

算简化的 Dedicated Server：独立进程、无渲染、无本地玩家控制、服务器权威战斗模拟。它尚不是大厅、匹配、多房间和多进程调度组成的商业服务架构。

### 4. 为什么不直接把 JSON 换成 Protobuf？

Protobuf 优化单条消息的体积与序列化成本，但不能替代兴趣范围、频率分级、快照基线、预测或可靠事件。当前少量玩家 Demo 先保留 JSON 的可读性；等性能数据证明序列化成为瓶颈，再在协议层替换。

---

## 十七、60 秒模块总结

> 服务端以 `GameWorld` 为无渲染权威逻辑核心，`UdpGameServer` 负责 UDP 收发与 Tick 调度，`ClientRegistry` 管理 Endpoint 会话和每客户端网络状态，`SnapshotBuilder` 与 `ClientReplicator` 分别负责读取世界和按客户端复制，可靠事件账本独立处理 ACK 与重发。当前使用 UDP + JSON：UDP 服务实时状态的新鲜度，JSON 服务学习阶段的可读和调试；二进制协议是后续性能优化而非同步正确性的替代。运行形态上，项目已是单进程、单对局的 Dedicated Server 雏形，未来可先房间化多个 `GameWorld`，再扩展大厅匹配与战斗服调度。Tank 二维静态碰撞与查询已经完成，当前最合理的后续是把网络底座迁移到人物 3C、三维角色碰撞和逻辑/表现分离中。
