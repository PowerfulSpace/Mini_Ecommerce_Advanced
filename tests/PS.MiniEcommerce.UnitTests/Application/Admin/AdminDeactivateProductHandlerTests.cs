using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Admin.Commands;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;

namespace PS.MiniEcommerce.UnitTests.Application.Admin;

public class AdminDeactivateProductHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_WhenProductExists_ReturnsProductAndPublishesEvent()
    {
        var product = CreateProduct();
        product.Deactivate(FixedUtcNow);
        var domainEvent = Assert.Single(product.DomainEvents.OfType<ProductDeactivated>());
        var repository = Substitute.For<IProductRepository>();
        repository
            .DeactivateAsync(product.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((product, new object[] { domainEvent }));
        var mediator = Substitute.For<IMediator>();
        var handler = CreateHandler(repository, mediator);

        var result = await handler.Handle(
            new AdminDeactivateProductCommand(product.Id),
            CancellationToken.None);

        Assert.Equal(product.Id, result.Product.Id);
        Assert.False(product.IsActive);
        await mediator.Received(1).Publish(
            Arg.Is<DomainEventNotification<object>>(notification =>
                ReferenceEquals(notification.Event, domainEvent)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenProductNotFound_ThrowsProductNotFound()
    {
        var repository = Substitute.For<IProductRepository>();
        repository
            .DeactivateAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(((Product?)null, Array.Empty<object>()));
        var handler = CreateHandler(repository);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(
                new AdminDeactivateProductCommand(Guid.NewGuid()),
                CancellationToken.None));

        Assert.Equal("product_not_found", exception.Code);
    }

    private static AdminDeactivateProductHandler CreateHandler(
        IProductRepository repository,
        IMediator? mediator = null) =>
        new(
            repository,
            mediator ?? Substitute.For<IMediator>(),
            Substitute.For<ILogger<AdminDeactivateProductHandler>>());

    private static Product CreateProduct() =>
        Product.Create(
            "Widget",
            "A widget.",
            Money.FromDecimal(9.99m),
            10,
            FixedUtcNow);
}
