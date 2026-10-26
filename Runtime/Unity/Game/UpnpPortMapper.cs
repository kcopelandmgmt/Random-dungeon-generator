using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DungeonGen.Unity
{
    /// <summary>What a port-mapping attempt found.</summary>
    public sealed class UpnpResult
    {
        /// <summary>The router accepted the mapping (it must be removed again: <see cref="UpnpPortMapper.Unmap"/>).</summary>
        public bool Mapped;
        /// <summary>Mapped, and the router's own address is a public one: friends outside the network can reach this machine.</summary>
        public bool Reachable;
        public ushort Port;
        public string ExternalIp, LocalIp, Gateway;
        /// <summary>Why not, for the log (null on success).</summary>
        public string Error;
        public int LeaseSeconds;
        public double Seconds;

        /// <summary>The line the host's lobby shows.</summary>
        public string Summary => Reachable
            ? $"Friends outside your network can join at {ExternalIp}:{Port}"
            : $"Port {Port} is not reachable from the internet; forward UDP {Port} on your router or use LAN";

        public override string ToString() => Reachable ? $"mapped UDP {Port} → {LocalIp} via {Gateway}; external {ExternalIp} ({Seconds:0.00} s)" : $"not reachable: {Error} ({Seconds:0.00} s)";
    }

    /// <summary>
    /// A small UPnP Internet Gateway Device client with no dependencies beyond System.Net (2026-10-26 online play beyond the LAN): SSDP discovery of the
    /// router, then SOAP <c>AddPortMapping</c>, <c>GetExternalIPAddress</c> and <c>DeletePortMapping</c> on its WANIPConnection (or WANPPPConnection)
    /// service, for UDP. Every call is synchronous and bounded by <see cref="TimeoutMs"/> (about 3 s); run it off the main thread
    /// (<see cref="MapAsync"/>, <see cref="UnmapAsync"/>). Nothing here touches Unity, so it runs on any thread.
    /// <para>
    /// A router that answers but refuses a timed lease (error 725) gets a permanent one; a port already forwarded to another machine (718) is a failure.
    /// A mapping whose router address is private or carrier-grade NAT (a second router upstream) is mapped but not reachable, and says so.
    /// Tests point <see cref="SsdpTarget"/> at a local endpoint instead of the multicast group.
    /// </para>
    /// </summary>
    public sealed class UpnpPortMapper
    {
        public static readonly IPEndPoint SsdpMulticast = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        private static readonly string[] SearchTargets =
        {
            "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANPPPConnection:1",
            "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
        };

        /// <summary>Where the SSDP search goes: the multicast group, or a test responder.</summary>
        public IPEndPoint SsdpTarget { get; set; } = SsdpMulticast;
        /// <summary>The whole attempt's budget, discovery and SOAP calls together.</summary>
        public int TimeoutMs { get; set; } = 3000;
        public string Description { get; set; } = "Dungeon Game";
        /// <summary>Lease asked for first (renew before it ends); 0 is permanent.</summary>
        public int LeaseSeconds { get; set; } = 3600;

        /// <summary>The router's control endpoint once found.</summary>
        public string ControlUrl { get; private set; }
        public string ServiceType { get; private set; }
        public ushort MappedPort { get; private set; }
        public bool Mapped { get; private set; }
        public UpnpResult Last { get; private set; }

        public Task<UpnpResult> MapAsync(ushort port) => Task.Run(() => Map(port));
        public Task<bool> UnmapAsync() => Task.Run(() => Unmap());

        /// <summary>Find the router and forward UDP <paramref name="port"/> (external and internal alike) to this machine. Never throws.</summary>
        public UpnpResult Map(ushort port)
        {
            var clock = Stopwatch.StartNew();
            var result = new UpnpResult { Port = port };
            try
            {
                int Remaining() => Math.Max(0, TimeoutMs - (int)clock.ElapsedMilliseconds);
                if (ControlUrl == null)
                {
                    var locations = Discover(Math.Max(300, TimeoutMs * 2 / 3), clock);
                    if (locations.Count == 0) { result.Error = "no UPnP gateway answered"; return Done(result, clock); }
                    foreach (var location in locations)
                    {
                        if (Remaining() <= 0) break;
                        try
                        {
                            var (control, service) = FindService(location, Http("GET", location, null, null, Remaining()).body);
                            if (control != null) { ControlUrl = control; ServiceType = service; break; }
                        }
                        catch (Exception e) { result.Error = "gateway description unreadable: " + e.Message; }
                    }
                    if (ControlUrl == null) { result.Error ??= "the gateway has no WAN connection service"; return Done(result, clock); }
                }
                result.Gateway = new Uri(ControlUrl).Host;
                result.LocalIp = LocalAddressToward(result.Gateway);
                if (result.LocalIp == null) { result.Error = "no local address toward the gateway"; return Done(result, clock); }

                int lease = LeaseSeconds;
                var add = AddMapping(port, result.LocalIp, lease, Remaining());
                if (add.code == 725 && lease != 0) { lease = 0; add = AddMapping(port, result.LocalIp, 0, Remaining()); }   // OnlyPermanentLeasesSupported
                if (!add.ok)
                {
                    result.Error = add.code == 718 ? $"UDP {port} is already forwarded to another machine" : $"AddPortMapping refused ({add.code} {add.error})";
                    return Done(result, clock);
                }
                Mapped = true; MappedPort = port; result.Mapped = true; result.LeaseSeconds = lease;

                var ip = Soap("GetExternalIPAddress", "", Remaining());
                result.ExternalIp = ip.ok ? Element(ip.body, "NewExternalIPAddress") : null;
                if (string.IsNullOrEmpty(result.ExternalIp) || !IPAddress.TryParse(result.ExternalIp, out var external)) result.Error = "the gateway did not say its external address";
                else if (!IsPublic(external)) result.Error = $"the gateway's own address {result.ExternalIp} is private (another router or carrier NAT is in the way)";
                else result.Reachable = true;
            }
            catch (Exception e) { result.Error = e.GetType().Name + ": " + e.Message; }
            return Done(result, clock);
        }

        private UpnpResult Done(UpnpResult r, Stopwatch clock) { r.Seconds = clock.Elapsed.TotalSeconds; Last = r; return r; }

        /// <summary>Remove the mapping this mapper made, if any. Never throws; bounded by <paramref name="timeoutMs"/>.</summary>
        public bool Unmap(int timeoutMs = 2000)
        {
            if (!Mapped || ControlUrl == null) return false;
            try
            {
                var r = Soap("DeletePortMapping", $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{MappedPort}</NewExternalPort><NewProtocol>UDP</NewProtocol>", timeoutMs);
                Mapped = false;
                return r.ok;
            }
            catch (Exception) { Mapped = false; return false; }
        }

        private (bool ok, int code, string error) AddMapping(ushort port, string localIp, int lease, int timeoutMs)
        {
            var args = $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{port}</NewExternalPort><NewProtocol>UDP</NewProtocol>" +
                       $"<NewInternalPort>{port}</NewInternalPort><NewInternalClient>{localIp}</NewInternalClient><NewEnabled>1</NewEnabled>" +
                       $"<NewPortMappingDescription>{Escape(Description)}</NewPortMappingDescription><NewLeaseDuration>{lease}</NewLeaseDuration>";
            var r = Soap("AddPortMapping", args, timeoutMs);
            return (r.ok, r.code, r.error);
        }

        // ---------------- SSDP ----------------

        /// <summary>Search for a gateway; returns the description URLs that answered (the first answer ends the wait, after a short grace for others).</summary>
        private List<string> Discover(int windowMs, Stopwatch clock)
        {
            var found = new List<string>();
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            // Windows: a silent target's ICMP "port unreachable" must not turn the next receive into a connection reset.
            try { udp.Client.IOControl(unchecked((int)0x9800000C), new byte[] { 0, 0, 0, 0 }, null); } catch (Exception) { }
            var target = SsdpTarget;
            void Search()
            {
                foreach (var st in SearchTargets)
                {
                    var msg = Encoding.ASCII.GetBytes($"M-SEARCH * HTTP/1.1\r\nHOST: {SsdpMulticast.Address}:{SsdpMulticast.Port}\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: {st}\r\n\r\n");
                    try { udp.Send(msg, msg.Length, target); } catch (SocketException) { }
                }
            }
            Search();
            long start = clock.ElapsedMilliseconds, resent = -1, firstAt = -1;
            var buffer = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                long now = clock.ElapsedMilliseconds;
                if (resent < 0 && now - start > 400) { resent = now; Search(); }   // SSDP is UDP: ask twice
                long until = firstAt >= 0 ? Math.Min(start + windowMs, firstAt + 150) : start + windowMs;
                int wait = (int)(until - now);
                if (wait <= 0) break;
                udp.Client.ReceiveTimeout = Math.Min(wait, resent < 0 ? 400 : wait);
                byte[] data;
                try { data = udp.Receive(ref buffer); }
                catch (SocketException) { continue; }   // timeout (or an ICMP unreachable from a silent test target)
                var location = Header(Encoding.ASCII.GetString(data), "LOCATION");
                if (location != null && Uri.TryCreate(location.Trim(), UriKind.Absolute, out _) && !found.Contains(location.Trim()))
                {
                    found.Add(location.Trim());
                    if (firstAt < 0) firstAt = clock.ElapsedMilliseconds;
                }
            }
            return found;
        }

        private static string Header(string response, string name)
        {
            foreach (var line in response.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && line.Substring(0, colon).Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return line.Substring(colon + 1).Trim();
            }
            return null;
        }

        // ---------------- description and SOAP ----------------

        /// <summary>The WANIPConnection (preferred) or WANPPPConnection control URL in a device description, made absolute.</summary>
        public static (string control, string service) FindService(string location, string xml)
        {
            if (string.IsNullOrEmpty(xml)) return (null, null);
            string urlBase = Element(xml, "URLBase");
            var baseUri = new Uri(string.IsNullOrWhiteSpace(urlBase) ? location : urlBase.Trim());
            (string, string) ppp = (null, null);
            foreach (Match m in Regex.Matches(xml, @"<(?:\w+:)?service>(.*?)</(?:\w+:)?service>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                string type = Element(m.Groups[1].Value, "serviceType"), control = Element(m.Groups[1].Value, "controlURL");
                if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(control)) continue;
                var abs = new Uri(baseUri, control.Trim()).ToString();
                if (type.Contains(":WANIPConnection:")) return (abs, type.Trim());
                if (type.Contains(":WANPPPConnection:") && ppp.Item1 == null) ppp = (abs, type.Trim());
            }
            return ppp;
        }

        private (bool ok, int code, string error, string body) Soap(string action, string args, int timeoutMs)
        {
            if (timeoutMs <= 0) return (false, 0, "timed out", null);
            string body = "<?xml version=\"1.0\"?>\r\n<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                          $"<s:Body><u:{action} xmlns:u=\"{ServiceType}\">{args}</u:{action}></s:Body></s:Envelope>\r\n";
            var (status, text) = Http("POST", ControlUrl, body, $"\"{ServiceType}#{action}\"", timeoutMs);
            if (status == 200) return (true, 0, null, text);
            int.TryParse(Element(text ?? "", "errorCode"), out int code);
            return (false, code, Element(text ?? "", "errorDescription") ?? ("HTTP " + status), text);
        }

        /// <summary>One HTTP request, no proxy, bounded by <paramref name="timeoutMs"/>. Returns the status and the body (an error response's body too).</summary>
        private static (int status, string body) Http(string method, string url, string body, string soapAction, int timeoutMs)
        {
            if (timeoutMs <= 0) throw new TimeoutException("out of time");
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method; req.Proxy = null; req.Timeout = timeoutMs; req.ReadWriteTimeout = timeoutMs; req.KeepAlive = false;
            req.UserAgent = "DungeonGame UPnP/1.0";
            if (body != null)
            {
                req.ContentType = "text/xml; charset=\"utf-8\"";
                if (soapAction != null) req.Headers["SOAPAction"] = soapAction;
                var bytes = Encoding.UTF8.GetBytes(body);
                req.ContentLength = bytes.Length;
                using var s = req.GetRequestStream();
                s.Write(bytes, 0, bytes.Length);
            }
            try
            {
                using var resp = (HttpWebResponse)req.GetResponse();
                using var r = new StreamReader(resp.GetResponseStream());
                return ((int)resp.StatusCode, r.ReadToEnd());
            }
            catch (WebException e) when (e.Response is HttpWebResponse err)
            {
                using (err) using (var r = new StreamReader(err.GetResponseStream())) return ((int)err.StatusCode, r.ReadToEnd());
            }
        }

        private static string Element(string xml, string name)
        {
            var m = Regex.Match(xml, $@"<(?:\w+:)?{name}(?:\s[^>]*)?>(.*?)</(?:\w+:)?{name}>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value.Trim()) : null;
        }

        private static string Escape(string s) => WebUtility.HtmlEncode(s ?? "");

        // ---------------- addresses ----------------

        /// <summary>This machine's address on the interface that reaches <paramref name="host"/> (a connected UDP socket sends nothing).</summary>
        public static string LocalAddressToward(string host)
        {
            try
            {
                if (!IPAddress.TryParse(host, out var ip)) { var all = Dns.GetHostAddresses(host); if (all.Length == 0) return null; ip = all[0]; }
                using var s = new Socket(ip.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                s.Connect(new IPEndPoint(ip, 1900));
                return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
            }
            catch (Exception) { return null; }
        }

        /// <summary>This machine's LAN address (the interface with the default route), or null.</summary>
        public static string LanAddress() => LocalAddressToward("192.0.2.1");   // TEST-NET-1: routed by the default route, never contacted

        /// <summary>A public IPv4 or IPv6 address: not private, carrier-grade NAT, loopback, link-local or unspecified.</summary>
        public static bool IsPublic(IPAddress ip)
        {
            if (ip == null || IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;
            if (ip.AddressFamily == AddressFamily.InterNetworkV6) return !ip.IsIPv6LinkLocal && !ip.IsIPv6SiteLocal && (ip.GetAddressBytes()[0] & 0xFE) != 0xFC;
            var b = ip.GetAddressBytes();
            if (b[0] == 10 || b[0] == 0 || b[0] >= 224) return false;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
            if (b[0] == 192 && b[1] == 168) return false;
            if (b[0] == 169 && b[1] == 254) return false;
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;   // carrier-grade NAT
            return true;
        }
    }
}
