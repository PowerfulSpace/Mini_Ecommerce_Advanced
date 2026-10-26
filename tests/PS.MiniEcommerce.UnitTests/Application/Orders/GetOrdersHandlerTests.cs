using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Orders.Queries;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.UnitTests.Application.Orders;

public class GetOrdersHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProductA = Guid.NewGuid();

    [Fact]
    public async Task Handle_WithOrders_ReturnsPaginatedSummaryDtos()
    {
        var first = Order.Create(UserId, new[] { new OrderItemData(ProductA, "Widget", 19.99m, 1) }, FixedUtcNow);
        var second = Order.Create(UserId, new[] { new OrderItemData(ProductA, "Gadget", 5.50m, 2) }, FixedUtcNow);
        IReadOnlyList<Order> items = new List<Order> { first, second };

        var repository = Substitute.For<IOrderRepository>();
        repository
            .GetPagedByUserIdAsync(UserId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((items, 2));

        var handler = new GetOrdersHandler(repository, CreateCurrentUser());

        var result = await handler.Handle(new GetOrdersQuery(1, 20), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(2, result.TotalCount);

        Assert.Equal(first.Id, result.Items[0].Id);
        Assert.Equal("Created", result.Items[0].Status);
        Assert.Equal(first.CreatedAtUtc, result.Items[0].CreatedAtUtc);
        Assert.Equal(first.TotalAmount, result.Items[0].TotalAmount);

        Assert.Equal(second.Id, result.Items[1].Id);
        Assert.Equal("Created", result.Items[1].Status);
        Assert.Equal(second.CreatedAtUtc, result.Items[1].CreatedAtUtc);
        Assert.Equal(second.TotalAmount, result.Items[1].TotalAmount);
    }

    [Fact]
    public async Task Handle_PassesCurrentUserIdToRepository()
    {
        IReadOnlyList<Order> items = new List<Order>();

        var repository = Substitute.For<IOrderRepository>();
        repository
            .GetPagedByUserIdAsync(UserId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((items, 0));

        var handler = new GetOrdersHandler(repository, CreateCurrentUser());

        await handler.Handle(new GetOrdersQuery(1, 20), CancellationToken.None);

        await repository.Received(1).GetPagedByUserIdAsync(UserId, 1, 20, Arg.Any<CancellationToken>());
    }

    private static ICurrentUser CreateCurrentUser()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);
        return currentUser;
    }
}
