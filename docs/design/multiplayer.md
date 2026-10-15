# Multiplayer (co-op)

Status: **Survival Rift co-op implemented** (phases 1–2 of the owner's plan, 2026-10-15). Realities are next.

## What it is

Up to four friends play the Survival Rift together, each with their own saved character. One player hosts and the others join by IP. There is no PvP, no dedicated server and no matchmaking. Loot is instanced: each player sees and picks up only their own drops, and the rewards go into their own local profile.

Owner decisions (2026-10-15): co-op only, up to 4 players. One player hosts a listen server over ENet. Friends connect by IP, using a VPN (Tailscale, Radmin VPN, ZeroTier) or port forwarding. Steam/EOS relays come later. Loot is per player. Characters come from the clients and are trusted (friends only, no anti-cheat). The Survival Rift comes first.

## Architecture

**Host-authoritative.** The host runs the game exactly as it runs offline: director, monsters, damage, deaths, drops, experience. Two nodes are added to the run scene, and the game code doesn't know they exist.

- `scripts/Net/RiftHost.cs` (host) watches the game and tells clients what they need. It also applies their inputs and card picks, runs the revive rule, waits for everyone to load, and handles disconnects.
- `scripts/Net/RiftClient.cs` (client) shows the host's game. Nothing simulated on a client can deal damage: `NetMode.ClientSide` makes `Health` ignore every hit. Every number the player sees comes from the host.

Offline play doesn't change. Without an active session, no net node is added, `Coop` is false, and every single-player rule applies. All 11 pre-existing suites still pass.

### Players

- `Player.IsLocal`: the player this machine controls. It has the camera, HUD, inventory window and input, and is in the `local_player` group (`CombatQuery.LocalPlayer`).
- On the host, every other party member is a full `Player` driven by a `PlayerCommand` instead of the keyboard. The command carries move, aim, sprint, counters for dodge and flask presses, and a "choosing a card" flag. It has the member's own character (gear, gems, passives, ascendancy), rebuilt from the JSON their machine sent in the lobby (`Profile.ApplyCharacterTo`).
- On a client, the other players are **puppets**. Snapshots place them, and they never simulate.
- The client's own player moves at once from the keyboard (**prediction**). Every physics frame it sends the direction it actually moved, which covers WASD and click-to-move paths alike. The host's snapshot echoes the last input it applied, with the host's position at that point. If the host and the prediction for that input differ by more than 3 px, the client adds the difference and shifts every newer prediction by the same amount (**reconciliation**). It snaps outright beyond 300 px. The acked entry itself is shifted too: the first version didn't, and a repeated ack doubled the error every snapshot until the player flew off the map. The test caught it.

### Replication

| What | How | Rate / channel |
|---|---|---|
| Party numbers (time, kills, elites, bosses, surge, victory) | snapshot header | 20 Hz, unreliable |
| Every player: position, life, max life, downed, warded, invulnerable, revive progress | snapshot | 20 Hz, unreliable |
| The receiver's own extras: input ack, flask charges, the six cast timers | snapshot | 20 Hz, unreliable |
| Monsters near the receiver's player (1400 px; bosses always): id, position, life byte, hurt flash + ailment tint | snapshot | 20 Hz; far ones 5 Hz |
| Monster spawn (scene, rarity, mods, name, scale, max life) / despawn (died or removed) | event | reliable |
| Projectiles: spawn (position, heading, speed, range, scale, sprite), turn (chain/ricochet), end | event + local flight | reliable |
| Telegraphs and hits: `AreaBurst`, `LineStrike`, `BossBullet`, `SlashEffect`, `OrbitBlades` | event + local playback (no damage) | reliable, within 1800 px |
| The client's own drops (instanced): spawn, gone, what was collected | event, to that client only | reliable |
| Level / experience / new level-ups, character experience, gear found | event, to that client only | reliable, throttled |
| Banners, toasts, boss spawned / phase / attack name / callouts, exit portal, run begin/end | event | reliable |
| Damage numbers for the client's own hits | event, to that client only | reliable |
| Client → host: input (move, aim, sprint, choosing, dodge/flask counters, sequence) | input packet | every physics frame, unreliable |
| Client → host: ready, card pick (`UpgradeOffer.Key`) | event | reliable |

Notes:
- **Snapshots** are quantised: half-pixel 16-bit positions, life as a byte (monsters) or 16 bits (players). They are split into chunks of at most 1100 bytes, so no unreliable packet is fragmented (one lost fragment loses the whole packet).
- **Events** go out once per physics frame per client, in a batch stamped with the host clock. A client plays a batch when its interpolated view reaches that moment (100 ms in the past). A projectile then leaves the caster where the caster is drawn, and a monster's death puff plays where the client last saw it.
- **Interpolation**: clients draw monsters and puppets 100 ms behind the host clock, between the two snapshots around that moment. They extrapolate up to half a snapshot when one is late. The clock offset is the smallest delay seen, which drifts down slowly.
- **Strings** (scene paths, mod ids, sprite paths, orb ids) are interned once per session and referenced by 16-bit id.
- **Discovery is generic.** `RiftHost` listens to `SceneTree.NodeAdded` and describes every monster, projectile and effect at the end of the physics frame, after the director has applied rarity and scaling. Splitter children, boss adds and ascendancy effects are covered without touching their code.
- **Why not `MultiplayerSpawner`/`MultiplayerSynchronizer`?** In 4.1 they sync per node with no delta compression, and per-peer visibility (needed for instanced loot) is clumsy. Events plus batched snapshots give one place to quantise, chunk, cull by distance and measure.

### Engine notes (Godot 4.1.1)

- **Every RPC uses transfer channel 0.** In 4.1.1, `ENetMultiplayerPeer.put_packet` only applies the transfer mode on channel 0. On any higher channel it sends with no ENet flags, which means *unreliable*, whatever the `[Rpc]` attribute says. ENet's throttle then drops those packets when the round trip spikes. The test caught this: "reliable" ready messages, test answers and card picks were silently lost while a client was loading. Channel 0 still keeps reliable traffic (ENet system channel 0) apart from unreliable traffic (channel 1).
- Headless processes are uncapped. `--quit-after N` counts frames, so the co-op test sets `Engine.MaxFps = 60`.
- `GetPacketMode()`/`GetPacketChannel()` called inside an RPC describe the *next* queued packet, not the current one. Don't debug with them.

## Session and lobby

`scripts/Net/NetSession.cs` is an autoload (`/root/NetSession` on every machine, so its RPCs resolve whatever scene is loaded). It owns the peer, the lobby, starting the run, and every RPC.

- `NetTransport` is **the one place that creates the peer** (`ENetMultiplayerPeer`, default port 7777). A Steam or EOS relay only has to return its own `MultiplayerPeer` from `Host`/`Join`.
- Main menu → **Co-op (Survival Rift)** (`UI/CoopPanel`): Host (port) or Join (`ip:port`). The lobby lists up to 4 players with name, class and level, and marks the host. The host gets Start; everyone gets Leave and Back. Each player plays their *active* character.
- On connect, a client sends its character (save-file JSON), its window size and a protocol number. The host turns away a mismatched build, a full party, or a run already in progress (no late join).
- Start: everyone loads `Run.tscn`. The host holds the clock and the director until every client reports ready (clients repeat "ready" until the host answers), or 20 s pass.
- A session lasts one run. Leaving the results screen leaves the party.

## Party rules (`scripts/Net/PartyRules.cs`, one table, proposals)

| players | spawn rate | enemy cap | monster life | boss life | xp / drop share |
|---|---|---|---|---|---|
| 1 | ×1.00 | ×1.00 | ×1.00 | ×1.00 | ×1.00 |
| 2 | ×1.50 | ×1.15 | ×1.50 | ×1.70 | ×0.67 |
| 3 | ×2.00 | ×1.30 | ×2.00 | ×2.40 | ×0.50 |
| 4 | ×2.50 | ×1.45 | ×2.50 | ×3.10 | ×0.40 |

- **Loot is instanced.** Every kill rolls drops separately for every player, owned by them: experience gems, life orbs, gold, orbs and gear. On the host, a remote player's drops are invisible but simulated there. The client shows cosmetic copies and receives what was collected into its own wallet. Gear goes to the client as an item and lands in their game only. The share brings each player's experience and drop rate back near solo. Elite and boss drops are not shared down. Power-ups (Cataclysm, Lodestone) are rolled once per kill and go to the killer. The gem cap (300) is per player. **Shared loot later:** `PartyRules.InstancedLoot` is the switch to build it behind. Rolling once per kill and making drops visible to everyone is not implemented.
- **Character experience:** every player-credited kill gives every party member character and gem experience, × share.
- **Level-ups:** each player picks their own cards, rolled on their own machine from their own build. The pick's key is replayed on the host's copy (`RunBuild.ApplyKey`). Co-op never pauses. A player with the cards open is **warded**: they can't be hurt or act, and monsters ignore them. After 20 s the first card is taken for them. The pause menu doesn't pause either.
- **Deaths:** a player at 0 life is **downed**. Monsters ignore them, and after 1.5 s their camera follows a standing ally (spectating). A living ally standing within 70 px for 3 s revives them at 40 % life with 2 s of invulnerability. Progress drains while nobody stands there. **The run is lost when everyone is down.** Offline, death still ends the run at once.
- **Aggro:** each monster goes for the nearest living, non-warded player and re-checks every 0.4–0.7 s.
- **Spawning:** an edge spawn borders a random living player's screen and is rerolled if it would land in another player's view. A remote player's view is a rectangle of the window size their client reported. Swarms ring every living player. Bosses appear above the party's centre. The leash pulls back only monsters far from *every* living player.
- **The exit portal** (endless phase) ends the run when every living player stands in it.
- **Disconnects:** if a client drops or leaves, their player and drops go, everyone gets a toast, and spawns from then on scale to the smaller party. A leaving client gets their own results and rewards. If the host leaves, every client's run ends there with a banner, and each gets results and rewards for what they did. All of it is saved to each player's own profile.
- **Results and rewards** are computed per player on their own machine from the shared numbers (time, kills, bosses, outcome). They are saved to that player's local profile only.

## Testing

**Automated:** `./scripts/run_all_tests.sh CoopSmokeTest` (about 30 s). The test process hosts and starts two more Godot processes as clients on 127.0.0.1. It then drives a real Rift over ENet and asserts 27 checks:

- lobby on both sides
- players on every machine (puppets on clients)
- a host monster appears on both clients at the right spot
- client input moves its player on the host, and the prediction agrees with the host
- a kill seen on the clients, with drops instanced (the host's own visible, remote ones hidden, each client sees exactly its own)
- a level-up wards the chooser, and the pick is applied on the host with the same build history on both sides
- a client leaving: handled on the host and on the client, and the party carries on
- the host leaving: handled on the client

It judges by its RESULT line, mirrored to `user://test_results/CoopSmokeTest.txt`.

**Load bench:** `./scripts/run_headless.sh res://scenes/tools/CoopBench.tscn -- [--clients=3] [--run-time=350]` runs a full party of four at a late wave, then a Cataclysm. Output goes to `user://bench/coop_bench.txt`.

### Testing it yourself

1. **One PC:** run `./scripts/coop_local.sh`. It builds, opens a host window with the lobby on port 7777, then a second window that joins 127.0.0.1. Press Start in the host window. `COOP_CLIENTS=3` opens four windows. The joining windows play a throwaway copy of your profile (`--profile-readonly`), so the two windows never overwrite each other's save. Both use your active character.
2. **With friends:** everyone needs the same build.
   - **Tailscale / Radmin VPN / ZeroTier (easiest):** everyone joins the same network. The host presses Host, and friends Join with the host's VPN address, e.g. `100.101.102.103:7777`.
   - **Port forwarding:** the host forwards **UDP** 7777 to their PC on the router, allows the game through the firewall, and shares their public IP.
   - LAN: the host's local IP (`192.168.x.y:7777`).

## Performance (measured 2026-10-15)

Bench: a party of 4 (host plus 3 headless clients) on a 4-core cloud container. All four processes share the CPU, so times are pessimistic. Wire bytes come from ENet's own counters.

| Late wave (Rift clock 350 s) | 4 players |
|---|---|
| Monsters alive | 387 |
| Upload per client (wire) | 40 KB/s avg, 46 KB/s p95 (snapshots 38, events 0.6) |
| Host upload, 3 clients | ~120 KB/s (~1 Mbit/s) |
| Host replication step (`RiftHost`) | 0.8 ms avg, 3.0 ms p95 |
| Host physics step (whole game) | 82 ms avg (solo, same clock: 253 monsters, 29 ms) |
| Client frame (headless) | 16.9 ms avg, 20.6 ms p95 |
| Client replica update | 0.8 ms avg |
| Cataclysm killing 366 at once | replication ≤0.5 ms; 12 KB/s per client over the next 2 s |

- **What co-op itself costs:** under 1 ms of host CPU per step and ~40 KB/s per client. Mass deaths are cheap on the wire (a despawn is 4 bytes).
- **The heavy part is the monster count.** A first table with the enemy cap ×2.05 for four put 535 monsters on the host and pushed its physics step to 129 ms. The cap now grows slowly (×1.45). Extra pressure comes from spawn rate and life instead.
- The host's whole-game physics step is still well above 16 ms on this shared container. That comes from per-monster simulation, which is already 29 ms solo here. Measure it on a real PC.

## Known limitations

- Survival Rift only. Realities, the hideout and the tutorial are single-player.
- Not replicated: shadow clones and consecrated ground (the host's ascendancy effects still work there), boss cast bars (attack names and callouts are), dodge afterimages of other players, other players' facing while standing still, and the sound of hits that land on other players' monsters.
- No late join, no rejoin after a drop, and no lobby between runs: host again.
- Remote characters are trusted, with no validation (friends only, by decision).
- Clients predict only their own movement. Monsters and projectiles show 100 ms in the past plus latency, so a projectile can look like it misses at high ping.
- `--profile-readonly` windows on one PC share the same active character.
- Not yet measured: real internet latency and packet loss, and a host on real desktop hardware.

## Next steps

- **Realities:** the same host/client split. `RealityController` gets the `RunState` treatment (per-player loot and portals), clients build the cavern from the seed (`CaveGenerator` is already seeded), `FlowField` follows several players (a multi-source field), and the boss arena gate waits for the party.
- **Steam/EOS:** a `NetTransport` branch returning a Steam/EOS `MultiplayerPeer`, a lobby invite instead of an IP, and a relay with no port forwarding.
- Bandwidth, if needed: delta-encode monster positions against the last acked snapshot, and send far monsters at a lower rate still.
- A lobby that persists between runs, and rejoin by name after a drop.
