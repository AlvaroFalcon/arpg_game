#nullable enable
using System;
using System.Linq;
using Godot;
using ArpgGame.World;

namespace ArpgGame.Net;

/// <summary>
/// Editing the host's town with friends — docs/design/multiplayer.md -> "Edit
/// permission" (D10). The host allows editing per friend, for this session only
/// (never saved), and revokes it at any time. One editor at a time, the host
/// included: whoever holds the lock edits a copy with the host's storage, and a
/// guest's result comes back as a whole layout the host checks
/// (<see cref="HideoutLayoutRules.ValidateEdit"/>: stations kept, everything on
/// open ground, every decoration and tile conserved) before it saves and
/// publishes it. A refused edit costs the guest nothing but the edit.
/// </summary>
public partial class NetSession
{
    /// <summary>Client: the host granted the lock; the town to edit, with the host's storage.</summary>
    public event Action<HideoutData>? EditGranted;

    /// <summary>Client: the edit ended without being applied (refused, revoked): why.</summary>
    public event Action<string>? EditEnded;

    /// <summary>Host: a guest's edit passed the checks; the hideout applies, saves and publishes it.</summary>
    public event Action<HideoutData>? LayoutSubmitted;

    /// <summary>Host: guest edits refused so far, and the last reason (tests).</summary>
    public int RefusedEdits { get; private set; }
    public string LastEditRefusal { get; private set; } = "";

    /// <summary>Host: allows or forbids a friend to edit this town (this session only).</summary>
    public void SetCanEdit(long peer, bool allowed)
    {
        if (!IsHost || peer == 1 || Member(peer) is null || _canEdit.Contains(peer) == allowed)
        {
            return;
        }

        if (allowed)
        {
            _canEdit.Add(peer);
        }
        else
        {
            _canEdit.Remove(peer);
            if (EditHolder == peer)
            {
                ReleaseEditLock($"{NameOf(1)} took back editing rights");
            }
        }

        SendToClient(peer, SessionProtocol.Notice, w => w.Str(allowed ? $"{NameOf(1)} lets you edit the town (H)" : $"{NameOf(1)} took back editing rights"));
        BroadcastRoster();
    }

    /// <summary>Host: takes the edit lock for its own editor; false (and who holds it) if a friend has it.</summary>
    public bool TryHostEditLock(out string holder)
    {
        holder = "";
        if (!IsHost)
        {
            return true;
        }

        if (EditHolder != 0 && EditHolder != 1)
        {
            holder = NameOf(EditHolder);
            return false;
        }

        if (EditHolder != 1)
        {
            EditHolder = 1;
            BroadcastRoster();
        }

        return true;
    }

    /// <summary>Host: its editor closed.</summary>
    public void ReleaseHostEditLock()
    {
        if (IsHost && EditHolder == 1)
        {
            EditHolder = 0;
            BroadcastRoster();
        }
    }

    /// <summary>Client: asks for the lock (granted with <see cref="EditGranted"/>).</summary>
    public void RequestEdit() => SendToHost(SessionProtocol.EditRequest);

    /// <summary>Client: gives the lock back without changes.</summary>
    public void CancelEdit() => SendToHost(SessionProtocol.EditCancel);

    /// <summary>Client: the edited town, storage included, for the host to check and apply.</summary>
    public void SubmitLayout(HideoutData data)
    {
        if (IsClient)
        {
            RpcId(1, MethodName.RpcLayoutSubmit, HideoutLayoutRules.Serialize(data, withStock: true));
        }
    }

    /// <summary>Host: frees the lock; a guest holding it is told why (its editor closes).</summary>
    private void ReleaseEditLock(string? why)
    {
        long holder = EditHolder;
        if (holder == 0)
        {
            return;
        }

        EditHolder = 0;
        if (holder != 1 && why is not null)
        {
            SendToClient(holder, SessionProtocol.EditRevoked, w => w.Str(why));
        }

        BroadcastRoster();
    }

    private string? WhyNoEdit(long peer)
    {
        if (!CanEditOf(peer))
        {
            return $"{NameOf(1)} hasn't allowed you to edit the town";
        }

        if (PartyPhase != SessionPhase.Hideout || PlaceOf(1) != MemberPlace.Hideout || PlaceOf(peer) != MemberPlace.Hideout)
        {
            return "The town can only be edited while everyone who edits is in it";
        }

        if (EditHolder != 0 && EditHolder != peer)
        {
            return $"{NameOf(EditHolder)} is editing the town";
        }

        return null;
    }

    /// <summary>Host: edit-lock traffic from a client.</summary>
    private void OnEditToHost(long sender, byte kind)
    {
        switch (kind)
        {
            case SessionProtocol.EditRequest:
                if (WhyNoEdit(sender) is { } why)
                {
                    SendToClient(sender, SessionProtocol.EditRefused, w => w.Str(why));
                    return;
                }

                if (CurrentLayout() is not { } town)
                {
                    SendToClient(sender, SessionProtocol.EditRefused, w => w.Str("The town isn't loaded"));
                    return;
                }

                EditHolder = sender;
                RpcId(sender, MethodName.RpcEditCopy, HideoutLayoutRules.Serialize(town, withStock: true));
                BroadcastRoster();
                break;
            case SessionProtocol.EditCancel:
                if (EditHolder == sender)
                {
                    ReleaseEditLock(null);
                }

                break;
        }
    }

    /// <summary>Client: edit-lock answers from the host.</summary>
    private void OnEditToClient(byte kind, NetReader r)
    {
        if (kind is SessionProtocol.EditRefused or SessionProtocol.EditRevoked)
        {
            EditEnded?.Invoke(SessionProtocol.Cut(r.Str()));
        }
    }

    /// <summary>Client: the lock is ours; this is the town to edit, with the host's storage.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcEditCopy(string json)
    {
        if (!IsClient)
        {
            return;
        }

        if (HideoutLayoutRules.SanitizeReceived(json, NetLimits.LayoutChars, keepStock: true) is { } data)
        {
            EditGranted?.Invoke(data);
        }
        else
        {
            CancelEdit();
        }
    }

    /// <summary>Host: a guest's edited town. Only from the lock holder; checked, then applied
    /// or refused (the lock is freed either way). Oversized: the sender is kicked.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcLayoutSubmit(string json)
    {
        long sender = _api.GetRemoteSenderId();
        if (!Admit(sender, json.Length, NetLimits.LayoutChars, "layout"))
        {
            return;
        }

        if (sender != EditHolder)
        {
            Refuse("not holding the edit lock");
            return;
        }

        string? why = CurrentLayout() is not { } before ? "the town isn't loaded"
            : HideoutLayoutRules.SanitizeReceived(json, NetLimits.LayoutChars, keepStock: true) is not { } after ? "the layout can't be read"
            : HideoutLayoutRules.ValidateEdit(before, after);
        if (why is not null)
        {
            Refuse(why);
            SendToClient(sender, SessionProtocol.EditRefused, w => w.Str($"Your changes weren't kept ({why})"));
            ReleaseEditLock(null);
            PublishLayout(sender);
            return;
        }

        EditHolder = 0;
        LayoutSubmitted?.Invoke(HideoutLayoutRules.SanitizeReceived(json, NetLimits.LayoutChars, keepStock: true)!);
        BroadcastRoster();
        PublishLayout();

        void Refuse(string reason)
        {
            RefusedEdits++;
            LastEditRefusal = reason;
            Security($"refused a town edit from {NameOf(sender)}: {reason}");
        }
    }
}
