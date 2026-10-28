using MediatR;

namespace PS.MiniEcommerce.Application.Admin.Commands;

public sealed record AdminUpdateProductStockCommand(Guid ProductId, int Available)
    : IRequest<AdminProductResult>;
