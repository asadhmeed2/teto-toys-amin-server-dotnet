using MySql.Data.MySqlClient;
using AdmineTetoToys.Domain.Entities;
using AdmineTetoToys.Domain.Interfaces;

namespace AdmineTetoToys.Infrastructure.Data;

public class ProductRepository : IProductRepository
{
    private readonly string _connectionString;

    public ProductRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task CreateProductWithPartsAsync(Product product, List<string> partIds)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // 1. Insert product
            const string insertProductSql = @"
                INSERT INTO products (product_id, title, subtitle, description, category, subcategory, price)
                VALUES (@productId, @title, @subtitle, @description, @category, @subcategory, @price)";
            
            await using var productCmd = new MySqlCommand(insertProductSql, conn, transaction);
            productCmd.Parameters.AddWithValue("@productId", product.ProductId);
            productCmd.Parameters.AddWithValue("@title", product.Title);
            productCmd.Parameters.AddWithValue("@subtitle", (object?)product.Subtitle ?? DBNull.Value);
            productCmd.Parameters.AddWithValue("@description", (object?)product.Description ?? DBNull.Value);
            productCmd.Parameters.AddWithValue("@category", product.Category);
            productCmd.Parameters.AddWithValue("@subcategory", (object?)product.Subcategory ?? DBNull.Value);
            productCmd.Parameters.AddWithValue("@price", product.Price);
            productCmd.Parameters.AddWithValue("@image_urls", product.ImageUrls != null ? string.Join(",", product.ImageUrls) : DBNull.Value);
            await productCmd.ExecuteNonQueryAsync();

            // 2. Insert relationships in product_parts
            if (partIds != null && partIds.Count > 0)
            {
                const string insertRelationSql = @"
                    INSERT INTO product_parts (product_id, part_id)
                    VALUES (@productId, @partId)";

                foreach (var partId in partIds)
                {
                    await using var relationCmd = new MySqlCommand(insertRelationSql, conn, transaction);
                    relationCmd.Parameters.AddWithValue("@productId", product.ProductId);
                    relationCmd.Parameters.AddWithValue("@partId", partId);
                    await relationCmd.ExecuteNonQueryAsync();
                }
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task CreatePartAsync(Part part)
    {
        const string sql = @"
            INSERT INTO parts (part_id, title, description, price)
            VALUES (@partId, @title, @description, @price)";

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@partId", part.PartId);
        cmd.Parameters.AddWithValue("@title", part.Title);
        cmd.Parameters.AddWithValue("@description", (object?)part.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@price", part.Price);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> PartExistsAsync(string partId)
    {
        const string sql = "SELECT COUNT(1) FROM parts WHERE part_id = @partId";

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@partId", partId);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }
}
