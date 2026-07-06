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

        // Helper to validate Admin session in Redis
        async Task<(bool Authorized, object? UserInfo, IResult? ErrorResult)> ValidateAdminSessionAsync(HttpContext context)
        {
            var tokenService = context.RequestServices.GetRequiredService<ITokenService>();
            var redisService = context.RequestServices.GetRequiredService<IRedisCacheService>();
            var config = context.RequestServices.GetRequiredService<IConfiguration>();

            var authHeader = context.Request.Headers.Authorization.ToString();
            if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return (false, null, Results.Json(new { error = "unauthorized", error_description = "Missing or invalid Authorization header." }, statusCode: 401));

            var secret = config["JWT:SECRET"] ?? "SuperSecretKeyForTetoToysTokenAuth2026";
            var userInfo = tokenService.ValidateAndGetUserInfo(authHeader[7..], secret);
            if (userInfo == null)
                return (false, null, Results.Json(new { error = "unauthorized", error_description = "Token is invalid or expired." }, statusCode: 401));

            var emailProp = userInfo.GetType().GetProperty("email");
            var callerEmail = emailProp?.GetValue(userInfo)?.ToString();
            if (string.IsNullOrEmpty(callerEmail))
                return (false, null, Results.Json(new { error = "unauthorized", error_description = "Could not identify caller." }, statusCode: 401));

            var session = await redisService.GetAdminSessionAsync(callerEmail);
            if (session == null)
                return (false, null, Results.Json(new { error = "unauthorized", error_description = "Session expired. Please log in again." }, statusCode: 401));

            if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                return (false, null, Results.Json(new { error = "forbidden", error_description = "Only Admin users can perform this action." }, statusCode: 403));

            return (true, userInfo, null);
        }

        // POST /api/admin/parts — Add a part
        partsGroup.MapPost("/", async (AddPartRequest request, HttpContext context) =>
        {
            var authCheck = await ValidateAdminSessionAsync(context);
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
            var authCheck = await ValidateAdminSessionAsync(context);
            if (!authCheck.Authorized) return authCheck.ErrorResult!;

            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Category) || request.Price < 0)
                return Results.Json(new { error = "invalid_request", error_description = "Title, Category and a valid positive price are required." }, statusCode: 400);

            var productRepo = context.RequestServices.GetRequiredService<IProductRepository>();

            // ponytail: verify all provided part IDs actually exist in DB
            if (request.PartIds != null && request.PartIds.Count > 0)
            {
                foreach (var partId in request.PartIds)
                {
                    if (!await productRepo.PartExistsAsync(partId))
                        return Results.Json(new { error = "invalid_request", error_description = $"Part ID '{partId}' does not exist." }, statusCode: 400);
                }
            }

            var product = new Product
            {
                ProductId = Guid.NewGuid().ToString(),
                Title = request.Title.Trim(),
                Subtitle = request.Subtitle?.Trim(),
                Description = request.Description?.Trim(),
                Category = request.Category.Trim(),
                Subcategory = request.Subcategory?.Trim(),
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
    }
}
