# Graph Report - arpg_game  (2026-10-30)

## Corpus Check
- 426 files · ~548,406 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 151 file(s) not represented in the graph (top: .tres 73, .tscn 60, (none) 7)

## Summary
- 7469 nodes · 21448 edges · 262 communities (213 shown, 49 thin omitted)
- Extraction: 86% EXTRACTED · 14% INFERRED · 0% AMBIGUOUS · INFERRED: 3044 edges (avg confidence: 0.87)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `777f1801`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- NetSession
- CoopHost
- CoopPanel
- town.ts
- CoopClient
- godot
- Profile
- EquipmentItem
- .Label
- Hideout
- .Join
- HideoutHud
- QuestDef
- RealityController
- StatModifier
- RiftHost
- Control
- ArpgGame.Run
- SkillUser
- RunController
- ArpgGame.UI
- Menus, hideout, profile, settings, audio
- ActorStats
- NetWriter
- Player
- Action
- PortMapper
- system_collections_generic
- Health
- Enemy
- JewelNode
- List
- SkillTags
- Color
- CastModifier
- Task
- PackedScene
- SkillInstance
- RasterLayout
- GemShopView
- IReadOnlyList
- anime.ts
- StatId
- RunDirector
- CoopTestHost
- Projectile
- VoxelStage
- RiftProgressionTest
- RunState
- QuestLogPanel
- ItemsSmokeTest
- ModalPanel
- system
- CharacterData
- HideoutData
- LevelUpScreen
- index.ts
- projectiles.ts
- endgame.ts
- Vector2I
- classes.ts
- Node
- SkillTreeNode
- CashOutPanel
- lib.ts
- InventoryUI
- ._PhysicsProcess
- .ApplyRarity
- Vector2
- CharacterBuild
- AutoplayBot
- LauncherView
- Rarity
- .Answer
- spritegen.py
- CombatQuery
- with
- LayoutDef
- InventoryGridControl
- StashPanel
- launcher.ts
- QuestToast
- Image
- ArtificerPanel
- LauncherForm
- TreeView
- MirrorPanel
- RunSmokeTest
- Dictionary
- .Scenario
- SkillTree
- BaseItemDefinition
- .Wait
- heroes.ts
- LootFeedView
- .Main
- TownMap
- RiftProposal
- RealityClient
- VoxelDebris
- DamageNumberLayer
- .TakeDamage
- Quests and the quest log
- CurrencyDrop
- Run progression (level-ups, skills, supports, passives, keystones)
- SaveCharacter
- RunHud
- main
- PerfBench
- Updater.cs
- ItemDescriptor
- HideoutEditor
- GemData
- EquipmentStripControl
- SupportDefinition
- Items, Loot & Drops (first pass)
- RunRewards
- CharacterPanels
- IRunLink
- Ailments
- BossEnemy
- RiftProposalPopup
- SkillTreePanel
- .HideoutScenario
- .Get
- HdView
- ClassDefinition
- enemies.ts
- Updater
- .Spawn
- HitProfile
- PlayerRewardSink
- TownPaths
- realms.ts
- Pickup
- HideoutParty
- DamageNumber
- MerchantPanel
- Dodge
- GameSettings
- CoopTestClient
- Equipment
- InventorySmokeTest
- PassiveTree
- Performance plan: the 2D checklist, C1–C11 (PLAN phase)
- labels.yml
- MonsterScaling
- PartyRules
- CoopTestDriver
- CaveStyle
- Sfx
- CLAUDE.md
- LineStrike
- Classes and ascendancies
- .IssueFeedbackChecks
- The Run (survivor layer)
- TargetDummy
- poses.py
- Flasks
- ItemClass
- TutorialGuide
- InstallLocation
- bug_report.yml
- MovementCapture
- aseprite_io.py
- PropDef
- ReviveStop
- Arena
- AffixSmokeTest
- RealityTiers
- feature_request.yml
- idea.yml
- PassiveTreePanel
- HitTag
- ChargerEnemy
- DropCapture
- ExplosionBench
- ._Ready
- ReviveControl
- GameCursor
- .StartFinalBoss
- batch.sh
- Performance pass: where it stopped and how to continue
- DamageType
- SkillReworkTest
- K
- TargetFrame
- ARPG Game — Project Guide
- Crafting, merchant and disenchanting
- The hideout town
- ARPG Game
- SaveItem
- AscendancyCatalog
- Nereth
- ProposalView
- SkillCapture
- ResultsScreen
- RealityTheme
- Endgame: Mirror Shards and Altered Realities
- Movement
- Cash-out offer and the infinite Rift **[IMPLEMENTED 2026-10-18]**
- ItemTooltipText
- package.json
- CraftAction
- ForgeGrade
- DamageHit
- QuestDialog
- RealityModifier
- The introduction: The Threshold **[IMPLEMENTED 2026-10-12]**
- generate
- LauncherPhase
- Distribution: releases and the launcher **[IMPLEMENTED 2026-10-12]**
- Copy
- ProjectileCapabilitiesTest
- RiftBench
- View
- Dodge
- Enemies
- Inventory interaction **[DECIDED]**
- SkillTreeModifier
- DamageNumberMode
- UpgradeKind
- multiplayer-expert.md
- security-expert.md
- ui-ux-expert.md
- .Draw
- PassiveKind
- ._Ready
- WindowModeKind
- Mode
- Affix tiers and item level **[DECIDED]**
- Drop tables (cascade) **[IMPLEMENTED 2026-10-14]**
- hb.sh
- ArpgGameLauncher.csproj
- skill-trees_e9deb1b1.md
- coop_local.sh
- ReviveOutcome
- run_editor.sh
- run_headless.sh
- CastFailureKind
- CostMode
- HudWidgets.cs
- ArpgGame
- RiftKind
- perf_bench.sh
- SkillTreeNodeKind
- .Main
- cmp.sh
- autoplay.sh
- capture_devlog_screenshot.sh
- capture_drop_screenshots.sh
- capture_inventory_screenshot.sh
- capture_skill_screenshots.sh
- run_all_tests.sh
- fetch_easypixel.sh

## God Nodes (most connected - your core abstractions)
1. `NetSession` - 259 edges
2. `Player` - 229 edges
3. `RealityController` - 156 edges
4. `Profile` - 142 edges
5. `Enemy` - 138 edges
6. `EquipmentItem` - 130 edges
7. `CharacterData` - 128 edges
8. `Hideout` - 127 edges
9. `CoopHost` - 121 edges
10. `RunState` - 102 edges

## Surprising Connections (you probably didn't know these)
- `Shared behaviour — `Enemies/Enemy.cs`` --references--> `CombatQuery`  [INFERRED]
  docs/design/enemies.md → scripts/Combat/CombatQuery.cs
- `Ailments` --references--> `DamagePayload`  [INFERRED]
  docs/design/progression.md → scripts/Combat/DamagePayload.cs
- `Thorns needs a recursion guard **[DECIDED]**` --references--> `Health`  [INFERRED]
  docs/design/affixes.md → scripts/Combat/Health.cs
- `Open questions added by the third pass` --references--> `Health`  [INFERRED]
  docs/design/stats.md → scripts/Combat/Health.cs
- `First-pass effect pool **[PROPOSED]**` --references--> `Projectile`  [INFERRED]
  docs/design/items.md → scripts/Combat/Projectile.cs

## Import Cycles
- None detected.

## Communities (262 total, 49 thin omitted)

### Community 0 - "NetSession"
Cohesion: 0.02
Nodes (93): Decisions 2026-10-18, Edit permission (D10), Invite friends (hideout pause menu), Party session (2026-10-18, phase A), Proposals, Protocol 5, Shared hideout, State (+85 more)

### Community 1 - "CoopHost"
Cohesion: 0.03
Nodes (52): SlashEffect, ArcDegrees, Range, Tint, HoldInputs, HostTime, LastStepMicros, Peers (+44 more)

### Community 2 - "CoopPanel"
Cohesion: 0.03
Nodes (42): Look, CoopUiTestDriver, LogPath, Tag, CharacterSelect, Profile, Purpose, StartInCreate (+34 more)

### Community 3 - "town.ts"
Cohesion: 0.05
Nodes (87): mulberry32(), Pix, ascendant(), ash(), boneDagger(), boot(), copperAmulet(), copperRing() (+79 more)

### Community 4 - "CoopClient"
Cohesion: 0.05
Nodes (30): CoopClient, Choosing, Corrections, Enemies, EnemyCount, HaveOffset, HeardFromHost, HostNow (+22 more)

### Community 5 - "godot"
Cohesion: 0.05
Nodes (8): ArpgGame.Stats, ArpgGame.Skills, ArpgGame.Combat, ArpgGame, ArpgGame.View3D, ArpgGame.Progression, CollisionLayers, Main

### Community 6 - "Profile"
Cohesion: 0.04
Nodes (47): A hostile host (what a client accepts), Persistence and migration, Test plan (headless), Active, ActiveIndex, BestLevel, BestTime, Characters (+39 more)

### Community 7 - "EquipmentItem"
Cohesion: 0.05
Nodes (23): Pending items (reward overflow) **[IMPLEMENTED 2026-10-17]**, Where the events come from, CurrencyId, Value, EquipmentItem, Base, Descriptor, HasGroundLabel (+15 more)

### Community 8 - ".Label"
Cohesion: 0.08
Nodes (4): UiTheme, BigNumberFont, NumberFont, Theme

### Community 9 - "Hideout"
Cohesion: 0.04
Nodes (33): Co-op: whose town **[IMPLEMENTED 2026-10-18]**, Edit permission and the lock, BossBullet, Frames, Altar, Hideout, Data, EditBlockedReason (+25 more)

### Community 10 - ".Join"
Cohesion: 0.04
Nodes (35): Co-op **[REDESIGNED 2026-10-16]**, Co-op realities and the refactor (measured 2026-10-19), Join code (`scripts/Net/JoinCode.cs`), Known limitations, Multiplayer (co-op), Next steps, Performance (measured 2026-10-15), Security (direct IP without a VPN, 2026-10-16) (+27 more)

### Community 11 - "HideoutHud"
Cohesion: 0.04
Nodes (37): HideoutHud, BannerSub, BigMap, BigMapOpen, Corner, DirectoryEntries, DirectoryMarkers, DirectoryShowing (+29 more)

### Community 12 - "QuestDef"
Cohesion: 0.05
Nodes (52): Game structure (PoE-style endgame + persistent characters), QuestBook, AttentionCount, Clock, QuestCatalog, QuestContext, Character, Jewels (+44 more)

### Community 13 - "RealityController"
Cohesion: 0.04
Nodes (42): FpsOverlay, AnnouncedEventHandler, BossStartedEventHandler, RealityController, Boss, BossFightActive, Collapsed, Completed (+34 more)

### Community 14 - "StatModifier"
Cohesion: 0.04
Nodes (64): Affixes, Attributes **[DECIDED]**, Authoring shape **[DECIDED]**, Condition semantics **[DECIDED]**, Derivations need a recompute hook, Dexterity is deliberately temporary **[DECIDED]**, Name generation **[DECIDED]**, Open questions (+56 more)

### Community 15 - "RiftHost"
Cohesion: 0.05
Nodes (39): Allies on the maps (2026-10-23), Architecture, Deaths and revives (2026-10-24), Engine notes (Godot 4.1.1), Host limits (`scripts/Net/NetGuard.cs` → `NetLimits`, one table), Level-ups (2026-10-17), Party rules (`scripts/Net/PartyRules.cs`, one table, proposals), Players (+31 more)

### Community 16 - "Control"
Cohesion: 0.04
Nodes (27): Orb tooltips **[IMPLEMENTED 2026-10-18]**, DirectoryRow, Quest, KeyChip, HoverTooltip, AllText, HasContent, Source (+19 more)

### Community 17 - "ArpgGame.Run"
Cohesion: 0.06
Nodes (6): ArpgGame.Run, ArpgGame.Tools, ArpgGame.Tests, ArpgGame.Enemies, CoopTestDriverArgs, Role

### Community 18 - "SkillUser"
Cohesion: 0.06
Nodes (33): The base classes, The skills, Fourth pass: seam 6 goes live **[DECIDED — implemented]**, RangedEnemy, GlobalCastModifier, Priority, AreaSkillDefinition, CastContext (+25 more)

### Community 19 - "RunController"
Cohesion: 0.04
Nodes (34): Loading screen (2026-10-14), I4. C8: `GC.Collect()` behind the loading screen, Scenes, MouseActionGate, CastMode, Bound, Hold, Survival (+26 more)

### Community 20 - "ArpgGame.UI"
Cohesion: 0.05
Nodes (8): ArpgGame.Quests, ArpgGame.Characters, ArpgGame.UI, ArpgGame.World, ArpgGame.Core, LoadoutSlot, SkillUid, SupportUids

### Community 21 - "Menus, hideout, profile, settings, audio"
Cohesion: 0.05
Nodes (32): Audio, Character panels (K / P), Characters, Controls (2026-10-12, four skill keys since 2026-10-29), Cursor **[IMPLEMENTED 2026-10-16]**, Flow, Hideout, Hideout HUD **[IMPLEMENTED 2026-10-13]** (+24 more)

### Community 22 - "ActorStats"
Cohesion: 0.04
Nodes (45): The six evolutions (2026-10-30) **[IMPLEMENTED]**, The Witch's curses, Base values move to a `StatBlock` resource, The registry **[DECIDED]**, CurseEffect, Curses, Active, Any (+37 more)

### Community 24 - "Player"
Cohesion: 0.04
Nodes (33): Four directions **[IMPLEMENTED 2026-10-12]**, F picks up **[IMPLEMENTED 2026-10-16]**, How this interacts with filters, Pickup has a reach **[DECIDED]**, Pickup is manual for items, automatic for currency **[DECIDED]**, Player, AimPoint, AutoWalk (+25 more)

### Community 25 - "Action"
Cohesion: 0.05
Nodes (25): BrushMenuOption, Selected, FeedbackPanel, Extra, Info, Kind, LastUrl, OpenUrl (+17 more)

### Community 26 - "PortMapper"
Cohesion: 0.05
Nodes (25): UPnP: opening the port automatically (`scripts/Net/PortMapper.cs`), IPortGateway, MapResult, Failed, Ok, OnlyPermanent, MapState, Failed (+17 more)

### Community 28 - "Health"
Cohesion: 0.05
Nodes (29): Ascendancies, I5: a monster ticks its own Health, Ailments and ActorStats (C6), AscendancyEffects, Now, Player, ConsecratedGround, DamageHooks, IDamageHook (+21 more)

### Community 29 - "Enemy"
Cohesion: 0.04
Nodes (30): Faction / friendly fire **[DECIDED]**, The three layers **[DECIDED]**, What it is, I2. C1a: skip `MoveAndSlide` at zero velocity, ChaserEnemy, Enemy, Ailments, DamageScalingExponent (+22 more)

### Community 30 - "JewelNode"
Cohesion: 0.06
Nodes (36): Mirror jewels (2026-10-14), JewelTreeView, BranchSpec, JewelBonuses, JewelData, Found, Free, Socketed (+28 more)

### Community 31 - "List"
Cohesion: 0.07
Nodes (36): The character in the Rift (2026-10-17), The start pick (2026-10-17), Candidate, RunBuild, ChainReaction, Character, ClassSkills, ForcedElement (+28 more)

### Community 32 - "SkillTags"
Cohesion: 0.04
Nodes (47): SkillTags, Area, Attack, Channelled, Cold, Curse, Duration, Fire (+39 more)

### Community 33 - "Color"
Cohesion: 0.07
Nodes (12): QuestMarker, Active, New, None, Ready, Stroke, QuestBadge, Marker (+4 more)

### Community 34 - "CastModifier"
Cohesion: 0.05
Nodes (54): Aiming **[DECIDED]**, Charges — out of scope, but the state shape is decided **[DECIDED]**, Collision and hit resolution **[DECIDED]**, Consequences for the stat system, Cooldown and use time are independent levers, Cooldown starts on **cast start** **[DECIDED]**, `CooldownRecovery` stat **[DECIDED]**, Cooldowns **[DECIDED]** (+46 more)

### Community 37 - "SkillInstance"
Cohesion: 0.06
Nodes (29): Alternatives considered and rejected, BuiltInAction, Move, None, LoadoutSlot, BuiltIn, IsEmpty, Skill (+21 more)

### Community 38 - "RasterLayout"
Cohesion: 0.08
Nodes (19): Altered Realities (the dungeons), Caverns: cellular-automata maps (2026-10-14, experiment), Drops and pickups, Monster rarity, Navigation: one flow field, not the NavigationServer, Reality HUD, Spawning (2026-10-14), The boss (+11 more)

### Community 39 - "GemShopView"
Cohesion: 0.09
Nodes (18): As built (deviations and additions), GemOffer, Price, GemShop, GemShopView, BuyButton, CharacterSource, DetailTitle (+10 more)

### Community 40 - "IReadOnlyList"
Cohesion: 0.12
Nodes (15): AffixDefinition, AffixPoolKind, Sigil, Temper, AffixRoller, RolledAffix, Crafted, Definition (+7 more)

### Community 41 - "anime.ts"
Cohesion: 0.15
Nodes (34): arcanistCast(), bruteRear(), finish(), rogueLeap(), sageRead(), shield(), style(), T() (+26 more)

### Community 42 - "StatId"
Cohesion: 0.04
Nodes (47): StatId, AreaOfEffect, Armour, AttackSpeed, AutoCastSpeed, CastSpeed, ColdPenetration, ColdResistance (+39 more)

### Community 43 - "RunDirector"
Cohesion: 0.08
Nodes (16): AnnouncedEventHandler, BossSpawnedEventHandler, RunDirector, ActiveBoss, Arena, AutoCashOut, AwaitingCashOut, ColossusDefeated (+8 more)

### Community 44 - "CoopTestHost"
Cohesion: 0.14
Nodes (12): LobbyMember, Character, ClassName, Level, Name, PeerId, ViewHeight, ViewSize (+4 more)

### Community 45 - "Projectile"
Cohesion: 0.07
Nodes (23): Fireball and Spark, Initial tag set **[DECIDED — as a starting point, not a commitment]**, Open questions, Skill identity: archetype vs tags **[DECIDED]**, Worked example, and two sharpenings **[DECIDED]**, OnHitOutcome, ContinueOutcome, DespawnOutcome (+15 more)

### Community 46 - "VoxelStage"
Cohesion: 0.08
Nodes (16): Cave walls **[IMPLEMENTED 2026-10-15]**, Liquids **[IMPLEMENTED 2026-10-14]**, Voxels — the HD-2D view **[IMPLEMENTED 2026-10-12]**, I11. C4: one surface and one material per voxel mesh (art pipeline + import), I6. C11: `player_pos` as a global shader uniform (before C3), I7. C3a: chunk the floor, liquid-bed, wall and liquid-surface MultiMeshes, I8. C3b: chunk the cave rock mesh, Terrain chunking (agent 1) (+8 more)

### Community 48 - "RunState"
Cohesion: 0.05
Nodes (34): LevelUpReadyEventHandler, RunEndedEventHandler, RunState, AllDown, BossesKilled, Build, ClockRunning, Coop (+26 more)

### Community 49 - "QuestLogPanel"
Cohesion: 0.06
Nodes (14): CheckBox2, ListRow, QuestLogPanel, Blocked, CanWalk, CompletedOpen, FrameSize, Hideout (+6 more)

### Community 50 - "ItemsSmokeTest"
Cohesion: 0.09
Nodes (14): BuiltInDefaultRules, DisplayStyle, BackgroundColor, Beam, BorderColor, FontSize, Hidden, Sound (+6 more)

### Community 51 - "ModalPanel"
Cohesion: 0.06
Nodes (24): AscendancyPanel, Character, PendingChoice, Player, StationUid, Title, DecorShopPanel, Data (+16 more)

### Community 52 - "system"
Cohesion: 0.07
Nodes (4): ArpgGame.Net, NetMode, ClientSide, RiftProtocol

### Community 53 - "CharacterData"
Cohesion: 0.05
Nodes (35): CharacterData, Ascendancy, AscendancyDef, AscendancyNodes, AscendancyPoints, AscendancyPointsFree, BestSurvivalTime, Class (+27 more)

### Community 54 - "HideoutData"
Cohesion: 0.10
Nodes (17): HideoutData, Floors, GuideSeen, Initialized, Props, Stock, PlacedProp, Kind (+9 more)

### Community 55 - "LevelUpScreen"
Cohesion: 0.09
Nodes (17): LevelUpScreen, Armed, BlocksInput, Build, Coop, FirstOffer, IsOpen, Offers (+9 more)

### Community 56 - "index.ts"
Cohesion: 0.05
Nodes (36): animeCharacters, arrowLegend, arrowRows, cursorArrow, cursorAttack, cursorGrab, cursorHover, cursors (+28 more)

### Community 57 - "projectiles.ts"
Cohesion: 0.05
Nodes (37): Alternatives considered, Art pipeline (EasyPixel), Contents, Editing art by hand, HD-2D with flat figures — sprites in the voxel world **[DECIDED 2026-10-29]**, High-definition anime characters **[IMPLEMENTED 2026-10-12]**, decodePng(), pngGrid() (+29 more)

### Community 58 - "endgame.ts"
Cohesion: 0.05
Nodes (37): What it is, recolor(), sym(), frame, iconCleave, iconDoubleStrike, iconFrostShards, iconGlacialHammer (+29 more)

### Community 59 - "Vector2I"
Cohesion: 0.10
Nodes (18): Code, FloorStyle, Atlas, Id, Name, PackPrice, Swatch, Tileset (+10 more)

### Community 60 - "classes.ts"
Cohesion: 0.05
Nodes (36): Art (EasyPixel), patch(), shift(), bosses, colossus, colossusA, colossusB, colossusLegend (+28 more)

### Community 61 - "Node"
Cohesion: 0.12
Nodes (5): Deviations from the plan, Implementation **[2026-10-18]**, CameraShake, QuestService, LastClaim

### Community 62 - "SkillTreeNode"
Cohesion: 0.12
Nodes (14): SkillTreeNode, Apply, Branch, Depth, Describe, Exclusive, Id, Kind (+6 more)

### Community 63 - "CashOutPanel"
Cohesion: 0.07
Nodes (22): CashOutPanel, Armed, Blocks, IntroText, IsOpen, Mode, PartyChosenThisRound, PartyClockUrgent (+14 more)

### Community 64 - "lib.ts"
Cohesion: 0.09
Nodes (31): args, contactSheet(), only, previewAt, spec(), AnimSpec, buildDocument(), chunk() (+23 more)

### Community 65 - "InventoryUI"
Cohesion: 0.08
Nodes (20): Party overlay (hideout) **[IMPLEMENTED 2026-10-18]**, Pause (Escape), Rift proposal popup **[IMPLEMENTED 2026-10-18]**, InventoryUI, _blockRect, Board, CraftingShown, IsOpen (+12 more)

### Community 66 - "._PhysicsProcess"
Cohesion: 0.08
Nodes (23): Baseline (I0, 2026-10-24, this container), Frame budget pass (2026-10-24): the 2D checklist, I0: probes and the bench (no gameplay change), I1: allocation-free `Stat.ComputeFormula` (C5), plus the `IsInGroup` string (C13a), I2: idle monsters skip `MoveAndSlide` (C1a), I6: `player_pos` as a global shader uniform (C11), Method, I0. Tooling: Perf probes, PerfBench, perf_bench.sh, baseline (no gameplay change) (+15 more)

### Community 67 - ".ApplyRarity"
Cohesion: 0.10
Nodes (13): I12. C9a + C1b: monster registry and grid, soft crowd separation (owner decision; changes the feel), MonsterMod, Id, Name, RangedOnly, MonsterRarities, MonsterRarity, Magic (+5 more)

### Community 68 - "Vector2"
Cohesion: 0.11
Nodes (8): I9. C10: the Cataclysm as a ripple over 8 physics frames (owner decision: kills ≤ ~0.15 s later), MapOverlay, FlowField, CellSize, Current, Height, Origin, Width

### Community 69 - "CharacterBuild"
Cohesion: 0.07
Nodes (23): CharacterBuild, Ascendancy, ChainReaction, Data, ExperienceFraction, ForcedElement, PhaseDancer, Player (+15 more)

### Community 70 - "AutoplayBot"
Cohesion: 0.11
Nodes (5): AreaBurst, HasLiveLeader, IsHostileTelegraph, TimeToImpact, AutoplayBot

### Community 71 - "LauncherView"
Cohesion: 0.09
Nodes (17): LauncherView, CanPlay, ChangeVisible, Detail, HoverChange, HoverClose, HoverFolder, HoverMinimize (+9 more)

### Community 72 - "Rarity"
Cohesion: 0.07
Nodes (26): CraftingCurrency, Action, BuyPrice, Color, DustOf, Effect, Id, IsDust (+18 more)

### Community 73 - ".Answer"
Cohesion: 0.12
Nodes (11): PlayerCommand, HideoutCoopGuest, LogPath, PlayerName, Tag, Town, HideoutCoopHost, LogPath (+3 more)

### Community 74 - "spritegen.py"
Cohesion: 0.09
Nodes (18): anim_prompt(), api_to_ui(), d(), build_graph(), build_qwen_sheet_graph(), cmd_approve_env(), cmd_concept(), cmd_export_workflows() (+10 more)

### Community 75 - "CombatQuery"
Cohesion: 0.10
Nodes (17): Budgets, Cause, Co-op (Survival Rift, 4 players) — measured 2026-10-16, Fixes (no gameplay change; the results are the same), I4: a full GC behind the loading screen (C8) — **rejected**, tools kept, Method, Proposals (not done), Results (+9 more)

### Community 76 - "with"
Cohesion: 0.06
Nodes (35): concurrency, env, GODOT_VERSION, version, jobs, windows, name, on (+27 more)

### Community 77 - "LayoutDef"
Cohesion: 0.06
Nodes (33): Layouts: room graphs, rasterised (kept for reuse; the tutorial), CorridorDef, From, Group, Packs, To, VerticalFirst, Width (+25 more)

### Community 78 - "InventoryGridControl"
Cohesion: 0.09
Nodes (10): InventoryGridControl, EquipmentNode, Inventory, InventoryNode, LeftClickOverride, RightClickEmpty, RightClickOverride, Tint (+2 more)

### Community 79 - "StashPanel"
Cohesion: 0.11
Nodes (11): Inventory, Placements, InventorySorter, ItemShape, StashPanel, FrameSize, PendingGrid, Player (+3 more)

### Community 80 - "launcher.ts"
Cohesion: 0.08
Nodes (31): add(), ambientLeft, ambientRight, arc(), arcane, blit(), blitPainted(), ember (+23 more)

### Community 81 - "QuestToast"
Cohesion: 0.08
Nodes (20): Hideout HUD after the owner's second pass (2026-10-29), HUD: the mirror, HUD: top-right corner, Open questions, Sizes after the owner's third pass (2026-10-30), Title screen, UI from the owner's sketch **[IMPLEMENTED 2026-10-29]**, Known limits (+12 more)

### Community 82 - "Image"
Cohesion: 0.10
Nodes (18): contact_sheet(), despeckle(), dominant_downscale(), env_palette(), fit_frames(), load_config(), palette_from(), projectile_outputs() (+10 more)

### Community 83 - "ArtificerPanel"
Cohesion: 0.11
Nodes (12): ItemArt, ArtificerPanel, Current, ForgeItemLevel, FrameSize, LastMessage, Player, StationUid (+4 more)

### Community 85 - "TreeView"
Cohesion: 0.11
Nodes (13): Passive tree (P), Redesign (2026-10-15), PassiveNode, ClassId, Id, Keystone, Kind, Links (+5 more)

### Community 86 - "MirrorPanel"
Cohesion: 0.07
Nodes (20): MirrorPanel, Character, FrameSize, Gold, InParty, Jewels, JewelView, Party (+12 more)

### Community 87 - "RunSmokeTest"
Cohesion: 0.20
Nodes (3): ProjectileSkillDefinition, RunSmokeTest, SeedFireball

### Community 88 - "Dictionary"
Cohesion: 0.15
Nodes (6): I3: the voxel actor's mesh from a frame table, resolved on the sprite's signals (C2), OrbitBlades, VoxelActor, CurrentFrame, CurrentMesh, VoxelLibrary

### Community 89 - ".Scenario"
Cohesion: 0.10
Nodes (11): Damage numbers **[IMPLEMENTED 2026-10-24]**, CoopRealityGuest, LogPath, PlayerName, Reality, Tag, CoopRealityHost, LogPath (+3 more)

### Community 90 - "SkillTree"
Cohesion: 0.16
Nodes (8): SkillTree, Branches, Nodes, SkillId, TotalRanks, SkillTreeRules, DumpSkillTrees, TreeCanvas

### Community 91 - "BaseItemDefinition"
Cohesion: 0.11
Nodes (10): AffixLibrary, AffixTierOverride, BaseItemDefinition, BaseItemStatEntry, EquipmentDropSpawner, ItemFactory, Affixes, Bases (+2 more)

### Community 92 - ".Wait"
Cohesion: 0.16
Nodes (6): BoneHerald, Element, PhaseThresholds, EmberColossus, Cadence, PhaseThresholds

### Community 93 - "heroes.ts"
Cohesion: 0.14
Nodes (25): BOOTS, CAPE, common, dagger(), HAIR, HAT, LEGS, Painted (+17 more)

### Community 94 - "LootFeedView"
Cohesion: 0.11
Nodes (12): Loot feed, LootEntry, LootFeed, LootFeedView, Lines, Row, CountText, Entry (+4 more)

### Community 95 - ".Main"
Cohesion: 0.10
Nodes (17): Open questions (party session), CoopBench, CoopBenchClient, LogPath, Measure, PlayerName, Port, Tag (+9 more)

### Community 96 - "TownMap"
Cohesion: 0.11
Nodes (9): The quest log (J), AllyMarker, PartyMarkers, Minimap, RiftMinimap, HudEditing, Party, TownMap (+1 more)

### Community 97 - "RiftProposal"
Cohesion: 0.08
Nodes (23): Annotated tree, Conventions worth knowing, How the code layers depend on each other, How the scenes connect, Mind map, Project map, Where to look for…, Result (+15 more)

### Community 98 - "RealityClient"
Cohesion: 0.07
Nodes (18): RealityClient, Local, Reality, SendsHeldSlots, Teleports, TestHeld, World, PickupKind (+10 more)

### Community 99 - "VoxelDebris"
Cohesion: 0.17
Nodes (8): Fixes, first pass (none change gameplay), DeathBurst, DeathBurstPreset, DeathBurstStyle, None, Pixels, Cube, VoxelDebris

### Community 100 - "DamageNumberLayer"
Cohesion: 0.12
Nodes (9): Column, DamageNumberLayer, ColumnCount, Created, IdleCount, LiveCount, MergedHits, Reused (+1 more)

### Community 101 - ".TakeDamage"
Cohesion: 0.18
Nodes (3): Adding a damage type **[DECIDED — third pass]**, DamageMitigation, StatsSmokeTest

### Community 102 - "Quests and the quest log"
Cohesion: 0.08
Nodes (19): UX review 2026-10-18: plan **[IMPLEMENTED 2026-10-18]**, Acceptance criteria, Alternatives considered, Concepts, Data model and code, E prompt and station panels, Markers, Open questions (owner) (+11 more)

### Community 103 - "CurrencyDrop"
Cohesion: 0.12
Nodes (7): CurrencyDefinition, CurrencyDrop, Descriptor, DropSpawner, GroundLabel, GroundLabelPool, Slot

### Community 104 - "Run progression (level-ups, skills, supports, passives, keystones)"
Cohesion: 0.08
Nodes (24): Ailments, Alternatives considered, Energy Shield (2026-10-17), Implementation notes, Keystone cadence: lesser and great (2026-10-17), Keystones (great and lesser — see "Keystone cadence"), Offer composition (2026-10-17), Open questions (+16 more)

### Community 105 - "SaveCharacter"
Cohesion: 0.07
Nodes (27): SaveCharacter, Ascendancy, AscendancyNodes, AscendancyPoints, BestSurvivalTime, ClassId, Currency, Deaths (+19 more)

### Community 106 - "RunHud"
Cohesion: 0.10
Nodes (13): SideGauge, RunHud, Build, Director, PartyMap, Player, RewardLine1, RewardLine2 (+5 more)

### Community 107 - "main"
Cohesion: 0.14
Nodes (17): anim_keys(), approved(), approved_dir(), cmd_anims(), cmd_approve(), cmd_guides(), cmd_pack(), cmd_pack_sheet() (+9 more)

### Community 109 - "Updater.cs"
Cohesion: 0.10
Nodes (9): ArpgGameLauncher, AssetDto, Name, Url, ReleaseDto, Assets, Draft, Prerelease (+1 more)

### Community 110 - "ItemDescriptor"
Cohesion: 0.08
Nodes (25): Build order **[PROPOSED]**, Drop pipeline **[DECIDED]**, IDroppable, Descriptor, ItemDescriptor, AffixCount, AffixTiers, Amount (+17 more)

### Community 111 - "HideoutEditor"
Cohesion: 0.11
Nodes (11): Gizmo, HideoutEditor, Current, Hideout, Mouse, SelectedDecor, SelectedFloor, Tool (+3 more)

### Community 112 - "GemData"
Cohesion: 0.15
Nodes (13): GemData, Experience, Id, IsSupport, Level, Tree, TreePointsFree, TreePointsSpent (+5 more)

### Community 113 - "EquipmentStripControl"
Cohesion: 0.13
Nodes (9): EquipmentStripControl, DragSourceKind, EquipmentSlot, Grid, InventoryDragPayload, GridOrigin, Item, Kind (+1 more)

### Community 114 - "SupportDefinition"
Cohesion: 0.08
Nodes (21): Acceptance and tests (EndgameSmokeTest `GEMSHOP-*`), Findings that shape it, Layout (1280×720), Odran's Skill gems tab **[IMPLEMENTED 2026-10-18]**, Open questions, Small fixes in the same pass, Three new skills, Unlock levels and prices (+13 more)

### Community 115 - "Items, Loot & Drops (first pass)"
Cohesion: 0.08
Nodes (24): Affixes: Temper and Sigil **[DECIDED]**, Base items **[DECIDED]**, Currency **[DECIDED]**, Equipment slots **[DECIDED]**, Filters control display, never behaviour **[DECIDED]**, First-pass effect pool **[PROPOSED]**, Flasks and refill orbs **[IMPLEMENTED 2026-10-12]**, Grid inventory **[DECIDED]** (+16 more)

### Community 116 - "RunRewards"
Cohesion: 0.10
Nodes (16): Reward tiers, RunRewards, BonusAscendant, Choices, Gold, GuaranteedRare, ItemLevel, ItemsEarned (+8 more)

### Community 117 - "CharacterPanels"
Cohesion: 0.11
Nodes (11): CharacterPanel, FrameSize, Player, S, Title, CharacterPanels, Player, Quests (+3 more)

### Community 118 - "IRunLink"
Cohesion: 0.10
Nodes (3): IRunLink, ReadyWatcher, Ready

### Community 119 - "Ailments"
Cohesion: 0.11
Nodes (15): Ailments, ChillRemaining, CurseRemaining, FreezeRemaining, IgniteRemaining, IsChilled, IsCursed, IsFrozen (+7 more)

### Community 120 - "BossEnemy"
Cohesion: 0.11
Nodes (13): BossEnemy, Alive, Arena, Busy, CastProgress, CurrentAttack, DamageScalingExponent, Element (+5 more)

### Community 121 - "RiftProposalPopup"
Cohesion: 0.12
Nodes (12): RiftProposalPopup, Current, InfoText, NoText, SecondsShown, Session, Showing, StatusText (+4 more)

### Community 122 - "SkillTreePanel"
Cohesion: 0.14
Nodes (13): SkillEntry, Definition, Id, ResourcePath, SkillTreePanel, Character, FrameSize, Message (+5 more)

### Community 125 - "HdView"
Cohesion: 0.11
Nodes (12): Benchmark, Budgets, Causes (measured with temporary Stopwatch probes, 60 deaths), Co-op Altered Realities and the replication refactor — measured 2026-10-19, Damage numbers as columns (2026-10-24), Mass deaths ("matar muchos enemigos que explotan"), Performance, Rejected (+4 more)

### Community 126 - "ClassDefinition"
Cohesion: 0.09
Nodes (22): ClassDefinition, Ascendancies, BonusDodgeCharges, BonusLife, BonusMana, Description, Dexterity, Id (+14 more)

### Community 127 - "enemies.ts"
Cohesion: 0.09
Nodes (21): bat, batDown, batLegend, batUp, brute, bruteA, bruteB, bruteLegend (+13 more)

### Community 128 - "Updater"
Cohesion: 0.15
Nodes (11): Build, Version, ZipUrl, Updater, ApiUrl, BaseUrl, GameDir, GameExePath (+3 more)

### Community 130 - "HitProfile"
Cohesion: 0.18
Nodes (5): Results, second pass (60 Slimes, headless, median of 3 runs, ms), Second pass: the four proposals (approved by the owner 2026-10-15), HitProfile, ExploderEnemy, BuildQuery

### Community 131 - "PlayerRewardSink"
Cohesion: 0.15
Nodes (4): GemLoot, IRewardSink, PlayerRewardSink, TestSink

### Community 132 - "TownPaths"
Cohesion: 0.23
Nodes (3): TownPaths, Height, Width

### Community 133 - "realms.ts"
Cohesion: 0.10
Nodes (20): nereth, nerethA, nerethB, nerethCast, nerethLegend, siren, sirenA, sirenB (+12 more)

### Community 135 - "HideoutParty"
Cohesion: 0.17
Nodes (5): HideoutParty, Hideout, LastError, Popup, Puppets

### Community 136 - "DamageNumber"
Cohesion: 0.15
Nodes (11): DamageNumber, Amount, Big, Icon, Kind, Medium, Merged, Tags (+3 more)

### Community 137 - "MerchantPanel"
Cohesion: 0.13
Nodes (14): MerchantPanel, Buyback, Character, Customer, FrameSize, Gems, GemsTabDot, Player (+6 more)

### Community 138 - "Dodge"
Cohesion: 0.13
Nodes (10): Fourth pass: run stats **[DECIDED — implemented]**, Dodge, Charges, DashVelocity, InputEnabled, IsDashing, LastDodgeTime, MaxCharges (+2 more)

### Community 139 - "GameSettings"
Cohesion: 0.11
Nodes (18): ChangedEventHandler, GameSettings, AdminMode, AutoAim, AutoCast, DamageNumberMode, DamageNumbers, FillWideScreens (+10 more)

### Community 140 - "CoopTestClient"
Cohesion: 0.12
Nodes (7): LootQueue, Count, LogPath, PlayerName, Port, Tag, CoopTestClient

### Community 141 - "Equipment"
Cohesion: 0.16
Nodes (13): Equipment, EquipmentSlot, Amulet, Belt, BodyArmour, Boots, Gloves, Helmet (+5 more)

### Community 143 - "PassiveTree"
Cohesion: 0.19
Nodes (5): Cluster, PassiveMod, PassiveText, PassiveTree, Nodes

### Community 144 - "Performance plan: the 2D checklist, C1–C11 (PLAN phase)"
Cohesion: 0.11
Nodes (18): 0. Ground rules for both agents, 3. Visual comparison protocol (C3, C4, C7, C11, crowd), 4. Expected total (baseline → target at the end of I13), 5. Docs to update, 6. Risks at a glance, Agent 2, I10. C7: the sun shadow in `Orthogonal` mode (visual change: before/after captures required), I13. C9b: registry users (TargetFrame, RunDirector, RiftMinimap) (+10 more)

### Community 145 - "labels.yml"
Cohesion: 0.11
Nodes (17): GH_REPO, GH_TOKEN, jobs, labels, runs-on, steps, name, on (+9 more)

### Community 148 - "CoopTestDriver"
Cohesion: 0.14
Nodes (8): CoopTestDriver, LogPath, Run, Tag, SecurityTestClient, LogPath, Port, Tag

### Community 149 - "CaveStyle"
Cohesion: 0.12
Nodes (17): CaveLiquid, Lagoons, Pools, Rivers, CaveStyle, ArenaRadius, Bends, Branches (+9 more)

### Community 151 - "CLAUDE.md"
Cohesion: 0.12
Nodes (11): Boundaries, How you work, Boundaries, How you work, Memory model (per root `CLAUDE.md`), Boundaries, C#/.NET specifics, How you work (+3 more)

### Community 152 - "LineStrike"
Cohesion: 0.15
Nodes (12): Bosses, Bosses inside realities, Implementation, Nereth, the Drowned Reflection — boss of the Drowned Mirror, Open questions, The Bone Herald — mid boss (3:00), The Ember Colossus — final boss (7:00), The rules every boss follows (+4 more)

### Community 153 - "Classes and ascendancies"
Cohesion: 0.14
Nodes (13): Classes and ascendancies, Decisions, Naming the base classes, Open questions, Persistent character, Structure, AscendancyDefinition, ClassId (+5 more)

### Community 154 - ".IssueFeedbackChecks"
Cohesion: 0.23
Nodes (6): Feedback **[IMPLEMENTED 2026-10-13]**, Feedback, FeedbackKind, Bug, FeatureRequest, Idea

### Community 155 - "The Run (survivor layer)"
Cohesion: 0.12
Nodes (16): Alternatives considered, Controls, Controls in the endgame era (2026-10-12), Experience and pickups, Minimum for an item (2026-10-17), Open questions, Rewards, Run-only bonuses (+8 more)

### Community 156 - "TargetDummy"
Cohesion: 0.19
Nodes (5): Damage types **[DECIDED]**, `DamagePayload` carries a breakdown, not a float, TargetDummy, NumberOffset, DropSpawnerSmokeTest

### Community 157 - "poses.py"
Cohesion: 0.23
Nodes (9): _draw_front(), _draw_side(), arm(), leg(), guide_strip(), _limb(), _seg(), _side() (+1 more)

### Community 158 - "Flasks"
Cohesion: 0.17
Nodes (9): DrankEventHandler, Flask, Active, Full, Partial, Usable, Flasks, AllFull (+1 more)

### Community 159 - "ItemClass"
Cohesion: 0.12
Nodes (15): ItemClass, Amulet, Axe, Belt, BodyArmour, Boots, Currency, Dagger (+7 more)

### Community 160 - "TutorialGuide"
Cohesion: 0.19
Nodes (6): Step, TutorialGuide, Player, StepCount, StepIndex, StepTitle

### Community 161 - "InstallLocation"
Cohesion: 0.23
Nodes (4): InstallLocation, DefaultRoot, SettingsDir, SettingsFile

### Community 162 - "bug_report.yml"
Cohesion: 0.13
Nodes (14): description, label, placeholder, render, body, attributes, id, type (+6 more)

### Community 164 - "aseprite_io.py"
Cohesion: 0.31
Nodes (7): _cel(), _chunk(), _layer(), _palette(), _string(), _tags(), write()

### Community 165 - "PropDef"
Cohesion: 0.15
Nodes (12): PropDef, Accent, Blurb, Id, Name, PickRadius, Price, Radius (+4 more)

### Community 166 - "ReviveStop"
Cohesion: 0.13
Nodes (15): ReviveStop, Cast, Dodged, Left, Moved, None, RateLimited, Released (+7 more)

### Community 167 - "Arena"
Cohesion: 0.19
Nodes (5): Arena, Bounds, Center, TileWorld, LayoutBuilder

### Community 170 - "feature_request.yml"
Cohesion: 0.14
Nodes (13): description, label, render, body, attributes, id, type, validations (+5 more)

### Community 171 - "idea.yml"
Cohesion: 0.14
Nodes (13): description, label, render, body, attributes, id, type, validations (+5 more)

### Community 172 - "PassiveTreePanel"
Cohesion: 0.20
Nodes (5): PassiveTreePanel, Character, FrameSize, Player, Title

### Community 173 - "HitTag"
Cohesion: 0.14
Nodes (13): AilmentHit, Chill, Freeze, Ignite, None, Shock, HitTag, Chill (+5 more)

### Community 174 - "ChargerEnemy"
Cohesion: 0.14
Nodes (10): ChargerEnemy, IsCharging, IsWindingUp, LaneDirection, TimeToCharge, State, Charge, Recover (+2 more)

### Community 178 - "ReviveControl"
Cohesion: 0.21
Nodes (7): ReviveControl, Local, PromptText, StartsSent, StopsSent, Target, WantsInteract

### Community 180 - ".StartFinalBoss"
Cohesion: 0.18
Nodes (3): Code notes, Portal, Caption

### Community 181 - "batch.sh"
Cohesion: 0.22
Nodes (10): ab.sh script, abp.sh script, clean(), DOTNET_CLI_TELEMETRY_OPTOUT, DOTNET_ROOT, GODOT_BIN, PATH, pb() (+2 more)

### Community 182 - "Performance pass: where it stopped and how to continue"
Cohesion: 0.15
Nodes (9): Drafts, I0 (tooling), perf impl phase A — running notes, Done (on `main` once the PR merges), Owner decisions (binding), Performance pass: where it stopped and how to continue, Proposed CLAUDE.md lines (not applied), Remaining, in order (+1 more)

### Community 183 - "DamageType"
Cohesion: 0.15
Nodes (9): DamageType, Cold, Fire, Holy, Lightning, Physical, Shadow, DamageTypeResolution (+1 more)

### Community 185 - "K"
Cohesion: 0.15
Nodes (13): K, Count, Crowd, Director, EnemyPhys, EnemyTicks, FindTarget, MoveSlide (+5 more)

### Community 187 - "ARPG Game — Project Guide"
Cohesion: 0.17
Nodes (12): Agents, ARPG Game — Project Guide, Design documentation policy, Devlog policy, Engine rules, Knowledge graph (graphify), MCP tools available, Repo layout (create as needed, keep consistent) (+4 more)

### Community 189 - "Crafting, merchant and disenchanting"
Cohesion: 0.17
Nodes (12): Crafting, merchant and disenchanting, Dust and disenchanting **[IMPLEMENTED]**, Forging **[IMPLEMENTED]**, Merchant **[IMPLEMENTED]**, Open questions, Orbs **[IMPLEMENTED]**, Seals **[IMPLEMENTED 2026-10-13]**, Shared between characters **[DECIDED 2026-10-13]** (+4 more)

### Community 190 - "The hideout town"
Cohesion: 0.17
Nodes (12): Edit mode **[IMPLEMENTED]**, Escaldón (2026-10-30), Finding your way, Open questions, Props and stations, Saving, Shop **[IMPLEMENTED]**, The hideout town (+4 more)

### Community 191 - "ARPG Game"
Cohesion: 0.17
Nodes (12): ARPG Game, Co-op on one machine, Contents, Controls, Documentation, First run, Getting started, How the project is organised (+4 more)

### Community 192 - "SaveItem"
Cohesion: 0.17
Nodes (11): SaveItem, Affixes, Base, BaseStats, Ilvl, Name, Rarity, SealCount (+3 more)

### Community 193 - "AscendancyCatalog"
Cohesion: 0.21
Nodes (7): AscendancyCatalog, AscendancyNode, Description, Id, Mods, Name, Notable

### Community 194 - "Nereth"
Cohesion: 0.24
Nodes (4): Nereth, Cadence, Element, PhaseThresholds

### Community 195 - "ProposalView"
Cohesion: 0.17
Nodes (12): ProposalView, Accepted, Asked, Deadline, Declined, Id, Mine, Pending (+4 more)

### Community 197 - "ResultsScreen"
Cohesion: 0.18
Nodes (7): ResultsScreen, LastClaim, OnHideout, OnMenu, RewardCount, RewardHint, Showing

### Community 198 - "RealityTheme"
Cohesion: 0.18
Nodes (10): RealityTheme, Affinity, Ambient, Boss, BossLifeMultiplier, Id, Music, Roster (+2 more)

### Community 199 - "Endgame: Mirror Shards and Altered Realities"
Cohesion: 0.18
Nodes (11): Alternatives considered, Co-op realities (2026-10-19), Decisions (owner: "go with your recommendations"), Endgame: Mirror Shards and Altered Realities, Milestones (all six done), Monster scaling (2026-10-15), Open questions, The loop (+3 more)

### Community 200 - "Movement"
Cohesion: 0.18
Nodes (10): Alternatives considered, Fifth pass: sprint **[IMPLEMENTED 2026-10-12]**, Fourth pass: dodge and the Space key **[DECIDED — implemented]**, Implementation notes / version caveat, Movement, Open questions, What it is, Which input triggers path-follow **[DECIDED — changed by the third combat pass]** (+2 more)

### Community 201 - "Cash-out offer and the infinite Rift **[IMPLEMENTED 2026-10-18]**"
Cohesion: 0.20
Nodes (9): Acceptance and tests (RunSmokeTest / RiftProgressionTest `CASHOUT-*`), Cash-out offer and the infinite Rift **[IMPLEMENTED 2026-10-18]**, Co-op, In a party (2026-10-18), Owner decisions (2026-10-17), The flow, The panel, The reward on the HUD (endless phase) (+1 more)

### Community 203 - "package.json"
Cohesion: 0.20
Nodes (10): tsx, description, devDependencies, tsx@4.19.2, name, private, scripts, build (+2 more)

### Community 204 - "CraftAction"
Cohesion: 0.18
Nodes (11): CraftAction, Ascend, Ash, Crown, Flux, Graft, Kindle, None (+3 more)

### Community 206 - "ForgeGrade"
Cohesion: 0.20
Nodes (10): ForgeGrade, Cost, ExtraMax, ExtraMin, Gold, Name, PerfectValue, Result (+2 more)

### Community 207 - "DamageHit"
Cohesion: 0.27
Nodes (5): DamageHit, DamageNumberKind, Incoming, Own, Tick

### Community 208 - "QuestDialog"
Cohesion: 0.22
Nodes (7): QuestDialog, FrameSize, PrimaryText, Quest, ShowHeader, Title, TurnInMode

### Community 209 - "RealityModifier"
Cohesion: 0.22
Nodes (8): Arehucaz, the shard dealer (2026-10-14), Mirror Shards (the keys), Shard modifiers ("Empower"), RealityModifier, Id, Quantity, Rarity, Text

### Community 210 - "The introduction: The Threshold **[IMPLEMENTED 2026-10-12]**"
Cohesion: 0.22
Nodes (7): Alternatives considered, Open questions, The introduction: The Threshold **[IMPLEMENTED 2026-10-12]**, What it is, Why, With friends (2026-10-18, D11), CoopUiSmokeTest

### Community 212 - "LauncherPhase"
Cohesion: 0.25
Nodes (8): LauncherPhase, Checking, ChooseFolder, Downloading, Failed, Installing, Offline, Ready

### Community 213 - "Distribution: releases and the launcher **[IMPLEMENTED 2026-10-12]**"
Cohesion: 0.29
Nodes (6): Distribution: releases and the launcher **[IMPLEMENTED 2026-10-12]**, Install folder, Open questions, The rename, What it is, Why

### Community 214 - "Copy"
Cohesion: 0.29
Nodes (7): A jewel for the Mirror (`mirror_jewel`), A modifier of your choosing (`forge_forge`), Breaking it down (`forge_disenchant`), Choose an ascendancy (`ascend`), Copy, Odran's wares (`odran_wares`), The shard dealer (`arehucaz_shards`)

### Community 217 - "View"
Cohesion: 0.29
Nodes (7): View, Choose, Connecting, Failed, Lobby, NoCharacter, Unavailable

### Community 218 - "Dodge"
Cohesion: 0.33
Nodes (6): Alternatives considered, Dodge, Implementation, Open questions, What it is, Why

### Community 219 - "Enemies"
Cohesion: 0.33
Nodes (5): Alternatives considered, Enemies, Open questions, Roster, Shared behaviour — `Enemies/Enemy.cs`

### Community 220 - "Inventory interaction **[DECIDED]**"
Cohesion: 0.33
Nodes (6): Closing panels **[DECIDED]**, Inventory interaction **[DECIDED]**, Item tooltips **[DECIDED]**, Sorting **[DECIDED]**, The equipment strip **[DECIDED]**, UI must consume its own input **[DECIDED]**

### Community 221 - "SkillTreeModifier"
Cohesion: 0.33
Nodes (5): Mana and cooldowns, Rework of 2026-10-30 **[IMPLEMENTED]**, Skill upgrade trees, SkillTreeModifier, Priority

### Community 222 - "DamageNumberMode"
Cohesion: 0.33
Nodes (4): DamageNumberMode, Compact, Full, Off

### Community 223 - "UpgradeKind"
Cohesion: 0.33
Nodes (6): UpgradeKind, Keystone, NewSkill, Passive, SkillLevel, Support

### Community 224 - "multiplayer-expert.md"
Cohesion: 0.40
Nodes (4): Architecture you follow, Boundaries, How you work, Owner decisions (2026-10-15)

### Community 225 - "security-expert.md"
Cohesion: 0.40
Nodes (4): Boundaries, Context and owner decisions, How you work, Reporting

### Community 226 - "ui-ux-expert.md"
Cohesion: 0.40
Nodes (4): Boundaries, How you work, Principles you apply, Reporting

### Community 228 - "PassiveKind"
Cohesion: 0.40
Nodes (5): PassiveKind, Keystone, Notable, Small, Start

### Community 232 - "WindowModeKind"
Cohesion: 0.40
Nodes (4): WindowModeKind, Borderless, Fullscreen, Windowed

### Community 234 - "Mode"
Cohesion: 0.40
Nodes (5): Mode, Asked, Confirm, Hidden, Waiting

### Community 235 - "Affix tiers and item level **[DECIDED]**"
Cohesion: 0.50
Nodes (4): Affix tiers and item level **[DECIDED]**, Authoring: curve, not 7 hand-written rows, Min-ilvl is a wall, max-ilvl is a slope, Tiers are player-visible

### Community 236 - "Drop tables (cascade) **[IMPLEMENTED 2026-10-14]**"
Cohesion: 0.50
Nodes (4): Bonuses — one multiplier on every tier, Crafting orbs — same cascade, Drop tables (cascade) **[IMPLEMENTED 2026-10-14]**, Items — chance per tier, before bonuses

### Community 237 - "hb.sh"
Cohesion: 0.50
Nodes (3): DOTNET_ROOT, PATH, hb.sh script

### Community 238 - "ArpgGameLauncher.csproj"
Cohesion: 0.50
Nodes (3): net48, Microsoft.NETFramework.ReferenceAssemblies (1.0.3), Microsoft.NET.Sdk

### Community 239 - "skill-trees_e9deb1b1.md"
Cohesion: 0.50
Nodes (3): Sheet: Habilidades, Sheet: Mejoras (árboles), Sheet: Reglas

### Community 240 - "coop_local.sh"
Cohesion: 0.83
Nodes (3): canonicalise(), find_godot(), coop_local.sh script

### Community 241 - "ReviveOutcome"
Cohesion: 0.50
Nodes (4): ReviveOutcome, Respawned, RevivedByAlly, StillDown

### Community 242 - "run_editor.sh"
Cohesion: 0.83
Nodes (3): canonicalise(), find_godot_mono(), run_editor.sh script

### Community 243 - "run_headless.sh"
Cohesion: 0.83
Nodes (3): canonicalise(), find_godot(), run_headless.sh script

### Community 244 - "CastFailureKind"
Cohesion: 0.50
Nodes (4): CastFailureKind, InsufficientResource, SlotEmpty, StillRecovering

### Community 245 - "CostMode"
Cohesion: 0.50
Nodes (4): CostMode, Flat, PercentOfCurrent, PercentOfMax

### Community 247 - "ArpgGame"
Cohesion: 0.67
Nodes (3): ArpgGame, net6.0, Godot.NET.Sdk/4.1.1

### Community 248 - "RiftKind"
Cohesion: 0.67
Nodes (3): RiftKind, Reality, SurvivalRift

### Community 250 - "SkillTreeNodeKind"
Cohesion: 0.67
Nodes (3): SkillTreeNodeKind, Lesser, Notable

## Knowledge Gaps
- **1848 isolated node(s):** `net6.0`, `Godot.NET.Sdk/4.1.1`, `DOTNET_CLI_TELEMETRY_OPTOUT`, `DOTNET_ROOT`, `PATH` (+1843 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 2765 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **49 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `NetSession` connect `NetSession` to `CoopHost`, `CoopPanel`, `CoopClient`, `Profile`, `HideoutParty`, `.Label`, `Hideout`, `.Join`, `QuestDef`, `CoopTestClient`, `RealityController`, `RiftHost`, `RunController`, `ArpgGame.UI`, `NetWriter`, `PortMapper`, `system_collections_generic`, `List`, `SkillTags`, `Task`, `RasterLayout`, `IReadOnlyList`, `CoopTestHost`, `system`, `CharacterData`, `HideoutData`, `Node`, `ProposalView`, `Vector2`, `.Answer`, `Dictionary`, `.Scenario`, `.Main`, `RiftProposal`, `Quests and the quest log`, `IRunLink`, `RiftProposalPopup`?**
  _High betweenness centrality (0.107) - this node is a cross-community bridge._
- **Are the 4 inferred relationships involving `NetSession` (e.g. with `Game structure (PoE-style endgame + persistent characters)` and `Engine notes (Godot 4.1.1)`) actually correct?**
  _`NetSession` has 4 INFERRED edges - model-reasoned connections that need verification._
- **What connects `net6.0`, `Godot.NET.Sdk/4.1.1`, `DOTNET_CLI_TELEMETRY_OPTOUT` to the rest of the system?**
  _1848 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `NetSession` be split into smaller, more focused modules?**
  _Cohesion score 0.02076459210376607 - nodes in this community are weakly interconnected._
- **Why does `Player` connect `Player` to `CoopHost`, `.Spawn`, `PlayerRewardSink`, `CoopClient`, `godot`, `Profile`, `EquipmentItem`, `Pickup`, `Hideout`, `Dodge`, `HideoutHud`, `MerchantPanel`, `HideoutParty`, `RealityController`, `RiftHost`, `Performance plan: the 2D checklist, C1–C11 (PLAN phase)`, `Control`, `SkillUser`, `RunController`, `HitProfile`, `Menus, hideout, profile, settings, audio`, `ActorStats`, `NetWriter`, `Health`, `Enemy`, `Flasks`, `List`, `SkillTags`, `Color`, `TutorialGuide`, `MovementCapture`, `PackedScene`, `SkillInstance`, `Task`, `GemShopView`, `RunDirector`, `CoopTestHost`, `PassiveTreePanel`, `RiftProgressionTest`, `RunState`, `QuestLogPanel`, `ItemsSmokeTest`, `DropCapture`, `ExplosionBench`, `CharacterData`, `ModalPanel`, `ReviveControl`, `SkillReworkTest`, `._Ready`, `TargetFrame`, `InventoryUI`, `Vector2`, `CharacterBuild`, `AutoplayBot`, `Rarity`, `CombatQuery`, `StashPanel`, `ArtificerPanel`, `MirrorPanel`, `RunSmokeTest`, `Dictionary`, `.Scenario`, `BaseItemDefinition`, `TownMap`, `RealityClient`, `.TakeDamage`, `CurrencyDrop`, `RunHud`, `PerfBench`, `ItemDescriptor`, `GemData`, `RunRewards`, `CharacterPanels`, `SkillTreePanel`?**
  _High betweenness centrality (0.103) - this node is a cross-community bridge._
- **Are the 5 inferred relationships involving `Player` (e.g. with `Profile` and `Players`) actually correct?**
  _`Player` has 5 INFERRED edges - model-reasoned connections that need verification._
- **Should `CoopHost` be split into smaller, more focused modules?**
  _Cohesion score 0.026533996683250415 - nodes in this community are weakly interconnected._