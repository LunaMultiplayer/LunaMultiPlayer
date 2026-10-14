using LmpCommon;
using LmpCommon.Message.Data.MasterServer;
using LmpCommon.Time;
using LmpMasterServer.Dedicated;
using LmpMasterServer.Geolocalization;
using LmpMasterServer.Log;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace LmpMasterServer.Structure
{
    public class Server : ServerInfo
    {
        public static readonly TimeSpan MinCountryCodeRefreshInterval = TimeSpan.FromSeconds(5);
        public static readonly ConcurrentQueue<(long, IPEndPoint)> CountryCodeRefreshQueue = new();

        private static HashSet<string> _countryCodes;

        // For most cultures the LCID is 4096, which can't be mapped to a RegionInfo.
        // Instead use CultureInfo.Name, which is unique.
        private static HashSet<string> CountryCodes => _countryCodes ??= CultureInfo
                           .GetCultures(CultureTypes.SpecificCultures)
                           .Select(culture => new RegionInfo(culture.Name))
                           .Select(ri => ri.TwoLetterISORegionName).Distinct().ToHashSet();

        private static readonly TimeoutConcurrentDictionary<IPAddress, string> AddressCountries =
            new(TimeSpan.FromHours(24).TotalMilliseconds);


        public long LastRegisterTime { get; private set; }

        public void Update(MsRegisterServerMsgData msg, IPEndPoint externalEndpoint)
        {
            InternalEndpoint = msg.InternalEndpoint;
            InternalEndpoint6 = msg.InternalEndpoint6;

            // Due to NAT, non-static IP addresses and roaming the endpoint may change during the lifetime of a server.
            if (!externalEndpoint.Equals(ExternalEndpoint) && !IsLocalIpAddress(externalEndpoint.Address))
            {
                // Known endpoint differs from message source, and it is not a server within this master server's LAN.
                LunaLog.Normal($"ENDPOINT CHANGED: {ExternalEndpoint} to {externalEndpoint}");
                ExternalEndpoint = externalEndpoint;
            }

            LastRegisterTime = LunaNetworkTime.UtcNow.Ticks;
            Cheats = msg.Cheats;
            Password = msg.Password;
            ServerVersion = msg.ServerVersion;
            ServerName = msg.ServerName.Length > 30 ? msg.ServerName.Substring(0, 30) : msg.ServerName;
            Description = msg.Description.Length > 200 ? msg.Description.Substring(0, 200) : msg.Description;
            Website = msg.Website.Length > 60 ? msg.Website.Substring(0, 60) : msg.Website;
            WebsiteText = msg.WebsiteText.Length > 15 ? msg.WebsiteText.Substring(0, 15) : msg.WebsiteText;
            RainbowEffect = msg.RainbowEffect;
            Array.Copy(msg.Color, Color, 3);
            GameMode = msg.GameMode;
            MaxPlayers = msg.MaxPlayers;
            ModControl = msg.ModControl;
            DedicatedServer = DedicatedServerRetriever.IsDedicatedServer(ExternalEndpoint);
            PlayerCount = msg.PlayerCount;
            WarpMode = msg.WarpMode;
            TerrainQuality = msg.TerrainQuality;

            if (!string.IsNullOrEmpty(msg.CountryCode) && CountryCodes.Contains(msg.CountryCode.ToUpper()))
            {
                // Always override with the message value if it's valid
                Country = msg.CountryCode.ToUpper();
            }
            else if (string.IsNullOrEmpty(Country))
            {
                // Only if not yet set, try to fetch from cache or queue for refresh.
                if (AddressCountries.TryGet(externalEndpoint.Address, out var countryCode))
                {
                    Country = countryCode;
                }
                else if (!CountryCodeRefreshQueue.Contains((Id, ExternalEndpoint)))
                {
                    CountryCodeRefreshQueue.Enqueue((Id, ExternalEndpoint));
                }
            }

            if (!Website.Contains("://"))
            {
                Website = "http://" + Website;
            }

            if (string.IsNullOrEmpty(WebsiteText) && !string.IsNullOrEmpty(Website))
            {
                WebsiteText = "URL";
            }
        }

        public Server(MsRegisterServerMsgData msg, IPEndPoint externalEndpoint)
        {
            Id = msg.Id;
            // As the public IP address discovery process (GetOwnExternalIpAddress) is expensive,
            // only do it on the first update after registering a server and hope that it never changes.
            ExternalEndpoint = IsLocalIpAddress(externalEndpoint.Address)
                                ? new IPEndPoint(LunaNetUtils.GetOwnExternalIpAddress(), externalEndpoint.Port)
                                : externalEndpoint;

            Update(msg, externalEndpoint);
        }

        /// <summary> Looks up the country for this IP address at a GeoIP service
        /// and writes it to the Country field.
        /// </summary>
        /// <returns>
        /// A bool indicating whether a request to an external service has been made (true)
        /// or the value could be fetched from cache (false).
        /// </returns>
        public async Task<bool> SetCountryFromEndpointAsync(IPEndPoint externalEndpoint)
        {
            try
            {
                if (!string.IsNullOrEmpty(Country))
                    // Already resolved
                    return false;

                if (AddressCountries.TryGet(externalEndpoint.Address, out var countryCode))
                {
                    Country = countryCode;
                    return false;
                }
                else
                {
                    LunaLog.Normal($"COUNTRY CODE LOOKUP for {externalEndpoint}");
                    Country = await GeoIp2.GetCountryAsync(externalEndpoint);
                    if (string.IsNullOrEmpty(Country))
                        Country = await IpApi.GetCountryAsync(externalEndpoint);

                    if (string.IsNullOrEmpty(Country))
                        Country = await IpLocate.GetCountryAsync(externalEndpoint);

                    if (string.IsNullOrEmpty(Country))
                        LunaLog.Debug($"SetCountryFromEndpoint failed for {externalEndpoint}: No lookup successful");
                    else
                        AddressCountries.TryAdd(externalEndpoint.Address, Country);

                    return true;
                }
            }
            catch (Exception e)
            {
                LunaLog.Warning($"SetCountryFromEndpoint failed for {externalEndpoint}: " + e.Message);
                throw;
            }
        }

        public static bool IsLocalIpAddress(IPAddress host)
        {
            try
            {
                var localIPs = Dns.GetHostAddresses(Dns.GetHostName());

                // test if any host IP equals to any local IP or to localhost
                if (IPAddress.IsLoopback(host)) return true;
                if (localIPs.Any(l => l.Equals(host))) return true;

                /* The private address ranges are defined in RFC1918. They are:
                 * 10.0.0.0 - 10.255.255.255 (10/8 prefix)
                 * 172.16.0.0 - 172.31.255.255 (172.16/12 prefix)
                 * 192.168.0.0 - 192.168.255.255 (192.168/16 prefix)
                 * Documentation prefixes (RFC5737):
                 * 192.0.2.0 - 192.0.2.255 (192.0.2.0/24)
                 * 198.51.100.0 - 198.51.100.255 (198.51.100.0/24)
                 * 203.0.113.0 - 203.0.113.255 (203.0.113.0/24)
                 * CGNAT prefixes (RFC6598):
                 * 100.64.0.0 - 100.127.255.255 (100.64.0.0/10)
                 */

                var bytes = host.GetAddressBytes();
                switch (bytes[0])
                {
                    case 10:
                        return true;
                    case 100:
                        return bytes[1] < 128 && bytes[1] >= 64;
                    case 172:
                        return bytes[1] < 32 && bytes[1] >= 16;
                    case 192:
                        return bytes[1] == 168
                               || bytes[1] == 0 && bytes[2] == 2;
                    case 198:
                        return bytes[1] == 51 && bytes[2] == 100;
                    case 203:
                        return bytes[1] == 0 && bytes[2] == 113;
                    default:
                        return false;
                }
            }
            catch
            {
                // ignored
            }
            return false;
        }
    }
}
