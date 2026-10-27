using PS.MiniEcommerce.Application.Carts.DTOs;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Application.Carts;

public static class CartDtoBuilder
{
    public static CartDto Build(Cart cart, IReadOnlyList<Product> products)
    {
        var productsById = products.ToDictionary(product => product.Id);

        var itemDtos = new List<CartItemDto>(cart.Items.Count);
        var totalAmount = Money.Zero;

        foreach (var item in cart.Items)
        {
            var product = productsById[item.ProductId];
            var lineTotal = product.Price * item.Quantity;

            itemDtos.Add(new CartItemDto(
                item.ProductId,
                product.Name,
                product.Price.Amount,
                item.Quantity,
                lineTotal.Amount,
                product.IsActive));

            totalAmount += lineTotal;
        }

        return new CartDto(itemDtos, totalAmount.Amount);
    }
}
