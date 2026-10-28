using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Admin.Commands;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.UnitTests.Application.Admin;

public class AdminUpdateProductStockHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_WhenStockChanges_ReturnsProductAndPublishesStockAdjusted()
    {
        var product = CreateProduct(available: 10, reserved: 3);
        product.AdjustAvailable(20, FixedUtcNow);
        var domainEvent = Assert.Single(product.DomainEvents.OfType<StockAdjusted>());
        var repository = Substitute.For<IProductRepository>();
        repository
            .AdjustAvailableAsync(
                product.Id,
                20,
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns((AdminStockUpdateResult.Success, product, new object[] { domainEvent }));
        var mediator = Substitute.For<IMediator>();
        var handler = CreateHandler(repository, mediator);

        var result = await handler.Handle(
            new AdminUpdateProductStockCommand(product.Id, 20),
            CancellationToken.None);

        Assert.Equal(20, result.Product.Available);
        Assert.Equal(3, product.Reserved);
        await mediator.Received(1).Publish(
            Arg.Is<DomainEventNotification<object>>(notification =>
                ReferenceEquals(notification.Event, domainEvent)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenStockDoesNotChange_DoesNotPublishEvent()
    {
        var product = CreateProduct(available: 10);
        var repository = Substitute.For<IProductRepository>();
        repository
            .AdjustAvailableAsync(
                product.Id,
                10,
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns((AdminStockUpdateResult.Success, product, Array.Empty<object>()));
        var mediator = Substitute.For<IMediator>();
        var handler = CreateHandler(repository, mediator);

        var result = await handler.Handle(
            new AdminUpdateProductStockCommand(product.Id, 10),
            CancellationToken.None);

        Assert.Equal(10, result.Product.Available);
        await mediator.DidNotReceive().Publish(
            Arg.Any<INotification>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenProductNotFound_ThrowsProductNotFound()
    {
        var repository = Substitute.For<IProductRepository>();
        repository
            .AdjustAvailableAsync(
                Arg.Any<Guid>(),
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns((
                AdminStockUpdateResult.NotFound,
                (Product?)null,
                (IReadOnlyList<object>)Array.Empty<object>()));
        var handler = CreateHandler(repository);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(
                new AdminUpdateProductStockCommand(Guid.NewGuid(), 5),
                CancellationToken.None));

        Assert.Equal("product_not_found", exception.Code);
    }

    [Fact]
    public async Task Handle_WhenRetriesExhausted_ThrowsAdminStockConflict()
    {
        var repository = Substitute.For<IProductRepository>();
        repository
            .AdjustAvailableAsync(
                Arg.Any<Guid>(),
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns((
                AdminStockUpdateResult.Conflict,
                (Product?)null,
                (IReadOnlyList<object>)Array.Empty<object>()));
        var handler = CreateHandler(repository);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(
                new AdminUpdateProductStockCommand(Guid.NewGuid(), 5),
                CancellationToken.None));

        Assert.Equal("admin_stock_conflict", exception.Code);
    }

    private static AdminUpdateProductStockHandler CreateHandler(
        IProductRepository repository,
        IMediator? mediator = null) =>
        new(
            repository,
            mediator ?? Substitute.For<IMediator>(),
            Substitute.For<ILogger<AdminUpdateProductStockHandler>>());

    private static Product CreateProduct(int available, int reserved = 0) =>
        Product.Create(
            "Widget",
            "A widget.",
            Money.FromDecimal(9.99m),
            available,
            FixedUtcNow,
            reserved);
}
