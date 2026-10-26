using PS.MiniEcommerce.Application.Carts.DTOs;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Application.Carts;

public static class CartDtoBuilder
{
    public static CartDto Build(Cart cart, IReadOnlyList<Product> products)
    {
        var productsById = products.ToDictionary(product => product.Id);

        var itemDtos = cart.Items
            .Select(item =>
            {
                var product = productsById[item.ProductId];

                return new CartItemDto(
                    item.ProductId,
                    product.Name,
                    product.Price,
                    item.Quantity,
                    product.Price * item.Quantity,
                    product.IsActive);
            })
            .ToList();

        var totalAmount = itemDtos.Sum(item => item.LineTotal);

        return new CartDto(itemDtos, totalAmount);
    }
}
