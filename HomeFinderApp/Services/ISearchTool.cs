namespace HomeFinderApp.Services
{
    public interface ISearchTool
    {
        Task<string> Search(string argsJson);
    }
}