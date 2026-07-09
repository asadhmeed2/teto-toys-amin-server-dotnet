using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AdmineTetoToys.Application.DTOs;
using AdmineTetoToys.Domain.Entities;
using AdmineTetoToys.Domain.Interfaces;

public static class AdminUserEndpoints
{
    public static void MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users");

        // POST /api/admin/users — create a new Admin or Partner user (Admin-only)
        group.MapPost("/", async (CreateAdminUserRequest request, HttpContext context) =>
        {
            var adminRepo = context.RequestServices.GetRequiredService<IAdminUserRepository>();
            var hasher = context.RequestServices.GetRequiredService<IPasswordHasher>();

            var authCheck = await AdminSessionValidator.ValidateSessionAsync(context, "Admin");
            if (!authCheck.Authorized) return authCheck.ErrorResult!;

            // 3. Validate request
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password)
                || string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
                return Results.Json(new { error = "invalid_request", error_description = "All fields are required." }, statusCode: 400);

            if (request.Role != "Admin" && request.Role != "Partner")
                return Results.Json(new { error = "invalid_request", error_description = "Role must be 'Admin' or 'Partner'." }, statusCode: 400);

            // 4. Check if email already exists
            var existing = await adminRepo.GetByEmailAsync(request.Email);
            if (existing != null)
                return Results.Json(new { error = "conflict", error_description = "A user with this email already exists." }, statusCode: 409);

            // 5. Create the user
            var newUser = new AdminUser
            {
                AdminId = Guid.NewGuid().ToString(),
                Email = request.Email.Trim(),
                PasswordHash = hasher.HashPassword(request.Password),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Role = request.Role,
                IsActive = true,
            };

            await adminRepo.CreateAsync(newUser);

            return Results.Json(new
            {
                admin_id = newUser.AdminId,
                email = newUser.Email,
                first_name = newUser.FirstName,
                last_name = newUser.LastName,
                role = newUser.Role,
            }, statusCode: 201);
        });
    }
}
