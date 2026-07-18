using System.Text.Json.Serialization;

namespace AdmineTetoToys.Application.DTOs;

public record AddProductRequest(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("subtitle")] string? Subtitle,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("category")] int Category,
    [property: JsonPropertyName("subcategory")] int? Subcategory,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("part_ids")] List<string> PartIds,
    [property: JsonPropertyName("image_urls")] List<string>? ImageUrls,
    [property: JsonPropertyName("is_displayed")] bool? IsDisplayed,
    [property: JsonPropertyName("language")] string? Language
);

public record SetProductDisplayRequest(
    [property: JsonPropertyName("is_displayed")] bool IsDisplayed
);

public record AddPartRequest(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("image_urls")] List<string>? ImageUrls,
    [property: JsonPropertyName("language")] string? Language
);
