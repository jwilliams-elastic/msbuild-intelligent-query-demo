namespace HomeFinderApp.Services
{
    public interface IGeocodeTool
    {
        Task<string> GetGeocode(string argsJson);
    }
}