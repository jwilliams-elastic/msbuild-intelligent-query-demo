using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;

namespace HomeFinderApp.Services
{
    public class SearchTool : ISearchTool
    {
        private readonly ElasticsearchClient _es;

        public SearchTool(
            ElasticsearchClient es)
        {               
            _es = es;
        }
        public async Task<string> Search(string argsJson)
        {
            // 1. Parse the JSON arguments
            var args = JsonSerializer.Deserialize<ElasticsearchArgs>(
                argsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
            if (args == null)
            {
                Console.WriteLine("❌ Invalid arguments JSON.");
                return JsonSerializer.Serialize(new { error = "Invalid arguments JSON." });
            }

            Console.WriteLine("✅ Deserialized args: " + JsonSerializer.Serialize(args));

             // 2. Build cleaned parameters
            var cleanedParams = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(args.Query)) 
                cleanedParams["query"] = args.Query;

            if (args.Latitude.HasValue) 
                cleanedParams["latitude"] = args.Latitude.Value;

            if (args.Longitude.HasValue) 
                cleanedParams["longitude"] = args.Longitude.Value;

            if (!string.IsNullOrWhiteSpace(args.Feature)) 
                cleanedParams["feature"] = args.Feature;

            if (args.Distance != null) 
                cleanedParams["distance"] = args.Distance;
                
            if (args.Bedrooms.HasValue) 
                cleanedParams["bedrooms"] = args.Bedrooms.Value;

            if (args.Bathrooms.HasValue) 
                cleanedParams["bathrooms"] = args.Bathrooms.Value;

            if (args.Home_Price.HasValue) 
                cleanedParams["home_price"] = args.Home_Price.Value;

            if (args.Tax.HasValue) 
                cleanedParams["tax"] = args.Tax.Value;

            if (args.Maintenance.HasValue) 
                cleanedParams["maintenance-fee"] = args.Maintenance.Value;

            if (args.Square_Footage.HasValue)
                cleanedParams["square_footage"] = args.Square_Footage.Value;


            // Debug: log parameters
            Console.WriteLine("Parameters for Elasticsearch:");
            Console.WriteLine(JsonSerializer.Serialize(cleanedParams, new JsonSerializerOptions { WriteIndented = true }));

            var templateId = "properties-search-template";
            var indexName = "properties";
            var maxRetries = 2;
            var retryDelay = 2;  
            // 4. Construct the search_template body
            var queryBody = new
            {
                id     = templateId,
                @params = cleanedParams
            };
            Console.WriteLine("Elasticsearch Query:");
            Console.WriteLine(JsonSerializer.Serialize(queryBody, new JsonSerializerOptions { WriteIndented = true }));

            // 5. Invoke ES with retry logic
            Dictionary<string, object> responseBody = new Dictionary<string, object>();
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // low-level transport call to POST /{INDEX_NAME}/_search/template
                    var pathAndQuery = $"{indexName}/_search/template";
                    var endpoint    = new EndpointPath(Elastic.Transport.HttpMethod.POST, pathAndQuery);
                    var stringResp  = await _es.Transport
                        .RequestAsync<StringResponse>(
                            endpoint,
                            PostData.Serializable(queryBody)
                        )
                        .ConfigureAwait(false);

                    responseBody = JsonSerializer.Deserialize<Dictionary<string, object>>(stringResp.Body) ?? new Dictionary<string, object>();
                    break;
                }
                catch (TransportException ex) when (ex.Message.Contains("missing or invalid credentials"))
                {
                    Console.WriteLine("❌ Authentication failed: missing or invalid credentials.");
                    break;
                }
                catch (TransportException ex) when (
                    ex.Message.Contains("Starting deployment timed out") &&
                    attempt < maxRetries)
                {
                    Console.WriteLine($"⚠️ Model not ready yet (attempt {attempt+1}/{maxRetries}). Retrying...");
                    await Task.Delay(retryDelay).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error while querying Elasticsearch: {ex.Message}");
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            }

            // 6. Log total hits if present
            if (responseBody is not null
                && responseBody.TryGetValue("hits", out var hitsObj)
                && hitsObj is JsonElement hitsElem
                && hitsElem.TryGetProperty("total", out var totalElem)
                && totalElem.TryGetProperty("value", out var valueElem)
                && valueElem.TryGetInt32(out var totalCount))
            {
                Console.WriteLine($"Number of results found: {totalCount}");
            }

            return JsonSerializer.Serialize(responseBody);
        }

        private class ElasticsearchArgs
        {
            public string?  Query          { get; set; }
            public double? Latitude       { get; set; }
            public double? Longitude      { get; set; }
            public string?  Feature        { get; set; }
            public string? Distance       { get; set; }
            public int?    Bedrooms       { get; set; }
            public int?    Bathrooms      { get; set; }
            public decimal? Tax           { get; set; }
            public decimal? Maintenance   { get; set; }
            public decimal? Home_Price     { get; set; }
            public int?    Square_Footage  { get; set; }
        }
    }
}