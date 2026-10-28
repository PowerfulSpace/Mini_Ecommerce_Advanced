using MediatR;
using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Application.Products.DTOs;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Application.Admin.Commands;

public class AdminUpdateProductStockHandler
    : IRequestHandler<AdminUpdateProductStockCommand, AdminProductResult>
{
    private readonly IProductRepository _productRepository;
    private readonly IMediator _mediator;
    private readonly ILogger<AdminUpdateProductStockHandler> _logger;

    public AdminUpdateProductStockHandler(
        IProductRepository productRepository,
        IMediator mediator,
        ILogger<AdminUpdateProductStockHandler> logger)
    {
        _productRepository = productRepository;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<AdminProductResult> Handle(
        AdminUpdateProductStockCommand request,
        CancellationToken cancellationToken)
    {
        var (result, product, domainEvents) = await _productRepository.AdjustAvailableAsync(
            request.ProductId,
            request.Available,
            DateTime.UtcNow,
            cancellationToken);

        if (result == AdminStockUpdateResult.NotFound)
        {
            throw new NotFoundException(
                $"Product with id {request.ProductId} was not found.",
                "product_not_found");
        }

        if (result == AdminStockUpdateResult.Conflict)
        {
            throw new DomainConflictException(
                "The product stock was changed concurrently. Retry the operation.",
                "admin_stock_conflict");
        }

        var response = new AdminProductResult(ProductDto.FromProduct(product!), domainEvents);
        await PublishDomainEventsAsync(domainEvents, cancellationToken);

        return response;
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
