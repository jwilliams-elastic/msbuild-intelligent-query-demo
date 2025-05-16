using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Options;
using HomeFinderApp.Models;
using HomeFinderApp.Services;
using Azure;
using Moq;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;

[TestClass]
public class HomeSearchAgentServiceIntegrationTest
{
    private IConfiguration? _configuration;
    private IOptions<AzureMapsSettings>? _azureMapsSettings;
    private AzureOpenAISettings? _openAISettings;
    private HttpClient? _httpClient;
    private GeoCodePlugin? _geoCodePlugin;
    private Azure.AI.OpenAI.AzureOpenAIClient? _azureOpenAIClient;
    private ElasticSearchPlugin? _elasticSearchPlugin;
    private ExtractParametersPlugin? _extractParametersPlugin;
    private Mock<ILogger<HomeSearchAgentService>>? _mockLogger;
    private Mock<FunctionLoggingFilter>? _mockFunctionLoggingFilter;

    [TestInitialize]
    public void Setup()
    {
        _configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        _azureMapsSettings = Options.Create(new AzureMapsSettings
        {
            Url = _configuration["AzureMapsSettings:Url"]!,
            ApiKey = _configuration["AzureMapsSettings:ApiKey"]!
        });
        _httpClient = new HttpClient();
        _geoCodePlugin = new GeoCodePlugin(_httpClient, _azureMapsSettings);
        var mockLogger = new Mock<ILogger<ElasticSearchPlugin>>();
        var clientSettings = new ElasticsearchClientSettings(new Uri(_configuration["ElasticSettings:Url"]!)).Authentication(new ApiKey(_configuration["ElasticSettings:ApiKey"]!));
        _elasticSearchPlugin = new ElasticSearchPlugin(new ElasticsearchClient(clientSettings), mockLogger.Object);

        _openAISettings = new AzureOpenAISettings
        {
            Endpoint = _configuration["AzureOpenAISettings:Endpoint"]!,
            ApiKey = _configuration["AzureOpenAISettings:ApiKey"]!,
            DeploymentName = _configuration["AzureOpenAISettings:DeploymentName"]!
        };

        var credential = new AzureKeyCredential(_openAISettings.ApiKey);
        _azureOpenAIClient = new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(_openAISettings.Endpoint), credential);

        _extractParametersPlugin = new ExtractParametersPlugin();
        _mockLogger = new Mock<ILogger<HomeSearchAgentService>>();
        _mockFunctionLoggingFilter = new Mock<FunctionLoggingFilter>();
    }

    [TestMethod]
    public async Task LLMSearchWithTools_ReturnsExpected_HomesObjects()
    {
        // Arrange
        var service = new HomeSearchAgentService(_geoCodePlugin!, _azureOpenAIClient!, _extractParametersPlugin!, _elasticSearchPlugin!, _mockLogger!.Object, _mockFunctionLoggingFilter!.Object);
        string query = "2 bedroom house 50 miles from Orlando, FL with a pool";

        // Act
        var resultTuple = await service.LLMSearchWithTools(query);
        var result = resultTuple.Results;

        // Assert
        Assert.IsNotNull(result, "Result should not be null");
    }

    [TestMethod]
    public async Task LLMSearchWithTools_ReturnsEmptyList_ForUnknownQuery()
    {
        // Arrange
        var service = new HomeSearchAgentService(_geoCodePlugin!, _azureOpenAIClient!, _extractParametersPlugin!, _elasticSearchPlugin!, _mockLogger!.Object, _mockFunctionLoggingFilter!.Object);
        string query = "castle on the moon";

        // Act
        var resultTuple = await service.LLMSearchWithTools(query);
        var result = resultTuple.Results;

        // Assert
        Assert.IsNotNull(result, "Result should not be null");
        // Optionally, check for empty result
        // Assert.AreEqual(0, result.Count, "Result should be empty for unknown query");
    }

}
