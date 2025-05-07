using Azure.AI.OpenAI;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Options;
using HomeFinderApp.Models;

namespace HomeFinderApp.Services
{
    public interface IHomeSearchService
    {
        Task<List<HomeResult>> SearchHomesAsync(string query);
    }
}