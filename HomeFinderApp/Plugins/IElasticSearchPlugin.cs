//https://learn.microsoft.com/en-us/semantic-kernel/concepts/plugins/adding-native-plugins?pivots=programming-language-csharp
using System.ComponentModel;
using HomeFinderApp.Models;
using Microsoft.SemanticKernel;

public interface IElasticSearchPlugin
{
    [KernelFunction("Query_Elasticsearch")]
    [Description("Query the Elasticsearch database for homes based on parameters.")]
    Task<List<HomeResult>> Search(HomeSearchParameters parameters);
}