using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using System.Collections.Concurrent;

namespace HomeFinderApp.Services
{
    public class FunctionLoggingFilter : IFunctionInvocationFilter
    {
        private readonly ILogger logger;
        private readonly ConcurrentQueue<string> toolInvocations = new();

        public FunctionLoggingFilter(ILogger<FunctionLoggingFilter> logger)
        {
            this.logger = logger;
        }

        public FunctionLoggingFilter()
        {
            // Default constructor for DI
            this.logger = new NullLogger<FunctionLoggingFilter>();
        }
        public void ClearToolInvocations()
        {
            while (!toolInvocations.IsEmpty)
            {
                toolInvocations.TryDequeue(out _);
            }
        }
        public List<string> GetToolInvocations()
        {
            return toolInvocations.ToList();
        }

        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            // Before execution
            logger.LogInformation($"[FILTER] {context.Function.PluginName}.{context.Function.Name} invoked. Arguments: {string.Join(", ", context.Arguments)}");

            // If this is the elasticsearch plugin, record the query argument
            if (context.Function.PluginName == "query_elasticsearch")
            {
                // Assuming the query is the first argument
                if (context.Arguments.Count > 0)
                {
                    var queryArg = context.Arguments.First().Value?.ToString();
                    if (!string.IsNullOrEmpty(queryArg))
                    {
                        toolInvocations.Enqueue("Elasticsearch: " + queryArg);
                    }
                }
            }
            // If this is the geocode plugin, record the location argument
            else if (context.Function.PluginName == "geocode_location")
            {
                // Assuming the location is the first argument
                if (context.Arguments.Count > 0)
                {
                    var locationArg = context.Arguments.First().Value?.ToString();
                    if (!string.IsNullOrEmpty(locationArg))
                    {
                        toolInvocations.Enqueue("Geocode: " + locationArg);
                    }
                }
            }
            // If this is the extract parameters plugin, record the parameters argument
            else if (context.Function.PluginName == "extract_parameters")
            {
                // Assuming the parameters are the first argument
                if (context.Arguments.Count > 0)
                {
                    var parametersArg = context.Arguments.First().Value?.ToString();
                    if (!string.IsNullOrEmpty(parametersArg))
                    {
                        toolInvocations.Enqueue("Extract Parameters: " + parametersArg);
                    }
                }
            }
            // Execute the function
            await next(context);

            // After execution
            logger.LogInformation($"\n[FILTER] {context.Function.PluginName}.{context.Function.Name} completed. Result: {context.Result}");
        }
    }

}