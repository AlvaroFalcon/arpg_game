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
