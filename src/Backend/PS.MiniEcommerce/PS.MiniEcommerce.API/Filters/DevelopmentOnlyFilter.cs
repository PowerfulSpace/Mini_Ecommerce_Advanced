using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PS.MiniEcommerce.API.Filters;

public sealed class DevelopmentOnlyFilter : IAsyncAuthorizationFilter, IOrderedFilter
{
    private readonly IWebHostEnvironment _environment;

    public DevelopmentOnlyFilter(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public int Order => int.MinValue;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!_environment.IsDevelopment())
        {
            context.Result = new NotFoundResult();
        }

        return Task.CompletedTask;
    }
}
