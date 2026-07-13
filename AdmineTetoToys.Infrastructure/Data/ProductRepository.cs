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
                INSERT INTO products (product_id, title, subtitle, description, category, subcategory, price, image_urls, is_displayed)
                VALUES (@productId, @title, @subtitle, @description, @category, @subcategory, @price, @imageUrls, @isDisplayed)";

            await using var productCmd = new MySqlCommand(insertProductSql, conn, transaction);
            productCmd.Parameters.AddWithValue("@productId", product.ProductId);
            productCmd.Parameters.AddWithValue("@title", product.Title);
            productCmd.Parameters.AddWithValue("@subtitle", (object?)product.Subtitle ?? DBNull.Value);
            productCmd.Parameters.AddWithValue("@description", (object?)product.Description ?? DBNull.Value);
            productCmd.Parameters.AddWithValue("@category", product.Category);
            productCmd.Parameters.AddWithValue("@subcategory", (object?)product.Subcategory ?? DBNull.Value);
            productCmd.Parameters.AddWithValue("@price", product.Price);
            productCmd.Parameters.AddWithValue("@imageUrls", product.ImageUrls != null ? System.Text.Json.JsonSerializer.Serialize(product.ImageUrls) : DBNull.Value);
            productCmd.Parameters.AddWithValue("@isDisplayed", product.IsDisplayed);
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

            await RecalculateCategoryActiveCountAsync(conn, transaction, product.Category);
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
        cmd.Parameters.AddWithValue("@imageUrls", part.ImageUrls != null ? System.Text.Json.JsonSerializer.Serialize(part.ImageUrls) : DBNull.Value);
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
                        : System.Text.Json.JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("image_urls"))) ?? new List<string>()
                };
                items.Add(part);
            }
        }

        return (items, totalCount);
    }

    public async Task<(List<Product> Items, int TotalCount)> GetProductsPaginatedAsync(int page, int pageSize, string? search)
    {
        var items = new List<Product>();
        int totalCount = 0;
        int offset = (page - 1) * pageSize;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Get total count
        var countSql = "SELECT COUNT(1) FROM products WHERE 1=1";
        if (!string.IsNullOrEmpty(search))
        {
            countSql += " AND (title LIKE @search OR description LIKE @search)";
        }

        await using (var countCmd = new MySqlCommand(countSql, conn))
        {
            if (!string.IsNullOrEmpty(search))
            {
                countCmd.Parameters.AddWithValue("@search", $"%{search}%");
            }
            totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        // 2. Get paginated items — includes soft-deleted products, flagged via is_deleted
        var itemsSql = "SELECT product_id, title, subtitle, description, category, subcategory, price, image_urls, is_displayed, is_deleted FROM products WHERE 1=1";
        if (!string.IsNullOrEmpty(search))
        {
            itemsSql += " AND (title LIKE @search OR description LIKE @search)";
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
                var product = new Product
                {
                    ProductId = reader.GetGuid(reader.GetOrdinal("product_id")).ToString(),
                    Title = reader.GetString(reader.GetOrdinal("title")),
                    Subtitle = reader.IsDBNull(reader.GetOrdinal("subtitle")) ? null : reader.GetString(reader.GetOrdinal("subtitle")),
                    Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                    Category = reader.GetInt32(reader.GetOrdinal("category")),
                    Subcategory = reader.IsDBNull(reader.GetOrdinal("subcategory")) ? null : reader.GetInt32(reader.GetOrdinal("subcategory")),
                    Price = reader.GetDecimal(reader.GetOrdinal("price")),
                    ImageUrls = reader.IsDBNull(reader.GetOrdinal("image_urls"))
                        ? new List<string>()
                        : System.Text.Json.JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("image_urls"))) ?? new List<string>(),
                    IsDisplayed = reader.GetBoolean(reader.GetOrdinal("is_displayed")),
                    IsDeleted = reader.GetBoolean(reader.GetOrdinal("is_deleted"))
                };
                items.Add(product);
            }
        }

        return (items, totalCount);
    }

    public async Task CreateCategoryAsync(Category category)
    {
        const string sql = "INSERT INTO categories (name, slug) VALUES (@name, @slug)";
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@name", category.Name);
        cmd.Parameters.AddWithValue("@slug", category.Slug);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> CategoryExistsAsync(int categoryId)
    {
        const string sql = "SELECT COUNT(1) FROM categories WHERE id = @categoryId";
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@categoryId", categoryId);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    public async Task<bool> CategoryExistsBySlugAsync(string slug)
    {
        const string sql = "SELECT COUNT(1) FROM categories WHERE slug = @slug";
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@slug", slug);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    public async Task<(List<Category> Items, int TotalCount)> GetCategoriesPaginatedAsync(int page, int pageSize, string? search)
    {
        var items = new List<Category>();
        int totalCount = 0;
        int offset = (page - 1) * pageSize;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        var countSql = "SELECT COUNT(1) FROM categories";
        if (!string.IsNullOrEmpty(search))
        {
            countSql += " WHERE name LIKE @search OR slug LIKE @search";
        }
        await using (var countCmd = new MySqlCommand(countSql, conn))
        {
            if (!string.IsNullOrEmpty(search))
            {
                countCmd.Parameters.AddWithValue("@search", $"%{search}%");
            }
            totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        var itemsSql = "SELECT id, name, slug FROM categories";
        if (!string.IsNullOrEmpty(search))
        {
            itemsSql += " WHERE name LIKE @search OR slug LIKE @search";
        }
        itemsSql += " ORDER BY name ASC LIMIT @limit OFFSET @offset";

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
                items.Add(new Category
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    Name = reader.GetString(reader.GetOrdinal("name")),
                    Slug = reader.GetString(reader.GetOrdinal("slug"))
                });
            }
        }

        return (items, totalCount);
    }

    public async Task CreateSubcategoryAsync(Subcategory subcategory)
    {
        const string sql = "INSERT INTO subcategories (category_id, name) VALUES (@categoryId, @name)";
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@categoryId", subcategory.CategoryId);
        cmd.Parameters.AddWithValue("@name", subcategory.Name);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> SubcategoryExistsAsync(int categoryId, string name)
    {
        const string sql = "SELECT COUNT(1) FROM subcategories WHERE category_id = @categoryId AND name = @name";
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@categoryId", categoryId);
        cmd.Parameters.AddWithValue("@name", name);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    public async Task<(List<Subcategory> Items, int TotalCount)> GetSubcategoriesPaginatedAsync(int page, int pageSize, string? search)
    {
        var items = new List<Subcategory>();
        int totalCount = 0;
        int offset = (page - 1) * pageSize;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        var countSql = "SELECT COUNT(1) FROM subcategories";
        if (!string.IsNullOrEmpty(search))
        {
            countSql += " WHERE name LIKE @search";
        }
        await using (var countCmd = new MySqlCommand(countSql, conn))
        {
            if (!string.IsNullOrEmpty(search))
            {
                countCmd.Parameters.AddWithValue("@search", $"%{search}%");
            }
            totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        var itemsSql = "SELECT id, category_id, name FROM subcategories";
        if (!string.IsNullOrEmpty(search))
        {
            itemsSql += " WHERE name LIKE @search";
        }
        itemsSql += " ORDER BY name ASC LIMIT @limit OFFSET @offset";

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
                items.Add(new Subcategory
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    CategoryId = reader.GetInt32(reader.GetOrdinal("category_id")),
                    Name = reader.GetString(reader.GetOrdinal("name"))
                });
            }
        }

        return (items, totalCount);
    }

    public async Task<Product?> GetProductByIdAsync(string productId)
    {
        const string sql = "SELECT product_id, title, subtitle, description, category, subcategory, price, image_urls, is_displayed FROM products WHERE product_id = @productId AND is_deleted = 0";

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@productId", productId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new Product
        {
            ProductId = reader.GetGuid(reader.GetOrdinal("product_id")).ToString(),
            Title = reader.GetString(reader.GetOrdinal("title")),
            Subtitle = reader.IsDBNull(reader.GetOrdinal("subtitle")) ? null : reader.GetString(reader.GetOrdinal("subtitle")),
            Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
            Category = reader.GetInt32(reader.GetOrdinal("category")),
            Subcategory = reader.IsDBNull(reader.GetOrdinal("subcategory")) ? null : reader.GetInt32(reader.GetOrdinal("subcategory")),
            Price = reader.GetDecimal(reader.GetOrdinal("price")),
            ImageUrls = reader.IsDBNull(reader.GetOrdinal("image_urls"))
                ? new List<string>()
                : System.Text.Json.JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("image_urls"))) ?? new List<string>(),
            IsDisplayed = reader.GetBoolean(reader.GetOrdinal("is_displayed"))
        };
    }

    public async Task<List<string>> GetProductPartIdsAsync(string productId)
    {
        var partIds = new List<string>();
        const string sql = "SELECT part_id FROM product_parts WHERE product_id = @productId";

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@productId", productId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            partIds.Add(reader.GetGuid(0).ToString());
        }

        return partIds;
    }

    public async Task UpdateProductWithPartsAsync(Product product, List<string> partIds)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // 0. Get original category before update
            const string getOriginalCatSql = "SELECT category FROM products WHERE product_id = @productId";
            int originalCategoryId = 0;
            await using (var getCmd = new MySqlCommand(getOriginalCatSql, conn, transaction))
            {
                getCmd.Parameters.AddWithValue("@productId", product.ProductId);
                var result = await getCmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    originalCategoryId = Convert.ToInt32(result);
                }
            }

            // 1. Update product
            const string updateProductSql = @"
                UPDATE products 
                SET title = @title, subtitle = @subtitle, description = @description, 
                    category = @category, subcategory = @subcategory, price = @price, 
                    image_urls = @imageUrls, is_displayed = @isDisplayed
                WHERE product_id = @productId AND is_deleted = 0";

            await using var productCmd = new MySqlCommand(updateProductSql, conn, transaction);
            productCmd.Parameters.AddWithValue("@productId", product.ProductId);
            productCmd.Parameters.AddWithValue("@title", product.Title);
            productCmd.Parameters.AddWithValue("@subtitle", (object?)product.Subtitle ?? DBNull.Value);
            productCmd.Parameters.AddWithValue("@description", (object?)product.Description ?? DBNull.Value);
            productCmd.Parameters.AddWithValue("@category", product.Category);
            productCmd.Parameters.AddWithValue("@subcategory", (object?)product.Subcategory ?? DBNull.Value);
            productCmd.Parameters.AddWithValue("@price", product.Price);
            productCmd.Parameters.AddWithValue("@imageUrls", product.ImageUrls != null ? System.Text.Json.JsonSerializer.Serialize(product.ImageUrls) : DBNull.Value);
            productCmd.Parameters.AddWithValue("@isDisplayed", product.IsDisplayed);
            await productCmd.ExecuteNonQueryAsync();

            // 2. Delete existing relationships in product_parts
            const string deleteRelationSql = "DELETE FROM product_parts WHERE product_id = @productId";
            await using var deleteCmd = new MySqlCommand(deleteRelationSql, conn, transaction);
            deleteCmd.Parameters.AddWithValue("@productId", product.ProductId);
            await deleteCmd.ExecuteNonQueryAsync();

            // 3. Insert relationships in product_parts
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

            // 4. Recalculate original and new category active counts
            if (originalCategoryId > 0)
            {
                await RecalculateCategoryActiveCountAsync(conn, transaction, originalCategoryId);
            }
            if (product.Category != originalCategoryId)
            {
                await RecalculateCategoryActiveCountAsync(conn, transaction, product.Category);
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task SetProductDisplayAsync(string productId, bool isDisplayed)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Get category for count recalculation
        const string getCatSql = "SELECT category FROM products WHERE product_id = @productId";
        int categoryId = 0;
        await using (var getCmd = new MySqlCommand(getCatSql, conn))
        {
            getCmd.Parameters.AddWithValue("@productId", productId);
            var result = await getCmd.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
                categoryId = Convert.ToInt32(result);
        }

        // 2. Flip is_displayed
        const string updateSql = "UPDATE products SET is_displayed = @isDisplayed WHERE product_id = @productId AND is_deleted = 0";
        await using (var updateCmd = new MySqlCommand(updateSql, conn))
        {
            updateCmd.Parameters.AddWithValue("@isDisplayed", isDisplayed ? 1 : 0);
            updateCmd.Parameters.AddWithValue("@productId", productId);
            await updateCmd.ExecuteNonQueryAsync();
        }

        // 3. Recalculate category active count
        if (categoryId > 0)
            await RecalculateCategoryActiveCountAsync(conn, null, categoryId);
    }

    public async Task SoftDeleteProductAsync(string productId)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Get original category
        const string getCatSql = "SELECT category FROM products WHERE product_id = @productId";
        int categoryId = 0;
        await using (var getCmd = new MySqlCommand(getCatSql, conn))
        {
            getCmd.Parameters.AddWithValue("@productId", productId);
            var result = await getCmd.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
            {
                categoryId = Convert.ToInt32(result);
            }
        }

        // 2. Set is_deleted = 1
        const string deleteSql = "UPDATE products SET is_deleted = 1 WHERE product_id = @productId";
        await using (var deleteCmd = new MySqlCommand(deleteSql, conn))
        {
            deleteCmd.Parameters.AddWithValue("@productId", productId);
            await deleteCmd.ExecuteNonQueryAsync();
        }

        // 3. Recalculate category count
        if (categoryId > 0)
        {
            await RecalculateCategoryActiveCountAsync(conn, null, categoryId);
        }
    }

    private async Task RecalculateCategoryActiveCountAsync(MySqlConnection conn, MySqlTransaction? transaction, int categoryId)
    {
        const string sql = @"
            UPDATE categories 
            SET number_of_active_products = (
                SELECT COUNT(*) 
                FROM products 
                WHERE category = @categoryId AND is_deleted = 0 AND is_displayed = 1
            )
            WHERE id = @categoryId";

        await using var cmd = new MySqlCommand(sql, conn, transaction);
        cmd.Parameters.AddWithValue("@categoryId", categoryId);
        await cmd.ExecuteNonQueryAsync();
    }
}
