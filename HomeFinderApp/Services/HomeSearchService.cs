using Azure.AI.OpenAI;
using OpenAI.Chat;
using System.Text.Json;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.WebUtilities;

using HomeFinderApp.Models;
using Azure;

namespace HomeFinderApp.Services
{
    public class HomeSearchService : IHomeSearchService
    {
        private readonly ElasticsearchClient _es;
        private readonly AzureOpenAIClient _azureOpenAIClient;
        private readonly string _azureMapsUrl;
        private readonly string _azureMapsApiKey;
        private readonly HttpClient _httpClient;

        public HomeSearchService(
            AzureOpenAIClient azureClient,
            ElasticsearchClient es,
            IConfiguration configuration,
            HttpClient httpClient)
        {             
            _azureOpenAIClient = azureClient;  
            _es = es;
            _httpClient = new HttpClient();
            
            // Retrieve the Azure Maps settings from appsettings.json
            _azureMapsUrl = configuration["AzureMapsSettings:Url"] 
                ?? throw new InvalidOperationException("Azure Maps URL is missing.");
            _azureMapsApiKey = configuration["AzureMapsSettings:ApiKey"] 
                ?? throw new InvalidOperationException("Azure Maps API key is missing.");
        }

        public async Task<List<HomeResult>> SearchHomesAsync(string query)
        {   
            // 1) Create a list of messages to send to the model
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are an assistant that only provides home finder recommendations " +
                    "based on the search results retrieved from Elasticsearch. " +
                    "Do not make up information or answer based on assumptions. " +
                    "Only use the provided data to respond to the user's queries." +
                    "Don't make assumptions about what values to use with functions. Ask for clarification if a user request is ambiguous." +
                    "Provide details about the homes in valid JSON format, as one-line strings, without any markdown formatting or triple backticks. " +
                    "Do not wrap the output in json or , and do not include line breaks. " +
                    "Seperate each home json object by comma and newline " +
                    "enclose each json object with <home></home> " +
                    "for features, add a comma per feature such as Central air Garage Carpet Flooring Central Air Cooling this converted to Central air, Garage, Carpet Flooring, Central Air Cooling"
                ),
                new UserChatMessage(query)
            };

            // 2) Define each tool as a ChatTool
            var extractTool = ChatTool.CreateFunctionTool(
                functionName: "extract_home_search_parameters",
                functionDescription: "Extract search parameters for finding homes (excluding the query itself).",
                functionParameters: BinaryData.FromString(
                    JsonSerializer.Serialize(new
                    {
                        type = "object",
                        properties = new
                        {
                            query        = new { type = "string", description = "the full input query" },
                            distance     = new { type = "string", description = "The search radius (e.g., 500m)" },
                            bedrooms     = new { type = "number", description = "Number of bedrooms" },
                            bathrooms    = new { type = "number", description = "Number of bathrooms" },
                            tax          = new { type = "number", description = "Tax amount without $ or commas" },
                            maintenance  = new { type = "number", description = "HOA fees without $ or commas" },
                            location     = new { type = "string", description = "Location name to geocode" },
                            square_footage = new { type = "number", description = "Square footage without commas" },
                            home_price   = new { type = "number", description = "Home price without $ or commas" },
                            feature      = new { type = "string", description = "Delimited features, e.g., *pool*garage*" }
                        },
                        required = new[] { "query", "feature" }
                    })
                )
            );

            var geocodeTool = ChatTool.CreateFunctionTool(
                functionName: "geocode_location",
                functionDescription: "Resolve a location to its latitude and longitude.",
                functionParameters: BinaryData.FromString(
                    JsonSerializer.Serialize(new
                    {
                        type       = "object",
                        properties = new { location = new { type = "string", description = "Location name to geocode" } },
                        required   = new[] { "location" }
                    })
                )
            );

            var queryEsTool = ChatTool.CreateFunctionTool(
                functionName: "query_elasticsearch",
                functionDescription: "Query Elasticsearch for accommodations based on parameters.",
                functionParameters: BinaryData.FromString(
                    JsonSerializer.Serialize(new
                    {
                        type = "object",
                        properties = new
                        {
                            query          = new { type = "string", description = "The original search query (e.g., 'homes near Belongil Beach')." },
                            latitude       = new { type = "number", description = "Latitude of the location." },
                            longitude      = new { type = "number", description = "Longitude of the location." },
                            location       = new { type = "string", description = "Location mentioned in the query (e.g., Belongil Beach, The woodlands texas)." },
                            distance       = new { type = "string", description = "Search radius.  Miles should be abbreviated as mi and kilometers as km" },
                            maintenance    = new { type = "number", description = "maintenance fees or HOA fees.  Convert text representation of numbers into numeric" },
                            tax            = new { type = "number", description = "Tax amount.  Convert text representation of numbers into numeric without $ or commas. If the query supplies $10,000 then parse it as 10000" },
                            bedrooms       = new { type = "number", description = "The number of bedrooms a home may have (e.g., 2, 3, 4).  Convert text representation of numbers into numeric" },
                            bathrooms      = new { type = "number", description = "The number of bathrooms a home may have (e.g., 2, 2.5, 3).  Convert text representation of numbers into numeric" },
                            square_footage = new { type = "number", description = "Sqaure footage of home (e.g., 1200, 15000). No commas just the number. If the query supplies 5,000 then parse it as 5000" },
                            home_price     = new { type = "number", description = "The price of the home for sale without $ or commas. If the query supplies $100,000 then parse it as 100000" },
                            feature        = new { type = "string", description = "home features, amenities, or descriptive terms (e.g., 2 car garage, pool, gym, modern, luxurious). This can include multiple options.  Each feature option must be enclosed with *.  For example pool and updated kitchen should be formated to *pool*updated kitchen* . Single features such as pool shoudl be encapusalted with *pool*" }
                        },
                        required = new[] { "query" }
                    })
                )
            );

            // 3) Configure your options to include those tools
            var options = new ChatCompletionOptions
            {
                // register your functions-as-tools
                Tools = { extractTool, geocodeTool, queryEsTool },

                // let the model decide if/which tool to call
                ToolChoice = ChatToolChoice.CreateAutoChoice()
            };
            string resultJson = "{}";
            var parameters = new Dictionary<string, object>();
            
            while (true)
            {
                // 1) Send the chat & let it pick a tool
                ChatClient chatClient = _azureOpenAIClient.GetChatClient("gpt-4o");
                var response = await chatClient.CompleteChatAsync(messages, options);
                var completion = response.Value;
                messages.Add(new AssistantChatMessage(completion));
                
                if (completion.FinishReason == ChatFinishReason.ToolCalls)
                {
                    foreach (var call in completion.ToolCalls)
                    {
                        string toolName = call.FunctionName;
                        string argsJson = call.FunctionArguments.ToString();
                        string toolCallId = call.Id;
                        
                        Console.WriteLine($"▶️  Calling {toolName}: {argsJson}");

                        var argsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(argsJson);
                        if (argsDict != null)
                            foreach (var kv in argsDict)
                                parameters[kv.Key] = kv.Value;

                        switch (toolName)
                        {
                            case "extract_home_search_parameters":
                                resultJson = HandleExtractHomeSearchParameters(argsJson);
                                break;
                            case "geocode_location":
                                resultJson = await HandleGeocodeLocationAsync(argsJson);
                                break;
                            case "query_elasticsearch":
                                if (!parameters.ContainsKey("query"))
                                    throw new InvalidOperationException("'query' is required before calling Elasticsearch.");
                                // pass the **entire**, updated parameter set into your ES query
                                resultJson     = await HandleQueryElasticsearchAsync(argsJson);
                                break;
                            default:
                                throw new InvalidOperationException($"Unknown tool: {toolName}");
                        }

                        Console.WriteLine($"◀️  {toolName} returned: {resultJson}");
                        
                        // 2) merge the tool’s JSON response back into parameters
                        var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(resultJson);
                        if (resultDict != null)
                        {
                            foreach (var kv in resultDict)
                                parameters[kv.Key] = kv.Value;
                        }

                        // 3) inject the tool’s output back into the message list
                        messages.Add(new ToolChatMessage(call.Id, resultJson));
                    }
                    continue;
                }

                break;
            }

            // 6) Once we get here, the last `choice` is the final assistant response
           
            var homes = ParseHomeResultsFrom(resultJson);
            return homes;
        }

        private string HandleExtractHomeSearchParameters(string argsJson)
        {
            Console.WriteLine("HandleExtractHomeSearchParameters: " + argsJson);
            // 1) Parse the incoming JSON into a dictionary of JsonElements
            var incoming = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson)
                        ?? throw new InvalidOperationException("Invalid arguments JSON");

            // 2) Convert to a Dictionary<string, object> so we can inspect & mutate easily
            var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in incoming)
            {
                switch (kv.Value.ValueKind)
                {
                    case JsonValueKind.Number:
                        if (kv.Value.TryGetDecimal(out var dec))
                            parameters[kv.Key] = dec;
                        break;
                    case JsonValueKind.String:
                        parameters[kv.Key] = kv.Value.GetString()!;
                        break;
                    default:
                        // preserve any other JSON value as raw text
                        parameters[kv.Key] = kv.Value.GetRawText();
                        break;
                }
            }

            // 3) If we have lat & lon but no distance, set a default
            if (parameters.ContainsKey("latitude") 
            && parameters.ContainsKey("longitude") 
            && !parameters.ContainsKey("distance"))
            {
                parameters["distance"] = "5000m";
            }

            // 4) Return it all as a JSON string (this becomes the function’s “tool” output)
            Console.WriteLine("Extracted Parameters: " + JsonSerializer.Serialize(parameters));
            return JsonSerializer.Serialize(parameters);
        }

        private async Task<string> HandleGeocodeLocationAsync(string argsJson)
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

        private async Task<string> HandleQueryElasticsearchAsync(string argsJson)
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

                    Console.WriteLine("✅ Elasticsearch query successful.");
                    Console.WriteLine("Response:");
                    Console.WriteLine(stringResp.Body);
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
            public decimal? HomePrice     { get; set; }
            public int?    SquareFootage  { get; set; }
        }
    //     private static List<HomeResult> ParseHomeResultsFrom(string resultJson)
    //     {
    //         Console.WriteLine("ParseHomeResultsFrom: " + resultJson);
    //         var results = new List<HomeResult>();
    //         var raw = resultJson.Trim();

    //         // 1) Extract each JSON snippet between <home>…</home>
    //         var matches = Regex.Matches(raw, "<home>(.*?)</home>", RegexOptions.Singleline);
    //         foreach (Match match in matches)
    //         {
    //             var jsonBlob = match.Groups[1].Value;

    //             // 2) Deserialize into a dict of JsonElements
    //             var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonBlob)
    //                     ?? new Dictionary<string, JsonElement>();

    //             // 3) Flatten any single-element arrays
    //             var flat = new Dictionary<string, JsonElement>();
    //             foreach (var kv in dict)
    //             {
    //                 if (kv.Value.ValueKind == JsonValueKind.Array && kv.Value.GetArrayLength() == 1)
    //                     flat[kv.Key] = kv.Value[0];
    //                 else
    //                     flat[kv.Key] = kv.Value;
    //             }

    //             // 4) Map into HomeResult
    //             var hr = new HomeResult
    //             {
    //                 Title = flat.TryGetValue("title", out var t) && t.ValueKind == JsonValueKind.String
    //                         ? t.GetString()!
    //                         : ""
    //             };

    //             static void SetDecimal(string key, Dictionary<string, JsonElement> src, Action<decimal> setter)
    //             {
    //                 if (src.TryGetValue(key, out var el))
    //                 {
    //                     if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
    //                         setter(d);
    //                     else if (el.ValueKind == JsonValueKind.String && 
    //                             decimal.TryParse(el.GetString(), out var d2))
    //                         setter(d2);
    //                 }
    //             }

    //             static void SetInt(string key, Dictionary<string, JsonElement> src, Action<int> setter)
    //             {
    //                 if (src.TryGetValue(key, out var el))
    //                 {
    //                     if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i))
    //                         setter(i);
    //                     else if (el.ValueKind == JsonValueKind.String &&
    //                             int.TryParse(el.GetString(), out var i2))
    //                         setter(i2);
    //                 }
    //             }

    //             SetDecimal("home-price",          flat, v => hr.HomePrice      = v);
    //             SetDecimal("number-of-bedrooms",  flat, v => hr.Bedrooms       = v);
    //             SetDecimal("number-of-bathrooms", flat, v => hr.Bathrooms      = v);
    //             SetInt    ("square-footage",      flat, v => hr.SquareFootage = v);
    //             SetDecimal("annual-tax",          flat, v => hr.AnnualTax      = v);
    //             SetDecimal("maintenance-fee",     flat, v => hr.MaintenanceFee = v);

    //             // 5) Parse features (comma-separated string, possibly wrapped in a single-element array)
    //             var features = new List<string>();
    //             if (flat.TryGetValue("property-features", out var feats))
    //             {
    //                 string? rawFeatString = null;
    //                 if (feats.ValueKind == JsonValueKind.Array && feats.GetArrayLength() == 1)
    //                     rawFeatString = feats[0].GetString();
    //                 else if (feats.ValueKind == JsonValueKind.String)
    //                     rawFeatString = feats.GetString();

    //                 if (!string.IsNullOrWhiteSpace(rawFeatString))
    //                 {
    //                     features = rawFeatString
    //                         .Split(',', StringSplitOptions.RemoveEmptyEntries)
    //                         .Select(f => f.Trim())
    //                         .Where(f => f.Length > 0)
    //                         .ToList();
    //                 }
    //             }

    //             hr.Features = features;
    //             results.Add(hr);
    //         }
    //         Console.WriteLine("Parsed results: " + JsonSerializer.Serialize(results));
    //         return results;
    //     }

    // }

        private static List<HomeResult> ParseHomeResultsFrom(string resultJson)
        {
            Console.WriteLine("ParseHomeResultsFrom: " + resultJson);
            var results = new List<HomeResult>();

            try
            {
                // Parse the JSON response
                var document = JsonDocument.Parse(resultJson);

                // Navigate to the "hits" array
                if (document.RootElement.TryGetProperty("hits", out var hitsElement) &&
                    hitsElement.TryGetProperty("hits", out var hitsArray) &&
                    hitsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var hit in hitsArray.EnumerateArray())
                    {
                        if (hit.TryGetProperty("fields", out var fields))
                        {
                            var homeResult = new HomeResult();

                            // Extract and map fields
                            if (fields.TryGetProperty("title", out var titleElement) &&
                                titleElement.ValueKind == JsonValueKind.Array &&
                                titleElement.GetArrayLength() > 0)
                            {
                                homeResult.Title = titleElement[0].GetString() ?? string.Empty;
                            }

                            if (fields.TryGetProperty("home-price", out var homePriceElement) &&
                                homePriceElement.ValueKind == JsonValueKind.Array &&
                                homePriceElement.GetArrayLength() > 0 &&
                                homePriceElement[0].TryGetDecimal(out var homePrice))
                            {
                                homeResult.HomePrice = homePrice;
                            }

                            if (fields.TryGetProperty("number-of-bedrooms", out var bedroomsElement) &&
                                bedroomsElement.ValueKind == JsonValueKind.Array &&
                                bedroomsElement.GetArrayLength() > 0 &&
                                bedroomsElement[0].TryGetDecimal(out var bedrooms))
                            {
                                homeResult.Bedrooms = bedrooms;
                            }

                            if (fields.TryGetProperty("number-of-bathrooms", out var bathroomsElement) &&
                                bathroomsElement.ValueKind == JsonValueKind.Array &&
                                bathroomsElement.GetArrayLength() > 0 &&
                                bathroomsElement[0].TryGetDecimal(out var bathrooms))
                            {
                                homeResult.Bathrooms = bathrooms;
                            }

                            if (fields.TryGetProperty("square-footage", out var squareFootageElement) &&
                                squareFootageElement.ValueKind == JsonValueKind.Array &&
                                squareFootageElement.GetArrayLength() > 0 &&
                                squareFootageElement[0].TryGetInt32(out var squareFootage))
                            {
                                homeResult.SquareFootage = squareFootage;
                            }

                            if (fields.TryGetProperty("annual-tax", out var annualTaxElement) &&
                                annualTaxElement.ValueKind == JsonValueKind.Array &&
                                annualTaxElement.GetArrayLength() > 0 &&
                                annualTaxElement[0].TryGetDecimal(out var annualTax))
                            {
                                homeResult.AnnualTax = annualTax;
                            }

                            if (fields.TryGetProperty("maintenance-fee", out var maintenanceFeeElement) &&
                                maintenanceFeeElement.ValueKind == JsonValueKind.Array &&
                                maintenanceFeeElement.GetArrayLength() > 0 &&
                                maintenanceFeeElement[0].TryGetDecimal(out var maintenanceFee))
                            {
                                homeResult.MaintenanceFee = maintenanceFee;
                            }

                            if (fields.TryGetProperty("property-features", out var featuresElement) &&
                                featuresElement.ValueKind == JsonValueKind.Array &&
                                featuresElement.GetArrayLength() > 0)
                            {
                                var rawFeatures = featuresElement[0].GetString();
                                if (!string.IsNullOrWhiteSpace(rawFeatures))
                                {
                                    homeResult.Features = rawFeatures
                                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                        .Select(f => f.Trim())
                                        .ToList();
                                }
                            }

                            results.Add(homeResult);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error parsing home results: " + ex.Message);
            }

            Console.WriteLine("Parsed results: " + JsonSerializer.Serialize(results));
            return results;
        }
    }
}