using PS.MiniEcommerce.Infrastructure.Persistence;

namespace PS.MiniEcommerce.UnitTests.Infrastructure;

public class OutboxMessageTests
{
    [Fact]
    public void MarkFailedAttempt_WhenBelowMax_KeepsMessagePending()
    {
        var utcNow = DateTime.UtcNow;
        var message = OutboxMessage.Create("event", "{}", utcNow);

        message.MarkFailedAttempt("failure", utcNow.AddSeconds(1), maxAttempts: 5);

        Assert.Equal(1, message.Attempts);
        Assert.Equal(utcNow.AddSeconds(1), message.LastAttemptAtUtc);
        Assert.Equal("failure", message.LastError);
        Assert.Null(message.ProcessedAtUtc);
    }

    [Fact]
    public void MarkFailedAttempt_WhenReachingMax_MarksMessageProcessed()
    {
        var utcNow = DateTime.UtcNow;
        var message = OutboxMessage.Create("event", "{}", utcNow);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            message.MarkFailedAttempt("failure", utcNow.AddSeconds(attempt), maxAttempts: 5);
        }

        Assert.Equal(5, message.Attempts);
        Assert.Equal(utcNow.AddSeconds(5), message.ProcessedAtUtc);
        Assert.Equal("failure", message.LastError);
    }
}
