//https://learn.microsoft.com/en-us/semantic-kernel/concepts/plugins/adding-native-plugins?pivots=programming-language-csharp
using System.ComponentModel;
using Microsoft.SemanticKernel;

public class ExtractParametersPlugin : IExtractParametersPlugin
{
    [KernelFunction("extract_home_search_parameters")]
    [Description("Extract search parameters for finding homes (excluding the query itself).")]
    public HomeSearchParameters ExtractParameters(HomeSearchParameters parameters)
    {
        // If latitude and longitude are set but distance is not, set distance to 5000m
        if (parameters.Latitude.HasValue && parameters.Longitude.HasValue && string.IsNullOrWhiteSpace(parameters.Distance))
        {
            parameters.Distance = "5000m";
        }
        return parameters;
    }
}


