using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace PS.MiniEcommerce.API.ExceptionHandling;

public class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            var problem = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
                Title = "Unauthorized.",
                Status = StatusCodes.Status401Unauthorized,
                Detail = "Authentication is required to access this resource.",
                Instance = context.Request.Path,
                Extensions = { ["code"] = "unauthenticated" }
            };

            await context.Response.WriteAsJsonAsync(problem);
            return;
        }

        if (authorizeResult.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            var problem = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
                Title = "Forbidden.",
                Status = StatusCodes.Status403Forbidden,
                Detail = "You do not have permission to access this resource.",
                Instance = context.Request.Path,
                Extensions = { ["code"] = "forbidden" }
            };

            await context.Response.WriteAsJsonAsync(problem);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
