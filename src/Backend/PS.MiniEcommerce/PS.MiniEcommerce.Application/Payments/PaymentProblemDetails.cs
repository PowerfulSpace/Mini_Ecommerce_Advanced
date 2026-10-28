using System.Text.Json;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Application.Payments;

public static class PaymentProblemDetails
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static (int StatusCode, string Payload) Map(Exception exception)
    {
        return exception switch
        {
            ValidationException validation => (400, BuildPayload(
                "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                "One or more validation errors occurred.",
                400,
                "The request body is invalid.",
                "validation_error",
                validation.Errors)),

            DomainValidationException domainValidation => (400, BuildPayload(
                "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                "Validation error.",
                400,
                domainValidation.Message,
                "validation_error")),

            DomainConflictException domainConflict => MapConflict(domainConflict),

            NotFoundException notFound => (404, BuildPayload(
                "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                "Resource not found.",
                404,
                notFound.Message,
                notFound.Code)),

            _ => (409, BuildPayload(
                "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                "Conflict.",
                409,
                exception.Message,
                "conflict"))
        };
    }

    private static (int StatusCode, string Payload) MapConflict(DomainConflictException exception)
    {
        return exception.Code switch
        {
            "idempotency_in_progress" => (503, BuildPayload(
                "https://tools.ietf.org/html/rfc9110#section-15.6.4",
                "Service unavailable.",
                503,
                exception.Message,
                exception.Code)),

            _ => (409, BuildPayload(
                "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                "Conflict.",
                409,
                exception.Message,
                exception.Code))
        };
    }

    private static string BuildPayload(
        string type,
        string title,
        int status,
        string detail,
        string code,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["title"] = title,
            ["status"] = status,
            ["detail"] = detail,
            ["code"] = code
        };

        if (errors is not null)
        {
            payload["errors"] = errors;
        }

        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
