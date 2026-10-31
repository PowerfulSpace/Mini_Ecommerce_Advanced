using Microsoft.Extensions.Options;
using PS.MiniEcommerce.Application.Abstractions.Payments;

namespace PS.MiniEcommerce.Infrastructure.Payments;

public sealed class StripeFeature : IStripeFeature
{
    private readonly StripeOptions _options;

    public StripeFeature(IOptions<StripeOptions> options)
    {
        _options = options.Value;
    }

    public bool Enabled => _options.Enabled;
}
