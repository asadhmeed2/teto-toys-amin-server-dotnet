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

    public async Task CreateProductWithPartsAsync(Product product, List<string> partIds, string language = "en")
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // 1. Insert product (non-translatable columns only)
            const string insertProductSql = @"
                INSERT INTO products (product_id, category, subcategory, price, image_urls, is_displayed)
                VALUES (@productId, @category, @subcategory, @price, @imageUrls, @isDisplayed)";

            await using var productCmd = new MySqlCommand(insertProductSql, conn, transaction);
            productCmd.Parameters.AddWithValue("@productId", product.ProductId);
            productCmd.Parameters.AddWithValue("@category", product.Category);
            productCmd.Parameters.AddWithValue("@subcategory", (object?)product.Subcategory ?? DBNull.Value);
            productCmd.Parameters.AddWithValue("@price", product.Price);
            productCmd.Parameters.AddWithValue("@imageUrls", product.ImageUrls != null ? System.Text.Json.JsonSerializer.Serialize(product.ImageUrls) : DBNull.Value);
            productCmd.Parameters.AddWithValue("@isDisplayed", product.IsDisplayed);
            await productCmd.ExecuteNonQueryAsync();

            // 1b. Insert the translation row for the given (default 'en') language
            const string insertTranslationSql = @"
                INSERT INTO product_translations (product_id, language_code, title, subtitle, description)
                VALUES (@productId, @language, @title, @subtitle, @description)";

            await using var translationCmd = new MySqlCommand(insertTranslationSql, conn, transaction);
            translationCmd.Parameters.AddWithValue("@productId", product.ProductId);
            translationCmd.Parameters.AddWithValue("@language", language);
            translationCmd.Parameters.AddWithValue("@title", product.Title);
            translationCmd.Parameters.AddWithValue("@subtitle", (object?)product.Subtitle ?? DBNull.Value);
            translationCmd.Parameters.AddWithValue("@description", (object?)product.Description ?? DBNull.Value);
            await translationCmd.ExecuteNonQueryAsync();

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

    public async Task CreatePartAsync(Part part, string language = "en")
    {
        const string insertPartSql = @"
            INSERT INTO parts (part_id, price, image_urls)
            VALUES (@partId, @price, @imageUrls)";
        const string insertTranslationSql = @"
            INSERT INTO part_translations (part_id, language_code, title, description)
            VALUES (@partId, @language, @title, @description)";

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            await using (var cmd = new MySqlCommand(insertPartSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@partId", part.PartId);
                cmd.Parameters.AddWithValue("@price", part.Price);
                cmd.Parameters.AddWithValue("@imageUrls", part.ImageUrls != null ? System.Text.Json.JsonSerializer.Serialize(part.ImageUrls) : DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var cmd = new MySqlCommand(insertTranslationSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@partId", part.PartId);
                cmd.Parameters.AddWithValue("@language", language);
                cmd.Parameters.AddWithValue("@title", part.Title);
                cmd.Parameters.AddWithValue("@description", (object?)part.Description ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
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

    public async Task<(List<Part> Items, int TotalCount)> GetPartsPaginatedAsync(int page, int pageSize, string? search, string language = "en")
    {
        var items = new List<Part>();
        int totalCount = 0;
        int offset = (page - 1) * pageSize;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Get total count (same translation joins as the items query)
        var countSql = @"
            SELECT COUNT(1) FROM parts pa
            LEFT JOIN part_translations req ON req.part_id = pa.part_id AND req.language_code = @language
            LEFT JOIN part_translations fb ON fb.part_id = pa.part_id AND fb.language_code = 'en'";
        if (!string.IsNullOrEmpty(search))
        {
            countSql += " WHERE (COALESCE(req.title, fb.title) LIKE @search OR COALESCE(req.description, fb.description) LIKE @search)";
        }

        await using (var countCmd = new MySqlCommand(countSql, conn))
        {
            countCmd.Parameters.AddWithValue("@language", language);
            if (!string.IsNullOrEmpty(search))
            {
                countCmd.Parameters.AddWithValue("@search", $"%{search}%");
            }
            totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        // 2. Get paginated items (double LEFT JOIN part_translations resolves requested-language text with an 'en' fallback)
        var itemsSql = @"
            SELECT pa.part_id,
                   COALESCE(req.title, fb.title) AS title,
                   COALESCE(req.description, fb.description) AS description,
                   pa.price, pa.image_urls
            FROM parts pa
            LEFT JOIN part_translations req ON req.part_id = pa.part_id AND req.language_code = @language
            LEFT JOIN part_translations fb ON fb.part_id = pa.part_id AND fb.language_code = 'en'";
        if (!string.IsNullOrEmpty(search))
        {
            itemsSql += " WHERE (COALESCE(req.title, fb.title) LIKE @search OR COALESCE(req.description, fb.description) LIKE @search)";
        }
        itemsSql += " ORDER BY pa.created_at DESC LIMIT @limit OFFSET @offset";

        await using (var itemsCmd = new MySqlCommand(itemsSql, conn))
        {
            itemsCmd.Parameters.AddWithValue("@language", language);
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

    public async Task<(List<Product> Items, int TotalCount)> GetProductsPaginatedAsync(int page, int pageSize, string? search, string language = "en")
    {
        var items = new List<Product>();
        int totalCount = 0;
        int offset = (page - 1) * pageSize;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Get total count (same translation joins as the items query)
        var countSql = @"
            SELECT COUNT(1) FROM products p
            LEFT JOIN product_translations req ON req.product_id = p.product_id AND req.language_code = @language
            LEFT JOIN product_translations fb ON fb.product_id = p.product_id AND fb.language_code = 'en'
            WHERE 1=1";
        if (!string.IsNullOrEmpty(search))
        {
            countSql += " AND (COALESCE(req.title, fb.title) LIKE @search OR COALESCE(req.description, fb.description) LIKE @search)";
        }

        await using (var countCmd = new MySqlCommand(countSql, conn))
        {
            countCmd.Parameters.AddWithValue("@language", language);
            if (!string.IsNullOrEmpty(search))
            {
                countCmd.Parameters.AddWithValue("@search", $"%{search}%");
            }
            totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        // 2. Get paginated items — includes soft-deleted products, flagged via is_deleted.
        // Double LEFT JOIN product_translations resolves requested-language text with an 'en' fallback.
        var itemsSql = @"
            SELECT p.product_id,
                   COALESCE(req.title, fb.title) AS title,
                   COALESCE(req.subtitle, fb.subtitle) AS subtitle,
                   COALESCE(req.description, fb.description) AS description,
                   p.category, p.subcategory, p.price, p.image_urls, p.is_displayed, p.is_deleted
            FROM products p
            LEFT JOIN product_translations req ON req.product_id = p.product_id AND req.language_code = @language
            LEFT JOIN product_translations fb ON fb.product_id = p.product_id AND fb.language_code = 'en'
            WHERE 1=1";
        if (!string.IsNullOrEmpty(search))
        {
            itemsSql += " AND (COALESCE(req.title, fb.title) LIKE @search OR COALESCE(req.description, fb.description) LIKE @search)";
        }
        itemsSql += " ORDER BY p.created_at DESC LIMIT @limit OFFSET @offset";

        await using (var itemsCmd = new MySqlCommand(itemsSql, conn))
        {
            itemsCmd.Parameters.AddWithValue("@language", language);
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

    public async Task CreateCategoryAsync(Category category, string language = "en")
    {
        // categories.id is AUTO_INCREMENT, so the base row is inserted first and its
        // generated id is read back before the translation row can reference it.
        const string insertCategorySql = "INSERT INTO categories (slug) VALUES (@slug)";
        const string insertTranslationSql = @"
            INSERT INTO category_translations (category_id, language_code, name)
            VALUES (@categoryId, @language, @name)";

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            int categoryId;
            await using (var cmd = new MySqlCommand(insertCategorySql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@slug", category.Slug);
                await cmd.ExecuteNonQueryAsync();
                categoryId = (int)cmd.LastInsertedId;
            }

            await using (var cmd = new MySqlCommand(insertTranslationSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@categoryId", categoryId);
                cmd.Parameters.AddWithValue("@language", language);
                cmd.Parameters.AddWithValue("@name", category.Name);
                await cmd.ExecuteNonQueryAsync();
            }

            category.Id = categoryId;
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
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

    public async Task<(List<Category> Items, int TotalCount)> GetCategoriesPaginatedAsync(int page, int pageSize, string? search, string language = "en")
    {
        var items = new List<Category>();
        int totalCount = 0;
        int offset = (page - 1) * pageSize;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        // search matches translated name (with fallback) or the canonical, non-translatable slug
        var countSql = @"
            SELECT COUNT(1) FROM categories c
            LEFT JOIN category_translations req ON req.category_id = c.id AND req.language_code = @language
            LEFT JOIN category_translations fb ON fb.category_id = c.id AND fb.language_code = 'en'";
        if (!string.IsNullOrEmpty(search))
        {
            countSql += " WHERE (COALESCE(req.name, fb.name) LIKE @search OR c.slug LIKE @search)";
        }
        await using (var countCmd = new MySqlCommand(countSql, conn))
        {
            countCmd.Parameters.AddWithValue("@language", language);
            if (!string.IsNullOrEmpty(search))
            {
                countCmd.Parameters.AddWithValue("@search", $"%{search}%");
            }
            totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        var itemsSql = @"
            SELECT c.id, COALESCE(req.name, fb.name) AS name, c.slug
            FROM categories c
            LEFT JOIN category_translations req ON req.category_id = c.id AND req.language_code = @language
            LEFT JOIN category_translations fb ON fb.category_id = c.id AND fb.language_code = 'en'";
        if (!string.IsNullOrEmpty(search))
        {
            itemsSql += " WHERE (COALESCE(req.name, fb.name) LIKE @search OR c.slug LIKE @search)";
        }
        itemsSql += " ORDER BY name ASC LIMIT @limit OFFSET @offset";

        await using (var itemsCmd = new MySqlCommand(itemsSql, conn))
        {
            itemsCmd.Parameters.AddWithValue("@language", language);
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

    public async Task CreateSubcategoryAsync(Subcategory subcategory, string language = "en")
    {
        // subcategories.id is AUTO_INCREMENT, so the base row is inserted first and its
        // generated id is read back before the translation row can reference it.
        const string insertSubcategorySql = "INSERT INTO subcategories (category_id) VALUES (@categoryId)";
        const string insertTranslationSql = @"
            INSERT INTO subcategory_translations (subcategory_id, language_code, name)
            VALUES (@subcategoryId, @language, @name)";

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            int subcategoryId;
            await using (var cmd = new MySqlCommand(insertSubcategorySql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@categoryId", subcategory.CategoryId);
                await cmd.ExecuteNonQueryAsync();
                subcategoryId = (int)cmd.LastInsertedId;
            }

            await using (var cmd = new MySqlCommand(insertTranslationSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@subcategoryId", subcategoryId);
                cmd.Parameters.AddWithValue("@language", language);
                cmd.Parameters.AddWithValue("@name", subcategory.Name);
                await cmd.ExecuteNonQueryAsync();
            }

            subcategory.Id = subcategoryId;
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> SubcategoryExistsAsync(int categoryId, string name, string language = "en")
    {
        // uq_subcat_name was dropped along with subcategories.name in the translation-table
        // migration; duplicate-name prevention is now an app-level check against the
        // translation table for the given language, scoped by category via a join.
        const string sql = @"
            SELECT COUNT(1) FROM subcategory_translations st
            JOIN subcategories s ON s.id = st.subcategory_id
            WHERE s.category_id = @categoryId AND st.language_code = @language AND st.name = @name";
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@categoryId", categoryId);
        cmd.Parameters.AddWithValue("@language", language);
        cmd.Parameters.AddWithValue("@name", name);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    public async Task<(List<Subcategory> Items, int TotalCount)> GetSubcategoriesPaginatedAsync(int page, int pageSize, string? search, string language = "en")
    {
        var items = new List<Subcategory>();
        int totalCount = 0;
        int offset = (page - 1) * pageSize;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        var countSql = @"
            SELECT COUNT(1) FROM subcategories s
            LEFT JOIN subcategory_translations req ON req.subcategory_id = s.id AND req.language_code = @language
            LEFT JOIN subcategory_translations fb ON fb.subcategory_id = s.id AND fb.language_code = 'en'";
        if (!string.IsNullOrEmpty(search))
        {
            countSql += " WHERE COALESCE(req.name, fb.name) LIKE @search";
        }
        await using (var countCmd = new MySqlCommand(countSql, conn))
        {
            countCmd.Parameters.AddWithValue("@language", language);
            if (!string.IsNullOrEmpty(search))
            {
                countCmd.Parameters.AddWithValue("@search", $"%{search}%");
            }
            totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        var itemsSql = @"
            SELECT s.id, s.category_id, COALESCE(req.name, fb.name) AS name
            FROM subcategories s
            LEFT JOIN subcategory_translations req ON req.subcategory_id = s.id AND req.language_code = @language
            LEFT JOIN subcategory_translations fb ON fb.subcategory_id = s.id AND fb.language_code = 'en'";
        if (!string.IsNullOrEmpty(search))
        {
            itemsSql += " WHERE COALESCE(req.name, fb.name) LIKE @search";
        }
        itemsSql += " ORDER BY name ASC LIMIT @limit OFFSET @offset";

        await using (var itemsCmd = new MySqlCommand(itemsSql, conn))
        {
            itemsCmd.Parameters.AddWithValue("@language", language);
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

    public async Task<Product?> GetProductByIdAsync(string productId, string language = "en")
    {
        const string sql = @"
            SELECT p.product_id,
                   COALESCE(req.title, fb.title) AS title,
                   COALESCE(req.subtitle, fb.subtitle) AS subtitle,
                   COALESCE(req.description, fb.description) AS description,
                   p.category, p.subcategory, p.price, p.image_urls, p.is_displayed
            FROM products p
            LEFT JOIN product_translations req ON req.product_id = p.product_id AND req.language_code = @language
            LEFT JOIN product_translations fb ON fb.product_id = p.product_id AND fb.language_code = 'en'
            WHERE p.product_id = @productId AND p.is_deleted = 0";

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@productId", productId);
        cmd.Parameters.AddWithValue("@language", language);

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

    public async Task UpdateProductWithPartsAsync(Product product, List<string> partIds, string language = "en")
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

            // 1. Update product (non-translatable columns only)
            const string updateProductSql = @"
                UPDATE products
                SET category = @category, subcategory = @subcategory, price = @price,
                    image_urls = @imageUrls, is_displayed = @isDisplayed
                WHERE product_id = @productId AND is_deleted = 0";

            await using var productCmd = new MySqlCommand(updateProductSql, conn, transaction);
            productCmd.Parameters.AddWithValue("@productId", product.ProductId);
            productCmd.Parameters.AddWithValue("@category", product.Category);
            productCmd.Parameters.AddWithValue("@subcategory", (object?)product.Subcategory ?? DBNull.Value);
            productCmd.Parameters.AddWithValue("@price", product.Price);
            productCmd.Parameters.AddWithValue("@imageUrls", product.ImageUrls != null ? System.Text.Json.JsonSerializer.Serialize(product.ImageUrls) : DBNull.Value);
            productCmd.Parameters.AddWithValue("@isDisplayed", product.IsDisplayed);
            await productCmd.ExecuteNonQueryAsync();

            // 1b. Upsert the translation row for the given language — a PUT with a
            // different language adds/updates that translation without touching others.
            const string upsertTranslationSql = @"
                INSERT INTO product_translations (product_id, language_code, title, subtitle, description)
                VALUES (@productId, @language, @title, @subtitle, @description)
                ON DUPLICATE KEY UPDATE title = VALUES(title), subtitle = VALUES(subtitle), description = VALUES(description)";

            await using (var translationCmd = new MySqlCommand(upsertTranslationSql, conn, transaction))
            {
                translationCmd.Parameters.AddWithValue("@productId", product.ProductId);
                translationCmd.Parameters.AddWithValue("@language", language);
                translationCmd.Parameters.AddWithValue("@title", product.Title);
                translationCmd.Parameters.AddWithValue("@subtitle", (object?)product.Subtitle ?? DBNull.Value);
                translationCmd.Parameters.AddWithValue("@description", (object?)product.Description ?? DBNull.Value);
                await translationCmd.ExecuteNonQueryAsync();
            }

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
