using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Products.Queries;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.UnitTests.Application.Products;

public class GetProductsQueryHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_WithProducts_ReturnsPaginatedDtos()
    {
        var first = Product.Create("Alpha", "First product.", 1.11m, 11, FixedUtcNow);
        var second = Product.Create("Beta", "Second product.", 2.22m, 22, FixedUtcNow);

        IReadOnlyList<Product> items = new List<Product> { first, second };

        var repository = Substitute.For<IProductRepository>();
        repository
            .GetActivePagedAsync(1, 20, Arg.Any<CancellationToken>())
            .Returns((items, 2));

        var handler = new GetProductsQueryHandler(repository);

        var result = await handler.Handle(new GetProductsQuery(1, 20), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(2, result.TotalCount);

        Assert.Equal(first.Id, result.Items[0].Id);
        Assert.Equal(first.Name, result.Items[0].Name);
        Assert.Equal(first.Description, result.Items[0].Description);
        Assert.Equal(first.Price, result.Items[0].Price);
        Assert.Equal(first.StockQuantity, result.Items[0].StockQuantity);

        Assert.Equal(second.Id, result.Items[1].Id);
        Assert.Equal(second.Name, result.Items[1].Name);
        Assert.Equal(second.Description, result.Items[1].Description);
        Assert.Equal(second.Price, result.Items[1].Price);
        Assert.Equal(second.StockQuantity, result.Items[1].StockQuantity);
    }

    [Fact]
    public async Task Handle_WithNoProducts_ReturnsEmptyPaginatedResult()
    {
        IReadOnlyList<Product> items = new List<Product>();

        var repository = Substitute.For<IProductRepository>();
        repository
            .GetActivePagedAsync(1, 20, Arg.Any<CancellationToken>())
            .Returns((items, 0));

        var handler = new GetProductsQueryHandler(repository);

        var result = await handler.Handle(new GetProductsQuery(1, 20), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Handle_PassesPageAndPageSizeToRepository()
    {
        IReadOnlyList<Product> items = new List<Product>();

        var repository = Substitute.For<IProductRepository>();
        repository
            .GetActivePagedAsync(3, 5, Arg.Any<CancellationToken>())
            .Returns((items, 0));

        var handler = new GetProductsQueryHandler(repository);

        await handler.Handle(new GetProductsQuery(3, 5), CancellationToken.None);

        await repository.Received(1).GetActivePagedAsync(3, 5, Arg.Any<CancellationToken>());
    }
}
