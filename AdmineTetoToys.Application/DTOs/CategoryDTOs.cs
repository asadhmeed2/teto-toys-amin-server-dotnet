namespace AdmineTetoToys.Application.DTOs;

// ponytail: keep DTO models simple, clean, and direct
public class CreateCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Language { get; set; }
}

public class CreateSubcategoryRequest
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Language { get; set; }
}
