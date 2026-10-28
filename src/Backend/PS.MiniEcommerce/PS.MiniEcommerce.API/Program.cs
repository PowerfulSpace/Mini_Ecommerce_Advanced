using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PS.MiniEcommerce.API.Authentication;
using PS.MiniEcommerce.API.ExceptionHandling;
using PS.MiniEcommerce.API.Filters;
using PS.MiniEcommerce.Application;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Seed;

var builder = WebApplication.CreateBuilder(args);

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddApplication();

builder.Services.AddControllers();

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = InvalidModelStateProblemDetails.Create;
    options.SuppressMapClientErrors = true;
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

builder.Services.AddExceptionHandler<ExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

if (builder.Environment.IsDevelopment())
{
    builder.Services
        .AddAuthentication(TestUserAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, TestUserAuthenticationHandler>(
            TestUserAuthenticationHandler.SchemeName,
            _ => { });
}

builder.Services.AddAuthorization();

builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationResultHandler>();

builder.Services.AddScoped<DevelopmentOnlyFilter>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();
}

app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api/admin")
            || IsDevelopmentOnlyPaymentPath(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next();
    });
}

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.UseStatusCodePages(async statusCodeContext =>
{
    var context = statusCodeContext.HttpContext;

    if (context.Response.StatusCode != StatusCodes.Status415UnsupportedMediaType)
    {
        return;
    }

    var problem = new ProblemDetails
    {
        Type = "https://tools.ietf.org/html/rfc9110#section-15.5.16",
        Title = "Unsupported media type.",
        Status = StatusCodes.Status415UnsupportedMediaType,
        Detail = "The request Content-Type is not supported. Use application/json.",
        Instance = context.Request.Path,
        Extensions = { ["code"] = "unsupported_media_type" }
    };

    await context.Response.WriteAsJsonAsync(
        problem,
        options: null,
        contentType: "application/problem+json",
        cancellationToken: context.RequestAborted);
});

app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = WriteHealthResponse
});

app.Run();

static bool IsDevelopmentOnlyPaymentPath(PathString path)
{
    return path.StartsWithSegments("/api/payments")
        && path.Value?.EndsWith("/refund", StringComparison.OrdinalIgnoreCase) == true;
}

static async Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    if (report.Status == HealthStatus.Healthy)
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"status\":\"Healthy\"}");
        return;
    }

    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

    var problem = new ProblemDetails
    {
        Type = "https://tools.ietf.org/html/rfc9110#section-15.6.4",
        Title = "Service unavailable.",
        Status = StatusCodes.Status503ServiceUnavailable,
        Detail = "The database is not reachable.",
        Instance = context.Request.Path,
        Extensions = { ["code"] = "health_unavailable" }
    };

    await context.Response.WriteAsJsonAsync(
        problem,
        options: null,
        contentType: "application/problem+json",
        cancellationToken: context.RequestAborted);
}

public partial class Program { }
