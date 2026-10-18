#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.Net;
using ArpgGame.UI;

namespace ArpgGame.World;

/// <summary>
/// The hideout's party hooks — docs/design/multiplayer.md -> "Shared hideout".
/// A child of <see cref="Hideout"/> that does nothing offline. In a session it
/// reports this machine as home, sends where the local player stands, draws the
/// other members who are home as puppets (built like Rift puppets, interpolated
/// <see cref="NetSession.PresenceDelay"/> behind), keeps the tree running (no
/// pause in a party: D13), toasts arrivals and departures, and on a guest whose
/// party ended goes back to the main menu with the reason.
/// </summary>
public partial class HideoutParty : Node
{
    public Hideout Hideout { get; set; } = null!;

    /// <summary>Puppets by peer id (tests).</summary>
    public IReadOnlyDictionary<long, Player> Puppets => _puppets;

    /// <summary>The rift proposal popup (created with the party hooks).</summary>
    public RiftProposalPopup Popup { get; private set; } = null!;

    private NetSession? _session;
    private readonly Dictionary<long, Player> _puppets = new();
    private readonly Dictionary<long, string> _builtFrom = new();
    private readonly HashSet<long> _home = new();
    private bool _wasGuest;
    private bool _wasActive;
    private bool _leaving;
    private bool _controlsHeld;

    public override void _Ready()
    {
        Name = "HideoutParty";
        ProcessMode = ProcessModeEnum.Always;
        _session = NetSession.Get(this);
        if (_session is null)
        {
            return;
        }

        _session.Ended += OnEnded;
        _session.PartyChanged += OnPartyChanged;
        _session.ProposalClosed += OnProposalClosed;
        _session.Notice += OnNotice;
        Popup = new RiftProposalPopup { Session = _session, Name = "RiftProposalPopup" };
        AddChild(Popup);
        Attach();
    }

    public override void _ExitTree()
    {
        if (_session is not null)
        {
            _session.Ended -= OnEnded;
            _session.PartyChanged -= OnPartyChanged;
            _session.ProposalClosed -= OnProposalClosed;
            _session.Notice -= OnNotice;
        }
    }

    /// <summary>A session is active (now or from the start): this machine is home.</summary>
    private void Attach()
    {
        if (_session is not { Active: true } s)
        {
            return;
        }

        _wasActive = true;
        _wasGuest = s.IsClient;
        s.ReportPlace(MemberPlace.Hideout);
        if (Hideout.Pause is { } pause)
        {
            pause.NoPause = true;
        }
    }

    private void OnPartyChanged()
    {
        if (_session is not { Active: true } s || _leaving)
        {
            return;
        }

        if (!_wasActive)
        {
            Attach();
        }

        var home = s.Members.Where(m => m.PeerId != s.LocalPeerId && s.PlaceOf(m.PeerId) == MemberPlace.Hideout).Select(m => m.PeerId).ToHashSet();
        foreach (long peer in home.Except(_home))
        {
            Hideout.Toast($"{s.NameOf(peer)} is here", UiTheme.Experience);
        }

        foreach (long peer in _home.Except(home))
        {
            if (s.Member(peer) is null)
            {
                Hideout.Toast($"{s.NameOf(peer)} left the party", new Color(1f, 0.7f, 0.4f));
            }
        }

        _home.Clear();
        _home.UnionWith(home);
    }

    private void OnNotice(string text) => Hideout.Toast(text, UiTheme.Ember);

    private void OnProposalClosed(ProposalView view, bool started, string reason)
    {
        if (_leaving || _session is not { Active: true } s)
        {
            return;
        }

        if (!started)
        {
            string whose = view.Proposer == s.LocalPeerId ? "Your" : $"{view.ProposerName}'s";
            Hideout.Toast($"{whose} rift was cancelled — {reason}", new Color(1f, 0.7f, 0.4f));
        }
        else if (view.Mine != true && view.Proposer != s.LocalPeerId)
        {
            Hideout.Toast($"The party set off without you: {view.Spec.Short()} — you can join the next one", UiTheme.Gold);
        }
    }

    /// <summary>The standing line while the party is in a rift without this player.</summary>
    private void UpdateWaiting(NetSession s)
    {
        string line = "";
        if (s.HostRun is { } run && !run.Includes(s.LocalPeerId) && s.PartyPhase is SessionPhase.Run or SessionPhase.Returning)
        {
            float elapsed = (float)(NetSession.Clock - run.StartedAt);
            line = s.PartyPhase == SessionPhase.Returning
                ? $"{s.HostName}'s party is coming back from the {run.Spec.Short()}"
                : $"{s.HostName}'s party is in the {run.Spec.Short()} · {UiTheme.FormatTime(elapsed)} — wait here, you're asked next time";
        }

        Hideout.Hud.PartyLine = line;
    }

    private void OnEnded(string reason)
    {
        if (_leaving || !IsInsideTree())
        {
            return;
        }

        ClearPuppets();
        _home.Clear();
        _wasActive = false;
        if (Hideout.Pause is { } pause)
        {
            pause.NoPause = false;
        }

        if (_wasGuest)
        {
            // Owner rule: the host leaving sends friends to the main menu, which says why.
            _leaving = true;
            Hideout.SaveLayout();
            Scenes.Go(this, Scenes.MainMenu);
        }
    }

    /// <summary>Host (Stop hosting) or guest leaving on purpose: puppets go, the scene stays.</summary>
    public void Detach()
    {
        ClearPuppets();
        _home.Clear();
        _wasActive = false;
        if (Hideout.Pause is { } pause)
        {
            pause.NoPause = false;
        }

        ReleaseControls();
    }

    private void ClearPuppets()
    {
        foreach (Player p in _puppets.Values.Where(IsInstanceValid))
        {
            p.QueueFree();
        }

        _puppets.Clear();
        _builtFrom.Clear();
    }

    public override void _Process(double delta)
    {
        if (_session is not { Active: true } s || _leaving)
        {
            if (_puppets.Count > 0)
            {
                ClearPuppets();
            }

            if (IsInstanceValid(Hideout.Hud) && Hideout.Hud.PartyLine.Length > 0)
            {
                Hideout.Hud.PartyLine = "";
            }

            ReleaseControls();
            return;
        }

        if (!_wasActive)
        {
            Attach();
        }

        Player local = Hideout.Player;
        if (IsInstanceValid(local))
        {
            s.SetLocalPresence(local.GlobalPosition, FlagsOf(local));
            // D13: nothing pauses in a party, so a panel or the menu holds the player still instead.
            bool hold = PanelStack.HasOpen || Hideout.Pause is { IsOpen: true };
            if (hold != _controlsHeld)
            {
                _controlsHeld = hold;
                local.ControlsEnabled = !hold;
            }
        }

        SyncPuppets(s);
        UpdateWaiting(s);
    }

    private void ReleaseControls()
    {
        if (_controlsHeld && IsInstanceValid(Hideout.Player))
        {
            Hideout.Player.ControlsEnabled = true;
        }

        _controlsHeld = false;
    }

    private static byte FlagsOf(Player p)
    {
        byte flags = 0;
        if (p.Velocity.LengthSquared() > 25f)
        {
            flags |= SessionProtocol.PresenceMoving;
        }

        if (p.IsSprinting)
        {
            flags |= SessionProtocol.PresenceSprinting;
        }

        if (p.Dodge is { IsDashing: true })
        {
            flags |= SessionProtocol.PresenceDodging;
        }

        return flags;
    }

    private void SyncPuppets(NetSession s)
    {
        var wanted = new HashSet<long>();
        foreach (LobbyMember m in s.Members)
        {
            if (m.PeerId == s.LocalPeerId || s.PlaceOf(m.PeerId) != MemberPlace.Hideout || !s.TryPresence(m.PeerId, out Vector2 at, out _))
            {
                continue;
            }

            wanted.Add(m.PeerId);
            if (_puppets.TryGetValue(m.PeerId, out Player? puppet) && (!IsInstanceValid(puppet) || _builtFrom[m.PeerId] != m.Character))
            {
                // Their gear or class changed (a character update): dress a fresh copy.
                if (IsInstanceValid(puppet))
                {
                    puppet.QueueFree();
                }

                _puppets.Remove(m.PeerId);
            }

            if (!_puppets.TryGetValue(m.PeerId, out puppet))
            {
                puppet = Spawn(m, at);
                _puppets[m.PeerId] = puppet;
                _builtFrom[m.PeerId] = m.Character;
            }

            Vector2 was = puppet.GlobalPosition;
            puppet.GlobalPosition = at;
            double dt = GetProcessDeltaTime();
            // The puppet's walk animation and facing come from its velocity.
            puppet.Velocity = dt > 0.0 ? (at - was) / (float)dt : Vector2.Zero;
        }

        foreach (long gone in _puppets.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            if (IsInstanceValid(_puppets[gone]))
            {
                _puppets[gone].QueueFree();
            }

            _puppets.Remove(gone);
            _builtFrom.Remove(gone);
        }
    }

    /// <summary>A puppet: no input, no skills, no collision (the local player walks through a lagging friend).</summary>
    private Player Spawn(LobbyMember member, Vector2 at)
    {
        Player p = PartySpawner.Build(Hideout, member, puppet: true, commanded: false, at);
        p.CollisionLayer = 0;
        p.CollisionMask = 0;
        p.GetNode<PlayerSkillInput>("PlayerSkillInput").InputEnabled = false;
        if (p.Dodge is { } dodge)
        {
            dodge.InputEnabled = false;
        }

        if (Characters.CharacterBuild.Of(p)?.Ascendancy is { } ascendancy)
        {
            ascendancy.ProcessMode = ProcessModeEnum.Disabled;
        }

        if (p.Health is { } health)
        {
            health.IgnoreDamage = true;
        }

        return p;
    }
}
