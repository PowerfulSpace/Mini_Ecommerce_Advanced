using System.Security.Claims;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;

namespace PS.MiniEcommerce.API.Authentication;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid UserId
    {
        get
        {
            var sub = _httpContextAccessor.HttpContext?.User.FindFirstValue("sub");

            if (string.IsNullOrWhiteSpace(sub) || !Guid.TryParse(sub, out var userId))
            {
                throw new InvalidOperationException("The current user is not authenticated.");
            }

            return userId;
        }
    }
}
