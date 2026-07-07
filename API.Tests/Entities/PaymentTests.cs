using API.Entities;
using Xunit;

namespace API.Tests.Entities;

public class PaymentTests
{
    [Fact]
    public void MarkPaid_FromPending_SetsPaidAndCompletedAt()
    {
        var payment = new Payment { AuctionId = 1, UserId = "winner", Amount = 150m };
        var completedAt = DateTimeOffset.UtcNow;

        payment.MarkPaid(completedAt);

        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(completedAt, payment.CompletedAt);
    }

    [Fact]
    public void MarkPaid_WhenAlreadyPaid_IsNoOp()
    {
        var firstCompletedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var payment = new Payment
        {
            AuctionId = 1,
            UserId = "winner",
            Amount = 150m,
            Status = PaymentStatus.Paid,
            CompletedAt = firstCompletedAt
        };

        payment.MarkPaid(DateTimeOffset.UtcNow); // later timestamp must not overwrite

        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(firstCompletedAt, payment.CompletedAt);
    }
}
