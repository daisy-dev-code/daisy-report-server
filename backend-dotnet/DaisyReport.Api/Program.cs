using System.Text;
using Dapper;
using DaisyReport.Api.Discovery.Endpoints;
using DaisyReport.Api.Discovery.Services;
using DaisyReport.Api.DynamicList;
using DaisyReport.Api.Endpoints;
using DaisyReport.Api.ExpressionEngine;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Middleware;
using DaisyReport.Api.PowerBi;
using DaisyReport.Api.PowerBi.Endpoints;
using DaisyReport.Api.PowerBi.Services;
using DaisyReport.Api.ReportEngine;
using DaisyReport.Api.Repositories;
using DaisyReport.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

// Enable Dapper snake_case → PascalCase column mapping
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

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
    builder.Services.AddSingleton<IExpressionService, ExpressionService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<IAclService, AclService>();
    builder.Services.AddScoped<ISearchService, SearchService>();

    // Report Engine
    builder.Services.AddScoped<IReportExecutionPipeline, ReportExecutionPipeline>();
    builder.Services.AddScoped<IOutputFormatter, OutputFormatter>();
    builder.Services.AddScoped<EngineRouter>();

    // Repositories
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IAclRepository, AclRepository>();
    builder.Services.AddScoped<IAuditRepository, AuditRepository>();
    builder.Services.AddScoped<IGroupRepository, GroupRepository>();
    builder.Services.AddScoped<IOrgUnitRepository, OrgUnitRepository>();
    builder.Services.AddScoped<IReportRepository, ReportRepository>();
    builder.Services.AddScoped<IReportFolderRepository, ReportFolderRepository>();
    builder.Services.AddScoped<IDatasourceRepository, DatasourceRepository>();
    builder.Services.AddScoped<IDatasinkRepository, DatasinkRepository>();
    builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
    builder.Services.AddScoped<ISchedulerRepository, SchedulerRepository>();
    builder.Services.AddScoped<IConfigRepository, ConfigRepository>();
    builder.Services.AddScoped<INotificationRepository, NotificationRepository>();

    // Dynamic List Engine
    builder.Services.AddScoped<IDynamicListEngine, DynamicListEngine>();
    builder.Services.AddScoped<IExportService, ExportService>();

    // Power BI Integration
    builder.Services.AddHttpClient<IPowerBiApiClient, PowerBiApiClient>();
    builder.Services.AddSingleton<IPowerBiAuthService, PowerBiAuthService>();
    builder.Services.AddScoped<IPowerBiSyncService, PowerBiSyncService>();
    builder.Services.AddScoped<IPowerBiRepository, PowerBiRepository>();

    // Discovery Engine
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<IPortScanner, PortScanner>();
    builder.Services.AddSingleton<IServiceProber, ServiceProber>();
    builder.Services.AddSingleton<IDnsResolver, DnsResolver>();
    builder.Services.AddScoped<IDiscoveryService, DiscoveryService>();

    // Background Services
    builder.Services.AddHostedService<SchedulerService>();

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

    // Run migrations on startup (skip errors if already applied)
    try
    {
        using var scope = app.Services.CreateScope();
        var migrationRunner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
        await migrationRunner.RunAsync();
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Migration runner encountered an error (database may already be initialized)");
    }

    // Ensure admin password hash is valid (seed data may have placeholder hash)
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDatabase>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        using var conn = await db.GetConnectionAsync();
        var adminHash = await conn.ExecuteScalarAsync<string>("SELECT password_hash FROM RS_USER WHERE username = 'admin'");
        if (adminHash != null && !hasher.VerifyPassword("DaisyAdmin2026!", adminHash))
        {
            var newHash = hasher.HashPassword("DaisyAdmin2026!");
            await conn.ExecuteAsync("UPDATE RS_USER SET password_hash = @Hash WHERE username = 'admin'", new { Hash = newHash });
            Log.Information("Admin password hash updated to valid Argon2id format");
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Could not verify admin password hash");
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
    app.MapGroupEndpoints();
    app.MapOrgUnitEndpoints();
    app.MapReportEndpoints();
    app.MapReportFolderEndpoints();
    app.MapDatasourceEndpoints();
    app.MapDatasinkEndpoints();
    app.MapPermissionEndpoints();
    app.MapDashboardEndpoints();
    app.MapSchedulerEndpoints();
    app.MapExportEndpoints();
    app.MapSearchEndpoints();
    app.MapAuditEndpoints();
    app.MapConfigEndpoints();
    app.MapNotificationEndpoints();
    app.MapConstantEndpoints();
    app.MapPowerBiEndpoints();
    app.MapDiscoveryEndpoints();

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
