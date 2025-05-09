using System.ComponentModel;

public class HomeSearchParameters
{
    [Description("The original search query (e.g., 'homes near Belongil Beach').")]
    public required string Query { get; set; }                // the full input query

    [Description("Search radius.  Miles should be abbreviated as mi and kilometers as km")]
    public required string Distance { get; set; }             // The search radius (e.g., 500m)

    [Description("The number of bedrooms a home may have (e.g., 2, 3, 4).  Convert text representation of numbers into numeric values.")]
    public int? Bedrooms { get; set; }               // Number of bedrooms

    [Description("The number of bathrooms a home may have (e.g., 2, 2.5, 3).  Convert text representation of numbers into numeric values.")]
    public int? Bathrooms { get; set; }              // Number of bathrooms

    [Description("Tax amount.  Convert text representation of numbers into numeric without $ or commas. If the query supplies $10,000 then parse it as 10000.")]
    public decimal? Tax { get; set; }                // Tax amount without $ or commas
    
    [Description("Maintenance or Homeowners Association (HOA) fees.  Convert text representation of numbers into numeric without $ or commas. If the query supplies $10,000 then parse it as 10000.")]
    public decimal? Maintenance { get; set; }        // HOA fees without $ or commas
    
    [Description("Location mentioned in the query (e.g., Belongil Beach, The woodlands texas).")]
    public required string Location { get; set; }             // Location name to geocode
    
    [Description("Square footage of the home (e.g., 1200, 15000). Convert text representation of numbers into numeric without commas. If the query supplies 1,000 then parse it as 1000.")]
    public int? SquareFootage { get; set; }          // Square footage without commas
    
    [Description("Home price.  Convert text representation of numbers into numeric without $ or commas. If the query supplies $0,000 then parse it as 100000.")]
    public decimal? HomePrice { get; set; }          // Home price without $ or commas
    
    [Description("home features, amenities, or descriptive terms (e.g., 2 car garage, pool, gym, modern, luxurious). This can include multiple options.  Each feature option must be enclosed with double quotes. Comma delimit multiple features. For example pool and updated kitchen should be formated to \"pool\", \"updated kitchen\".")]
    public string? Feature { get; set; }              // Delimited features, e.g., pool, garage
    
    [Description("Latitude for geolocation.  Convert text representation of numbers into numeric values.")]
    public decimal? Latitude { get; set; }           // Optional: latitude for geolocation
    
    [Description("Longitude for geolocation.  Convert text representation of numbers into numeric values.")]
    public decimal? Longitude { get; set; }          // Optional: longitude for geolocation
}