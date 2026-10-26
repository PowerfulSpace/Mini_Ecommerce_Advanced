using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PS.MiniEcommerce.Application.Checkout.Exceptions;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.API.ExceptionHandling;

public class ExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, problem) = MapException(exception, httpContext);

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }

    private static (int StatusCode, ProblemDetails? Problem) MapException(Exception exception, HttpContext context)
    {
        var instance = context.Request.Path;

        return exception switch
        {
            ValidationException validation => (
                StatusCodes.Status400BadRequest,
                new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                    Title = "One or more validation errors occurred.",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "The request body is invalid.",
                    Instance = instance,
                    Extensions =
                    {
                        ["errors"] = validation.Errors,
                        ["code"] = "validation_error"
                    }
                }),

            DomainValidationException domainValidation => (
                StatusCodes.Status400BadRequest,
                new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                    Title = "Validation error.",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = domainValidation.Message,
                    Instance = instance,
                    Extensions = { ["code"] = "validation_error" }
                }),

            DomainConflictException domainConflict => MapDomainConflict(domainConflict, instance),

            NotFoundException notFound => (
                StatusCodes.Status404NotFound,
                new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                    Title = "Resource not found.",
                    Status = StatusCodes.Status404NotFound,
                    Detail = notFound.Message,
                    Instance = instance,
                    Extensions = { ["code"] = notFound.Code }
                }),

            CheckoutCommitUncertainException commitUncertain => (
                StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.6.4",
                    Title = "Service unavailable.",
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Detail = commitUncertain.Message,
                    Instance = instance,
                    Extensions = { ["code"] = "idempotency_in_progress" }
                }),

            _ => (-1, null)
        };
    }

    private static (int StatusCode, ProblemDetails Problem) MapDomainConflict(
        DomainConflictException exception,
        PathString instance)
    {
        var (statusCode, type, title) = exception.Code switch
        {
            "precondition_required" => (
                StatusCodes.Status428PreconditionRequired,
                "https://tools.ietf.org/html/rfc6585#section-3",
                "Precondition required."),
            "cart_version_conflict" => (
                StatusCodes.Status412PreconditionFailed,
                "https://tools.ietf.org/html/rfc9110#section-15.5.13",
                "Precondition failed."),
            "idempotency_in_progress" => (
                StatusCodes.Status503ServiceUnavailable,
                "https://tools.ietf.org/html/rfc9110#section-15.6.4",
                "Service unavailable."),
            _ => (
                StatusCodes.Status409Conflict,
                "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                "Conflict.")
        };

        var problem = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = statusCode,
            Detail = exception.Message,
            Instance = instance,
            Extensions = { ["code"] = exception.Code }
        };

        return (statusCode, problem);
    }
}
