using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AdmineTetoToys.Application.DTOs;
using AdmineTetoToys.Domain.Interfaces;
using System.Text.Json;

public static class AdminAuthEndpoints
{
    public static void MapAdminAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // POST /api/auth/login
        group.MapPost("/login", async (LoginRequest request, HttpContext context) =>
        {
            var adminRepo = context.RequestServices.GetRequiredService<IAdminUserRepository>();
            var hasher = context.RequestServices.GetRequiredService<IPasswordHasher>();
            var tokenService = context.RequestServices.GetRequiredService<ITokenService>();
            var redisService = context.RequestServices.GetRequiredService<IRedisCacheService>();
            var config = context.RequestServices.GetRequiredService<IConfiguration>();

            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return Results.Json(new { error = "invalid_request", error_description = "Email and password are required." }, statusCode: 400);

            var admin = await adminRepo.GetByEmailAsync(request.Email);
            if (admin == null || !admin.IsActive)
                return Results.Json(new { error = "invalid_grant", error_description = "Invalid email or password." }, statusCode: 401);

            if (!hasher.VerifyPassword(request.Password, admin.PasswordHash))
                return Results.Json(new { error = "invalid_grant", error_description = "Invalid email or password." }, statusCode: 401);

            await adminRepo.UpdateLastLoginAsync(admin.AdminId);

            var secret = config["JWT:SECRET"] ?? "SuperSecretKeyForTetoToysTokenAuth2026";
            string accessToken = tokenService.GenerateAccessToken(admin.AdminId, admin.Role, secret, 15);
            string refreshToken = tokenService.GenerateRefreshToken(admin.AdminId, admin.Role, admin.FirstName, admin.LastName, secret, 1 * 24 * 60);
            await redisService.SetRefreshTokenAsync(refreshToken, TimeSpan.FromDays(7));

            // ponytail: persist admin session in Redis for auth checks on protected endpoints
            await redisService.SetAdminSessionAsync(admin.AdminId, admin.Role, TimeSpan.FromMinutes(15));

            // ponytail: store user permissions in Redis for 7 days (lifetime of session)
            var permissions = new { userCreation = admin.Role == "Admin" };
            var permissionsJson = JsonSerializer.Serialize(permissions);
            await redisService.SetPermissionsAsync(admin.AdminId, permissionsJson, TimeSpan.FromDays(1));

            context.Response.Cookies.Append("refresh_token", refreshToken, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = false, // set true in production
                MaxAge = TimeSpan.FromDays(7),
                Path = "/",
            });

            return Results.Ok(new
            {
                access_token = accessToken,
                token_type = "Bearer",
                expires_in = 900,
                role = admin.Role,
            });
        });

        // POST /api/auth/logout
        group.MapPost("/logout", async (HttpContext context) =>
        {
            var redisService = context.RequestServices.GetRequiredService<IRedisCacheService>();
            var tokenService = context.RequestServices.GetRequiredService<ITokenService>();

            var refreshToken = context.Request.Cookies["refresh_token"];
            if (!string.IsNullOrEmpty(refreshToken))
            {
                await redisService.InvalidateRefreshTokenAsync(refreshToken);

                // ponytail: clear admin session from Redis
                var adminId = tokenService.GetAdminIdFromToken(refreshToken);
                if (!string.IsNullOrEmpty(adminId))
                {
                    await redisService.InvalidateAdminSessionAsync(adminId);
                    await redisService.InvalidatePermissionsAsync(adminId);
                }
            }
            context.Response.Cookies.Delete("refresh_token");
            return Results.Ok(new { message = "Logged out successfully." });
        });

        // POST /api/auth/refresh — issue a new access token from the existing refresh token
        group.MapPost("/refresh", async (HttpContext context) =>
        {
            var redisService = context.RequestServices.GetRequiredService<IRedisCacheService>();
            var tokenService = context.RequestServices.GetRequiredService<ITokenService>();
            var config = context.RequestServices.GetRequiredService<IConfiguration>();

            var refreshToken = context.Request.Cookies["refresh_token"];
            if (string.IsNullOrEmpty(refreshToken) || !await redisService.ValidateRefreshTokenAsync(refreshToken))
                return Results.Json(new { error = "invalid_token", error_description = "Missing or invalid session refresh token." }, statusCode: 401);

            var adminId = tokenService.GetAdminIdFromToken(refreshToken);
            var role = tokenService.GetRoleFromToken(refreshToken);
            if (string.IsNullOrEmpty(adminId) || string.IsNullOrEmpty(role))
                return Results.Json(new { error = "invalid_token", error_description = "Malformed refresh token." }, statusCode: 401);

            var secret = config["JWT:SECRET"] ?? "SuperSecretKeyForTetoToysTokenAuth2026";
            string newAccessToken = tokenService.GenerateAccessToken(adminId, role, secret, 15);

            // Keep the admin session alive in Redis for the same duration as the new access token
            await redisService.SetAdminSessionAsync(adminId, role, TimeSpan.FromMinutes(15));

            return Results.Ok(new
            {
                access_token = newAccessToken,
                token_type = "Bearer",
                expires_in = 900,
                role,
            });
        });

        // GET /api/auth/me
        group.MapGet("/me", async (HttpContext context) =>
        {
            var authHeader = context.Request.Headers.Authorization.ToString();
            if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { error = "unauthorized", error_description = "Missing or invalid Authorization header." }, statusCode: 401);

            var tokenService = context.RequestServices.GetRequiredService<ITokenService>();
            var redisService = context.RequestServices.GetRequiredService<IRedisCacheService>();
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var secret = config["JWT:SECRET"] ?? "SuperSecretKeyForTetoToysTokenAuth2026";

            var userInfo = tokenService.ValidateAndGetUserInfo(authHeader[7..], secret);
            if (userInfo == null)
                return Results.Json(new { error = "unauthorized", error_description = "Token is invalid or expired." }, statusCode: 401);

            // ponytail: the refresh token carries first/last name — pull it from there for the full profile
            var refreshToken = context.Request.Cookies["refresh_token"];
            if (!string.IsNullOrEmpty(refreshToken) && await redisService.ValidateRefreshTokenAsync(refreshToken))
            {
                var refreshInfo = tokenService.ValidateAndGetUserInfo(refreshToken, secret);
                if (refreshInfo != null)
                    return Results.Ok(refreshInfo);
            }

            return Results.Ok(userInfo);
        });

        // GET /api/auth/permissions — return user permissions from Redis (check both access token and refresh token)
        group.MapGet("/permissions", async (HttpContext context) =>
        {
            var tokenService = context.RequestServices.GetRequiredService<ITokenService>();
            var redisService = context.RequestServices.GetRequiredService<IRedisCacheService>();
            var config = context.RequestServices.GetRequiredService<IConfiguration>();

            // 1. Validate Access Token (JWT)
            var authHeader = context.Request.Headers.Authorization.ToString();
            
            var secret = config["JWT:SECRET"] ?? "SuperSecretKeyForTetoToysTokenAuth2026";
            var userInfo = tokenService.ValidateAndGetUserInfo(authHeader[7..], secret);
            if (userInfo == null)
                return Results.Json(new { error = "unauthorized", error_description = "Token is invalid or expired." }, statusCode: 401);

            var adminIdProp = userInfo.GetType().GetProperty("adminId");
            var callerAdminId = adminIdProp?.GetValue(userInfo)?.ToString();
            if (string.IsNullOrEmpty(callerAdminId))
                return Results.Json(new { error = "unauthorized", error_description = "Could not identify caller." }, statusCode: 401);

            // 2. Validate Refresh Token cookie is in Redis
            var refreshToken = context.Request.Cookies["refresh_token"];
            if (string.IsNullOrEmpty(refreshToken) || !await redisService.ValidateRefreshTokenAsync(refreshToken))
                return Results.Json(new { error = "unauthorized", error_description = "Missing or invalid session refresh token." }, statusCode: 401);

            // 3. Retrieve permissions from Redis
            var permissionsJson = await redisService.GetPermissionsAsync(callerAdminId);
            if (string.IsNullOrEmpty(permissionsJson))
            {
                // ponytail: fallback just in case key was evicted but tokens are fully valid
                var roleProp = userInfo.GetType().GetProperty("role");
                var role = roleProp?.GetValue(userInfo)?.ToString() ?? "Partner";
                var permissions = new { userCreation = role == "Admin" };
                permissionsJson = JsonSerializer.Serialize(permissions);
                await redisService.SetPermissionsAsync(callerAdminId, permissionsJson, TimeSpan.FromDays(7));
            }

            return Results.Content(permissionsJson, "application/json");
        });
    }
}
