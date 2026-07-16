using API.Entities;

namespace API.Interfaces;

public interface IOutboxRepository
{
    void Add(OutboxMessage message);

    /// <summary> Atomically claim up to batchSize due messages: leases each past VisibleAt and counts
    /// the receive. A single statement, so it needs no ambient transaction. Returns ids, not entities.
    /// the dispatcher reloads each one in its own scope. </summary>
    Task<IReadOnlyList<Guid>> ClaimAndLeaseAsync(int batchSize, TimeSpan lease, int maxAttempts);
    Task<OutboxMessage?> GetAsync(Guid id);

    // dead letter rows that burned receive budget without ever reporting failure
    Task<int> ReapExhaustedAsync(int maxAttempts);
}
