namespace PS.MiniEcommerce.Application.Common.Pagination;

public sealed record PaginatedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);
