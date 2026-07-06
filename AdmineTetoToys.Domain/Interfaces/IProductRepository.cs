using AdmineTetoToys.Domain.Entities;

namespace AdmineTetoToys.Domain.Interfaces;

public interface IProductRepository
{
    Task CreateProductWithPartsAsync(Product product, List<string> partIds);
    Task CreatePartAsync(Part part);
    Task<bool> PartExistsAsync(string partId);
}
