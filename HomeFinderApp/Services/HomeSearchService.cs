using Azure.AI.OpenAI;
using OpenAI.Chat;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using HomeFinderApp.Models;

namespace HomeFinderApp.Services
{
    public class HomeSearchService : IHomeSearchService
    {
        
        private readonly AzureOpenAIClient _azureOpenAIClient;
        private readonly HttpClient _httpClient;
        
        private readonly IParameterExtractionTool _parameterExtractionTool;
        private readonly IGeocodeTool _geocodeTool;
        private readonly ISearchTool _searchTool;

        public HomeSearchService(
            IParameterExtractionTool parameterExtractionTool,
            IGeocodeTool geocodeTool,
            ISearchTool searchTool,
            AzureOpenAIClient azureClient,
            IConfiguration configuration,
            HttpClient httpClient)
        {             
            _parameterExtractionTool = parameterExtractionTool;
            _geocodeTool = geocodeTool;
            _searchTool = searchTool;
            _azureOpenAIClient = azureClient;  
            _httpClient = new HttpClient();
        }

        public async Task<List<HomeResult>> LLMSearchWithTools(string query)
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
                            feature      = new { type = "string", description = "Delimited features, e.g., pool, garage" }
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
                            feature        = new { type = "string", description = "home features, amenities, or descriptive terms (e.g., 2 car garage, pool, gym, modern, luxurious). This can include multiple options.  Each feature option must be enclosed with double quotes. Comma delimit multiple features. For example pool and updated kitchen should be formated to \"pool\", \"updated kitchen\" ." }
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
                                resultJson = await _parameterExtractionTool.ExtractParameters(argsJson);
                                break;
                            case "geocode_location":
                                resultJson = await _geocodeTool.GetGeocode(argsJson);
                                break;
                            case "query_elasticsearch":
                                resultJson     = await _searchTool.Search(argsJson);
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
           
            var homes = ParseHomeResultsFrom(resultJson);
            return homes;
        }
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

                            if (fields.TryGetProperty("property-description", out var propertyDescriptionElement) &&
                                propertyDescriptionElement.ValueKind == JsonValueKind.Array &&
                                propertyDescriptionElement.GetArrayLength() > 0)
                            {
                                homeResult.PropertyDescription = propertyDescriptionElement[0].GetString() ?? string.Empty;
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