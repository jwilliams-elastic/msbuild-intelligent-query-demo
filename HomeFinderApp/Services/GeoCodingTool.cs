using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace HomeFinderApp.Services
{
    public class GeoCodingTool : IGeocodingTool
    {
        private readonly string _azureMapsUrl;
        private readonly string _azureMapsApiKey;
        private readonly HttpClient _httpClient;

        public GeoCodingTool(
            IConfiguration configuration)
        {             
            _httpClient = new HttpClient();
            
            // Retrieve the Azure Maps settings from appsettings.json
            _azureMapsUrl = configuration["AzureMapsSettings:Url"] 
                ?? throw new InvalidOperationException("Azure Maps URL is missing.");
            _azureMapsApiKey = configuration["AzureMapsSettings:ApiKey"] 
                ?? throw new InvalidOperationException("Azure Maps API key is missing.");
        }
        public async Task<string> GetGeocode(string argsJson)
        {
             // 1) Parse arguments JSON
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson)
                    ?? throw new ArgumentException("Invalid arguments JSON", nameof(argsJson));

            if (!args.TryGetValue("location", out var locElem) 
                || locElem.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException("Missing or invalid 'location' parameter.");
            }
            string location = locElem.GetString()!;

            // 2) Build the request URL with query parameters
            var queryParams = new Dictionary<string, string?>
            {
                ["subscription-key"] = _azureMapsApiKey,
                ["api-version"]      = "2025-01-01",
                ["query"]            = location,
                ["limit"]            = "1",
                ["countrySet"]       = "US",
                ["language"]         = "en-US"
            };
            string requestUrl = QueryHelpers.AddQueryString(_azureMapsUrl, queryParams);

            // 3) Call Azure Maps
            var response = await _httpClient.GetAsync(requestUrl);
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                using var doc    = await JsonDocument.ParseAsync(stream);

                // 4) Extract coordinates from GeoJSON
                if (doc.RootElement.TryGetProperty("features", out var features)
                    && features.ValueKind == JsonValueKind.Array
                    && features.GetArrayLength() > 0)
                {
                    var coords = features[0]
                        .GetProperty("geometry")
                        .GetProperty("coordinates");
                    decimal lon = coords[0].GetDecimal();
                    decimal lat = coords[1].GetDecimal();

                    // 5) Return as JSON
                    var result = new Dictionary<string, decimal>
                    {
                        ["latitude"]  = lat,
                        ["longitude"] = lon
                    };
                    return JsonSerializer.Serialize(result);
                }
            }
            return JsonSerializer.Serialize(new { error = "Unable to geocode location." });
        }
    }
}