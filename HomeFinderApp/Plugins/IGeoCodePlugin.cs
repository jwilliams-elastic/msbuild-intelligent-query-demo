//https://learn.microsoft.com/en-us/semantic-kernel/concepts/plugins/adding-native-plugins?pivots=programming-language-csharp
using System.ComponentModel;
using Microsoft.SemanticKernel;

public interface IGeoCodePlugin
{
    [KernelFunction("get_coordinates")]
    [Description("Accepts a location and returns the coordinates of that location.")]
    Task<string> GetCoordinates(string location);
}