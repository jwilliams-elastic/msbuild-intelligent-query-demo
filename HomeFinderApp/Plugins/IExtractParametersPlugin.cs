//https://learn.microsoft.com/en-us/semantic-kernel/concepts/plugins/adding-native-plugins?pivots=programming-language-csharp
using System.ComponentModel;
using Microsoft.SemanticKernel;

public interface IExtractParametersPlugin
{
    [KernelFunction("extract_home_search_parameters")]
    [Description("Extract search parameters for finding homes (excluding the query itself).")]
    HomeSearchParameters ExtractParameters(HomeSearchParameters parameters);
}


