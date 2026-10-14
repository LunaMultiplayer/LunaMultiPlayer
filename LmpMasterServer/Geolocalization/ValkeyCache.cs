
using Valkey.Glide;
using static Valkey.Glide.ConnectionConfiguration;
using LmpMasterServer.Log;
using System;
using System.Net;
using System.Threading.Tasks;
using Valkey.Glide.Commands.Options;

namespace LmpMasterServer.Geolocalization
{
    internal class ValkeyCache : IGeolocalization
    {

        private static GlideClient client;
        private static readonly Random random = Random.Shared;

        static ValkeyCache()
        {
            var address = Environment.GetEnvironmentVariable("LMP_VALKEY_ADDRESS");
            var port = Environment.GetEnvironmentVariable("LMP_VALKEY_PORT");
            if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(port))
                return;

            if (!int.TryParse(port, out var portNumber))
            {
                LunaLog.Error($"Invalid port number for Valkey: {port}");
                return;
            }

            var tls = bool.TryParse(Environment.GetEnvironmentVariable("LMP_VALKEY_TLS"), out var tlsValue) && tlsValue;
            var username = Environment.GetEnvironmentVariable("LMP_VALKEY_USERNAME");
            var password = Environment.GetEnvironmentVariable("LMP_VALKEY_PASSWORD");

            ServerCredentials credentials = null;
            if (!string.IsNullOrEmpty(password))
            {
                username = username == "" ? null : username; // Set empty username to null to let the library fall back to default username
                credentials = new ServerCredentials(username, password);
            }

            var builder = new StandaloneClientConfigurationBuilder()
                .WithAddress(address, (ushort)portNumber)
                .WithTls(tls)
                .WithClientName("LMPMasterServer");

            if (credentials != null)
            {
                builder.WithCredentials(credentials);
            }

            var config = builder.Build();

            // CreateClient() is async but we are in a static constructor, so we can't await it.
            // Run it in a background task and
            _ = Task.Run(async () =>
            {
                try
                {
                    client = await GlideClient.CreateClient(config);
                    LunaLog.Debug($"Valkey client setup successful");
                }
                catch (Exception ex)
                {
                    LunaLog.Error($"Error setting up Valkey client: {ex.Message}");
                }
            });

        }

        public static async Task<string> GetCountryAsync(IPEndPoint externalEndpoint)
        {
            if (client == null)
                return null;
            try
            {
                var country = await client.GetAsync("lmp:geolocalization:" + externalEndpoint.Address);
                if (country == ValkeyValue.Null || country == ValkeyValue.EmptyString)
                    return null;
                return country.ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static async Task SetCountryAsync(IPEndPoint externalEndpoint, string country)
        {
            if (client == null)
                return;

            var expiryOptions = SetExpiryOptions.ExpireIn(TimeSpan.FromDays(14) + TimeSpan.FromHours(random.Next(-12, 12))); // Randomize expiry a bit to avoid thundering herd on expiry
            await client.SetAsync("lmp:geolocalization:" + externalEndpoint.Address, country, expiryOptions);
        }
    }
}
