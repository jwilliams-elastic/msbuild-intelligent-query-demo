using System.Text.Json;

namespace HomeFinderApp.Services
{
    public class ParameterExtractionTool : IParameterExtractionTool
    {
        public async Task<string> ExtractParameters(string argsJson)
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
            var parameterJson = JsonSerializer.Serialize(parameters);
            await Console.Out.WriteLineAsync("Extracted Parameters: " + parameterJson);
            return parameterJson;
        }
    }
}