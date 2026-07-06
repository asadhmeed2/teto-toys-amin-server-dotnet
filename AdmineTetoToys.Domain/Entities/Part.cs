namespace AdmineTetoToys.Domain.Entities;

public class Part
{
    public string PartId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public List<string> ImageUrls { get; set; } = new List<string>();
}
