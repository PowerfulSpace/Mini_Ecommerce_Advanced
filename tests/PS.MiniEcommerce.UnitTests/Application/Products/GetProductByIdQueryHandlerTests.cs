using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Application.Products.Queries;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.UnitTests.Application.Products;

public class GetProductByIdQueryHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_WhenProductExists_ReturnsDto()
    {
        var product = Product.Create("Alpha", "First product.", 1.11m, 11, FixedUtcNow);

        var repository = Substitute.For<IProductRepository>();
        repository
            .GetActiveByIdAsync(product.Id, Arg.Any<CancellationToken>())
            .Returns(product);

        var handler = new GetProductByIdQueryHandler(repository);

        var dto = await handler.Handle(new GetProductByIdQuery(product.Id), CancellationToken.None);

        Assert.Equal(product.Id, dto.Id);
        Assert.Equal(product.Name, dto.Name);
        Assert.Equal(product.Description, dto.Description);
        Assert.Equal(product.Price, dto.Price);
        Assert.Equal(product.StockQuantity, dto.StockQuantity);
    }

    [Fact]
    public async Task Handle_WhenProductMissing_ThrowsNotFoundExceptionWithProductNotFoundCode()
    {
        var repository = Substitute.For<IProductRepository>();
        repository
            .GetActiveByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Product?)null);

        var handler = new GetProductByIdQueryHandler(repository);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new GetProductByIdQuery(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("product_not_found", exception.Code);
    }
}
