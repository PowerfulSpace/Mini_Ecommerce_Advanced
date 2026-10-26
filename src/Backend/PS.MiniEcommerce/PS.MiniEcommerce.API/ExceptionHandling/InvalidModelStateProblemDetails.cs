using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace PS.MiniEcommerce.API.ExceptionHandling;

public static class InvalidModelStateProblemDetails
{
    public static IActionResult Create(ActionContext context)
    {
        var factory = context.HttpContext.RequestServices
            .GetRequiredService<ProblemDetailsFactory>();

        var problemDetails = factory.CreateValidationProblemDetails(
            context.HttpContext,
            context.ModelState);

        problemDetails.Extensions["code"] = "validation_error";

        return new BadRequestObjectResult(problemDetails)
        {
            ContentTypes = { "application/problem+json" }
        };
    }
}
