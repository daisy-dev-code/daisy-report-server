using System.Security.Claims;
using DaisyReport.Api.Models;

namespace DaisyReport.Api.Services;

public interface IJwtService
{
    string GenerateToken(User user);
    ClaimsPrincipal? ValidateToken(string token);
}
