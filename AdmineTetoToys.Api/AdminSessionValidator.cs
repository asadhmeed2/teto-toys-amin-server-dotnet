using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AdmineTetoToys.Domain.Interfaces;
using System;
using System.Threading.Tasks;

public static class AdminSessionValidator
{
    public static async Task<(bool Authorized, object? UserInfo, IResult? ErrorResult)> ValidateSessionAsync(HttpContext context, string? allowedRole = null)
    {
        var tokenService = context.RequestServices.GetRequiredService<ITokenService>();
        var redisService = context.RequestServices.GetRequiredService<IRedisCacheService>();
        var config = context.RequestServices.GetRequiredService<IConfiguration>();

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return (false, null, Results.Json(new { error = "unauthorized", error_description = "Missing or invalid Authorization header." }, statusCode: 401));

        var secret = config["JWT:SECRET"] ?? "SuperSecretKeyForTetoToysTokenAuth2026";
        var userInfo = tokenService.ValidateAndGetUserInfo(authHeader[7..], secret);
        if (userInfo == null)
            return (false, null, Results.Json(new { error = "unauthorized", error_description = "Token is invalid or expired." }, statusCode: 401));

        var adminIdProp = userInfo.GetType().GetProperty("adminId");
        var callerAdminId = adminIdProp?.GetValue(userInfo)?.ToString();
        if (string.IsNullOrEmpty(callerAdminId))
            return (false, null, Results.Json(new { error = "unauthorized", error_description = "Could not identify caller." }, statusCode: 401));

        var session = await redisService.GetAdminSessionAsync(callerAdminId);
        if (session == null)
            return (false, null, Results.Json(new { error = "unauthorized", error_description = "Session expired. Please log in again." }, statusCode: 401));

        if (allowedRole != null)
        {
            if (!string.Equals(session.Role, allowedRole, StringComparison.OrdinalIgnoreCase))
            {
                var action = allowedRole == "Admin" ? "create new users" : "perform this action";
                return (false, null, Results.Json(new { error = "forbidden", error_description = $"Only {allowedRole} users can {action}." }, statusCode: 403));
            }
        }
        else
        {
            if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(session.Role, "Partner", StringComparison.OrdinalIgnoreCase))
                return (false, null, Results.Json(new { error = "forbidden", error_description = "Only Admin or Partner users can perform this action." }, statusCode: 403));
        }

        return (true, userInfo, null);
    }
}
