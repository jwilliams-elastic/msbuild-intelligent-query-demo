using Azure;
using Azure.AI.OpenAI;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Options;
using HomeFinderApp.Models;
using HomeFinderApp.Services;

var builder = WebApplication.CreateBuilder(args);

// bind settings from appsettings.json
builder.Services.Configure<ElasticSettings>(builder.Configuration.GetSection("ElasticSettings"));
builder.Services.Configure<AzureOpenAISettings>(builder.Configuration.GetSection("AzureOpenAISettings"));
builder.Services.Configure<AzureMapsSettings>(builder.Configuration.GetSection("AzureMapsSettings"));

// configure ElasticsearchClient
builder.Services.AddSingleton<ElasticsearchClient>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<ElasticSettings>>().Value;

    var clientSettings = new ElasticsearchClientSettings(new Uri(settings.Url))
        .Authentication(new ApiKey(settings.ApiKey));
       
    return new ElasticsearchClient(clientSettings);
});

// configure Azure OpenAI client
builder.Services.AddSingleton<AzureOpenAIClient>(sp =>
{
    var cfg = sp.GetRequiredService<IOptions<AzureOpenAISettings>>().Value;
    var credential = new AzureKeyCredential(cfg.ApiKey);
    return new AzureOpenAIClient(new Uri(cfg.Endpoint), credential);
});

builder.Services.AddHttpClient<IHomeSearchService, HomeSearchService>();

// register your home‐search service
builder.Services.AddScoped<IHomeSearchService, HomeSearchService>();
builder.Services.AddScoped<IParameterExtractionTool, ParameterExtractionTool>();
builder.Services.AddScoped<IGeocodingTool, GeoCodingTool>();
builder.Services.AddScoped<IPropertySearchTool, PropertySearchTool>();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}
app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();