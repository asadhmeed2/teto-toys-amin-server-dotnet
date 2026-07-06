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
                INSERT INTO products (product_id, title, subtitle, description, category, subcategory, price, image_urls)
                VALUES (@productId, @title, @subtitle, @description, @category, @subcategory, @price, @imageUrls)";
            
            await using var productCmd = new MySqlCommand(insertProductSql, conn, transaction);
            productCmd.Parameters.AddWithValue("@productId", product.ProductId);
            productCmd.Parameters.AddWithValue("@title", product.Title);
            productCmd.Parameters.AddWithValue("@subtitle", (object?)product.Subtitle ?? DBNull.Value);
            productCmd.Parameters.AddWithValue("@description", (object?)product.Description ?? DBNull.Value);
            productCmd.Parameters.AddWithValue("@category", product.Category);
            productCmd.Parameters.AddWithValue("@subcategory", (object?)product.Subcategory ?? DBNull.Value);
            productCmd.Parameters.AddWithValue("@price", product.Price);
            productCmd.Parameters.AddWithValue("@imageUrls", product.ImageUrls != null ? string.Join(",", product.ImageUrls) : DBNull.Value);
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
            INSERT INTO parts (part_id, title, description, price, image_urls)
            VALUES (@partId, @title, @description, @price, @imageUrls)";

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@partId", part.PartId);
        cmd.Parameters.AddWithValue("@title", part.Title);
        cmd.Parameters.AddWithValue("@description", (object?)part.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@price", part.Price);
        cmd.Parameters.AddWithValue("@imageUrls", part.ImageUrls != null ?  System.Text.Json.JsonSerializer.Serialize(part.ImageUrls) : DBNull.Value);
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

    public async Task<(List<Part> Items, int TotalCount)> GetPartsPaginatedAsync(int page, int pageSize, string? search)
    {
        var items = new List<Part>();
        int totalCount = 0;
        int offset = (page - 1) * pageSize;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Get total count
        var countSql = "SELECT COUNT(1) FROM parts";
        if (!string.IsNullOrEmpty(search))
        {
            countSql += " WHERE title LIKE @search OR description LIKE @search";
        }

        await using (var countCmd = new MySqlCommand(countSql, conn))
        {
            if (!string.IsNullOrEmpty(search))
            {
                countCmd.Parameters.AddWithValue("@search", $"%{search}%");
            }
            totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        // 2. Get paginated items
        var itemsSql = "SELECT part_id, title, description, price, image_urls FROM parts";
        if (!string.IsNullOrEmpty(search))
        {
            itemsSql += " WHERE title LIKE @search OR description LIKE @search";
        }
        itemsSql += " ORDER BY created_at DESC LIMIT @limit OFFSET @offset";

        await using (var itemsCmd = new MySqlCommand(itemsSql, conn))
        {
            if (!string.IsNullOrEmpty(search))
            {
                itemsCmd.Parameters.AddWithValue("@search", $"%{search}%");
            }
            itemsCmd.Parameters.AddWithValue("@limit", pageSize);
            itemsCmd.Parameters.AddWithValue("@offset", offset);

            await using var reader = await itemsCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var part = new Part
                {
                    PartId = reader.GetGuid(reader.GetOrdinal("part_id")).ToString(),
                    Title = reader.GetString(reader.GetOrdinal("title")),
                    Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                    Price = reader.GetDecimal(reader.GetOrdinal("price")),
                    ImageUrls = reader.IsDBNull(reader.GetOrdinal("image_urls")) 
                        ? new List<string>() 
                        : System.Text.Json.JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("image_urls")))
                };
                items.Add(part);
            }
        }

        return (items, totalCount);
    }
}
