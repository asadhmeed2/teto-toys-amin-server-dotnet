using AdmineTetoToys.Domain.Entities;

namespace AdmineTetoToys.Domain.Interfaces;

public interface IProductRepository
{
    Task CreateProductWithPartsAsync(Product product, List<string> partIds);
    Task<Product?> GetProductByIdAsync(string productId);
    Task<List<string>> GetProductPartIdsAsync(string productId);
    Task UpdateProductWithPartsAsync(Product product, List<string> partIds);
    Task SoftDeleteProductAsync(string productId);
    Task SetProductDisplayAsync(string productId, bool isDisplayed);
    Task CreatePartAsync(Part part);
    Task<bool> PartExistsAsync(string partId);
    Task<(List<Part> Items, int TotalCount)> GetPartsPaginatedAsync(int page, int pageSize, string? search);
    Task<(List<Product> Items, int TotalCount)> GetProductsPaginatedAsync(int page, int pageSize, string? search);
    
    Task CreateCategoryAsync(Category category);
    Task<bool> CategoryExistsAsync(int categoryId);
    Task<bool> CategoryExistsBySlugAsync(string slug);
    Task<(List<Category> Items, int TotalCount)> GetCategoriesPaginatedAsync(int page, int pageSize, string? search);
    Task CreateSubcategoryAsync(Subcategory subcategory);
    Task<bool> SubcategoryExistsAsync(int categoryId, string name);
    Task<(List<Subcategory> Items, int TotalCount)> GetSubcategoriesPaginatedAsync(int page, int pageSize, string? search);
}
