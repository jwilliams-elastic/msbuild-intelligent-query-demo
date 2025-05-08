namespace HomeFinderApp.Services
{
    public interface IParameterExtractionTool
    {
        Task<string> ExtractParameters(string argsJson);
    }
}