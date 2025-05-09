//https://learn.microsoft.com/en-us/semantic-kernel/concepts/plugins/adding-native-plugins?pivots=programming-language-csharp
using System.ComponentModel;
using HomeFinderApp.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using System.Text.Json;

public class GeoCodePlugin : IGeoCodePlugin
{
    private readonly HttpClient httpClient;
    private readonly AzureMapsSettings azureMapsSettings;
    public GeoCodePlugin(HttpClient httpClient, IOptions<AzureMapsSettings> azureMapsSettings)
    {
        this.httpClient = httpClient;
        this.azureMapsSettings = azureMapsSettings.Value;
    }
    [KernelFunction("get_coordinates")]
    [Description("Accepts a location and returns the coordinates of that location.")]
    public async Task<string> GetCoordinates(string location)
    {
        string requestUrl = makeRequest(location);

        var response = await httpClient.GetAsync(requestUrl);
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;
            var features = root.GetProperty("features");
            if (features.GetArrayLength() > 0)
            {
                var geometry = features[0].GetProperty("geometry");
                var coordinates = geometry.GetProperty("coordinates");
                if (coordinates.GetArrayLength() == 2)
                {
                    var lon = coordinates[0].GetDouble();
                    var lat = coordinates[1].GetDouble();
                    var output = new { lon, lat };
                    return JsonSerializer.Serialize(output);
                }
            }
            throw new Exception("No coordinates found in Azure Maps response.");
        }
        throw new Exception($"Error calling Azure Maps: {response.StatusCode}");
    }

    private string makeRequest(string location)
    {
        var queryParams = new Dictionary<string, string?>
        {
            ["subscription-key"] = azureMapsSettings.ApiKey,
            ["api-version"] = "2025-01-01",
            ["query"] = location,
            ["limit"] = "1",
            ["countrySet"] = "US",
            ["language"] = "en-US",
        };
        string requestUrl = QueryHelpers.AddQueryString(azureMapsSettings.Url, queryParams);
        return requestUrl;
    }
}
