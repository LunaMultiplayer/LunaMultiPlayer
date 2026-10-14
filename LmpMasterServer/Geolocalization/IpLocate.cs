using System;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace LmpMasterServer.Geolocalization
{
    internal class IpLocate : IGeolocalization
    {
        public static async Task<string> GetCountryAsync(IPEndPoint externalEndpoint)
        {
            try
            {
                var client = GeolocationHttpClient.GetClient();
                var output = JsonNode.Parse(
                    await client.GetStringAsync($"https://www.iplocate.io/api/lookup/{externalEndpoint.Address}"));
                return output?["country_code"]?.GetValue<string>();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
