using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PS.MiniEcommerce.API.Authentication;
using PS.MiniEcommerce.API.ExceptionHandling;
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

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = WriteHealthResponse
});

app.Run();

static async Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    if (report.Status == HealthStatus.Healthy)
    {
        await context.Response.WriteAsync("{\"status\":\"Healthy\"}");
        return;
    }

    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

    var problem = new
    {
        type = "https://tools.ietf.org/html/rfc9110#section-15.6.4",
        title = "Service unavailable.",
        status = StatusCodes.Status503ServiceUnavailable,
        detail = "The database is not reachable.",
        code = "health_unavailable"
    };

    await context.Response.WriteAsJsonAsync(problem);
}

public partial class Program { }
