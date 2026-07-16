using API.Entities;
using API.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Data;

public class OutboxRepository(AppDbContext context) : IOutboxRepository
{
    public void Add(OutboxMessage message) => context.OutboxMessages.Add(message);

    public async Task<IReadOnlyList<Guid>> ClaimAndLeaseAsync(int batchSize, TimeSpan lease, int maxAttempts)
    {
        var leaseSeconds = lease.TotalSeconds;
        return await context.Database
            .SqlQuery<Guid>(
                $"""
                 UPDATE "OutboxMessages"
                 SET "VisibleAt" = now() + make_interval(secs => {leaseSeconds}),
                     "Attempts"  = "Attempts" + 1
                 WHERE "Id" IN (
                     SELECT "Id" FROM "OutboxMessages"
                     WHERE "Status" = 0
                       AND "VisibleAt" <= now()
                       AND "Attempts" < {maxAttempts}
                     ORDER BY "VisibleAt", "CreatedAt"
                     LIMIT {batchSize}
                     FOR UPDATE SKIP LOCKED
                 )
                 RETURNING "Id" AS "Value"
                 """)
            .ToListAsync();
    }

    public async Task<OutboxMessage?> GetAsync(Guid id) =>
        await context.OutboxMessages.FirstOrDefaultAsync(m => m.Id == id);

    public async Task<int> ReapExhaustedAsync(int maxAttempts) => await context.Database.ExecuteSqlAsync(
        $"""
         UPDATE "OutboxMessages"
         SET "Status" = 2,
             "LastError" = COALESCE("LastError", 'exhausted receives without reporting a failure')
         WHERE "Status" = 0 AND "Attempts" >= {maxAttempts} AND "VisibleAt" <= now()
         """
        );
}
