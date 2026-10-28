using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Admin;
using PS.MiniEcommerce.Application.Admin.Commands;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.UnitTests.Application.Admin;

public class AdminDeliverOrderHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenOrderIsShipped_ReturnsDeliveredOrderAndPublishesEvent()
    {
        var order = CreateOrder(OrderStatus.Shipped);
        order.Deliver(FixedUtcNow);
        var domainEvent = Assert.Single(order.DomainEvents.OfType<OrderDelivered>());
        var repository = Substitute.For<IOrderRepository>();
        repository
            .DeliverAsync(order.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((
                OrderTransitionResult.Success,
                order,
                OrderStatus.Shipped,
                new byte[] { 1, 2, 3 },
                new object[] { domainEvent }));
        var mediator = Substitute.For<IMediator>();
        var handler = CreateHandler(repository, mediator);

        var result = await handler.Handle(
            new AdminDeliverOrderCommand(order.Id),
            CancellationToken.None);

        Assert.Equal(order.Id, result.Order.Id);
        Assert.Equal("Delivered", result.Order.Status);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.RowVersion);
        await mediator.Received(1).Publish(
            Arg.Is<DomainEventNotification<object>>(notification =>
                ReferenceEquals(notification.Event, domainEvent)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenStatusDoesNotAllowDelivery_ThrowsInvalidOrderTransition()
    {
        var repository = Substitute.For<IOrderRepository>();
        repository
            .DeliverAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((
                OrderTransitionResult.InvalidTransition,
                (Order?)null,
                OrderStatus.Paid,
                (byte[]?)null,
                (IReadOnlyList<object>)Array.Empty<object>()));
        var handler = CreateHandler(repository);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(
                new AdminDeliverOrderCommand(Guid.NewGuid()),
                CancellationToken.None));

        Assert.Equal("invalid_order_transition", exception.Code);
    }

    [Fact]
    public async Task Handle_WhenOrderNotFound_ThrowsOrderNotFound()
    {
        var repository = Substitute.For<IOrderRepository>();
        repository
            .DeliverAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((
                OrderTransitionResult.NotFound,
                (Order?)null,
                (OrderStatus?)null,
                (byte[]?)null,
                (IReadOnlyList<object>)Array.Empty<object>()));
        var handler = CreateHandler(repository);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(
                new AdminDeliverOrderCommand(Guid.NewGuid()),
                CancellationToken.None));

        Assert.Equal("order_not_found", exception.Code);
    }

    private static AdminDeliverOrderHandler CreateHandler(
        IOrderRepository repository,
        IMediator? mediator = null) =>
        new(
            repository,
            mediator ?? Substitute.For<IMediator>(),
            Substitute.For<ILogger<AdminDeliverOrderHandler>>());

    private static Order CreateOrder(OrderStatus status)
    {
        var order = Order.Create(
            UserId,
            new[] { new OrderItemData(ProductId, "Widget", Money.FromDecimal(19.99m), 2) },
            FixedUtcNow,
            paymentId: Guid.NewGuid());

        order.ClearDomainEvents();

        if (status is OrderStatus.Paid or OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Returned)
        {
            order.MarkPaid(FixedUtcNow);
        }

        if (status is OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Returned)
        {
            order.Ship(FixedUtcNow);
        }

        if (status is OrderStatus.Delivered or OrderStatus.Returned)
        {
            order.Deliver(FixedUtcNow);
        }

        if (status == OrderStatus.Returned)
        {
            order.Return(FixedUtcNow);
        }

        order.ClearDomainEvents();

        return order;
    }
}
