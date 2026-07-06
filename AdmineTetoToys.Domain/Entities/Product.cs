namespace AdmineTetoToys.Domain.Entities;

public class Product
{
    public string ProductId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Subcategory { get; set; }
    public decimal Price { get; set; }
    public List<string> ImageUrls { get; set; } = new List<string>();
}
