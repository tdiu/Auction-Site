using API.Entities;

namespace API.Interfaces;

public interface IOutboxHandler
{
    string Type { get; }

    /// <summary>Perform the side effect. Two rules, both load-bearing:
    /// <para>1. Any DB write MUST be keyed by a deterministic id derived from the payload. The
    /// dispatcher reads a unique violation as proof that a prior claim already delivered this
    /// message and marks it Processed — so a collision on any *other* unique constraint would be
    /// silently swallowed as success.</para>
    /// <para>2. Do external I/O here; stage DB writes only. The dispatcher owns the commit, and
    /// commits writes together with the message's Status in one transaction.</para></summary>
    Task Handle(OutboxMessage message, CancellationToken ct);
}
