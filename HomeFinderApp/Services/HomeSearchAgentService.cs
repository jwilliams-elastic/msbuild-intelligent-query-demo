using System.Text.Json;
using HomeFinderApp.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.ChatCompletion;
using Azure.AI.OpenAI;

namespace HomeFinderApp.Services
{
    public class HomeSearchAgentService : IHomeSearchService
    {
        private readonly AzureOpenAIClient azureOpenAIClient;
        private readonly IGeoCodePlugin geoCodePlugin;
        private readonly IExtractParametersPlugin extractPamatersPlugin;
        private readonly IElasticSearchPlugin elasticSearchPlugin;
        private readonly ILogger<HomeSearchAgentService>  logger;
        private readonly FunctionLoggingFilter functionLoggingFilter;

        public HomeSearchAgentService(
            IGeoCodePlugin geoCodePlugin,
            AzureOpenAIClient azureOpenAIClient,
            IExtractParametersPlugin extractPamatersPlugin, IElasticSearchPlugin elasticSearchPlugin, ILogger<HomeSearchAgentService> logger, FunctionLoggingFilter functionLoggingFilter)
        {
            this.geoCodePlugin = geoCodePlugin;
            this.azureOpenAIClient = azureOpenAIClient;
            this.extractPamatersPlugin = extractPamatersPlugin;
            this.elasticSearchPlugin = elasticSearchPlugin;
            this.logger = logger;
            this.functionLoggingFilter = functionLoggingFilter;
        }
        public async Task<(List<HomeResult> Results, List<string> ToolInvocations)> LLMSearchWithTools(string query)
        {
            ChatHistoryAgentThread agentThread = new();
            IKernelBuilder builder = Kernel.CreateBuilder();

            builder.AddAzureOpenAIChatCompletion("gpt-4o", azureOpenAIClient);
            Kernel kernel = builder.Build();
            kernel.ImportPluginFromObject(geoCodePlugin, "geocode_location");
            kernel.ImportPluginFromObject(extractPamatersPlugin, "extract_parameters");
            kernel.ImportPluginFromObject(elasticSearchPlugin, "query_elasticsearch");
            functionLoggingFilter.ClearToolInvocations();
            kernel.FunctionInvocationFilters.Add(functionLoggingFilter);
            var instructionsPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "HomeSearchAgentInstructions.txt");
            string instructions = await File.ReadAllTextAsync(instructionsPath);
            ChatCompletionAgent agent =
                new()
                {
                    Name = "PropertySearchAgent",
                    Description = "An agent that helps users find homes based on user queries.",
                    Instructions = instructions,
                    Kernel = kernel,
                    Arguments = new KernelArguments(new AzureOpenAIPromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() })
                };
            var message = new ChatMessageContent(AuthorRole.User, query);
            var responseMessages = string.Empty;
            await foreach (ChatMessageContent response in agent.InvokeAsync(message, agentThread))
            {
                responseMessages += response.Content;
            }
            List<HomeResult> results;
            try
            {
                results = JsonSerializer.Deserialize<List<HomeResult>>(responseMessages!) ?? new List<HomeResult>();
            }
            catch (JsonException ex)
            {
                // logger.LogError($"Failed to deserialize response: {responseMessages}");
                // results = new List<HomeResult>();

                logger.LogError(ex, "Failed to deserialize response: {ResponseMessages}", responseMessages);
                results = new List<HomeResult>();
            }

            // Retrieve the tool invocations from the filter
            var toolInvocations = functionLoggingFilter.GetToolInvocations(); // You need to implement this method

            return (results, toolInvocations);
        }
    }

}