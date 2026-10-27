using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Application.Orders.Commands;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.UnitTests.Application.Orders;

public class CancelOrderHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProductA = Guid.NewGuid();
    private static readonly byte[] RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
    private const string IfMatch = "\"AQIDBAUGBwg=\"";

    [Fact]
    public async Task Handle_WhenCreatedOrderCancelled_ReturnsCancelledOrderDtoAndRowVersion()
    {
        var order = CreateOrder();
        order.Cancel(FixedUtcNow);

        var repository = Substitute.For<IOrderRepository>();
        repository
            .CancelAsync(
                order.Id,
                UserId,
                IfMatch,
                Arg.Any<IReadOnlyList<(Guid OrderItemId, int Quantity)>?>(),
                Arg.Any<CancellationToken>())
            .Returns((CancelOrderResult.Success, (Order?)order, (OrderStatus?)OrderStatus.Created, RowVersion));

        var handler = new CancelOrderHandler(repository, CreateCurrentUser());

        var result = await handler.Handle(new CancelOrderCommand(order.Id, IfMatch), CancellationToken.None);

        Assert.Equal(order.Id, result.Order.Id);
        Assert.Equal("Cancelled", result.Order.Status);
        Assert.Equal(RowVersion, result.RowVersion);
        await repository.Received(1).CancelAsync(
            order.Id,
            UserId,
            IfMatch,
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenPartialCancellationRequested_PassesItemsToRepository()
    {
        var order = CreateOrder();
        var itemId = Assert.Single(order.Items).Id;
        order.CancelItems(new[] { (itemId, 1) }, FixedUtcNow);

        var repository = Substitute.For<IOrderRepository>();
        repository
            .CancelAsync(
                order.Id,
                UserId,
                IfMatch,
                Arg.Any<IReadOnlyList<(Guid OrderItemId, int Quantity)>?>(),
                Arg.Any<CancellationToken>())
            .Returns((CancelOrderResult.Success, (Order?)order, (OrderStatus?)OrderStatus.Created, RowVersion));

        var handler = new CancelOrderHandler(repository, CreateCurrentUser());

        var result = await handler.Handle(
            new CancelOrderCommand(order.Id, IfMatch, new[] { new CancelOrderItemDto(itemId, 1) }),
            CancellationToken.None);

        Assert.Equal("Created", result.Order.Status);
        var item = Assert.Single(result.Order.Items);
        Assert.Equal(1, item.CancelledQuantity);
        await repository.Received(1).CancelAsync(
            order.Id,
            UserId,
            IfMatch,
            Arg.Is<IReadOnlyList<(Guid OrderItemId, int Quantity)>?>(items =>
                items != null
                && items.Count == 1
                && items[0].OrderItemId == itemId
                && items[0].Quantity == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenIfMatchMissing_ThrowsPreconditionRequiredWithoutCallingRepository()
    {
        var repository = Substitute.For<IOrderRepository>();
        var handler = new CancelOrderHandler(repository, CreateCurrentUser());

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(new CancelOrderCommand(Guid.NewGuid(), string.Empty), CancellationToken.None));

        Assert.Equal("precondition_required", exception.Code);
        await repository.DidNotReceive().CancelAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<(Guid OrderItemId, int Quantity)>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenOrderNotFound_ThrowsNotFoundWithOrderNotFound()
    {
        var repository = Substitute.For<IOrderRepository>();
        repository
            .CancelAsync(
                Arg.Any<Guid>(),
                UserId,
                IfMatch,
                Arg.Any<IReadOnlyList<(Guid OrderItemId, int Quantity)>?>(),
                Arg.Any<CancellationToken>())
            .Returns((CancelOrderResult.NotFound, (Order?)null, (OrderStatus?)null, (byte[]?)null));

        var handler = new CancelOrderHandler(repository, CreateCurrentUser());

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new CancelOrderCommand(Guid.NewGuid(), IfMatch), CancellationToken.None));

        Assert.Equal("order_not_found", exception.Code);
    }

    [Fact]
    public async Task Handle_WhenOrderAlreadyCancelled_ThrowsDomainConflictWithOrderAlreadyCancelled()
    {
        var repository = Substitute.For<IOrderRepository>();
        repository
            .CancelAsync(
                Arg.Any<Guid>(),
                UserId,
                IfMatch,
                Arg.Any<IReadOnlyList<(Guid OrderItemId, int Quantity)>?>(),
                Arg.Any<CancellationToken>())
            .Returns((CancelOrderResult.AlreadyCancelled, (Order?)null, (OrderStatus?)OrderStatus.Cancelled, (byte[]?)null));

        var handler = new CancelOrderHandler(repository, CreateCurrentUser());

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(new CancelOrderCommand(Guid.NewGuid(), IfMatch), CancellationToken.None));

        Assert.Equal("order_already_cancelled", exception.Code);
    }

    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Returned)]
    public async Task Handle_WhenStatusDoesNotAllowCancellation_ThrowsInvalidOrderTransition(
        OrderStatus status)
    {
        var repository = Substitute.For<IOrderRepository>();
        repository
            .CancelAsync(
                Arg.Any<Guid>(),
                UserId,
                IfMatch,
                Arg.Any<IReadOnlyList<(Guid OrderItemId, int Quantity)>?>(),
                Arg.Any<CancellationToken>())
            .Returns((CancelOrderResult.InvalidTransition, (Order?)null, (OrderStatus?)status, (byte[]?)null));

        var handler = new CancelOrderHandler(repository, CreateCurrentUser());

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(new CancelOrderCommand(Guid.NewGuid(), IfMatch), CancellationToken.None));

        Assert.Equal("invalid_order_transition", exception.Code);
        Assert.Contains(status.ToString(), exception.Message);
        Assert.Contains("Cancelled", exception.Message);
    }

    private static Order CreateOrder(Guid? paymentId = null) =>
        Order.Create(
            UserId,
            new[] { new OrderItemData(ProductA, "Widget", Money.FromDecimal(19.99m), 3) },
            FixedUtcNow,
            paymentId);

    private static ICurrentUser CreateCurrentUser()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);
        return currentUser;
    }
}
