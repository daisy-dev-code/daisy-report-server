using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DaisyReport.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace DaisyReport.Api.Services;

public class JwtService : IJwtService
{
    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiryMinutes;
    private readonly SymmetricSecurityKey _signingKey;

    public JwtService(IConfiguration configuration)
    {
        var section = configuration.GetSection("Jwt");
        _secret = section["Secret"] ?? throw new InvalidOperationException("JWT Secret not configured.");
        _issuer = section["Issuer"] ?? "DaisyReport";
        _audience = section["Audience"] ?? "DaisyReport";
        _expiryMinutes = section.GetValue("ExpiryMinutes", 60);
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
    }

    public string GenerateToken(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("role", user.Role),
            new("display_name", user.DisplayName),
        };

        if (user.GroupId.HasValue)
            claims.Add(new Claim("group_id", user.GroupId.Value.ToString()));

        if (user.OrgUnitId.HasValue)
            claims.Add(new Claim("org_unit_id", user.OrgUnitId.Value.ToString()));

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _issuer,
                ValidAudience = _audience,
                IssuerSigningKey = _signingKey,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }
}
