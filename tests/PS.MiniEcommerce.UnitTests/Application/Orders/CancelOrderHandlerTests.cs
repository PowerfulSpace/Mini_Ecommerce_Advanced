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

    [Fact]
    public async Task Handle_WhenCancelSucceeds_ReturnsOrderDto()
    {
        var order = CreateOrder();
        order.Cancel();

        var repository = Substitute.For<IOrderRepository>();
        repository
            .CancelAsync(order.Id, UserId, Arg.Any<CancellationToken>())
            .Returns((CancelOrderResult.Success, (Order?)order));

        var handler = new CancelOrderHandler(repository, CreateCurrentUser());

        var dto = await handler.Handle(new CancelOrderCommand(order.Id), CancellationToken.None);

        Assert.Equal(order.Id, dto.Id);
        Assert.Equal("Cancelled", dto.Status);
    }

    [Fact]
    public async Task Handle_WhenOrderNotFound_ThrowsNotFoundWithOrderNotFound()
    {
        var repository = Substitute.For<IOrderRepository>();
        repository
            .CancelAsync(Arg.Any<Guid>(), UserId, Arg.Any<CancellationToken>())
            .Returns((CancelOrderResult.NotFound, (Order?)null));

        var handler = new CancelOrderHandler(repository, CreateCurrentUser());

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new CancelOrderCommand(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("order_not_found", exception.Code);
    }

    [Fact]
    public async Task Handle_WhenOrderAlreadyCancelled_ThrowsDomainConflictWithOrderAlreadyCancelled()
    {
        var repository = Substitute.For<IOrderRepository>();
        repository
            .CancelAsync(Arg.Any<Guid>(), UserId, Arg.Any<CancellationToken>())
            .Returns((CancelOrderResult.AlreadyCancelled, (Order?)null));

        var handler = new CancelOrderHandler(repository, CreateCurrentUser());

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(new CancelOrderCommand(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("order_already_cancelled", exception.Code);
    }

    private static Order CreateOrder() =>
        Order.Create(UserId, new[] { new OrderItemData(ProductA, "Widget", 19.99m, 1) }, FixedUtcNow);

    private static ICurrentUser CreateCurrentUser()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);
        return currentUser;
    }
}
