using HomeFinderApp.Models;

namespace HomeFinderApp.Services
{
    public interface IHomeSearchService
    {
        Task<(List<HomeResult> Results, List<string> ToolInvocations)> LLMSearchWithTools(string query);
    }
}