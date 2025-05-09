//https://learn.microsoft.com/en-us/semantic-kernel/concepts/plugins/adding-native-plugins?pivots=programming-language-csharp
using System.ComponentModel;
using System.Text.Json;
using Elastic.Transport;
using Elastic.Clients.Elasticsearch;
using Microsoft.SemanticKernel;
using HomeFinderApp.Models;

public class ElasticSearchPlugin : IElasticSearchPlugin
{
    private readonly ElasticsearchClient elasticsearchClient;
    private readonly ILogger<ElasticSearchPlugin> logger;
    public ElasticSearchPlugin(ElasticsearchClient elasticsearchClient, ILogger<ElasticSearchPlugin> logger)
    {
        this.elasticsearchClient = elasticsearchClient;
        this.logger = logger;
    }

    [KernelFunction("Query_Elasticsearch")]
    [Description("Query the Elasticsearch database for homes based on parameters.")]
    public async Task<List<HomeResult>> Search(HomeSearchParameters parameters)
    {
        var cleanedParams = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(parameters.Query))
            cleanedParams["query"] = parameters.Query;

        if (parameters.Latitude.HasValue)
            cleanedParams["latitude"] = parameters.Latitude.Value;

        if (parameters.Longitude.HasValue)
            cleanedParams["longitude"] = parameters.Longitude.Value;

        if (!string.IsNullOrWhiteSpace(parameters.Feature))
            cleanedParams["feature"] = parameters.Feature;

        if (parameters.Distance != null)
            cleanedParams["distance"] = parameters.Distance;

        if (parameters.Bedrooms.HasValue)
            cleanedParams["bedrooms"] = parameters.Bedrooms.Value;

        if (parameters.Bathrooms.HasValue)
            cleanedParams["bathrooms"] = parameters.Bathrooms.Value;

        if (parameters.HomePrice.HasValue)
            cleanedParams["home_price"] = parameters.HomePrice.Value;

        if (parameters.Tax.HasValue)
            cleanedParams["tax"] = parameters.Tax.Value;

        if (parameters.Maintenance.HasValue)
            cleanedParams["maintenance"] = parameters.Maintenance.Value;

        if (parameters.SquareFootage.HasValue)
            cleanedParams["square_footage"] = parameters.SquareFootage.Value;
        // Debug: log parameters
        logger.LogInformation($"Params for Elastic:{JsonSerializer.Serialize(cleanedParams, new JsonSerializerOptions { WriteIndented = false })}");
        var templateId = "properties-search-template";
        var indexName = "properties";
        var maxRetries = 2;
        var retryDelay = 2;
        // 4. Construct the search_template body
        var queryBody = new
        {
            id = templateId,
            @params = cleanedParams
        };
        logger.LogInformation($"Elastic query:{JsonSerializer.Serialize(queryBody, new JsonSerializerOptions { WriteIndented = false })}");

        // 5. Invoke ES with retry logic
        var responseBody = string.Empty;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                // low-level transport call to POST /{INDEX_NAME}/_search/template
                var pathAndQuery = $"{indexName}/_search/template";
                var endpoint = new EndpointPath(Elastic.Transport.HttpMethod.POST, pathAndQuery);
                var stringResp = await elasticsearchClient.Transport
                    .RequestAsync<StringResponse>(
                        endpoint,
                        PostData.Serializable(queryBody)
                    )
                    .ConfigureAwait(false);
                responseBody = stringResp.Body;
                logger.LogInformation($"✅ Elastic resp:{responseBody}");
            }
            catch (TransportException ex) when (ex.Message.Contains("missing or invalid credentials"))
            {
                logger.LogError("❌ Authentication failed: missing or invalid credentials.");
                break;
            }
            catch (TransportException ex) when (
                ex.Message.Contains("Starting deployment timed out") &&
                attempt < maxRetries)
            {
                logger.LogWarning($"⚠️ Model not ready yet (attempt {attempt + 1}/{maxRetries}). Retrying...");
                await Task.Delay(retryDelay).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError($"Error while querying Elasticsearch: {ex.Message}");
                throw; // Rethrow the exception to indicate failure
            }
        }

        var results = ParseHomeResultsFrom(responseBody, logger);
        return results;
    }


    private static List<HomeResult> ParseHomeResultsFrom(string responseBody, ILogger logger)
    {
        var results = new List<HomeResult>();

        try
        {
            // Parse the JSON response
            var document = JsonDocument.Parse(responseBody);

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
            logger.LogError("Error parsing home results: " + ex.Message);
        }

        logger.LogInformation("Parsed results: " + JsonSerializer.Serialize(results));
        return results;
    }


}
