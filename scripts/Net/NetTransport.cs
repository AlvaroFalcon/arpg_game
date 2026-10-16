#nullable enable
using Godot;

namespace ArpgGame.Net;

/// <summary>
/// The one place that creates a <see cref="MultiplayerPeer"/> — docs/design/multiplayer.md
/// -> "Transport". Today: ENet over UDP, connected by IP (friends use Tailscale,
/// Radmin VPN, ZeroTier or a forwarded port). A Steam or EOS relay later only
/// has to return its own peer from these two functions; nothing else in the
/// game knows which transport is underneath.
/// </summary>
public static class NetTransport
{
    public const int DefaultPort = 7777;

    /// <summary>Extra ENet channels requested. Unused today: every RPC uses
    /// transfer channel 0, because Godot 4.1.1 sends anything on a higher channel
    /// unreliably (see NetSession). Kept so a later engine can split traffic.</summary>
    public const int Channels = 4;

    /// <summary>ENet connection slots on the host. Far more than the party: a
    /// stranger holding a few unauthenticated connections must not lock friends
    /// out (the party size is enforced by the lobby, not by ENet, which would
    /// drop extra connections silently). Each waits at most
    /// <see cref="NetLimits.AuthTimeout"/> for its code.</summary>
    public const int MaxConnections = 32;

    public static (MultiplayerPeer? Peer, Error Error) Host(int port, int maxClients)
    {
        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateServer(port, maxClients, Channels);
        return error == Error.Ok ? (peer, error) : (null, error);
    }

    public static (MultiplayerPeer? Peer, Error Error) Join(string address, int port)
    {
        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateClient(address, port, Channels);
        return error == Error.Ok ? (peer, error) : (null, error);
    }

    /// <summary>ENet's own defaults (enet.h): a peer that stops acknowledging is
    /// dropped after 5–30 s depending on its round trip.</summary>
    private const int DefaultTimeoutLimit = 32, DefaultTimeoutMinMs = 5000, DefaultTimeoutMaxMs = 30000;

    /// <summary>Client, right after <see cref="Join"/>: give up on a host that
    /// never answers after about <paramref name="maxMs"/> instead of ENet's ~30 s
    /// (a dead address is the common failure, and silence is the worst state).
    /// Applies to the connect request because ENet computes its retry limit when
    /// the request is first sent, on the next poll.</summary>
    public static void ShortenConnectTimeout(MultiplayerPeer? peer, int maxMs)
    {
        if (peer is ENetMultiplayerPeer enet && enet.GetPeer(1) is { } server)
        {
            // Retries keep ENet's pace; only the deadline moves (a fixed maximum).
            server.SetTimeout(DefaultTimeoutLimit, maxMs, maxMs);
        }
    }

    /// <summary>Client, once the host answered: back to ENet's defaults, so a
    /// short stall mid-run doesn't drop the player.</summary>
    public static void RestoreTimeout(MultiplayerPeer? peer)
    {
        if (peer is ENetMultiplayerPeer enet && enet.GetPeer(1) is { } server)
        {
            server.SetTimeout(DefaultTimeoutLimit, DefaultTimeoutMinMs, DefaultTimeoutMaxMs);
        }
    }

    /// <summary>The text "Copy invite" puts on the clipboard: the address and the
    /// code in one line a friend pastes into the address field.</summary>
    public static string Invite(string address, string code) => $"{address} {JoinCode.Format(code)}";

    /// <summary>A pasted invite ("203.0.113.7:7777 K7Q-29M", or a chat line
    /// around it) → its address and code. False when the text holds no address
    /// followed by a well-formed code (a bare address stays as typed).</summary>
    public static bool TryParseInvite(string text, out string address, out string code)
    {
        address = "";
        code = "";
        string[] tokens = text.Split(new[] { ' ', '\t', '\n', '\r', ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries);
        int at = -1;
        for (int i = 0; i < tokens.Length; i++)
        {
            string t = tokens[i].Trim('"', '\'', '(', ')', '<', '>');
            if ((t.Contains('.') || t.Contains(':')) && TryParseAddress(t, out _, out _))
            {
                address = t.TrimEnd('.');
                at = i;
                break;
            }
        }

        if (at < 0)
        {
            return false;
        }

        for (int i = at + 1; i < tokens.Length; i++)
        {
            // "K7Q-29M", or typed in two halves ("k7q 29m").
            string one = JoinCode.Normalize(tokens[i].Trim('"', '\'', '(', ')', '.', '<', '>'));
            string two = i + 1 < tokens.Length ? one + JoinCode.Normalize(tokens[i + 1].Trim('"', '\'', '(', ')', '.', '<', '>')) : "";
            foreach (string candidate in new[] { one, two })
            {
                if (JoinCode.IsWellFormed(candidate))
                {
                    code = JoinCode.Format(candidate);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Total bytes this machine sent and received on the wire since the
    /// last call (ENet's own counters, headers included) — the performance
    /// numbers in docs/design/multiplayer.md come from here. Zero for a
    /// transport that cannot tell.</summary>
    public static (long Sent, long Received) PopWireBytes(MultiplayerPeer? peer)
    {
        if (peer is ENetMultiplayerPeer enet && enet.Host is { } host)
        {
            long sent = (long)host.PopStatistic(ENetConnection.HostStatistic.SentData);
            long received = (long)host.PopStatistic(ENetConnection.HostStatistic.ReceivedData);
            return (sent, received);
        }

        return (0, 0);
    }

    /// <summary>The remote address of a connected peer, for the host's per-address
    /// limits ("" if the transport cannot tell — a relay would key on its own user id).</summary>
    public static string RemoteAddress(MultiplayerPeer? peer, long id)
    {
        if (peer is ENetMultiplayerPeer enet && enet.GetPeer((int)id) is { } packetPeer)
        {
            return packetPeer.GetRemoteAddress();
        }

        return "";
    }

    /// <summary>Disconnects a peer once what was already queued for it has been
    /// sent (the reason it was turned away). ENet's plain disconnect drops the
    /// outgoing queue first, so the reason would never arrive.</summary>
    public static void DisconnectAfterSend(MultiplayerPeer? peer, long id)
    {
        if (peer is ENetMultiplayerPeer enet && enet.GetPeer((int)id) is { } packetPeer)
        {
            packetPeer.PeerDisconnectLater();
            enet.Host?.Flush();
            return;
        }

        peer?.DisconnectPeer((int)id);
    }

    /// <summary>"192.168.1.20:7777", "my-pc.tail1234.ts.net", "[::1]:7777" → host and port.</summary>
    public static bool TryParseAddress(string text, out string host, out int port)
    {
        text = text.Trim();
        host = text;
        port = DefaultPort;
        if (text.Length == 0)
        {
            return false;
        }

        if (text.StartsWith('['))
        {
            int close = text.IndexOf(']');
            if (close < 0)
            {
                return false;
            }

            host = text.Substring(1, close - 1);
            string rest = text[(close + 1)..];
            return rest.Length == 0 || (rest.StartsWith(':') && int.TryParse(rest[1..], out port) && port is > 0 and < 65536);
        }

        int colon = text.LastIndexOf(':');
        if (colon > 0 && text.IndexOf(':') == colon)
        {
            host = text[..colon];
            return int.TryParse(text[(colon + 1)..], out port) && port is > 0 and < 65536 && host.Length > 0;
        }

        return true;
    }
}
