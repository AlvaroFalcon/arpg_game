#nullable enable
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Godot;

namespace ArpgGame.Net;

/// <summary>
/// What <see cref="PortMapper"/> needs from a router. Every call blocks (UPnP is
/// HTTP to the router) and runs on the mapper's worker, never the main thread.
/// <see cref="UpnpGateway"/> is the real one; tests use a fake.
/// </summary>
public interface IPortGateway
{
    /// <summary>Finds the router. Null if found, else why not (for the player).</summary>
    string? Discover();

    /// <summary>The router's own address on the internet side ("" if it won't say).</summary>
    string ExternalAddress();

    /// <summary>Opens <paramref name="port"/> (UDP) to this PC for <paramref name="leaseSeconds"/> (0 = until removed).</summary>
    MapResult Map(int port, string description, int leaseSeconds);

    void Unmap(int port);
}

public enum MapResult
{
    Ok,

    /// <summary>The router only accepts mappings with no lease (some older routers).</summary>
    OnlyPermanent,

    Failed,
}

/// <summary>The real router, via Godot's <see cref="Upnp"/> (miniupnpc).</summary>
public sealed class UpnpGateway : IPortGateway
{
    /// <summary>Milliseconds to wait for routers to answer the discovery broadcast.</summary>
    public const int DiscoverTimeoutMs = 2500;

    private Upnp? _upnp;

    public string? Discover()
    {
        _upnp = new Upnp();
        var result = (Upnp.UpnpResult)_upnp.Discover(DiscoverTimeoutMs, 2, "InternetGatewayDevice");
        if (result is Upnp.UpnpResult.NoDevices or Upnp.UpnpResult.NoGateway)
        {
            return NoGateway.Reason;
        }

        if (result != Upnp.UpnpResult.Success)
        {
            return $"UPnP discovery failed ({result})";
        }

        UpnpDevice? gateway = _upnp.GetGateway();
        return gateway is not null && gateway.IsValidGateway() ? null : NoGateway.Reason;
    }

    public string ExternalAddress() => _upnp?.QueryExternalAddress() ?? "";

    public MapResult Map(int port, string description, int leaseSeconds)
    {
        if (_upnp is null)
        {
            return MapResult.Failed;
        }

        var result = (Upnp.UpnpResult)_upnp.AddPortMapping(port, port, description, "UDP", leaseSeconds);
        return result switch
        {
            Upnp.UpnpResult.Success => MapResult.Ok,
            Upnp.UpnpResult.OnlyPermanentLeaseSupported => MapResult.OnlyPermanent,
            _ => MapResult.Failed,
        };
    }

    public void Unmap(int port) => _upnp?.DeletePortMapping(port, "UDP");
}

/// <summary>No router at all (UPnP turned off, or a test).</summary>
public sealed class NoGateway : IPortGateway
{
    public const string Reason = "no UPnP router answered — it may be off in the router's settings";

    public string? Discover() => Reason;
    public string ExternalAddress() => "";
    public MapResult Map(int port, string description, int leaseSeconds) => MapResult.Failed;
    public void Unmap(int port)
    {
    }
}

/// <summary>
/// Opens the host's port on the home router with UPnP while a co-op lobby is
/// hosted — docs/design/multiplayer.md -> "Security" -> "UPnP". A small state
/// machine on the main thread (<see cref="State"/>, <see cref="Message"/>) over
/// a single worker that runs every router call in order: discovery can take
/// seconds and must never freeze the game. Because calls run strictly in
/// sequence, a <see cref="Close"/> issued while discovery is still running
/// queues the removal after it, so a mapping can never be left behind by a
/// race. The mapping has a lease (<see cref="LeaseSeconds"/>) renewed while
/// hosting, so if the game crashes the router drops it on its own.
///
/// Results come back through a queue drained by <see cref="Poll"/>, which the
/// owner calls every frame on the main thread.
/// </summary>
public sealed class PortMapper
{
    public const int LeaseSeconds = 2 * 60 * 60;
    public const string Description = "ARPG co-op";

    public enum MapState
    {
        Off,
        Working,
        Open,
        Failed,
    }

    public MapState State { get; private set; }

    /// <summary>What to tell the player about the port (empty when off).</summary>
    public string Message { get; private set; } = "";

    /// <summary>The router's public address, when the port is open.</summary>
    public string ExternalAddress { get; private set; } = "";

    public int Port { get; private set; }

    /// <summary>The router only took a mapping with no lease: it stays until removed.</summary>
    public bool Permanent { get; private set; }

    /// <summary>State, message or address changed (main thread).</summary>
    public event Action? Changed;

    private readonly Func<IPortGateway> _factory;
    private readonly ConcurrentQueue<Action> _results = new();
    private readonly object _chainLock = new();
    private Task _chain = Task.CompletedTask;
    private int _generation;
    private ulong _renewAtMsec;

    /// <summary>The generation the worker may still act for. Set by the main thread
    /// on every Open/Close; an open whose discovery outlived a Close sees the
    /// change and maps nothing (so quitting never has to wait for a discovery).</summary>
    private volatile int _wanted;

    // Worker-only state (touched only inside the chain).
    private IPortGateway? _gateway;
    private int _mappedPort;
    private int _mappedLease;

    public PortMapper(Func<IPortGateway> factory) => _factory = factory;

    public static string FallbackMessage(int port) =>
        $"Your router didn't open the port automatically — forward UDP {port} manually or use a VPN like Tailscale/Radmin.";

    /// <summary>Starts opening <paramref name="port"/>; returns at once.</summary>
    public void Open(int port)
    {
        Close();
        int generation = ++_generation;
        _wanted = generation;
        Port = port;
        State = MapState.Working;
        Message = $"Opening UDP {port} on your router (UPnP)…";
        ExternalAddress = "";
        Permanent = false;
        Changed?.Invoke();
        Enqueue(() =>
        {
            (bool ok, string external, bool permanent, string detail) = WorkOpen(port, generation);
            _results.Enqueue(() => Apply(generation, ok, external, permanent, detail));
        });
    }

    /// <summary>Removes the mapping (if any) in the background. Safe to call any time.</summary>
    public void Close()
    {
        if (State == MapState.Off)
        {
            return;
        }

        _wanted = ++_generation;
        State = MapState.Off;
        Message = "";
        ExternalAddress = "";
        Enqueue(WorkClose);
        Changed?.Invoke();
    }

    /// <summary>Closes and waits up to <paramref name="timeoutMs"/> for the router
    /// calls to finish (quitting the game: the process is about to end).</summary>
    public void CloseAndWait(int timeoutMs)
    {
        Close();
        Task chain;
        lock (_chainLock)
        {
            chain = _chain;
        }

        try
        {
            chain.Wait(timeoutMs);
        }
        catch (AggregateException)
        {
            // Already logged inside the worker; nothing more to do on the way out.
        }
    }

    /// <summary>Main thread, every frame: applies worker results and renews the lease.</summary>
    public void Poll(ulong nowMsec)
    {
        while (_results.TryDequeue(out Action? apply))
        {
            apply();
        }

        if (State == MapState.Open && !Permanent && _renewAtMsec != 0 && nowMsec >= _renewAtMsec)
        {
            _renewAtMsec = nowMsec + (ulong)LeaseSeconds * 500UL; // every half lease
            int port = Port;
            int generation = _generation;
            Enqueue(() =>
            {
                if (!WorkRenew(port))
                {
                    _results.Enqueue(() =>
                    {
                        if (generation == _generation && State == MapState.Open)
                        {
                            State = MapState.Failed;
                            ExternalAddress = "";
                            Message = $"{FallbackMessage(Port)} (the router stopped renewing the mapping)";
                            GD.Print($"Co-op UPnP: {Message}");
                            Changed?.Invoke();
                        }
                    });
                }
            });
        }
    }

    /// <summary>Forces the next <see cref="Poll"/> to renew (tests).</summary>
    public void RenewSoon() => _renewAtMsec = 1;

    private void Apply(int generation, bool ok, string external, bool permanent, string detail)
    {
        if (generation != _generation)
        {
            return; // closed or reopened since: the worker already undid it
        }

        if (ok)
        {
            State = MapState.Open;
            ExternalAddress = external;
            Permanent = permanent;
            Message = $"Router port opened automatically (UPnP{(permanent ? ", until you stop hosting" : ", 2 h lease, renewed while you host")}).";
            _renewAtMsec = Time.GetTicksMsec() + (ulong)LeaseSeconds * 500UL;
        }
        else
        {
            State = MapState.Failed;
            ExternalAddress = "";
            Message = $"{FallbackMessage(Port)} ({detail})";
        }

        GD.Print($"Co-op UPnP: {Message}{(ok ? $" External address {external}." : "")}");
        Changed?.Invoke();
    }

    private void Enqueue(Action work)
    {
        lock (_chainLock)
        {
            _chain = _chain.ContinueWith(_ =>
            {
                try
                {
                    work();
                }
                catch (Exception e)
                {
                    GD.PushWarning($"UPnP: {e.Message}");
                }
            }, TaskScheduler.Default);
        }
    }

    // --- worker -------------------------------------------------------------------------

    private (bool Ok, string External, bool Permanent, string Detail) WorkOpen(int port, int generation)
    {
        _gateway ??= _factory();
        if (_gateway.Discover() is { } why)
        {
            _gateway = null;
            return (false, "", false, why);
        }

        if (_wanted != generation)
        {
            return (false, "", false, "closed before the router answered"); // nothing mapped, nothing to undo
        }

        string external = _gateway.ExternalAddress().Trim();
        if (!IsPublicAddress(external))
        {
            // CG-NAT (100.64.0.0/10) or a second router in front: opening this
            // router's port would not make the PC reachable, so don't leave one open.
            string what = external.Length == 0 ? "the router did not report a public address" : $"the router's internet address {external} is not public — your provider or a second router shares it (CG-NAT), so forwarding won't work either";
            return (false, "", false, what);
        }

        MapResult result = _gateway.Map(port, Description, LeaseSeconds);
        int lease = LeaseSeconds;
        if (result != MapResult.Ok && _wanted == generation)
        {
            // Error 725 (OnlyPermanentLeaseSupported) asks for this outright; some
            // routers answer a lease they dislike with another code, so retry any failure.
            lease = 0;
            result = _gateway.Map(port, Description, 0);
        }

        if (result != MapResult.Ok)
        {
            return (false, "", false, "the router refused the mapping (the port may be taken by another PC)");
        }

        _mappedPort = port;
        _mappedLease = lease;
        return (true, external, lease == 0, "");
    }

    /// <summary>Renews the lease; false if the router refused (the port may be closed now).</summary>
    private bool WorkRenew(int port)
    {
        if (_gateway is not null && _mappedPort == port)
        {
            return _gateway.Map(port, Description, _mappedLease) == MapResult.Ok;
        }

        return true;
    }

    private void WorkClose()
    {
        if (_gateway is not null && _mappedPort != 0)
        {
            _gateway.Unmap(_mappedPort);
        }

        _mappedPort = 0;
        _gateway = null;
    }

    // --- addresses ----------------------------------------------------------------------

    /// <summary>A routable internet address: not private, CG-NAT, loopback, link-local or empty.</summary>
    public static bool IsPublicAddress(string text)
    {
        if (!IPAddress.TryParse(text, out IPAddress? ip))
        {
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }
            else
            {
                byte[] v6 = ip.GetAddressBytes();
                return !IPAddress.IsLoopback(ip) && !ip.IsIPv6LinkLocal && !ip.IsIPv6SiteLocal && (v6[0] & 0xFE) != 0xFC && !ip.Equals(IPAddress.IPv6Any);
            }
        }

        byte[] b = ip.GetAddressBytes();
        return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224
                 || (b[0] == 100 && (b[1] & 0xC0) == 64) // 100.64.0.0/10, CG-NAT (and Tailscale)
                 || (b[0] == 169 && b[1] == 254)
                 || (b[0] == 172 && (b[1] & 0xF0) == 16)
                 || (b[0] == 192 && b[1] == 168));
    }

    /// <summary>This PC's addresses friends on the same network or VPN can use:
    /// LAN (192.168/10/172.16) and VPN (100.64/10: Tailscale; 26.x: Radmin).</summary>
    public static string[] LocalAddresses()
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (string a in IP.GetLocalAddresses())
        {
            if (!IPAddress.TryParse(a, out IPAddress? ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            byte[] b = ip.GetAddressBytes();
            if (b[0] == 127 || (b[0] == 169 && b[1] == 254))
            {
                continue;
            }

            list.Add(a);
        }

        // LAN first, then VPN-ish, then anything else.
        list.Sort((x, y) => Rank(x).CompareTo(Rank(y)));
        return list.ToArray();

        static int Rank(string a) => a.StartsWith("192.168.") || a.StartsWith("10.") || a.StartsWith("172.") ? 0 : a.StartsWith("100.") || a.StartsWith("26.") ? 1 : 2;
    }
}
