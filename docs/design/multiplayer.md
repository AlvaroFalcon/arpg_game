# Multiplayer (co-op)

Status: **Survival Rift co-op implemented** (phases 1–2 of the owner's plan, 2026-10-15). Co-op level-ups (one party bar, rounds that pause everyone, a 10 s timer) 2026-10-17. **Party session 2026-10-18/19** (phase A): the party lives in the host's hideout between rifts, anyone home proposes a rift with a 30 s popup, "Invite friends" from the hideout's pause menu, edit permission for the host's town, and a loot bonus for playing together. See "Party session". Co-op Altered Realities are phase B.

## What it is

Up to four friends play the Survival Rift together, each with their own saved character. One player hosts and the others join by IP. There is no PvP, no dedicated server and no matchmaking. Loot is instanced: each player sees and picks up only their own drops, and the rewards go into their own local profile.

Owner decisions (2026-10-15): co-op only, up to 4 players. One player hosts a listen server over ENet. Friends connect by IP, using a VPN (Tailscale, Radmin VPN, ZeroTier) or port forwarding. Steam/EOS relays come later. Loot is per player. Characters come from the clients and are trusted (friends only, no anti-cheat). The Survival Rift comes first.

Owner decision (2026-10-16, "option 3"): direct-IP hosting should work without a VPN, safely. Hosting opens the router port by UPnP, a join code protects the lobby, and the host enforces limits on everything a client sends. See "Security".

## Architecture

**Host-authoritative.** The host runs the game exactly as it runs offline: director, monsters, damage, deaths, drops, experience. Two nodes are added to the run scene, and the game code doesn't know they exist.

- `scripts/Net/RiftHost.cs` (host) watches the game and tells clients what they need. It also applies their inputs, runs the party's level-up rounds (`RiftHost.Rounds.cs`, see "Level-ups"), waits for everyone to load, and handles disconnects. The downed rule and revives are shared by every mode (`CoopHost.Revives.cs`, see "Deaths and revives").
- `scripts/Net/RiftClient.cs` (client) shows the host's game. Nothing simulated on a client can deal damage: `NetMode.ClientSide` makes `Health` ignore every hit. Every number the player sees comes from the host.

**Replication layers (2026-10-19).** The shared part lives in two abstract bases, `scripts/Net/CoopHost.cs` and `scripts/Net/CoopClient.cs`: snapshots, event batches, the string table, monster/projectile/effect capture and replay, inputs and reconciliation, instanced drops, boss tracking, the load handshake, disconnects. They talk to the activity through `ICoopWorld` (players, ended, toasts, the loot hooks). Each mode adds a thin subclass: `RiftHost`/`RiftClient` (the run's numbers, run level, director banners and portal, level-up and cash-out rounds, revives) and `RealityHost`/`RealityClient` (see "Co-op realities"). The split was a pure move: the Rift's bytes on the wire are unchanged and `CoopSmokeTest` passed untouched. Neither base is ever instantiated from a scene.

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
| Every player: position, life, max life, downed, warded, invulnerable, an ally's revive channel on them, their get-up timer, whom they are reviving (protocol 7) | snapshot | 20 Hz, unreliable |
| The receiver's own extras: input ack, flask charges, the six cast timers | snapshot | 20 Hz, unreliable |
| Monsters near the receiver's player (1400 px; bosses always): id, position, life byte, hurt flash + ailment tint | snapshot | 20 Hz; far ones 5 Hz |
| Monster spawn (scene, rarity, mods, name, scale, max life) / despawn (died or removed) | event | reliable |
| Projectiles: spawn (position, heading, speed, range, scale, sprite), turn (chain/ricochet), end | event + local flight | reliable |
| Telegraphs and hits: `AreaBurst`, `LineStrike`, `BossBullet`, `SlashEffect`, `OrbitBlades` | event + local playback (no damage) | reliable, within 1800 px |
| The client's own drops (instanced): spawn, gone, what was collected; gear as the item's JSON, shown with its icon and filtered label (`EquipmentItem.ShowOnGround`) | event, to that client only | reliable |
| Party level / experience (the shared bar) / new levels, character experience, gear found | event, to that client only | reliable, throttled |
| Level-up round: id, start pick or level, level, rounds left, chosen / total, deadline (host clock), "you have chosen" | event (full state, sent on every change) | reliable |
| The card the host applied for this player (their pick, or the timer's) | event, to that client only | reliable |
| Banners, toasts, boss spawned / phase / attack name / callouts, exit portal, run begin/end | event | reliable |
| Damage numbers for the client's own hits: amount, the hit's qualities in one byte (crit, damage type, ignite/chill/shock/freeze applied, a heavy share of an elite's or boss's life) and the skill's loadout slot for its icon (protocol 8) | event, to that client only | reliable |
| Client → host: input (move, aim, sprint, choosing, dodge/flask counters, sequence) | input packet | every physics frame, unreliable |
| Realities: kills, monsters total, the shared portal pool, boss fight / cleared / collapsed flags, the receiver's mana | snapshot header (instead of the Rift's) | 20 Hz, unreliable |
| Realities: a gem drop's kind (`PickupExtra` 64), teleports (65), the end for everyone (66), the gate (67), the clear (68), the exit portal (69) | event (`RealityProtocol`) | reliable |
| Client → host, realities only: the six skill buttons held (one byte at the end of the input) | input packet | every physics frame, unreliable |
| Client → host: ready; the row on screen in a round (keys, on open and per reroll); the card picked (round id + `UpgradeOffer.Key`) | event | reliable |
| Client → host, every mode: start or stop a revive on a downed ally (`ClientRevive`: op + peer) | event | reliable, rate-limited |

Notes:
- **Snapshots** are quantised: half-pixel 16-bit positions, life as a byte (monsters) or 16 bits (players). They are split into chunks of at most 1100 bytes, so no unreliable packet is fragmented (one lost fragment loses the whole packet).
- **Events** go out once per physics frame per client, in a batch stamped with the host clock. A client plays a batch when its interpolated view reaches that moment (100 ms in the past). A projectile then leaves the caster where the caster is drawn, and a monster's death puff plays where the client last saw it.
- **Interpolation**: clients draw monsters and puppets 100 ms behind the host clock, between the two snapshots around that moment. They extrapolate up to half a snapshot when one is late. The clock offset is the smallest delay seen, which drifts down slowly.
- **Strings** (scene paths, mod ids, sprite paths, orb ids) are interned once per session and referenced by 16-bit id.
- **Discovery is generic.** `RiftHost` listens to `SceneTree.NodeAdded` and describes every monster, projectile and effect at the end of the physics frame, after the director has applied rarity and scaling. Splitter children, boss adds and ascendancy effects are covered without touching their code.
- **Skill visuals (audited 2026-10-23).** Every player skill draws through one of five nodes the host captures: swings (`SlashEffect`: Cleave, Double Strike, Glacial Hammer, Storm Strike), lanes (`LineStrike`: Sunder), bursts (`AreaBurst`: Frost Nova, Ground Slam, Meteor), orbits (`OrbitBlades`: Blades) and projectiles (Fireball, Frost Shards, Spark, Throwing Knives). A client casts nothing itself, so its own casts and everyone else's come back the same way, as events. The owner's report ("the warrior's slash showed for me but not for him") was a unit bug in the swing's replay: the host writes the arc in **degrees** and the client clamped it to 2π as if it were radians, so every swing on every client was a sliver of at most 6.3°. The wire format didn't change (only the client's clamp, now 0–360°), so the protocol number stays. A replayed swing also plays its hit sound now. `CoopSmokeTest` casts all 13 run skills from a client's copy on the host and the host's own Cleave, and both clients must replay every one (`SKILL-VISUALS-ON-CLIENT-*`, with the swing's arc). These are 2D overlays, drawn over the voxel view like on the host; projectiles and orbit blades get their voxel bodies from `HdView` like any sprite.
- **Ground gear on clients (2026-10-23).** A client rebuilds the gear the host drops for it from JSON (`Profile.DeserializeItem` → `Restore`), which used to skip the icon and the ground label that a rolled drop gets in `Initialize`: friends saw the scene's grey placeholder square with no name. `ItemDrop` now calls `EquipmentItem.ShowOnGround()` (the same icon, shadow and `FilterChain` label as the host). Orbs, shards, gems, gold and experience were already drawn the same (the `Pickup` draws itself from its kind and key). Test: `REALITY-CLIENT-DROPS-DRAWN-LIKE-HOST`.
- **Life bars use the host's maximum (2026-10-24).** Every player's snapshot record always carried their maximum life, but clients threw it away and clamped the host's life to their *local* stat. For a client's own player that happens to match: it applies the same cards after the host's `PickApplied`. For an ally it doesn't: a client never applies another player's run cards to its copy, so after Vitality (+83 maximum on a 420 build) the other client drew that ally as 151/420 (36 %) when the host had 151/503 (30 %), and **full at 420** (the owner's Pact + Vitality report, seen from a friend's screen). Now `Health.SetNetState` takes the host's maximum, and `ResourcePool.NetMax` overrides the local stat on clients, for every player, the local one included. The maximum travels as a rounded 16-bit number (502.83 shows as 503, like the HUD already rounded it). No wire change. Tests: `PACT-VITALITY-CLIENT-MAX-MATCHES-HOST` and `PACT-VITALITY-CLIENT-KILLS-FILL-THE-BAR` (the client's own HUD, which was right) and `PACT-VITALITY-ALLY-BAR-ON-OTHER-CLIENT` (failed before: max 420, full at 420).
- **Why not `MultiplayerSpawner`/`MultiplayerSynchronizer`?** In 4.1 they sync per node with no delta compression, and per-peer visibility (needed for instanced loot) is clumsy. Events plus batched snapshots give one place to quantise, chunk, cull by distance and measure.

### Engine notes (Godot 4.1.1)

- **Every RPC uses transfer channel 0.** In 4.1.1, `ENetMultiplayerPeer.put_packet` only applies the transfer mode on channel 0. On any higher channel it sends with no ENet flags, which means *unreliable*, whatever the `[Rpc]` attribute says. ENet's throttle then drops those packets when the round trip spikes. The test caught this: "reliable" ready messages, test answers and card picks were silently lost while a client was loading. Channel 0 still keeps reliable traffic (ENet system channel 0) apart from unreliable traffic (channel 1).
- Headless processes are uncapped. `--quit-after N` counts frames, so the co-op test sets `Engine.MaxFps = 60`.
- `GetPacketMode()`/`GetPacketChannel()` called inside an RPC describe the *next* queued packet, not the current one. Don't debug with them.
- **Pausing the tree in co-op.** A level-up round sets `SceneTree.Paused` on every machine. `NetSession` (autoload), `RiftHost` and `RiftClient` set `ProcessMode = Always`, so they keep receiving, sending snapshots and events, interpolating and running the round's clock. The level-up and results screens were `Always` already. `SceneMultiplayer` polls and delivers RPCs while the tree is paused.

## Session and lobby

`scripts/Net/NetSession.cs` is an autoload (`/root/NetSession` on every machine, so its RPCs resolve whatever scene is loaded). It owns the peer, the lobby, starting the run, and every RPC.

- `NetTransport` is **the one place that creates the peer** (`ENetMultiplayerPeer`, default port 7777). A Steam or EOS relay only has to return its own `MultiplayerPeer` from `Host`/`Join`.
- Main menu → **Play → With friends** → character (`UI/CoopPanel`, redesigned 2026-10-16, see `menus.md` → "Co-op" and "Main menu"): *Host a game* or *Join a friend*. Joining needs **both** the host's address (it finds their PC) and the **join code** (it lets you in; the address alone is never enough). The host's lobby shows the address and the code in large type with Copy, **Copy invite** (`203.0.113.7:7777 K7Q-29M`, which a friend pastes into the address field to fill both), the router (UPnP) state, other LAN/VPN addresses, and **four party slots** with the free ones marked. The host gets **Go to the hideout** (it used to be Start run); everyone gets Leave. Each player plays the character they chose in Play → With friends ("Playing as X · Change"); without one, the screen offers to create one.
  - **Which address goes in the invite** (best first): the router's public address when UPnP opened the port; a VPN address (Tailscale 100.x, Radmin 26.x, Hamachi 25.x); the public IP the host looked up; otherwise this PC's local address. The local address is labelled in orange as *local network only — not your internet address*, because the owner tried inviting with a 192.168 address (2026-10-16). When the router didn't open the port and no VPN is running, the lobby offers **Find my public IP**. It asks `api.ipify.org`, only when pressed, and the result is labelled as reachable only once UDP 7777 is forwarded to this PC. CG-NAT connections have a public IP no forwarding can reach; for those the VPN is the way.
- **Join progress and failures.** `NetSession.Phase` tells the screen where a join is (`Connecting` → `CheckingCode` → `Joining` → `InLobby`) and `LastEnd` (`SessionEnd`) why the last session ended: unreachable, wrong code, version, party full, run started, character refused, not accepted (dropped before the code was read, e.g. a blocked address), host closed, host left the run. The host's refusal texts are the constants in `JoinRefusal`; the client classifies the reason it receives (the protocol number is checked first, so both ends run the same build). Nothing about what the host checks or how often changed.
- **Connect timeout: 12 s** (`NetSession.ConnectTimeout`). ENet alone keeps trying a dead address for ~30 s. The client now gives ENet a fixed 12 s maximum on the server peer right after `CreateClient` (`NetTransport.ShortenConnectTimeout`: `ENetPacketPeer.SetTimeout(32, 12000, 12000)`, which applies to the connect request because ENet computes its retry limit when the request is first sent, on the next poll), and the session has its own 12 s watchdog whatever the transport does. As soon as the host answers (the auth step starts) the peer goes back to ENet's defaults (32, 5 s, 30 s), so a stall mid-run doesn't drop a player sooner than before. Measured on loopback to an unused port: the failure card appears after 12.0 s.
- On connect, the client first sends the join code in the engine's authentication step (see "Security"). Once it is accepted, the client sends its hello: its character (save-file JSON), its window size and a protocol number. The host turns away a wrong code, a mismatched build, a full party, or a character that fails the sanity checks. Since protocol 5 a running rift no longer turns anyone away: the newcomer lands in the hideout and waits there (see "Party session").
- A rift's start: its participants load `Run.tscn`. The host holds the clock and the director until every participant reports ready (clients repeat "ready" until the host answers), or 20 s pass.
- ~~A session lasts one run.~~ Since 2026-10-18 the session lasts until the host closes it: results → Hideout goes home with the party (see "Party session").

## Party session (2026-10-18, phase A)

Owner decisions (2026-10-18, binding): "Rift N" means an **Altered Reality of tier N** (co-op realities are phase B; the proposal already carries kind and tier). **The host always takes part**: the host declining, or not answering a friend's proposal in time, cancels the rift for everyone. **"Invite friends" lives only in the hideout's pause menu.** **The host leaving sends friends to the main menu with a notice.**

### State

- **Phase** (host-authoritative, in every `Roster`): `Lobby` (the main-menu lobby) → `Hideout` → `Run` (a rift runs on the host) → `Returning` (the host's results) → `Hideout`. Hosting from the hideout ("Invite friends") starts in `Hideout`.
- **Place** per member (each machine reports its own: `NetSession.ReportPlace`): `Menu`, `Loading`, `Hideout`, `Run`. A participant whose place leaves `Run` has left the rift: the host drops them from it (`RiftHost.OnPeerLeft`, the same path as a disconnect) and they stay in the party.
- **The rift** (`PartyRun`): id, `RiftSpec` (kind, tier, layout, seed, modifiers), participants, proposer. `NetSession.StartRift(spec, participants, proposer)` sends `StartRift` to the participants only; `StartRun()` is the lobby's "everyone" wrapper (tools, tests, `--coop-autostart` still enters the hideout).
- `InRun` now means "this machine takes part in the running rift". The router port stays open after a run (friends can still join the hideout); it closes when hosting stops.

### Shared hideout

Every machine runs its own `Hideout.tscn` with its own player, wallet, inventory, panels and quests. What is shared:
- **The host's town.** The host sends its layout (props and floors, no storage) as JSON (`RpcHideoutLayout`, ≤ 96 K characters) when a member joins or comes home and after every edit. Guests check it like any host data (`HideoutLayoutRules.SanitizeReceived`: ≤ 400 known props, unique ids, each station once under its own id, inside the town; floors only on walkable cells with known styles; missing stations added back) and show a copy. **A guest never saves the host's town as its own**: `Hideout.Data` is the town on screen, `Hideout.OwnTown` the account's; `SaveLayout` on a guest only captures the player. Ysolde's purchases go to the guest's own storage ("You're in Ana's town: …"), and the guide flag is the guest's own. Tested with guests whose scratch saves really write (`HIDEOUT-GUEST-SAVE-SAFE`).
- **Presence.** Each machine sends where its player stands and three flags (moving, sprinting, dodging) at 10 Hz (`PartyRules.PresenceHz`); the host relays everyone home to everyone home, whatever scene the host shows (it lives in the `NetSession` autoload, so friends who stayed home keep seeing each other while the host runs a rift). Puppets (`HideoutParty`, built by `PartySpawner` like Rift puppets, no collision, no input) are drawn 150 ms behind with the Rift's interpolation (`NetTrack`). About 230 B/s per guest. **Presence is sent reliably**: unreliable presence was tried first and ENet's packet throttle dropped 30–70 % of the guests' packets on loopback (ENet's own loss counter at 0; the throttle drops unreliable packets whenever round trips vary, as they do when the socket is serviced once per frame). Presence is tiny, so reliability costs nothing; the Rift's unreliable inputs and snapshots probably meet the same throttle (open question).
- **Nothing pauses in a party** (D13): `ModalPanel` doesn't pause while a session is active and the pause menu runs with `NoPause`; the hideout holds the local player still while a panel, the menu or the party overlay is open.
- **Late join** (D2): allowed whenever the party has room, also during a rift; the newcomer lands in the hideout (the host serves its saved town while away).
- **Leaving**: the host's "Close the party" (pause menu) or Stop hosting ends it for everyone; a guest's "Leave the party" (D12) goes back to their own hideout, alone. The host leaving sends guests to the main menu, which shows why once (`NetSession.MenuNotice`, "Ana closed the party.").

### Proposals

- Anyone home proposes: the Mirror's "Enter the Survival Rift" or walking into the portal (a confirm card first). The host arbitrates one proposal at a time on its clock (`RiftProposal`, pure and unit-checked) and asks everyone whose place is `Hideout`.
- Everyone asked has **30 s** (`PartyRules.ProposalSeconds`); not answering counts as staying. The host declining or not answering in time cancels it for everyone ("Ana's rift was cancelled — the host declined"); the proposer can cancel; a proposer who leaves cancels it. Otherwise it starts once everyone answered, or when the time runs out, with the proposer, the host and whoever joined (`PartyRules.ProposalCooldownSeconds` = 3 s between a cancel and the same player's next proposal).
- A "yes" (and a proposal) re-sends the player's current character (`RpcCharacterUpdate`, checked like a hello, at most one per 2 s, the old one kept when it fails), so the host builds what they wear after shopping in the hideout. Every machine saves its player just before it loads the rift (`NetSession.Launching`).
- Decliners stay home and see "Ana's party is in the Survival Rift · 3:12"; they get no snapshots or events (they are not linked in `RiftHost`). When the host's rift ends, participants come home through the results, still in the party.
- The shard of a reality is the proposer's, spent when the rift starts (D8, wired for phase B). The Mirror's "Step through the Mirror" is disabled in a party until co-op realities exist (`PartyRules.CoopRealities`), with a tooltip saying so.

### Invite friends (hideout pause menu)

`PauseMenu` takes a list of entries with live labels. Solo: "Invite friends", "Edit hideout", leave "Character select". Hosting: "Party & invite", "Edit hideout", leave "Close the party". Guest: "Party", "Edit hideout" (only with permission), leave "Leave the party". The entry opens `PartyOverlay`: the co-op screen in its Invite purpose over the running hideout. Offline it has one card, **Open my hideout to friends**, which hosts in place (phase `Hideout`, no scene change, UPnP as in the lobby); hosting shows the invite card, the slots with **Allow editing** per friend, and **Stop hosting** (friends to the main menu, the host stays home alone); a guest sees the slots and **Leave the party**. Close and Esc never end anything.

### Edit permission (D10)

Per friend, toggled by the host, this session only (never saved), revocable. **One editor at a time**, the host included: the lock (`NetSession.EditHolder`) is granted when the guest may edit, nobody holds it, and both are home. The guest edits a copy with the host's storage; closing the editor sends the whole layout back (`RpcLayoutSubmit`, lock holder only, ≤ 96 K characters). The host checks it (`HideoutLayoutRules.ValidateEdit`: the same stations, every moved or new prop on open ground, kinds kept, and for every decoration and floor tile placed + stored unchanged), then applies, saves and publishes it; a refused edit shows the guest why and the host's town comes back (no kick). The lock is freed on submit, cancel, revoke (the guest's editor closes), the editor or the host leaving the hideout, a disconnect, or a rift starting. A guest can't buy for the host: Ysolde's purchases go to their own storage. Without permission the H chip is greyed with "ask them to allow it".

### Protocol 5

All on transfer channel 0. Byte messages are written with `NetWriter` (`scripts/Net/SessionProtocol.cs`).

| RPC | Mode | Limit on the host |
|---|---|---|
| `RpcPartyToClient(byte[])` | Authority, reliable | client sanitises |
| `RpcPartyToHost(byte[])` | AnyPeer, reliable | 2048 bytes (`PartyMessageBytes`), member only |
| `RpcPresenceToHost(byte[])` / `RpcPresenceToClients(byte[])` | reliable (see above) | 32 bytes, sender must be home |
| `RpcHideoutLayout(string)` / `RpcEditCopy(string)` | Authority, reliable | client: 96 K characters, sanitised |
| `RpcLayoutSubmit(string)` | AnyPeer, reliable | 96 K characters (kick), lock holder only |
| `RpcCharacterUpdate(string)` | AnyPeer, reliable | 256 K characters, one per 2 s, `CheckMember` |

Host → client kinds: Roster (phase, the rift, edit holder, every member's place and edit right), Proposal, ProposalStatus, ProposalClosed, StartRift (to participants only; replaces `RpcStartRun`), EditRefused / EditRevoked, Notice, TestAsk. Client → host: Place, Propose, Answer, CancelProposal, EditRequest, EditCancel, TestAnswer. `TestAsk`/`TestAnswer` only run when `NetSession.TestHooks` is set (tests). `RiftSpec` is sanitised on both ends: known kind, tier 1–20 for a reality (0 for the Rift), at most 3 known distinct modifiers, a known layout or none. The plan's separate `RiftOver` message wasn't needed: the roster's phase says it.

### Decisions 2026-10-18

| # | Question | Decision |
|---|---|---|
| D1 | Where decliners wait | In their own `Hideout.tscn` showing the host's town; presence relayed by the host's autoload; a standing line "Ana's party is in …"; proposals refused until the rift ends |
| D2 | Late join | Allowed whenever there's room, also during a rift (lands in the hideout); joining a running rift is refused (they're asked next time) |
| D3 | Loot bonus | +15 % item quantity and +10 % item rarity per extra participant (see "Party rules"; a proposal) |
| D4–D7, D9 | Co-op realities | Phase B (difficulty, shared portal pool, boss gate, per-player loot and clears, leaving) |
| D8 | The shard | The proposer's, spent when the rift starts, never on a cancel |
| D10 | Edit permission | Per friend, session only, one editor at a time, guests can't buy for the host |
| D11 | New characters with friends | "X hasn't played the introduction": Play it first (solo) / Skip it and play with friends (`tutorial.md`) |
| D12 | A guest leaves on purpose | Back to their own hideout, alone |
| D13 | Pausing | Nothing pauses in a party; panels hold the player still instead |

## Co-op realities (2026-10-19, phase B)

An Altered Reality of tier N ("Rift 20" = a tier-20 reality) is proposed from the Mirror in the hideout with the same 30 s popup as the Survival Rift ("Ana is opening an Altered Reality — Tier 20"). The host always takes part; the host declining or not answering cancels it for everyone; whoever doesn't join waits in the hideout (D1). The proposer's shard is spent on the proposer's machine when the reality starts, never on a cancel (D8). Design rules: `docs/design/endgame.md` → "Co-op realities".

How it runs (`RealityController` + `RealityHost`/`RealityClient`):
- **Same cavern everywhere.** The host draws the seed when the proposal starts (`StartRift` carries it); every participant builds the layout from it (`LayoutBuilder.Rasterize`/`BuildScene` use only the seeded RNG). `CoopRealityTest` compares a hash of the cavern on both machines.
- **Only the host** spawns packs and the boss, simulates monsters, resolves damage and rolls loot. Clients show replicas (`NetMode.ClientSide`). The host's `RealityHost` is added before the packs spawn so it sees every monster appear; a client that loads later gets them all, plus which replica is the boss, the gate and the exit portal (`CatchUp`).
- **Sleeping packs** far from a client's player are not re-sent in snapshots (`SkipFarDormant`); their spawn record places them. Awake or near ones go at the usual rates.
- **Casting is pressed, not automatic.** A reality client sends the six skill buttons it holds (`PlayerCommand.HeldSlots`) with its input; the host casts for that player's copy at that player's cursor (`PlayerSkillInput` reads the command and never the host's keyboard for a remote copy). The client doesn't cast locally (no prediction of casts); its mana comes from the snapshot.
- **Monsters chase the nearest living player** along the walls: `FlowField.Update(targets)` is one breadth-first pass from every living player's cell.
- **Rewards on each player's own machine.** The host never writes a remote player's pickups into its own profile: shards, jewels, gems, orbs and gold are applied by the owner's client when the host says the drop was collected (`RealityController.ApplyReward`, shared by solo and client). Deaths, kills and the clear (`RealitiesCleared`, best tier, ascendancy points, `GameEvents.BossKilled`) are recorded on each machine for its own player (`RealityCleared` event). No slow motion on deaths in co-op.
- **Leaving.** A client's E at the exit portal or "Leave the reality" goes home alone and stays in the party (the host drops its player and drops). The host's E with friends inside asks for a second E within 4 s ("Your friends are still inside"); the host leaving, the pause menu's "Close the reality for everyone", or a collapse sends `RealityEnd` and everyone goes home after 3 s, keeping what they collected. The host disconnecting sends guests to the main menu with the notice.
- **Nothing pauses** (D13): the pause menu and panels don't stop the tree in a session.

## Security (direct IP without a VPN, 2026-10-16)

Hosting by IP over the open internet means anyone who finds the address and port can try to connect. Friends only, no anti-cheat, still holds: the aim is that a stranger can't get in, and that a broken or hostile client can't crash or stall the host. It is not about stopping a friend from cheating.

### UPnP: opening the port automatically (`scripts/Net/PortMapper.cs`)

- When the lobby opens, `NetSession.Host` starts `PortMapper.Open(port)`. Discovery and mapping are HTTP calls to the router that can take seconds. They run on **one background worker that runs every router call in order** (a chained `Task`). The main thread only sees a state (`Off`, `Working`, `Open`, `Failed`) and a message, which `Poll()` updates every frame from a result queue. The game never freezes. The only blocking call is at quit (below).
- The mapping is **UDP `port` → this PC**, description "ARPG co-op", with a **2 h lease**, renewed every hour while hosting. A refused renewal turns the lobby's line into the manual-forwarding advice. If the game crashes, the router drops it on its own when the lease runs out. Some older routers only accept leases with no expiry (`OnlyPermanentLeaseSupported`, or another error code on some models: any refused lease is retried once with none). Then the mapper maps with no lease, which stays until removed, and says so in the lobby.
- **Removed** when the lobby or session closes (Leave, Back, End, the host leaving the run from the pause menu), and on **quit**. (Until 2026-10-18 it was also removed when the host's run ended; the party now lives on in the hideout and friends may still join, so it stays while hosting.) On quit, `NOTIFICATION_WM_CLOSE_REQUEST` and the autoload's `_ExitTree` both call `ReleaseOnExit`, which runs once (a window close followed by `_ExitTree` doesn't wait twice) and waits up to 1.5 s for the router. Because the calls are strictly ordered, a removal issued while discovery is running is queued behind it, and the open checks a cancellation flag after discovery: a lobby closed while the router is still being looked for maps nothing at all, so quitting never waits for a discovery. A test pins this: close during a slow discovery gives only `discover`. `GetTree().Quit()` (menu Quit, pause menu) sends no close request; `_ExitTree` covers it.
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
| Revive intents (start / stop) per peer | token bucket: 4/s, burst 6 (`NetLimits.ReviveIntentsPerSecond`); over it refused and counted, not kicked | 1 per E press, 1 per release, at most 2.5/s while a held E is refused |
| Wrong codes per address | 5 a minute, then ignored for 60 s | — |

| Auth wait | 5 s, then dropped and counted as a failure | under one round trip |
| Connections waiting for a code, per address | 4 | 1 |
| ENet slots | 32 | 4 |

- Over a size or rate limit, the peer is **kicked** (disconnected) and the lobby's security lines say who and why (`NetSession.SecurityLog`, the last three lines shown).
- `RpcInput` and `RpcClientEvents` are **ignored until the sender has a valid hello** (it is in the lobby). A second hello is ignored. Kicked peers' leftover messages are ignored.
- **Level-up messages (2026-10-17)** are checked against the open round, on the host. A row (`ClientOffers`) is taken only from a loaded player, for the round open now, before they have chosen: 1–9 well-formed keys, at most 6 rows per round. A pick (`ClientPick`) is taken only for the round open now, once, if its key is well-formed (the start pick takes only skill cards: `s:`/`l:`), is on the row that player reported, and applies to the host's copy of their build (`RunBuild.ApplyKey`, which now also refuses a level for a skill already at its maximum). Anything else is ignored and counted (`RiftHost.RefusedPicks`, `LastRefusal`), and the host's timer decides for that player. A client can't choose for anyone else (the sender id is the engine's), can't hold the game past the deadline, and can't end a round early.
- **What the host checks in a character** (`Profile.ValidateCharacterJson`, plain data, no item nodes): name 1–32 characters; level 1–70; known class; experience finite and ≥ 0; at most 600 passives, 64 ascendancy nodes, 200 gems (levels 1–20), 6 loadout slots, 160 items (16 affixes and 16 base stats each, values finite and within ±1 000 000, item level 0–200, valid rarity); every id at most 64 characters; base stats and modifier types must be known enum values, and an item's slot a real slot name; wallet values ≥ 0; quest progress (docs/design/quests.md) at most 32 entries with ids up to 64 characters, status 0–5 and count 0–999 ("bad quest data"; the host never reads it, it only travels with the character). NaN and infinity can't even be written in the JSON (`System.Text.Json` refuses the literals), and nesting deeper than 64 is refused by the parser. The lobby entry's name and level are taken from the character itself, and the window size must be finite (then clamped as before). Failing any check refuses the player with the reason ("Your character was refused (level 999 is out of range)").
- **Revive intents (2026-10-24)** are checked on the host (`CoopHost.StartRevive`): the target must be a party member other than the sender, down, and not already being revived by someone else; the sender must be standing, in control and still (not moving, not dodging), and within `PartyRules.ReviveRadius` + `ReviveRangeSlack` (70 + 30 px, the slack covers the client's 100 ms view of the body). Anything else is refused and counted by reason (`CoopHost.RefusedRevives`, `ReviveRefusals`). The host also re-checks every step and stops the channel itself, so a client that never sends "stop" still can't revive from across the map, while moving, or faster than 5 s. Tested: `REVIVE-FORGED-INTENTS-REFUSED` (itself, someone standing, nobody, across the map, a flood) and `REALITY-REVIVE-FORGED-REFUSED`.
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
- **Revives (protocol 7):** whom a player revives is an index into the snapshot's own player list; anything out of range, the player itself, or a target that isn't down means nobody. Progress bytes are 0–255 by construction.
- **Co-op realities (2026-10-19):** the shared rules above apply to `RealityClient` (it inherits them from `CoopClient`). A teleport or an exit portal must land on a walkable cell of the cavern this machine built (a teleport into the void is ignored: `REALITY-HOSTILE-TELEPORT`); a shard's tier is clamped to 1–20; a collected gem must be a known skill or support id (a made-up id would otherwise be written into the character's gems); a collected orb stack is clamped to 1–1000; a jewel never takes `Found` past the tree's sockets; mana from the snapshot is clamped to the player's own maximum; an unknown end reason counts as "the host closed it". On the host, a client's extra input byte (held skill buttons) only has meaning for the six slots and is covered by the input size cap; a client that isn't a participant has no link, so its inputs are dropped; a proposal may pin a seed and layout (harmless: the layout must exist and the host draws its own seed only when none is given).
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

| players | spawn rate | enemy cap | monster life | boss life | xp / drop share | party xp to level | item quantity | item rarity | reality packs | reality portals (shared pool) |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | ×1.00 | ×1.00 | ×1.00 | ×1.00 | ×1.00 | ×1 | +0 % | +0 % | ×1.0 | the tier's |
| 2 | ×1.50 | ×1.15 | ×1.50 | ×1.70 | ×0.67 | ×2 | +15 % | +10 % | ×1.2 | +1 |
| 3 | ×2.00 | ×1.30 | ×2.00 | ×2.40 | ×0.50 | ×3 | +30 % | +20 % | ×1.4 | +2 |
| 4 | ×2.50 | ×1.45 | ×2.50 | ×3.10 | ×0.40 | ×4 | +45 % | +30 % | ×1.6 | +3 |

**Loot bonus (D3, 2026-10-18, a proposal for the owner to tune).** Loot is instanced and the share brings each player back to about solo, so playing together paid nothing extra. In the Survival Rift the chance-based gold, orb and elite gear rolls are multiplied by 1 + item quantity (after the share; life orbs aren't loot and keep their chance; bosses' fixed drops unchanged), and gear rarity rolls with the Rift's multiplier × (1 + item rarity) (`RunState.DropChances`, `LootQuantity`, `LootRarity`). A party of one gets exactly the old numbers in the same order. Realities add both to their own quantity and rarity (with the shard modifiers and jewels); there every participant rolls the full solo loot of every kill (no share), so the bonus is on top of solo. In a reality monster and boss life use the same columns as the Rift, packs are bigger by the "reality packs" column (rounded per pack), and the party shares one portal pool of the tier's portals plus one per extra player. The proposal popup and the Rift's start banner show "+30 % item quantity · +20 % item rarity".

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
- **Deaths: see "Deaths and revives" below.** In short: a co-op player at 0 life goes down where they fell, gets up alone after 10 s, or sooner with an ally's 5 s revive (hold E, standing still). The Survival Rift ends when everyone is down at once; a co-op reality spends a portal per fall and brings a downed party back at the entrance. Offline, death ends the run at once with the reward reached (the Rift) or costs a portal and respawns at the entrance after 1.5 s (a reality), as before.
  - **"Allies respawn instantly" (owner's report 2026-10-23).** The rule itself held: the host keeps a downed player down for 10 s, and both clients agree (`CLIENT-DOWNED-HOLDS-*` in `CoopSmokeTest` kills a *client's* player and asks all three machines at 1.5 s, 6 s and after). What broke it on screen: **life kept regenerating while down** (`ActorStats` regenerated whatever the `Health` said), so with any life regeneration (the Regeneration passive or card, Troll Blood, gear) a downed ally's life bar was full again within a second or two, and the revive then dropped them to half. A character level gained while down (every player-credited kill gives everyone character experience) and a Vitality card picked while down refilled it too. And a downed player still **stood upright**, only faded, in 2D and in the voxel view. Now the dead don't regenerate and nothing refills their life (`ActorStats.OwnerDead`), and a downed player **lies on the ground** (the sprite turns 90°, its voxel body lies flat, `Player.ShowsDowned`), greyed, with the revive ring and a "DOWN" label, and shows hollow on every map. This applies offline too, where a dead player regenerating was invisible anyway (death ends the run or respawns at once). In realities the "instant" respawn was the solo rule (back at the entrance after 1.5 s); that is gone too (2026-10-24).
- **Aggro:** each monster goes for the nearest living, non-warded player and re-checks every 0.4–0.7 s.
- **Spawning:** an edge spawn borders a random living player's screen and is rerolled if it would land in another player's view. A remote player's view is a rectangle of the window size their client reported. Swarms ring every living player. Bosses appear above the party's centre. The leash pulls back only monsters far from *every* living player.
- **The exit portal** (endless phase) ends the run when every living player stands in it.
- **Disconnects:** if a client drops or leaves, their player and drops go, everyone gets a toast, and spawns from then on scale to the smaller party. A leaving client gets their own results and rewards. If the host leaves, every client's run ends there with a banner, and each gets results and rewards for what they did. All of it is saved to each player's own profile.
- **Results and rewards** are computed per player on their own machine from the shared numbers (time, kills, bosses, outcome). They are saved to that player's local profile only.

### Deaths and revives (2026-10-24)

Owner decision (2026-10-24, binding): *"When a friend dies, they get back up after a 10 s delay, or an ally can revive them with a 5 s cast without moving, cancellable in case they need to dodge."* Every co-op mode: the Survival Rift and co-op Altered Realities. Solo play is unchanged. Code: `scripts/Net/CoopHost.Revives.cs` (the host's rule, shared by `RiftHost` and `RealityHost`), `PartyRules` (numbers and the pure rules), `UI/ReviveControl.cs` (E and the line on screen, every machine).

**Down.** A co-op player at 0 life goes down where they fell: lying, greyed, "DOWN", a hollow ring on every map. Monsters ignore them. They don't regenerate. They get up **by themselves after 10 s** (`PartyRules.RespawnSeconds`) with 50 % life (`RespawnLife`) and 2 s of invulnerability (`ReviveGrace`). The 10 s timer runs whatever happens, including while an ally channels; whichever finishes first wins. In the Rift the camera follows a standing ally after 1.5 s, as before.

**An ally's revive.** A living ally within 70 px (`ReviveRadius`) of a downed player sees **"Hold E to revive Ana"** above the skill bar. Holding E (`interact`, the key that already uses stations and portals) channels for **5 s** (`ReviveChannelSeconds`); completing it revives at the **same 50 %** as getting up alone (`ReviveLife` = `RespawnLife`; the old stand-by revive gave 40 %) with the same 2 s grace. The channel **stops**, and its progress is lost, when the reviver moves (any movement input: WASD, a click-to-move path, an automatic walk), dodges (Space), casts a skill they pressed, loses control (stunned: no control or warded; the game has no player stun yet, so this is a level-up card screen or the cash-out panel), goes down, lets go of E, or ends up further than 100 px (70 + `ReviveRangeSlack` 30). **Taking damage does not stop it.** In the Survival Rift skills cast by themselves on a timer; those casts don't count as the player acting and don't stop the channel (otherwise nobody could ever revive there). With E still held after a stop, the start is asked again every 0.4 s, so stopping again resumes from zero without a second press.

**Two allies.** One reviver per downed player, no double speed: the first ally whose start reaches the host owns the channel; a second ally's start is refused (`ReviveStop.Taken`) and their prompt says "Bob is reviving Ana". If the first ally stops, the second (still holding E) takes over from zero on their next retry. Why not let both work and keep the fastest: it would look like it works twice as fast; why not ignore the second silently: the prompt would lie. Switching targets (E next to another downed ally) stops the first channel.

**Authority.** The host decides everything. A client only sends a start intent (`RiftProtocol.ClientRevive`, op 1 + the target's peer id) when E is held next to a downed ally, and a stop (op 0) when E is released. The host checks it (see "Security" → host limits: range, standing, target down and free, rate), runs the channel on its clock, checks the stop conditions every physics step from its own simulation of the reviver (the reviver's movement and dodge come from their inputs, casts from the host's `SkillUser.CastStarted`), completes it and replicates the result. The host's own player goes through the same checks (`CoopHost.LocalReviveIntent`).

**Seen by everyone.** Every snapshot carries, per player, the channel on them, their get-up timer and whom they are reviving (protocol 7, two more bytes per player). The downed player shows a thin grey ring (getting up alone) and a thick green one (the ally's channel); the reviver shows a green bar over their head and a faint ring at their feet; on the party minimap and the reality maps the hollow ring of a downed ally fills in green ("Ana · being revived" on the big maps). The downed player's own screen reads **"Getting up in 7 s… or wait for an ally"**, or "Bob is reviving you… 3.1 s"; the reviver's "Reviving Ana… 3.1 s — stay still".

**Whole party down.**
- **Survival Rift:** unchanged: the run ends when everyone is down at once, paying the reward reached so far.
- **Co-op realities (decided 2026-10-24, there was no rule before):** nobody is left to revive, so 1.5 s after the last fall (`PartyRules.RealityWipeSeconds`) everyone is back at the entrance with full life and mana and 2.5 s of grace: the solo death rule, applied to the party. Each fall already paid its portal, so a wipe costs no extra portal. Meanwhile nobody gets up in place (`RealityHost.RevivesHeld`). Why not let everyone get up in place after their 10 s: monsters ignore the downed, so the party would wake up in the middle of whatever killed it, all at once; a wipe going back to the entrance is what a solo player knows. A participant leaving while everyone left is down is a wipe too.

**Realities and portals.** The 1.5 s respawn at the entrance is replaced by the downed state. **Every fall still spends one portal of the shared pool** (`PartyRules.RealityPortalsPerFall`), and the fall that empties it **collapses the reality** for everyone, as before (`RealityController.OnPartyMemberDied`, one place to change if portals are reworked). The boss fight resets once nobody standing is left in the arena (the gate opens, the boss sleeps); a downed player stays where they fell, in the arena or not, and someone getting up in the arena starts the fight again like anyone walking in. After the boss is down a fall costs nothing and the player gets up as usual (before, a fall after the clear left the player dead for good). A risen player keeps at least half their mana. Offline realities keep the solo rule.

| Number | Value | Where |
|---|---|---|
| Get up alone after | 10 s at 50 % life | `PartyRules.RespawnSeconds`, `RespawnLife` |
| Ally revive | 5 s channel, 50 % life | `ReviveChannelSeconds`, `ReviveLife` |
| Prompt range / host range | 70 px / 100 px | `ReviveRadius`, `+ ReviveRangeSlack` |
| Grace after getting up | 2 s | `ReviveGrace` |
| Reality: portals per fall | 1 | `RealityPortalsPerFall` |
| Reality: whole party down → entrance after | 1.5 s | `RealityWipeSeconds` |
| Revive intents per peer | 4/s, burst 6 | `NetLimits.ReviveIntentsPerSecond`, `ReviveIntentBurst` |

**Protocol 7.** The snapshot's player record grew by two bytes (get-up timer, reviving index) and clients send `ClientRevive` (client event 5), so `NetSession.Protocol` is 7: an older build is turned away at the door. All RPCs stay on channel 0.

**Tests.** `RiftProgressionTest`: `DOWNED-ALONE-RESPAWNS-AT-10S`, `DOWNED-ALLY-CHANNEL-5S`, `DOWNED-CHANNEL-LOST-WHEN-STOPPED`, `DOWNED-CHANNEL-BREAKS` (the pure rules). `CoopSmokeTest` (Rift, three processes): `DOWNED-RESPAWNS-IN-10S` (the host's own player, with its "Getting up in" line), `CLIENT-DOWNED-HOLDS-*` (a client's player, on all three machines), `REVIVE-ALLY-5S-STANDING-STILL` (a client holds E next to another client: up after 5.0 s at 50 %), `REVIVE-PROGRESS-SHOWN-EVERYWHERE` (ring, bar, party map, both clients' prompts), `REVIVE-ONE-REVIVER-AT-A-TIME`, `REVIVE-MOVING-CANCELS`, `REVIVE-DODGE-CANCELS`, `REVIVE-FORGED-INTENTS-REFUSED`, `RIFT-WIPE-ENDS-THE-RUN` (the end is caught by a test seam, `RunState.EndRunInterceptForTest`, so the rest of the run can be tested). `CoopRealityTest`: `REALITY-DEATH-POOL` (a portal spent, down in place, no teleport), `REALITY-SELF-REVIVE-10S`, `REALITY-ALLY-REVIVE-5S` (the host's own player holds E: the local path), `REALITY-REVIVE-MOVE-DODGE-CAST-CANCEL`, `REALITY-REVIVE-FORGED-REFUSED`, `REALITY-WIPE-BACK-AT-ENTRANCE`.

### Allies on the maps (2026-10-23)

Owner's question (2026-10-23): "In multiplayer, can we see the other players on the minimap?" Now yes, everywhere the party is:

| Where | Map | What an ally looks like |
|---|---|---|
| Shared hideout | town minimap and the big map (M / Tab) | a dot in their party colour; the big map adds the name |
| Altered Reality | the corner minimap and the Tab overlay | a dot (wherever they are, explored or not); the overlay adds the name and keeps an ally beyond the edge of the screen at the edge, in their direction |
| Survival Rift | the new **party minimap** (top left under the level, co-op only; movable in the HUD editor as "Party minimap (co-op)") | the arena's outline, the allies, the boss once it is up, the exit portal once it opens |

- **Party colours** (`UI/PartyMarkers.Palette`): by lobby slot, the host first: blue, orange, pink, yellow, away from your green arrow and the red of monsters. The name tag over a remote player uses the same colour, so the dot and the figure match. The roster is the host's, so a player has the same colour on every machine; when someone leaves, the players after them move up a slot (and a colour).
- **Down:** a hollow ring instead of a dot ("Name · down" on the big maps).
- Allies come from the scene's players (`CombatQuery.PlayerGroup`, never the local one), so the Rift's party, a reality's party and the hideout's puppets need nothing else. Nothing new on the wire: positions are the ones snapshots and presence already carry. Offline nobody is listed and the Rift shows no minimap, so single-player is unchanged.
- Tests: `HIDEOUT-MAP-SHOWS-ALLIES` (host and a guest, small and big map, names, position on the puppet, distinct colours, the host's colour the same on the guest), `REALITY-MAPS-SHOW-ALLIES` (minimap and overlay on both sides) and the downed ally on the host's minimap in `REALITY-DEATH-POOL`, `RIFT-PARTY-MAP-*` (host and both clients).

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

**The Colossus offer is a round too** (2026-10-18, run.md -> "Cash-out offer and the infinite Rift"). When the Colossus falls the host's director holds for 1.2 s and raises `CashOutOffered`; the host opens a round of kind `RoundCashOut` (3) once no other round holds the game. Same machinery: the whole game pauses, `PartyRound` carries the counter and a deadline (`PartyRules.CashOutTimeout`, 15 s), the screen is `CashOutPanel` with the "1 / 3 chosen" line and the red countdown. The row is fixed, "leave" or "stay"; a client answers with `ClientPick` (round id and the key), and the host accepts one answer per player in the open round, nothing else. **The party goes home only if everyone answers leave; silence counts as leave** (a player who is away is safest at home, and can't drag the others along). Any "stay" keeps everyone in: the host starts the endless phase, toasts "The party stays (Ana chose to keep going)...", and resumes with the one-second grace. All leave: `EndRun(true)`, a victory for everyone. A new `PartyRound` kind changes the wire format, so **`NetSession.Protocol` is 4**. Leaving later is not a vote: a client's Esc → Leave the party and the host's End the run for everyone open the panel in leave mode over the running game.

**Solo is unchanged:** no rounds, no timer, the screen pauses the tree, queued levels back to back. The co-op path only runs with a session. `LevelUpScreen.PauseTree` is a separate switch for tests that drive the cards over a running tree; they used to borrow the co-op flag for that.

**Caveats.** A few ascendancy windows are measured in wall-clock time (a 2 s shadowstep, the leech-per-second window), so a pause can run them out. Solo pauses have the same behaviour. Snapshots keep their full rate during a round, about the same bandwidth as the moment the game paused (they could drop to a few per second; not needed so far). After a resume, a client's inputs reach the host one trip later, as always.

**Open questions (for the owner).**
- Pace: × party size keeps the solo pace. Owner (2026-10-17): keep it as is until it has been playtested; co-op could level a little faster later (one number in `PartyRules.PartyExperience`). The 10 s limit also stays for keystone rounds (owner: picks should be quick).
- Should keystone rounds (levels 5, 10, 15…) get longer than 10 s? A great keystone is a big decision to make under a timer.
- A short grace after a resume (half a second of invulnerability, or a "3-2-1"), so a client whose resume arrives a trip later isn't hit before they can move?

## Testing

**Automated:** `./scripts/run_all_tests.sh CoopSmokeTest` (about 90 s). The test process hosts and starts two more Godot processes as clients on 127.0.0.1. It then drives a real Rift over ENet and asserts about 85 checks (2026-10-23 added: the party minimap on all three, every run skill's visual replayed on both clients with the swing's real arc, and a client's player staying down 10 s with life regeneration on, as seen by all three machines; 2026-10-24 added the revive checks, see "Deaths and revives": about 25 s more).

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

**Party session:** `./scripts/run_all_tests.sh HideoutCoopTest` (about 60–90 s). This process hosts and starts guest processes on loopback (`ana`, `bo`; later `cy` and `dee`), each with a scratch save that really writes; it asks them over the party channel (`TestHooks`) and judges everything. Pure checks first: `PROPOSAL-UNIT`, `PARTY-RULES-*`, `RIFT-QUANTITY-APPLIED`, `EDIT-RULES`. Then: everyone enters the host's hideout from the lobby, sees two puppets, presence follows a walking guest (< 24 px within 1.5 s) and is delivered (≥ 90 % each way), the guest shows the host's town, a guest's H opens nothing, a gem bought at Odran costs only the guest's gold, a panel open on a guest pauses nothing and holds the player, presence stays under 1 KB/s per guest, a guest's save keeps its own town; edit permission (allowed, lock, grant, a forged edit refused without a kick, revoke); proposals (popup texts, the host declining, a start for two with one guest waiting home, a late joiner during the rift, everyone home after the results, an unanswered host timing out); the host leaving (every guest at the main menu with the notice); then the host, alone at home, uses Invite friends, a new guest lands next to it, the pause entries for solo/host/guest, and Stop hosting leaves the host alone in the same town. Mirrored to `user://test_results/HideoutCoopTest.txt`.

**Co-op screen:** `./scripts/run_all_tests.sh CoopUiSmokeTest` (about 25 s). Real `CoopPanel`s on the autoload session (host) and on in-process friend sessions, each with its own panel: the no-character state; empty fields, a malformed code and a bad port show inline errors and start nothing; the invite pastes back into address + code (also from a chat line with the code in two halves); the host lobby lists 4 slots with 3 free and the invite holds the code; a friend's panel goes Connecting → Lobby and both sides show 2 free slots; a wrong code ends on the *Wrong join code* card (`SessionEnd.WrongCode`); a fifth player ends on *The party is full*; the run-started and version reasons classify to their cards; closing the lobby shows *The host closed the lobby* on the friend's side; a join to an unused local port shows Connecting and fails within 12 ± 3 s on *Couldn't reach the host*; Cancel while connecting stops the session. Since 2026-10-18 it also drives real main menus: `MENU-NOTICE` (a lost party's reason shows once), `MENU-PLAY-CHOOSER` (Play → Solo / With friends → the character list for each → the co-op screen), `MENU-FOCUS` (Back focuses the button that opened each screen), `MENU-TUTORIAL-PROMPT` (a new character with friends is asked about the introduction; Skip sets `TutorialDone`).

**Co-op realities:** `./scripts/run_all_tests.sh CoopRealityTest` (about 60 s). The host and one guest process (scratch save that really writes) go to the hideout; the host proposes a tier-3 reality with a pinned seed from the session's proposal path and the guest joins. Pure checks first (`FLOWFIELD-MULTI`, `PARTY-RULES-REALITY`). Then: the same cavern hash on both, two players on each side, every monster replicated, the shared pool and the boss known on the client; the guest walks and holds its skill buttons and the host's copy follows and casts; a kill gives both characters experience; a forced shard and item for the guest land in the guest's profile and floor while the host's shard count stays put and nothing of the guest's shows on the host; the host's pause menu stops nothing; a fall costs the pool one and the guest stays down where she fell, gets up alone after 10 s, is revived by the host holding E in 5 s, moving, dodging and a pressed cast stop that, forged intents are refused, and the whole party down brings both back at the entrance ("Deaths and revives"); a forged teleport outside the cavern is ignored; the host entering the arena pulls the guest in and closes the gate on both; the boss falls and the guest's clear is recorded on its machine with an exit portal; the guest goes home alone (its own save file has the clear) while the host plays on; the host closes it; a second reality collapses on the last portal and both are home. Mirrored to `user://test_results/CoopRealityTest.txt`.

**Load bench:** `./scripts/run_headless.sh res://scenes/tools/CoopBench.tscn -- [--clients=3] [--run-time=350]` runs a full party of four at a late wave, then a Cataclysm. Output goes to `user://bench/coop_bench.txt`. It sets `RiftHost.AutoPickAll`: every round, the start pick included, is decided at once with the first cards, so the bench measures the fight and not the pauses. `-- --mode=reality [--tier=10]` measures a co-op Altered Reality instead (exploring, the boss fight, a pack wipe) into `user://bench/coop_bench_reality.txt`. Both modes log snapshot delivery per client and input loss per client.

### Testing it yourself

1. **One PC:** run `./scripts/coop_local.sh`. It builds, opens a host window with the lobby on port 7777, then a second window that joins 127.0.0.1. Press Go to the hideout in the host window. `COOP_CLIENTS=3` opens four windows. The joining windows play a throwaway copy of your profile (`--profile-readonly`), so the two windows never overwrite each other's save. Both use your active character.
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

### Co-op realities and the refactor (measured 2026-10-19)

See `performance.md` → "Co-op Altered Realities". In short: the `CoopHost`/`CoopClient` split costs nothing (same bytes, same 0.3 ms step); a tier-10 reality with four players sends 16 KB/s per client while exploring, 8 KB/s in the boss fight and 4 KB/s after a 143-monster wipe, with a replication step under 0.5 ms. Snapshots arrive 98–100 % on loopback; 3–10 % of client inputs are lost to ENet's throttle, which is harmless because every input carries the full state and presses are counters.

## Known limitations

- The tutorial (The Threshold) is single-player.
- Co-op realities: a client doesn't predict its own casts (they show after the round trip plus 100 ms); its skill bar doesn't show cooldowns (casts happen on the host); Nereth's mirror reflections (cosmetic sprites) aren't shown on clients, though the attacks they telegraph are; jewels and their drop chance come from the host's Mirror for everyone (a jewel past the tree's sockets is ignored by the client); a client's `TotalKills` isn't counted for its kills in a co-op reality.
- Hideout presence carries position and three flags: puppets face where they move, not where their player aims; no skills, dodge trails or emotes are shown. The training dummies are local.
- A host playing alone while hosting gets co-op rules for a party of one in the Rift (all scaling ×1, but the pause menu doesn't pause).
- Not replicated: shadow clones and consecrated ground (the host's ascendancy effects still work there), boss cast bars (attack names and callouts are), dodge afterimages of other players, other players' aim-based facing (a puppet faces where it moves), and the sound of hits that land on other players' monsters.
- No joining a rift in progress (a newcomer waits in the hideout), and no rejoin after a drop.
- Remote characters are trusted beyond the host's sanity checks (friends only, by decision; see "Security").
- Clients predict only their own movement. Monsters and projectiles show 100 ms in the past plus latency, so a projectile can look like it misses at high ping.
- `--profile-readonly` windows on one PC share the same active character.
- Not yet measured: real internet latency and packet loss, and a host on real desktop hardware.
- Level-up rounds are only tested on loopback. Over the internet, a client sees the round a little after the host (about 100 ms plus the trip), and its countdown is set to arrive with the host's deadline.

## Next steps

- **Steam/EOS:** a `NetTransport` branch returning a Steam/EOS `MultiplayerPeer`, a lobby invite instead of an IP and code, and a relay with no port forwarding (see "Security" → "Why relays").
- Bandwidth, if needed: delta-encode monster positions against the last acked snapshot, and send far monsters at a lower rate still.
- Rejoin by name after a drop.

## Open questions (party session)

- ~~Should a co-op reality use the Rift's downed state instead of the solo rule?~~ Closed 2026-10-24: yes, every co-op mode goes down for 10 s or an ally's 5 s revive, and a reality fall still costs a portal ("Deaths and revives").

- Per-player Mirror jewels in a co-op reality (D7 uses the host's).
- Joining a rift already running (`RiftHost.MarkReady` has a partial catch-up).
- Two rifts at once (impossible while one process runs one scene).
- A guest buying decorations for the host's town.
- ENet's packet throttle: measured 2026-10-19 (`CoopBench`): snapshots 98–100 % delivered, inputs 3–10 % lost on loopback, harmless by design (full-state inputs, counters for presses). Re-measure over a real internet link before touching `ThrottleConfigure`.
- Should a host alone in the Rift play by solo rules (pausing menu) rather than co-op rules for a party of one?
