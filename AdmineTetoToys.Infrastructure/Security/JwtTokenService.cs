using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using AdmineTetoToys.Domain.Interfaces;

namespace AdmineTetoToys.Infrastructure.Security;

public class JwtTokenService : ITokenService
{
    public const string Issuer = "tatotoys-api";
    public const string Audience = "tatotoys-frontend";

    public string GenerateAccessToken(string adminId, string secretKey, int expireMinutes)
    {
        return GenerateTokenInternal(adminId, "User", secretKey, expireMinutes, "access");
    }

    public string GenerateRefreshToken(string adminId, string secretKey, int expireMinutes)
    {
        return GenerateTokenInternal(adminId, "User", secretKey, expireMinutes, "refresh");
    }

    public string GenerateAccessToken(string adminId, string role, string secretKey, int expireMinutes)
    {
        return GenerateTokenInternal(adminId, role, secretKey, expireMinutes, "access");
    }

    public string GenerateRefreshToken(string adminId, string role, string secretKey, int expireMinutes)
    {
        return GenerateTokenInternal(adminId, role, secretKey, expireMinutes, "refresh");
    }

    public string GenerateRefreshToken(string adminId, string role, string firstName, string lastName, string secretKey, int expireMinutes)
    {
        return GenerateTokenInternal(adminId, role, secretKey, expireMinutes, "refresh", firstName, lastName);
    }

    private string GenerateTokenInternal(string adminId, string role, string secretKey, int expireMinutes, string tokenType, string? firstName = null, string? lastName = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(secretKey);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, adminId),
            new Claim(ClaimTypes.NameIdentifier, adminId),
            new Claim(ClaimTypes.Role, role),
        };

        if (tokenType == "refresh")
        {
            claims.Add(new Claim("token_type", "refresh"));
        }

        if (!string.IsNullOrEmpty(firstName))
        {
            claims.Add(new Claim(ClaimTypes.GivenName, firstName));
        }

        if (!string.IsNullOrEmpty(lastName))
        {
            claims.Add(new Claim(ClaimTypes.Surname, lastName));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(expireMinutes),
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature
            )
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public string? GetAdminIdFromToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            return jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value;
        }
        catch
        {
            return null;
        }
    }

    public string? GetRoleFromToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            return jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
        }
        catch
        {
            return null;
        }
    }

    public object? ValidateAndGetUserInfo(string token, string secretKey)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(secretKey);

            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            }, out var validatedToken);

            var jwt = (JwtSecurityToken)validatedToken;
            var adminId = jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value;
            var role = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value ?? "User";
            var firstName = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.GivenName)?.Value ?? string.Empty;
            var lastName = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Surname)?.Value ?? string.Empty;

            return new { adminId, role, firstName, lastName };
        }
        catch
        {
            return null;
        }
    }
}
