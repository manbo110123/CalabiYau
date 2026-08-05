# 阶段 12B：可靠关键结果事件账本

本阶段只为少量“发生过一次”的战斗结果补充轻量 UDP 可靠语义，不改变快照和开火意图的职责。

## 消息分级

- `WorldSnapshot`：仍然不可靠、可覆盖，不等待任何 ACK。
- `FireEvent`、`HitEvent`、`HealthChangedEvent`：仍然不可靠；血量和 `isAlive` 同时持续存在于快照中，作为状态兜底。
- `DeathEvent`、`RespawnEvent`、`KillEvent`、`MatchEndEvent`：带全局递增的 `eventId` 与 `serverTick`，由可靠事件账本投递。
- `FireReceipt`：仍只确认 `FireRequest` 是否被服务器接收并做出入队决定，不承担命中、死亡或事件可靠确认。

当前 Demo 尚未定义对局胜负规则，因此 `MatchEndEvent` 的 DTO、客户端 ACK/去重与服务端账本路由已经就绪；未来产生 `MatchEndWorldEvent` 时会自动使用同一账本，不在本阶段凭空引入比赛规则。

## 服务端规则

每个 `ConnectedClient` 都有独立的 `ReliableEventLedger`。账本保存原始 JSON、关联实体、首次/上次发送 UTC 时间、发送次数和重发次数。默认每 250 ms 检查一次，最多重发 4 次；到达上限后删除待处理项并记录指标，不会无限发送。

客户端发送 `EventAck { eventId }` 后，服务器只从该客户端自己的账本删除对应项，并累计 ACK 延迟。离开复制范围的 Avatar 依赖事件会从该客户端账本取消；之后重新进入范围时以最新 `WorldSnapshot` 的生命状态为准。

## 客户端规则

客户端维护容量为 128 的最近 `eventId` 集合。首次收到事件才应用死亡/重生状态或记录击杀；重复包不重复播放或应用，只再次发送 `EventAck`。F3 面板显示最近已处理数与重复丢弃数；服务端每秒按客户端输出待确认数、重发数、ACK 延迟和超限数。

## 自动验证

`Server.Tests` 覆盖：

- 未 ACK 的关键事件到达重发间隔后重发；
- ACK 后不再重发；
- 达到重发上限后停止；
- 同一 `eventId` 只会首次处理；
- 待确认事件不会阻塞连续 `WorldSnapshot`。
