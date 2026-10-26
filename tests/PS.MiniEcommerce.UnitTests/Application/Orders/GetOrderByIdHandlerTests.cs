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

    [Fact]
    public async Task Handle_WhenOrderExists_ReturnsOrderDto()
    {
        var order = Order.Create(UserId, new[] { new OrderItemData(ProductA, "Widget", 19.99m, 3) }, FixedUtcNow);

        var repository = Substitute.For<IOrderRepository>();
        repository
            .GetByIdWithItemsAsync(order.Id, UserId, Arg.Any<CancellationToken>())
            .Returns(order);

        var handler = new GetOrderByIdHandler(repository, CreateCurrentUser());

        var dto = await handler.Handle(new GetOrderByIdQuery(order.Id), CancellationToken.None);

        Assert.Equal(order.Id, dto.Id);
        Assert.Equal("Created", dto.Status);
        Assert.Equal(order.CreatedAtUtc, dto.CreatedAtUtc);
        Assert.Equal(order.TotalAmount, dto.TotalAmount);
        var item = Assert.Single(dto.Items);
        Assert.Equal(ProductA, item.ProductId);
        Assert.Equal(59.97m, item.LineTotal);
    }

    [Fact]
    public async Task Handle_WhenOrderMissing_ThrowsNotFoundWithOrderNotFound()
    {
        var repository = Substitute.For<IOrderRepository>();
        repository
            .GetByIdWithItemsAsync(Arg.Any<Guid>(), UserId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

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
