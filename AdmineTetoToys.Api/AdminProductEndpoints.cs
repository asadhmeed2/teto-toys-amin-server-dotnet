using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AdmineTetoToys.Application.DTOs;
using AdmineTetoToys.Domain.Entities;
using AdmineTetoToys.Domain.Interfaces;

public static class AdminProductEndpoints
{
    public static void MapAdminProductEndpoints(this IEndpointRouteBuilder app)
    {
        var productsGroup = app.MapGroup("/api/admin/products");
        var partsGroup = app.MapGroup("/api/admin/parts");



        // GET /api/admin/parts — Get parts paginated
        partsGroup.MapGet("/", async (HttpContext context, int? page, int? pageSize, string? search) =>
        {
            var authCheck = await AdminSessionValidator.ValidateSessionAsync(context);
            if (!authCheck.Authorized) return authCheck.ErrorResult!;

            int pageVal = page ?? 1;
            int pageSizeVal = pageSize ?? 10;
            if (pageVal < 1) pageVal = 1;
            if (pageSizeVal < 1 || pageSizeVal > 100) pageSizeVal = 10;

            var productRepo = context.RequestServices.GetRequiredService<IProductRepository>();
            var (items, totalCount) = await productRepo.GetPartsPaginatedAsync(pageVal, pageSizeVal, search);

            var totalPages = (int)Math.Ceiling((double)totalCount / pageSizeVal);

            return Results.Ok(new
            {
                items = items.Select(p => new
                {
                    part_id = p.PartId,
                    title = p.Title,
                    description = p.Description,
                    price = p.Price,
                    image_urls = p.ImageUrls
                }),
                total_count = totalCount,
                page = pageVal,
                page_size = pageSizeVal,
                total_pages = totalPages
            });
        });

        // POST /api/admin/parts — Add a part
        partsGroup.MapPost("/", async (AddPartRequest request, HttpContext context) =>
        {
            var authCheck = await AdminSessionValidator.ValidateSessionAsync(context);
            if (!authCheck.Authorized) return authCheck.ErrorResult!;

            if (string.IsNullOrWhiteSpace(request.Title) || request.Price < 0)
                return Results.Json(new { error = "invalid_request", error_description = "Title and a valid positive price are required." }, statusCode: 400);

            var productRepo = context.RequestServices.GetRequiredService<IProductRepository>();

            var part = new Part
            {
                PartId = Guid.NewGuid().ToString(),
                Title = request.Title.Trim(),
                Description = request.Description?.Trim(),
                Price = request.Price,
                ImageUrls = request.ImageUrls ?? new List<string>()
            };

            await productRepo.CreatePartAsync(part);

            return Results.Json(new
            {
                part_id = part.PartId,
                title = part.Title,
                description = part.Description,
                price = part.Price
            }, statusCode: 201);
        });

        // POST /api/admin/products — Add a product with parts
        productsGroup.MapPost("/", async (AddProductRequest request, HttpContext context) =>
        {
            var authCheck = await AdminSessionValidator.ValidateSessionAsync(context);
            if (!authCheck.Authorized) return authCheck.ErrorResult!;

            if (string.IsNullOrWhiteSpace(request.Title) || request.Category <= 0 || request.Price < 0)
                return Results.Json(new { error = "invalid_request", error_description = "Title, Category and a valid positive price are required." }, statusCode: 400);

            var productRepo = context.RequestServices.GetRequiredService<IProductRepository>();

            // ponytail: run existence checks in parallel
            var categoryCheckTask = productRepo.CategoryExistsAsync(request.Category);
            
            var partCheckTasks = new List<(string PartId, Task<bool> Task)>();
            if (request.PartIds != null && request.PartIds.Count > 0)
            {
                foreach (var partId in request.PartIds)
                {
                    partCheckTasks.Add((partId, productRepo.PartExistsAsync(partId)));
                }
            }

            var tasksToAwait = new List<Task> { categoryCheckTask };
            tasksToAwait.AddRange(partCheckTasks.Select(x => x.Task));
            await Task.WhenAll(tasksToAwait);

            if (!await categoryCheckTask)
                return Results.Json(new { error = "invalid_request", error_description = $"Category ID '{request.Category}' does not exist." }, statusCode: 400);

            foreach (var (partId, task) in partCheckTasks)
            {
                if (!await task)
                    return Results.Json(new { error = "invalid_request", error_description = $"Part ID '{partId}' does not exist." }, statusCode: 400);
            }

            var product = new Product
            {
                ProductId = Guid.NewGuid().ToString(),
                Title = request.Title.Trim(),
                Subtitle = request.Subtitle?.Trim(),
                Description = request.Description?.Trim(),
                Category = request.Category,
                Subcategory = request.Subcategory,
                Price = request.Price,
                ImageUrls = request.ImageUrls ?? new List<string>()
            };

            await productRepo.CreateProductWithPartsAsync(product, request.PartIds ?? new List<string>());

            return Results.Json(new
            {
                product_id = product.ProductId,
                title = product.Title,
                subtitle = product.Subtitle,
                description = product.Description,
                category = product.Category,
                subcategory = product.Subcategory,
                price = product.Price,
                part_ids = request.PartIds ?? new List<string>()
            }, statusCode: 201);
        });

        // GET /api/admin/products — Get products paginated
        productsGroup.MapGet("/", async (HttpContext context, int? page, int? pageSize, string? search) =>
        {
            var authCheck = await AdminSessionValidator.ValidateSessionAsync(context);
            if (!authCheck.Authorized) return authCheck.ErrorResult!;

            int pageVal = page ?? 1;
            int pageSizeVal = pageSize ?? 10;
            if (pageVal < 1) pageVal = 1;
            if (pageSizeVal < 1 || pageSizeVal > 100) pageSizeVal = 10;

            var productRepo = context.RequestServices.GetRequiredService<IProductRepository>();
            var (items, totalCount) = await productRepo.GetProductsPaginatedAsync(pageVal, pageSizeVal, search);

            var totalPages = (int)Math.Ceiling((double)totalCount / pageSizeVal);

            return Results.Ok(new
            {
                items = items.Select(p => new
                {
                    product_id = p.ProductId,
                    title = p.Title,
                    subtitle = p.Subtitle,
                    description = p.Description,
                    category = p.Category,
                    subcategory = p.Subcategory,
                    price = p.Price,
                    image_urls = p.ImageUrls
                }),
                total_count = totalCount,
                page = pageVal,
                page_size = pageSizeVal,
                total_pages = totalPages
            });
        });

        // GET /api/admin/products/{productId} — Get a single product with associated part IDs
        productsGroup.MapGet("/{productId}", async (string productId, HttpContext context) =>
        {
            var authCheck = await AdminSessionValidator.ValidateSessionAsync(context);
            if (!authCheck.Authorized) return authCheck.ErrorResult!;

            var productRepo = context.RequestServices.GetRequiredService<IProductRepository>();
            var product = await productRepo.GetProductByIdAsync(productId);
            if (product == null)
                return Results.NotFound();

            var partIds = await productRepo.GetProductPartIdsAsync(productId);

            return Results.Ok(new
            {
                product_id = product.ProductId,
                title = product.Title,
                subtitle = product.Subtitle,
                description = product.Description,
                category = product.Category,
                subcategory = product.Subcategory,
                price = product.Price,
                image_urls = product.ImageUrls,
                part_ids = partIds
            });
        });

        // PUT /api/admin/products/{productId} — Update a product with parts
        productsGroup.MapPut("/{productId}", async (string productId, AddProductRequest request, HttpContext context) =>
        {
            var authCheck = await AdminSessionValidator.ValidateSessionAsync(context);
            if (!authCheck.Authorized) return authCheck.ErrorResult!;

            if (string.IsNullOrWhiteSpace(request.Title) || request.Category <= 0 || request.Price < 0)
                return Results.Json(new { error = "invalid_request", error_description = "Title, Category and a valid positive price are required." }, statusCode: 400);

            var productRepo = context.RequestServices.GetRequiredService<IProductRepository>();

            // Run existence checks in parallel
            var categoryCheckTask = productRepo.CategoryExistsAsync(request.Category);
            
            var partCheckTasks = new List<(string PartId, Task<bool> Task)>();
            if (request.PartIds != null && request.PartIds.Count > 0)
            {
                foreach (var partId in request.PartIds)
                {
                    partCheckTasks.Add((partId, productRepo.PartExistsAsync(partId)));
                }
            }

            var tasksToAwait = new List<Task> { categoryCheckTask };
            tasksToAwait.AddRange(partCheckTasks.Select(x => x.Task));
            await Task.WhenAll(tasksToAwait);

            if (!await categoryCheckTask)
                return Results.Json(new { error = "invalid_request", error_description = $"Category ID '{request.Category}' does not exist." }, statusCode: 400);

            foreach (var (partId, task) in partCheckTasks)
            {
                if (!await task)
                    return Results.Json(new { error = "invalid_request", error_description = $"Part ID '{partId}' does not exist." }, statusCode: 400);
            }

            var product = new Product
            {
                ProductId = productId,
                Title = request.Title.Trim(),
                Subtitle = request.Subtitle?.Trim(),
                Description = request.Description?.Trim(),
                Category = request.Category,
                Subcategory = request.Subcategory,
                Price = request.Price,
                ImageUrls = request.ImageUrls ?? new List<string>()
            };

            await productRepo.UpdateProductWithPartsAsync(product, request.PartIds ?? new List<string>());

            return Results.Ok(new
            {
                product_id = product.ProductId,
                title = product.Title,
                subtitle = product.Subtitle,
                description = product.Description,
                category = product.Category,
                subcategory = product.Subcategory,
                price = product.Price,
                part_ids = request.PartIds ?? new List<string>()
            });
        });
    }
}

