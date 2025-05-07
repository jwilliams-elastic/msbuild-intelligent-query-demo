namespace HomeFinderApp.Models
{
    public class HomeResult
    {
        public string Title { get; set; } = "";
        public decimal HomePrice { get; set; }
        public decimal Bedrooms { get; set; }
        public decimal Bathrooms { get; set; }
        public int SquareFootage { get; set; }
        public decimal AnnualTax { get; set; }
        public decimal MaintenanceFee { get; set; }
        public List<string> Features { get; set; } = new();
    }
}

