using MediatR;
using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Application.Products.DTOs;

namespace PS.MiniEcommerce.Application.Admin.Commands;

public class AdminDeactivateProductHandler : IRequestHandler<AdminDeactivateProductCommand, AdminProductResult>
{
    private readonly IProductRepository _productRepository;
    private readonly IMediator _mediator;
    private readonly ILogger<AdminDeactivateProductHandler> _logger;

    public AdminDeactivateProductHandler(
        IProductRepository productRepository,
        IMediator mediator,
        ILogger<AdminDeactivateProductHandler> logger)
    {
        _productRepository = productRepository;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<AdminProductResult> Handle(
        AdminDeactivateProductCommand request,
        CancellationToken cancellationToken)
    {
        var (product, domainEvents) = await _productRepository.DeactivateAsync(
            request.ProductId,
            DateTime.UtcNow,
            cancellationToken);

        if (product is null)
        {
            throw new NotFoundException(
                $"Product with id {request.ProductId} was not found.",
                "product_not_found");
        }

        var result = new AdminProductResult(ProductDto.FromProduct(product), domainEvents);
        await PublishDomainEventsAsync(domainEvents, cancellationToken);

        return result;
    }

    private async Task PublishDomainEventsAsync(
        IReadOnlyList<object> domainEvents,
        CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            try
            {
                await _mediator.Publish(
                    new DomainEventNotification<object>(domainEvent),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to publish domain event {DomainEventType}.",
                    domainEvent.GetType().Name);
            }
        }
    }
}
