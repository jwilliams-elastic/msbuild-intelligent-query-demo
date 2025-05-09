using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;

namespace HomeFinderApp.Services
{
    public class FunctionLoggingFilter : IFunctionInvocationFilter
    {
        private readonly ILogger logger;

        public FunctionLoggingFilter(ILogger<FunctionLoggingFilter> logger)
        {
            this.logger = logger;
        }

        public FunctionLoggingFilter()
        {
            // Default constructor for DI
            this.logger = new NullLogger<FunctionLoggingFilter>();
        }
        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            // Before execution
            logger.LogInformation($"[FILTER] {context.Function.PluginName}.{context.Function.Name} invoked. Arguments: {string.Join(", ", context.Arguments)}");

            // Execute the function
            await next(context);

            // After execution. A
            logger.LogInformation($"\n[FILTER] {context.Function.PluginName}.{context.Function.Name} completed. Result: {context.Result}");
        }
    }

}