using System.Text;
using DaisyReport.Api.Endpoints;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Middleware;
using DaisyReport.Api.Repositories;
using DaisyReport.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/daisyreport-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting DaisyReport API");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // Infrastructure
    builder.Services.AddSingleton<IDatabase, Database>();
    builder.Services.AddSingleton<IRedisCache, RedisCache>();
    builder.Services.AddSingleton<MigrationRunner>();

    // Services
    builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
    builder.Services.AddSingleton<IJwtService, JwtService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<IAclService, AclService>();

    // Repositories
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IAclRepository, AclRepository>();
    builder.Services.AddScoped<IAuditRepository, AuditRepository>();

    // JWT Authentication
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            var jwtConfig = builder.Configuration.GetSection("Jwt");
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtConfig["Issuer"],
                ValidAudience = jwtConfig["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtConfig["Secret"]!))
            };
        });
    builder.Services.AddAuthorization();

    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins("http://localhost:3000")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    var app = builder.Build();

    // Run migrations on startup
    using (var scope = app.Services.CreateScope())
    {
        var migrationRunner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
        await migrationRunner.RunAsync();
    }

    // Middleware pipeline
    app.UseMiddleware<ErrorHandlingMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<AuthenticationMiddleware>();

    // Map endpoints
    app.MapHealthEndpoints();
    app.MapAuthEndpoints();
    app.MapUserEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
