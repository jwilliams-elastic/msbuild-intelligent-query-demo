using HomeFinderApp.Models;

namespace HomeFinderApp.Services
{
    public interface IHomeSearchService
    {
        Task<List<HomeResult>> LLMSearchWithTools(string query);
    }

    public interface IParameterExtractionTool
    {
        Task<string> ExtractParameters(string argsJson);
    }

    public interface IGeocodeTool
    {
        Task<string> GetGeocode(string argsJson);
    }

    public interface ISearchTool
    {
        Task<string> Search(string argsJson);
    }
}