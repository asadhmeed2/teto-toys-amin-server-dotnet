namespace AdmineTetoToys.Domain.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(string adminId, string secretKey, int expireMinutes);
    string GenerateRefreshToken(string adminId, string secretKey, int expireMinutes);
    string GenerateAccessToken(string adminId, string role, string secretKey, int expireMinutes);
    string GenerateRefreshToken(string adminId, string role, string secretKey, int expireMinutes);
    string GenerateRefreshToken(string adminId, string role, string firstName, string lastName, string secretKey, int expireMinutes);
    string? GetAdminIdFromToken(string token);
    string? GetRoleFromToken(string token);
    object? ValidateAndGetUserInfo(string token, string secretKey);
}
