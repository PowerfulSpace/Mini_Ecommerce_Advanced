namespace PS.MiniEcommerce.Application.Abstractions.Persistence;

public enum OrderTransitionResult
{
    Success,
    NotFound,
    InvalidTransition
}
