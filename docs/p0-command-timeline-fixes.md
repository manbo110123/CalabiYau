# P0: Command Timeline and Snapshot Fixes

## Purpose

This note records the first corrective pass after the stage-10 command timeline and
stage-11 per-client replication work. Its goal is not to add a new gameplay feature.
It closes protocol and authority gaps which could make the existing combat loop behave
incorrectly under packet loss, reordering, or a malicious client.

## Completed Changes

### Input lease

`GameWorld` now treats the latest movement input as a short lease rather than permanent
state. If no newer input is consumed for `InputHoldTimeoutTicks` (six ticks by default),
the authoritative movement and turn axes return to zero while the last valid aim point is
kept. A lost key-up packet can no longer move a server-side player forever.

### Discrete fire command queue

`FireRequest` now carries a positive `fireSequence`, `requestTick`, aim point, and the
client's RTT/interpolation estimates. The client no longer sends muzzle position or shot
direction. `UdpGameServer` only converts the packet to `FireCommand` and queues it through
`GameWorld.TryQueueFire`.

On the next server Tick, `GameWorld` consumes queued fire commands in sequence order and
performs cooldown checks, lag compensation, hit detection, damage, death, and event
creation. The server derives the muzzle and ray direction from its own current or rewound
authoritative shooter state and the validated aim point.

This makes the rule explicit: a UDP receive callback never changes combat state directly.

### Snapshot sequence

Every `ClientReplicator` now assigns its own `snapshotSequence`. It is separate from
`serverTick` because snapshot frequency can be lower than simulation frequency and different
clients receive different snapshot streams. The Unity debug panel's estimated snapshot loss
now counts gaps in this datagram sequence, not gaps in simulation ticks.

### Explicit full-state semantics

The temporary `UseFullStateFallback` switch was removed. Until a later baseline
acknowledgement protocol is implemented, every sent state entry is explicitly full state.
`changeMask` remains a reserved future protocol field; it is not advertised as real delta
compression.

### Source-control hygiene

The Unity-root ignore rules still ignore generated Unity solution files, but explicitly keep
the authoritative .NET server's `.csproj` and `.slnx` files. Nested `bin` and `obj`
directories are ignored so build products cannot be mistaken for recoverable source.

## Automated Checks

`Server.Tests` now verifies:

- out-of-order inputs still use the newest valid intent;
- expired input leases stop movement;
- a queued fire command does no damage before a Tick and duplicate fire sequences are rejected;
- dead players cannot submit input or fire commands;
- each per-client snapshot has an independent sequence, including snapshots built at the same server Tick.

## Deliberately Deferred

The next P1 topic is a reliable event ledger for death, respawn, and match-result events:
event identifiers, client acknowledgement, deduplication, and bounded resend. It should be
implemented before true delta snapshots, reconnect catch-up, or binary protocol experiments.
