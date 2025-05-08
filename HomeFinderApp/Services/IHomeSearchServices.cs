using HomeFinderApp.Models;

namespace HomeFinderApp.Services
{
    public interface IHomeSearchService
    {
        Task<List<HomeResult>> LLMSearchWithTools(string query);
    }
}