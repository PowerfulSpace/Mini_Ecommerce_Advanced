using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Application.Orders.Queries;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.UnitTests.Application.Orders;

public class GetOrderByIdHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProductA = Guid.NewGuid();
    private static readonly byte[] RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

    [Fact]
    public async Task Handle_WhenOrderExists_ReturnsOrderDtoAndRowVersion()
    {
        var order = Order.Create(UserId, new[] { new OrderItemData(ProductA, "Widget", Money.FromDecimal(19.99m), 3) }, FixedUtcNow);

        var repository = Substitute.For<IOrderRepository>();
        repository
            .GetByIdWithItemsAsync(order.Id, UserId, Arg.Any<CancellationToken>())
            .Returns(((Order?)order, (byte[]?)RowVersion));

        var handler = new GetOrderByIdHandler(repository, CreateCurrentUser());

        var result = await handler.Handle(new GetOrderByIdQuery(order.Id), CancellationToken.None);

        Assert.Equal(order.Id, result.Order.Id);
        Assert.Equal("Created", result.Order.Status);
        Assert.Equal(order.CreatedAtUtc, result.Order.CreatedAtUtc);
        Assert.Equal(order.TotalAmount.Amount, result.Order.TotalAmount);
        Assert.Equal(RowVersion, result.RowVersion);
        var item = Assert.Single(result.Order.Items);
        Assert.Equal(ProductA, item.ProductId);
        Assert.Equal(59.97m, item.LineTotal);
    }

    [Fact]
    public async Task Handle_WhenOrderMissing_ThrowsNotFoundWithOrderNotFound()
    {
        var repository = Substitute.For<IOrderRepository>();
        repository
            .GetByIdWithItemsAsync(Arg.Any<Guid>(), UserId, Arg.Any<CancellationToken>())
            .Returns(((Order?)null, (byte[]?)null));

        var handler = new GetOrderByIdHandler(repository, CreateCurrentUser());

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new GetOrderByIdQuery(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("order_not_found", exception.Code);
    }

    private static ICurrentUser CreateCurrentUser()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);
        return currentUser;
    }
}
