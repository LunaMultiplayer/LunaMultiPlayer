using LmpCommon.Collection;
using LmpCommon.Time;
using LmpGlobal;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

#pragma warning disable SYSLIB0014

namespace LmpCommon.RepoRetrievers
{
    /// <summary>
    /// This class retrieves the banned ips stored in <see cref="RepoConstants.BannedIpListUrl"/>
    /// </summary>
    public static class BannedIpsRetriever
    {
        private static readonly ConcurrentHashSet<IPAddress> PrivBannedIPs = new ConcurrentHashSet<IPAddress>();
        private static readonly object RefreshLock = new object();

        private static ConcurrentHashSet<IPAddress> BannedIps
        {
            get
            {
                var now = LunaComputerTime.UtcNow;
                if (_lastRequestTime == DateTime.MinValue)
                {
                    //Run synchronously if it's the first time
                    RefreshBannedIps();
                }
                else if (now - _lastRequestTime > MaxRequestInterval)
                {
                    // Update the timestamp eagerly so concurrent callers don't schedule
                    // a flurry of background refreshes before the first one completes.
                    _lastRequestTime = now;
                    Task.Run(RefreshBannedIps);
                }

                return PrivBannedIPs;
            }
        }

        private static readonly TimeSpan MaxRequestInterval = TimeSpan.FromMinutes(10);
        private static DateTime _lastRequestTime = DateTime.MinValue;

        public static bool IsBanned(IPEndPoint endpoint)
        {
            return BannedIps.Contains(endpoint.Address);
        }

        /// <summary>
        /// Kick off an asynchronous refresh so the banned-ip cache is populated as early
        /// as possible. Safe to call before any <see cref="IsBanned"/> call; subsequent
        /// refreshes are coalesced via <see cref="MaxRequestInterval"/>.
        /// </summary>
        public static void Prewarm()
        {
            Task.Run(RefreshBannedIps);
        }

        /// <summary>
        /// Download the banned ips list from <see cref="RepoConstants.BannedIpListUrl"/>
        /// and store the correctly formed entries in <see cref="PrivBannedIPs"/>.
        /// Concurrent calls are serialized; calls that arrive while a recent refresh is
        /// still considered fresh are coalesced.
        /// </summary>
        private static void RefreshBannedIps()
        {
            lock (RefreshLock)
            {
                // Another caller already refreshed recently; skip to avoid stomping the set.
                if (_lastRequestTime != DateTime.MinValue && LunaComputerTime.UtcNow - _lastRequestTime < MaxRequestInterval)
                    return;

                try
                {
                    ServicePointManager.ServerCertificateValidationCallback = GithubCertification.MyRemoteCertificateValidationCallback;
                    using (var client = new WebClient())
                    using (var stream = client.OpenRead(RepoConstants.BannedIpListUrl))
                    using (var reader = new StreamReader(stream))
                    {
                        var content = reader.ReadToEnd();
                        var ips = content
                            .Split('\n')
                            .Select(s => s.Trim()) // Trim whitespace and \r in case of Windows line breaks
                            .Where(s => !s.StartsWith("#") && !string.IsNullOrWhiteSpace(s))
                            .ToArray();

                        PrivBannedIPs.Clear();

                        foreach (var ip in ips)
                        {
                            if (IPAddress.TryParse(ip, out var ipAddr))
                            {
                                PrivBannedIPs.Add(ipAddr);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    //Ignored
                }

                // Always update so failures fall back to the normal periodic cadence
                // rather than retrying synchronously on every IsBanned call.
                _lastRequestTime = LunaComputerTime.UtcNow;
            }
        }
    }
}
