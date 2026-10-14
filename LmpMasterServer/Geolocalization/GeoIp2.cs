using MaxMind.GeoIP2;
using LmpMasterServer.Log;
using System;
using System.Net;
using System.Threading.Tasks;

namespace LmpMasterServer.Geolocalization
{
    internal class GeoIp2 : IGeolocalization
    {

        private static WebServiceClient client;

        static GeoIp2()
        {
            var licenseKey = Environment.GetEnvironmentVariable("LMP_GEOIP2_LICENSE_KEY");
            if (string.IsNullOrEmpty(licenseKey))
                return;

            if (!int.TryParse(Environment.GetEnvironmentVariable("LMP_GEOIP2_ACCOUNT_ID"), out var accountId))
                return;

            var clientOptions = (Microsoft.Extensions.Options.IOptions<WebServiceClientOptions>)new WebServiceClientOptions() { AccountId = accountId, LicenseKey = licenseKey };
            client = new WebServiceClient(GeolocationHttpClient.GetClient(), clientOptions);
            LunaLog.Debug($"GeoIP2 client setup successful, using account ID {accountId}");
        }

        public static async Task<string> GetCountryAsync(IPEndPoint externalEndpoint)
        {
            if (client == null)
                return null;
            try
            {
                var country = await client.CountryAsync(externalEndpoint.Address);
                return country.Country.IsoCode;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
