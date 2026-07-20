using AdmineTetoToys.Domain.Entities;

namespace AdmineTetoToys.Domain.Interfaces;

public interface IProductRepository
{
    Task CreateProductWithPartsAsync(Product product, List<string> partIds, string language = "en");
    Task<Product?> GetProductByIdAsync(string productId, string language = "en");
    Task<List<string>> GetProductPartIdsAsync(string productId);
    Task UpdateProductWithPartsAsync(Product product, List<string> partIds, string language = "en");
    Task SoftDeleteProductAsync(string productId);
    Task SetProductDisplayAsync(string productId, bool isDisplayed);
    Task CreatePartAsync(Part part, string language = "en");
    Task<bool> PartExistsAsync(string partId);
    Task<(List<Part> Items, int TotalCount)> GetPartsPaginatedAsync(int page, int pageSize, string? search, string language = "en");
    Task<(List<Product> Items, int TotalCount)> GetProductsPaginatedAsync(int page, int pageSize, string? search, string language = "en");

    Task CreateCategoryAsync(Category category, string language = "en");
    Task<bool> CategoryExistsAsync(int categoryId);
    Task<bool> CategoryExistsBySlugAsync(string slug);
    Task<(List<Category> Items, int TotalCount)> GetCategoriesPaginatedAsync(int page, int pageSize, string? search, string language = "en");
    Task CreateSubcategoryAsync(Subcategory subcategory, string language = "en");
    Task<bool> SubcategoryExistsAsync(int categoryId, string name, string language = "en");
    Task<(List<Subcategory> Items, int TotalCount)> GetSubcategoriesPaginatedAsync(int page, int pageSize, string? search, string language = "en");

    Task<List<(string Code, string Name, bool IsRtl)>> GetLanguagesAsync();
}
