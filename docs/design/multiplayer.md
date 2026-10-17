# Multiplayer (co-op)

Status: **Survival Rift co-op implemented** (phases 1–2 of the owner's plan, 2026-10-15). Co-op level-ups (one party bar, rounds that pause everyone, a 10 s timer) 2026-10-17. Realities are next.

## What it is

Up to four friends play the Survival Rift together, each with their own saved character. One player hosts and the others join by IP. There is no PvP, no dedicated server and no matchmaking. Loot is instanced: each player sees and picks up only their own drops, and the rewards go into their own local profile.

Owner decisions (2026-10-15): co-op only, up to 4 players. One player hosts a listen server over ENet. Friends connect by IP, using a VPN (Tailscale, Radmin VPN, ZeroTier) or port forwarding. Steam/EOS relays come later. Loot is per player. Characters come from the clients and are trusted (friends only, no anti-cheat). The Survival Rift comes first.

Owner decision (2026-10-16, "option 3"): direct-IP hosting should work without a VPN, safely. Hosting opens the router port by UPnP, a join code protects the lobby, and the host enforces limits on everything a client sends. See "Security".

## Architecture

**Host-authoritative.** The host runs the game exactly as it runs offline: director, monsters, damage, deaths, drops, experience. Two nodes are added to the run scene, and the game code doesn't know they exist.

- `scripts/Net/RiftHost.cs` (host) watches the game and tells clients what they need. It also applies their inputs, runs the party's level-up rounds (`RiftHost.Rounds.cs`, see "Level-ups"), runs the revive rule, waits for everyone to load, and handles disconnects.
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
| Party level / experience (the shared bar) / new levels, character experience, gear found | event, to that client only | reliable, throttled |
| Level-up round: id, start pick or level, level, rounds left, chosen / total, deadline (host clock), "you have chosen" | event (full state, sent on every change) | reliable |
| The card the host applied for this player (their pick, or the timer's) | event, to that client only | reliable |
| Banners, toasts, boss spawned / phase / attack name / callouts, exit portal, run begin/end | event | reliable |
| Damage numbers for the client's own hits | event, to that client only | reliable |
| Client → host: input (move, aim, sprint, choosing, dodge/flask counters, sequence) | input packet | every physics frame, unreliable |
| Client → host: ready; the row on screen in a round (keys, on open and per reroll); the card picked (round id + `UpgradeOffer.Key`) | event | reliable |

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
- **Pausing the tree in co-op.** A level-up round sets `SceneTree.Paused` on every machine. `NetSession` (autoload), `RiftHost` and `RiftClient` set `ProcessMode = Always`, so they keep receiving, sending snapshots and events, interpolating and running the round's clock. The level-up and results screens were `Always` already. `SceneMultiplayer` polls and delivers RPCs while the tree is paused.

## Session and lobby

`scripts/Net/NetSession.cs` is an autoload (`/root/NetSession` on every machine, so its RPCs resolve whatever scene is loaded). It owns the peer, the lobby, starting the run, and every RPC.

- `NetTransport` is **the one place that creates the peer** (`ENetMultiplayerPeer`, default port 7777). A Steam or EOS relay only has to return its own `MultiplayerPeer` from `Host`/`Join`.
- Main menu → **Co-op (Survival Rift)** (`UI/CoopPanel`, redesigned 2026-10-16, see `menus.md` → "Co-op"): *Host a game* or *Join a friend*. Joining needs **both** the host's address (it finds their PC) and the **join code** (it lets you in; the address alone is never enough). The host's lobby shows the address and the code in large type with Copy, **Copy invite** (`203.0.113.7:7777 K7Q-29M`, which a friend pastes into the address field to fill both), the router (UPnP) state, other LAN/VPN addresses, and **four party slots** with the free ones marked. The host gets Start run; everyone gets Leave. Each player plays their *active* character; without one, the screen offers to create one.
  - **Which address goes in the invite** (best first): the router's public address when UPnP opened the port; a VPN address (Tailscale 100.x, Radmin 26.x, Hamachi 25.x); the public IP the host looked up; otherwise this PC's local address. The local address is labelled in orange as *local network only — not your internet address*, because the owner tried inviting with a 192.168 address (2026-10-16). When the router didn't open the port and no VPN is running, the lobby offers **Find my public IP**. It asks `api.ipify.org`, only when pressed, and the result is labelled as reachable only once UDP 7777 is forwarded to this PC. CG-NAT connections have a public IP no forwarding can reach; for those the VPN is the way.
- **Join progress and failures.** `NetSession.Phase` tells the screen where a join is (`Connecting` → `CheckingCode` → `Joining` → `InLobby`) and `LastEnd` (`SessionEnd`) why the last session ended: unreachable, wrong code, version, party full, run started, character refused, not accepted (dropped before the code was read, e.g. a blocked address), host closed, host left the run. The host's refusal texts are the constants in `JoinRefusal`; the client classifies the reason it receives (the protocol number is checked first, so both ends run the same build). Nothing about what the host checks or how often changed.
- **Connect timeout: 12 s** (`NetSession.ConnectTimeout`). ENet alone keeps trying a dead address for ~30 s. The client now gives ENet a fixed 12 s maximum on the server peer right after `CreateClient` (`NetTransport.ShortenConnectTimeout`: `ENetPacketPeer.SetTimeout(32, 12000, 12000)`, which applies to the connect request because ENet computes its retry limit when the request is first sent, on the next poll), and the session has its own 12 s watchdog whatever the transport does. As soon as the host answers (the auth step starts) the peer goes back to ENet's defaults (32, 5 s, 30 s), so a stall mid-run doesn't drop a player sooner than before. Measured on loopback to an unused port: the failure card appears after 12.0 s.
- On connect, the client first sends the join code in the engine's authentication step (see "Security"). Once it is accepted, the client sends its hello: its character (save-file JSON), its window size and a protocol number. The host turns away a wrong code, a mismatched build, a full party, a run already in progress (no late join), or a character that fails the sanity checks.
- Start: everyone loads `Run.tscn`. The host holds the clock and the director until every client reports ready (clients repeat "ready" until the host answers), or 20 s pass.
- A session lasts one run. Leaving the results screen leaves the party.

## Security (direct IP without a VPN, 2026-10-16)

Hosting by IP over the open internet means anyone who finds the address and port can try to connect. Friends only, no anti-cheat, still holds: the aim is that a stranger can't get in, and that a broken or hostile client can't crash or stall the host. It is not about stopping a friend from cheating.

### UPnP: opening the port automatically (`scripts/Net/PortMapper.cs`)

- When the lobby opens, `NetSession.Host` starts `PortMapper.Open(port)`. Discovery and mapping are HTTP calls to the router that can take seconds. They run on **one background worker that runs every router call in order** (a chained `Task`). The main thread only sees a state (`Off`, `Working`, `Open`, `Failed`) and a message, which `Poll()` updates every frame from a result queue. The game never freezes. The only blocking call is at quit (below).
- The mapping is **UDP `port` → this PC**, description "ARPG co-op", with a **2 h lease**, renewed every hour while hosting. A refused renewal turns the lobby's line into the manual-forwarding advice. If the game crashes, the router drops it on its own when the lease runs out. Some older routers only accept leases with no expiry (`OnlyPermanentLeaseSupported`, or another error code on some models: any refused lease is retried once with none). Then the mapper maps with no lease, which stays until removed, and says so in the lobby.
- **Removed** when the lobby or session closes (Leave, Back, End, the host leaving the run from the pause menu), when **the host's run ends** (`RiftHost.OnRunEnded` → `ReleasePortMapping`; nobody can join a finished run), and on **quit**. On quit, `NOTIFICATION_WM_CLOSE_REQUEST` and the autoload's `_ExitTree` both call `ReleaseOnExit`, which runs once (a window close followed by `_ExitTree` doesn't wait twice) and waits up to 1.5 s for the router. Because the calls are strictly ordered, a removal issued while discovery is running is queued behind it, and the open checks a cancellation flag after discovery: a lobby closed while the router is still being looked for maps nothing at all, so quitting never waits for a discovery. A test pins this: close during a slow discovery gives only `discover`. `GetTree().Quit()` (menu Quit, pause menu) sends no close request; `_ExitTree` covers it.
- **Fallback.** No router answered, UPnP is turned off, the router refused, or the router's external address is not public. A non-public address is private, `100.64.0.0/10` CG-NAT, loopback, link-local or empty: a provider or second router shares it, so forwarding wouldn't work either. In that case nothing stays mapped and the lobby says: *"Your router didn't open the port automatically — forward UDP 7777 manually or use a VPN like Tailscale/Radmin."*, followed by the reason. Hosting keeps working for LAN and VPN friends either way.
- Not detected: a router that reports a public address while a second NAT sits in front of it (double NAT with a public-looking address). Spotting it would need an outside service to tell us our address, and we add no services. In that case the lobby shows an address friends can't reach, and the advice is the same: VPN.
- The lobby shows **"Friends join with: <external-ip>:<port>"** and Copy (`DisplayServer.ClipboardSet`), plus **"Same network / VPN:"** with this PC's LAN and VPN addresses (`IP.GetLocalAddresses`; 192.168/10/172.16 first, then 100.x Tailscale and 26.x Radmin).
- Tests and scripts on one PC skip UPnP (`Host(..., openRouterPort: false)`, `--coop-no-upnp`). The router is behind `IPortGateway`: `UpnpGateway` (Godot's `Upnp`) in the game, `NoGateway` and a recording fake in tests (`NetSession.GatewayFactory`).

### Join code (`scripts/Net/JoinCode.cs`)

- Every lobby draws a random **6-character code** (`RandomNumberGenerator`, crypto) from an alphabet without look-alikes (`23456789ABCDEFGHJKMNPQRSTUVWXYZ`, no 0/O/1/I/L), shown as `K7Q-29M`. "New code" draws another for people who haven't joined yet. Typing ignores case, spaces and dashes.
- The code travels in Godot's **authentication step** (`SceneMultiplayer.AuthCallback`/`SendAuth`/`CompleteAuth`). Until both sides complete it, the engine admits no peer and **drops every other packet from it**. So no RPC from a peer without a code ever runs, and the hello with the character is not even sent before the code is accepted. The message is `"ARPG"`, a 16-bit protocol and the code (at most 64 bytes, else refused). The comparison is constant-time.
- A wrong code gets an answer with the reason (the joiner sees "Wrong join code…") and the peer is disconnected once the answer has left. `ENetPacketPeer.PeerDisconnectLater`: ENet's plain disconnect would drop the answer first. A peer that never acknowledges the answer is dropped outright 2 s later (`PeerDisconnectNow`), so it can't linger ~30 s. A peer that sends nothing is dropped by the engine after `AuthTimeout` (5 s), **and that counts as a failed attempt**. The hello timeout (10 s after admission) stays.
- **Failed attempts are rate-limited per address**: 5 failures (wrong or malformed code, or no code before the timeout) within a minute, then the address is ignored for a minute. Ignored means disconnected on arrival, with no answer, even with the right code. IPv6 counts per /64 (a home connection owns a whole /64). At most 4 connections per address may wait for their code at once. Across all addresses, 120 failures in a minute turn every newcomer away until the window drains, so many addresses don't multiply the guessing rate. The tables are bounded (1024 entries, stale ones pruned, then the oldest), never reset wholesale. 31⁶ ≈ 887 million codes at 120 guesses a minute is about 14 years for half the space. A version mismatch doesn't count as a failure.
- **ENet has 32 connection slots**, far more than the party. The party size (4) is enforced by the lobby. Before, ENet had exactly 3: three strangers holding code-less connections locked every friend out, and ENet drops extra connections silently, so even the "party is full" answer never went out.
- **Fails closed.** If the session can't install the join-code callback on the `SceneMultiplayer` its RPCs go through, it refuses to host or join. The engine only runs the auth step when a callback is set; without it, every peer would be admitted.
- Tests and the bench use the fixed `JoinCode.TestCode`. `scripts/coop_local.sh` draws a random code per launch and passes it to every window. The game never hosts without a code.
- Engine quirk (4.1.1): `complete_auth` sends its confirmation to whatever target peer was set last. The host therefore calls `SendAuth(id, ok)` right before `CompleteAuth(id)`. `SendAuth` sets the target, channel 0 and reliable mode.

### Host limits (`scripts/Net/NetGuard.cs` → `NetLimits`, one table)

| What | Limit | Real use |
|---|---|---|
| Auth message | 64 bytes | 12 |
| Hello (lobby entry + character JSON) | 256 K characters | about 1 KB for a fresh level-70 test character, tens of KB with a full bag |
| Input packet | 64 bytes | 14 |
| Client event batch | 4096 bytes | under 100 |
| Messages per peer | token bucket: 150/s, burst 300 | about 60/s (input every physics frame) |
| Rows reported per player per level-up round | 6 (`PartyRules.OfferReportsPerRound`) | 1, plus 1 per reroll (2 per run) |
| Cards in a reported row | 9 (`PartyRules.MaxOffersPerRow`) | 3 |
| Card key | 64 characters, shape checked (`RiftHost.WellFormedKey`) | under 30 |
| Wrong codes per address | 5 a minute, then ignored for 60 s | — |

| Auth wait | 5 s, then dropped and counted as a failure | under one round trip |
| Connections waiting for a code, per address | 4 | 1 |
| ENet slots | 32 | 4 |

- Over a size or rate limit, the peer is **kicked** (disconnected) and the lobby's security lines say who and why (`NetSession.SecurityLog`, the last three lines shown).
- `RpcInput` and `RpcClientEvents` are **ignored until the sender has a valid hello** (it is in the lobby). A second hello is ignored. Kicked peers' leftover messages are ignored.
- **Level-up messages (2026-10-17)** are checked against the open round, on the host. A row (`ClientOffers`) is taken only from a loaded player, for the round open now, before they have chosen: 1–9 well-formed keys, at most 6 rows per round. A pick (`ClientPick`) is taken only for the round open now, once, if its key is well-formed (the start pick takes only skill cards: `s:`/`l:`), is on the row that player reported, and applies to the host's copy of their build (`RunBuild.ApplyKey`, which now also refuses a level for a skill already at its maximum). Anything else is ignored and counted (`RiftHost.RefusedPicks`, `LastRefusal`), and the host's timer decides for that player. A client can't choose for anyone else (the sender id is the engine's), can't hold the game past the deadline, and can't end a round early.
- **What the host checks in a character** (`Profile.ValidateCharacterJson`, plain data, no item nodes): name 1–32 characters; level 1–70; known class; experience finite and ≥ 0; at most 600 passives, 64 ascendancy nodes, 200 gems (levels 1–20), 6 loadout slots, 160 items (16 affixes and 16 base stats each, values finite and within ±1 000 000, item level 0–200, valid rarity); every id at most 64 characters; base stats and modifier types must be known enum values, and an item's slot a real slot name; wallet values ≥ 0. NaN and infinity can't even be written in the JSON (`System.Text.Json` refuses the literals), and nesting deeper than 64 is refused by the parser. The lobby entry's name and level are taken from the character itself, and the window size must be finite (then clamped as before). Failing any check refuses the player with the reason ("Your character was refused (level 999 is out of range)").
- `ServerRelay` is off: clients only talk to the host and don't learn each other's peer ids. `AllowObjectDecoding` stays off (Godot's default), so no RPC can make the host build an arbitrary object.
- `NetWriter.Str` no longer overruns on strings over 65 535 bytes. It cuts at the last whole UTF-8 character that fits. The old version wrote the whole string after a clamped length.

### A hostile host (what a client accepts)

Clients trust the host's *game*, not its bytes. A friend's host may be modified or broken; it must not hang, exhaust or corrupt the client's machine or save. `RiftClient` (limits at the top of the file) and `NetSession.SanitizeLobby`:

- **Lobby list:** at most 4 members with distinct ids; names cut to 32 characters; window sizes finite and clamped; a character that fails the same checks the host runs is dropped (that puppet gets a fresh build). Before, 50 junk members would have spawned 50 full players. A refusal reason is cut to 200 characters.
- **Numbers:** every float from the wire is made finite and clamped (radius, delay, speed, range, scale, duration, life). The snapshot clock must be finite (a NaN clock made every interpolated position NaN). Event batches with a NaN stamp play at once; at most 600 batches wait.
- **What reaches the save:** the run clock is clamped to a day, kills to ≥ 0, a collected drop to 0–1 000 000 gold, character experience to 0–10⁹, and gear drops must pass the same item checks as a character. **This was the critical bug:** an infinite run clock from the host became an infinite best time, `Profile.Save` truncated `profile.json` before serialising, the JSON writer threw on infinity, and the client's whole account (every character, the stash, the orbs) was left as an empty file. A finite 10¹⁰ s clock instead would have rolled 10⁸ reward orbs and hung the client.
- **Saving is atomic now:** `Profile.Save` serialises first and refuses to write on failure (the file on disk stays as it was), then writes a temporary file and moves it over the old one (`Profile.WriteAtomically`). This protects against any NaN anywhere in the account, not only from the network.
- **Scenes:** a monster spawn may only instantiate a `.tscn` under `res://scenes/enemies/` whose root really is an `Enemy`. Sprite frames must be `res://` resources that are `SpriteFrames`. Before, any scene of the game could be instantiated by path (a cast failure after instantiation, or a non-monster scene).
- **Node budget:** nothing new is spawned from host events once the run scene has 6 000 children (a late wave is under 2 000). 20 000 never-ending effects in one batch are tested: 14 000 are refused.
- The host can't remove the client's own player (`PlayerLeft` with the client's id is ignored). A malformed batch costs the rest of that batch, never the run. Strings are kept up to 512 characters, banner and toast texts up to 200.
- **Level-up rounds:** an unknown round kind is ignored; chosen/total are clamped to the party size, the level to 0–999, the deadline must be finite (0–60 s from now, else the countdown shows 0). A card the host applies is applied once per round and only if its key is at most 64 characters. The run's end always closes a round, and the client's own results screen follows. Accepted: a host can hold the pause for as long as it likes (as it could stop sending anything at all); the client can still close the game. Tested in `SecuritySmokeTest` (hostile host).
- **Accepted:** a host can still give a client rewards (gold, orbs, gear, experience within the bounds above). Friends could already edit their own saves, and loot is the host's to decide; the bounds only keep the numbers valid. A host can also send as much data as its bandwidth allows (memory grows with what is received, not faster).

### Still trusted, still exposed

- **Characters are trusted beyond sanity**: a friend can edit their save and bring a stronger character. That is by decision. The checks only keep the host from breaking.
- **Inputs are trusted the way a keyboard is.** Movement speed, cooldowns and damage come from the host's simulation of that character, so a client can't send "I hit for a million". A modified client can still play perfectly: aim, dodge timing.
- **The engine decodes a packet before our handler sees its size.** ENet accepts packets up to 32 MB, and Godot 4.1 doesn't expose that limit. That includes **auth packets from strangers**: the engine reassembles them before our 64-byte check (tested: a 1 MB "code" arrives in about a second on loopback and is then refused). The auth timeout and the per-address limits bound how often. Someone with the code can do the same with an RPC once before being kicked.
- **Pre-auth junk is dropped by the engine with an error line each** (`scene_multiplayer.cpp`: `ERR_CONTINUE` for any non-auth packet from a pending peer). C# can't rate-limit that. Tested: 5 000 junk packets cost the host a few frames and it keeps admitting friends. Flooding the UDP port itself (bandwidth) is beyond what any game can stop; relays hide the address.
- **Denial of service by many addresses:** someone with dozens of addresses can keep the global failure ceiling or the slots busy, so friends can't join while it lasts. Nobody already in the party is affected. Accepted for friends-only direct IP; relays remove it.
- **UPnP on the LAN:** any device on the host's network can answer discovery and pose as the router (it can show a wrong external address in the lobby, or refuse). UPnP has no authentication by design. The worst case is a wrong address shown, or no port opened.
- **A crash with a permanent mapping** (routers that only take leases with no expiry) leaves UDP 7777 forwarded to the PC until removed in the router's page. The lobby says "until you stop hosting" in that case.
- **IP exposure.** Everyone who joins learns the host's public IP (direct UDP). Clients don't learn each other's IPs. Strangers who scan the port learn that something answers there, and nothing more without the code. Share the code only with the people you invite. A new lobby means a new code.
- **No encryption.** ENet traffic is plain UDP: someone on the path (same café Wi-Fi) could read or tamper with it. Godot can wrap ENet in DTLS (`ENetConnection.DtlsServerSetup`), but certificates for player-hosted games are their own project. Relays solve it better.

### Why relays (Steam/EOS) are the step for a public release

UPnP fails on many networks: CG-NAT is common on mobile and fibre ISPs, and some routers have UPnP off or don't support it. Manual forwarding is too much to ask of players. Direct IP also exposes the host's address to everyone who joins. Steam Networking Sockets or EOS P2P give NAT traversal with a relay fallback, no open ports, no IP exposure (the relay hides it), encryption, identity (an account instead of a code) and invites. `NetTransport` is still the one place that creates the peer, so a relay transport returns its own `MultiplayerPeer`. The join code becomes a lobby invite, and the per-address limiter keys on the account id (`NetTransport.RemoteAddress`). The host limits and character checks stay as they are.

## Party rules (`scripts/Net/PartyRules.cs`, one table, proposals)

| players | spawn rate | enemy cap | monster life | boss life | xp / drop share | party xp to level |
|---|---|---|---|---|---|---|
| 1 | ×1.00 | ×1.00 | ×1.00 | ×1.00 | ×1.00 | ×1 |
| 2 | ×1.50 | ×1.15 | ×1.50 | ×1.70 | ×0.67 | ×2 |
| 3 | ×2.00 | ×1.30 | ×2.00 | ×2.40 | ×0.50 | ×3 |
| 4 | ×2.50 | ×1.45 | ×2.50 | ×3.10 | ×0.40 | ×4 |

Level-up numbers (same file):

| What | Value | Why |
|---|---|---|
| Party bar, experience to the next level | solo curve × players (`PartyExperience`) | keeps levelling at about the solo pace (below) |
| Time to choose in co-op | 10 s (`LevelUpTimeout`), host clock | owner request |
| Countdown turns red | at 5 s (`LevelUpUrgentSeconds`) | owner request |
| On timeout | the first card of the row that player was shown | predictable and said on screen |
| Rows reported per round / cards per row / key length | 6 / 9 / 64 | host limits (see "Security") |

- **Loot is instanced.** Every kill rolls drops separately for every player, owned by them: experience gems, life orbs, gold, orbs and gear. On the host, a remote player's drops are invisible but simulated there. The client shows cosmetic copies and receives what was collected into its own wallet. Gear goes to the client as an item and lands in their game only. The share brings each player's experience and drop rate back near solo. Elite and boss drops are not shared down. Power-ups (Cataclysm, Lodestone) are rolled once per kill and go to the killer. The gem cap (300) is per player. **Shared loot later:** `PartyRules.InstancedLoot` is the switch to build it behind. Rolling once per kill and making drops visible to everyone is not implemented.
- **Character experience:** every player-credited kill gives every party member character and gem experience, × share.
- **Level-ups:** one party bar, everyone levels together, the whole game pauses while the cards are up, picks are individual, 10 s to choose. See "Level-ups" below. (Until 2026-10-17 each player had their own level, the run never paused, and a chooser was warded for up to 20 s; that is gone.) The pause menu still doesn't pause the game in co-op.
- **Deaths:** a player at 0 life is **downed**. Monsters ignore them, and after 1.5 s their camera follows a standing ally (spectating). A living ally standing within 70 px for 3 s revives them at 40 % life with 2 s of invulnerability. Progress drains while nobody stands there. **The run is lost when everyone is down.** Offline, death still ends the run at once.
- **Aggro:** each monster goes for the nearest living, non-warded player and re-checks every 0.4–0.7 s.
- **Spawning:** an edge spawn borders a random living player's screen and is rerolled if it would land in another player's view. A remote player's view is a rectangle of the window size their client reported. Swarms ring every living player. Bosses appear above the party's centre. The leash pulls back only monsters far from *every* living player.
- **The exit portal** (endless phase) ends the run when every living player stands in it.
- **Disconnects:** if a client drops or leaves, their player and drops go, everyone gets a toast, and spawns from then on scale to the smaller party. A leaving client gets their own results and rewards. If the host leaves, every client's run ends there with a banner, and each gets results and rewards for what they did. All of it is saved to each player's own profile.
- **Results and rewards** are computed per player on their own machine from the shared numbers (time, kills, bosses, outcome). They are saved to that player's local profile only.

### Level-ups (2026-10-17)

Owner requests (2026-10-17): shared experience, so everyone levels up together; the cards pause the whole game for everyone; picks are individual and the game resumes when everyone has picked; show "chosen / total"; a 10 s limit with a countdown that turns red from 5, a tick every second, and an automatic pick; a skill pick for everyone at the start of the run.

**One party bar.** `RunState` keeps `PartyLevel`, `PartyExperience` and `PartyPendingLevelUps` in co-op, and mirrors them into every `RunPlayer`, so everything that reads "the player's level" still works. Experience gems stay instanced (you collect your own, as before), but every gem goes into the same bar, with the collector's own experience gain. The bar needs the solo amount **× the party size** (`PartyRules.PartyExperience`). Why: each player's gems are worth `Share` = 1 / spawn rate of a solo gem, and the party kills about spawn rate × as much, so the bar fills at players × share × spawn rate = **players ×** the solo rate. Scaling the need by the party size keeps the pace of a solo run: about twenty level-ups, so the party doesn't stop more often than one player would. A player who never collects slows the bar; that seemed fairer than levelling the party on one player's pickups. When someone leaves, the need shrinks with the party and the bar is checked again (it may level at once). Monster scaling per player is unchanged. Alternatives considered: XP needed unscaled (four players would level four times as fast, so twice as many pauses as a long solo run's twenty); gems shared by everyone (one gem pickup for all; it drops instanced loot's "your own drops" rule for one kind of drop).

**Rounds** (`RiftHost.Rounds.cs`). When the bar fills, the host opens a round:

1. The host pauses its scene tree (`SceneTree.Paused`), exactly the offline level-up pause. The director, monsters, projectiles, telegraphs, ailments (DoTs), cooldowns, the run clock, loot spawning and every player stop. The revive rule is held too, and a remote player's movement command is zeroed. The host still sends snapshots (positions don't change) and events.
2. Every client gets a `PartyRound` event, a full state: round id, start pick or level, the level, how many rounds are queued, chosen / total, the deadline, and whether that client has chosen. The client pauses its own tree and opens the cards. Like every event, it plays when the client's view reaches that moment (about 100 ms behind the host), so the world stops where it stopped on the host. A client's own player, predicted ahead, is pulled back to the host's position by reconciliation under the dimmed screen. While paused, a client sends no movement.
3. Each player's row is rolled on their own machine from their own build (composition table, keystone tiers, rerolls: progression.md). The client reports its row to the host when the round opens and after every reroll. Rerolls stay local; only the row is reported.
4. A choice is not applied on the client. It goes to the host, which checks it (see "Security"), applies it to its copy of that player's build, and answers with `PickApplied`. Only then does the client apply the same key. So the host and the client can never apply different cards, even when a pick and the timer cross on the wire. The host's own choice is applied at once.
5. Everyone in the party picks, **downed players included**. The round closes when every player still in the party has chosen. **Several levels at once** (a big burst of experience) are rounds back to back: the next round opens at once, still paused, with its own 10 s and its own counter, and the title says how many more come after. Why not queue the picks per player within one pause: the counter and the timer would mean different things for different players. With rounds, "1/3 chosen, 7 s" means the same on every screen.
6. The game resumes when the last round closes (`PartyRound` with id 0, and the host unpauses).

**The timer.** 10 s per round on **the host's clock**. The host sends the deadline as a host-clock time. A client converts it with the clock offset it already keeps for interpolation, so its countdown reaches 0 when a message sent at the host's deadline would arrive. Clients never decide on their own. At the deadline the host takes, for every player who hasn't chosen, **the first card of the row that player was shown** (it has the reported row). If a client never reported one, the host rolls a row from its copy of that build and takes the first card. The host's own player gets the first card of the host's screen. The client is told which card was taken ("Time's up — Fireball was taken for you"). Why the first card: it's predictable, the screen says so ("When the time runs out, the first card is taken for you"), and it's what the old 20 s timeout did. A random card was the alternative: no better for the player, and harder to anticipate.

**The start pick** is round one, for everyone, every co-op run, whether or not the character brought skills (progression.md -> "The start pick"). The host opens it once every client has loaded, or after the 20 s load timeout. Until it closes, the clock doesn't run and the director doesn't spawn. A client that loads late gets the open round on arrival (its `MarkReady` writes the state) and picks too. Tool runs with a preset first skill skip it (`RiftHost.SkipStartPick`). Every machine still starts the character's skills at level 1 the same way (`RunBuild.AdoptCharacterSkills`).

**Disconnects.** A player who leaves mid-round drops out of the total. Their pick, if any, is forgotten (they have left), and the round closes at once if everyone left has chosen. The host leaving ends every client's run as before. The run's end closes any round and unpauses, then the results screen pauses the tree as it does offline.

**The screen** (menus.md -> "Level-up and results"): the "1 / 3 chosen" line and the countdown sit in a panel at the top of the card screen. The countdown turns red at 5 s, with a tick every second (`Sfx` `tick`, and a brighter `tick_urgent` in the red) — only for a player still choosing; once you've picked, the countdown stays visible but silent (owner decision 2026-10-17). When the round closes every player gets `PartyRules.ResumeGraceSeconds` (1 s) of invulnerability, so nobody is hit in the frame the action restarts (owner chose this over a 3-2-1; solo gets the same after its cards). After choosing, the row gives way to "You chose X. Waiting for the others…" at the same height, so nothing jumps. The cards still own the keyboard. Esc doesn't reach the pause menu during a round.

**Solo is unchanged:** no rounds, no timer, the screen pauses the tree, queued levels back to back. The co-op path only runs with a session. `LevelUpScreen.PauseTree` is a separate switch for tests that drive the cards over a running tree; they used to borrow the co-op flag for that.

**Caveats.** A few ascendancy windows are measured in wall-clock time (a 2 s shadowstep, the leech-per-second window), so a pause can run them out. Solo pauses have the same behaviour. Snapshots keep their full rate during a round, about the same bandwidth as the moment the game paused (they could drop to a few per second; not needed so far). After a resume, a client's inputs reach the host one trip later, as always.

**Open questions (for the owner).**
- Pace: × party size keeps the solo pace. Owner (2026-10-17): keep it as is until it has been playtested; co-op could level a little faster later (one number in `PartyRules.PartyExperience`). The 10 s limit also stays for keystone rounds (owner: picks should be quick).
- Should keystone rounds (levels 5, 10, 15…) get longer than 10 s? A great keystone is a big decision to make under a timer.
- A short grace after a resume (half a second of invulnerability, or a "3-2-1"), so a client whose resume arrives a trip later isn't hit before they can move?

## Testing

**Automated:** `./scripts/run_all_tests.sh CoopSmokeTest` (about 60 s). The test process hosts and starts two more Godot processes as clients on 127.0.0.1. It then drives a real Rift over ENet and asserts 68 checks.

Security (2026-10-16). The attacks come from extra sessions in the same process, each on its own `SceneMultiplayer` branch (`SceneTree.SetMultiplayer(api, "/root/IntruderN")`), so the host sees them as separate machines:
- logic: join code format and matching, the attempt limiter, the message budget, `NetWriter.Str` past 65 535 bytes, character validation, public/CG-NAT address detection
- UPnP with a recording fake router: open → renew → close (`discover, map, map, unmap`); CG-NAT refused and nothing mapped; close during a slow discovery on a permanent-lease-only router still ends in `unmap`
- **no router at all** (the real `NoGateway` path): the fallback message, and hosting still works (the whole run below happens on that lobby)
- a wrong code is refused with the reason and disconnected; the right code (typed `test-42`) is accepted
- a flood of 1000 messages gets the peer kicked; an oversized hello is kicked; a level-999 character is refused
- after the party is gone, 5 wrong codes from 127.0.0.1, then the 6th is ignored; Leave releases the port mapping
- UPnP: close during discovery maps nothing (`discover` only); a permanent-only router gets lease 0; a failed renewal falls back to the manual-forwarding message

**Security suite:** `./scripts/run_all_tests.sh SecuritySmokeTest` (about 60 s). The host attacks itself with raw ENet peers (`RawPeer`, no Godot protocol) and extra sessions, then turns hostile against a client process:
- limiter logic: IPv6 /64, address rotation (5 000 addresses: a block survives, memory stays bounded, the global ceiling trips and drains), lobby sanitising, item and character validation (unknown stat id, bad slot, NaN literal, deep nesting), and a save with NaN that leaves the file on disk unchanged
- 3 code-less connections held while a friend still joins; a 5th waiting connection from one address ignored; timeouts counted; the slot holder ends blocked even with the right code
- 65 B, 64 KB, 256 KB and 1 MB auth packets refused by size; 5 000 pre-auth junk packets survived; 50 codes on one connection judged once
- a peer with the code that never says hello: its input, events and host-only RPCs (`RpcStartRun`, `RpcLobby`, `RpcRejected`, `RpcSnapshot`) are ignored or refused by the engine, and it's dropped after 10 s; five malformed hellos refused
- 20 connect/leave cycles leave no per-peer state behind
- **hostile host:** a 50-member poisoned lobby, monster spawns of a menu scene, a test scene, `user://profile.json` and a `..` path, NaN/infinite looks, gold and experience, an attempt to remove the client's own player, broken gear, a 60 000-character banner, 20 000 never-ending effects, a NaN clock, then run clocks of infinity and 10¹⁰ s. The client survives, its own player stays, and the profile it saves (a scratch file) is valid with finite values

The game itself:

- lobby on both sides
- players on every machine (puppets on clients)
- a host monster appears on both clients at the right spot
- client input moves its player on the host, and the prediction agrees with the host
- a kill seen on the clients, with drops instanced (the host's own visible, remote ones hidden, each client sees exactly its own)
- level-ups (2026-10-17):
  - the start pick opens for all three before anything acts: the host is paused with the clock stopped, the director off and no monsters; each client is paused and shows its own row with "0/3" and about 10 s
  - the counter replicates ("1/3" on a client after the host picks), and the run waits for the last pick, then begins with the same start card on both sides of every build
  - the party bar needs 3× the solo amount; half of it from alpha's gems opens nothing, the rest from beta's levels all three players together
  - during a round: on the host an awake slime doesn't move, the host takes no damage, the run clock is frozen and a 0.4 s telegraph stays pending for 1.5 s; on each client the round is open at the new level, the tree is paused, and the monster replica and the clock don't move
  - "2/3" shows on beta while beta hasn't chosen, and the game stays paused
  - beta never chooses: the countdown is red on the host and on beta at ≤ 5 s, ticks are played, and the round closes after 10.0 s (9.5–11.5 accepted) with beta's first card applied on the host and on beta alike
  - after the resume, the clock runs, the slime walks and the telegraph lands
  - 5 bad messages from alpha are refused (wrong round, a card not on its row, a malformed key, a 50-card row, a malformed row), and nothing is applied
  - a burst of two levels is two rounds back to back, paused between them
  - build histories match on both sides after all of it
  - a client leaving mid-round (host and beta have chosen) closes the round and resumes the game; beta is not left paused
- a client leaving: handled on the host and on the client, and the party carries on
- the host leaving: handled on the client

It judges by its RESULT line, mirrored to `user://test_results/CoopSmokeTest.txt`.

**Co-op screen:** `./scripts/run_all_tests.sh CoopUiSmokeTest` (about 25 s). Real `CoopPanel`s on the autoload session (host) and on in-process friend sessions, each with its own panel: the no-character state; empty fields, a malformed code and a bad port show inline errors and start nothing; the invite pastes back into address + code (also from a chat line with the code in two halves); the host lobby lists 4 slots with 3 free and the invite holds the code; a friend's panel goes Connecting → Lobby and both sides show 2 free slots; a wrong code ends on the *Wrong join code* card (`SessionEnd.WrongCode`); a fifth player ends on *The party is full*; the run-started and version reasons classify to their cards; closing the lobby shows *The host closed the lobby* on the friend's side; a join to an unused local port shows Connecting and fails within 12 ± 3 s on *Couldn't reach the host*; Cancel while connecting stops the session.

**Load bench:** `./scripts/run_headless.sh res://scenes/tools/CoopBench.tscn -- [--clients=3] [--run-time=350]` runs a full party of four at a late wave, then a Cataclysm. Output goes to `user://bench/coop_bench.txt`. It sets `RiftHost.AutoPickAll`: every round, the start pick included, is decided at once with the first cards, so the bench measures the fight and not the pauses.

### Testing it yourself

1. **One PC:** run `./scripts/coop_local.sh`. It builds, opens a host window with the lobby on port 7777, then a second window that joins 127.0.0.1. Press Start in the host window. `COOP_CLIENTS=3` opens four windows. The joining windows play a throwaway copy of your profile (`--profile-readonly`), so the two windows never overwrite each other's save. Both use your active character.
2. **With friends:** everyone needs the same build and a character. The host presses **Copy invite** and sends it; friends paste it into the address field (it fills the code too) and press Join.
   - **Direct (UPnP):** the host presses Host lobby. After a second or two the router line turns green ("Internet: ready") and the address shown is the public one. If the operating system asks, allow the game through the firewall. If the line is ember ("Internet: not reachable yet"), use one of the next two options.
   - **Tailscale / Radmin VPN / ZeroTier:** everyone joins the same network. The lobby then puts the VPN address (`100.x` / `26.x`) in the invite, e.g. `100.101.102.103:7777`.
   - **Port forwarding:** the host forwards **UDP** 7777 to their PC on the router, allows the game through the firewall, and shares their public IP.
   - LAN: the host's local IP (`192.168.x.y:7777`), listed under the invite with its own Copy invite.
   - Not testable in the cloud, so it needs a real check: UPnP on a real router (mapping shown in the router's page, removed on Leave, at run end and on quit), a friend joining from outside, and the CG-NAT message on a mobile hotspot.

## Performance (measured 2026-10-15)

> **Superseded 2026-10-16:** see `performance.md` -> "Co-op". The "physics step" below is Godot's worst-step-per-second monitor. The host was actually stuck at 8 physics steps per frame, running at about 45 % speed, so snapshots went at about 9 Hz. After the auto-aim and targeting fixes, the real-time figures are about 65 KB/s per client (0.52 Mbit/s) and about 1.6 Mbit/s host upload.

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
- Not replicated: shadow clones and consecrated ground (the host's ascendancy effects still work there), boss cast bars (attack names and callouts are), dodge afterimages of other players, other players' aim-based facing (a puppet faces where it moves), and the sound of hits that land on other players' monsters.
- No late join, no rejoin after a drop, and no lobby between runs: host again.
- Remote characters are trusted beyond the host's sanity checks (friends only, by decision; see "Security").
- Clients predict only their own movement. Monsters and projectiles show 100 ms in the past plus latency, so a projectile can look like it misses at high ping.
- `--profile-readonly` windows on one PC share the same active character.
- Not yet measured: real internet latency and packet loss, and a host on real desktop hardware.
- Level-up rounds are only tested on loopback. Over the internet, a client sees the round a little after the host (about 100 ms plus the trip), and its countdown is set to arrive with the host's deadline.

## Next steps

- **Realities:** the same host/client split. `RealityController` gets the `RunState` treatment (per-player loot and portals), clients build the cavern from the seed (`CaveGenerator` is already seeded), `FlowField` follows several players (a multi-source field), and the boss arena gate waits for the party.
- **Steam/EOS:** a `NetTransport` branch returning a Steam/EOS `MultiplayerPeer`, a lobby invite instead of an IP and code, and a relay with no port forwarding (see "Security" → "Why relays").
- Bandwidth, if needed: delta-encode monster positions against the last acked snapshot, and send far monsters at a lower rate still.
- A lobby that persists between runs, and rejoin by name after a drop.
