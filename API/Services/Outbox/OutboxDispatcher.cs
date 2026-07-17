using API.Entities;
using API.Extensions;
using API.Interfaces;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace API.Services.Outbox;

public class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOutboxRepository outboxRepository,
    IConfiguration config,
    ILogger<OutboxDispatcher> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 55)]
    public async Task DispatchAsync(CancellationToken ct)
    {
        var batchSize = config.GetValue("Outbox:BatchSize", 20);
        var maxAttempts = config.GetValue("Outbox:MaxAttempts", 8);
        var lease = TimeSpan.FromMinutes(config.GetValue("Outbox:LeaseMinutes", 5));

        var reaped = await outboxRepository.ReapExhaustedAsync(maxAttempts);
        if (reaped > 0)
            logger.LogError("Outbox reaped {N} exhausted messages", reaped);

        var ids = await outboxRepository.ClaimAndLeaseAsync(batchSize, lease, maxAttempts);

        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested)
                break;
            await ProcessAsync(id, maxAttempts, ct);
        }
    }

    private async Task ProcessAsync(Guid id, int maxAttempts, CancellationToken ct)
    {
        // A DI scope per message. Handler that throws part-way through writes has
        // whole DbContext discarded unsaved, so partial writes can't reach database
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

        var message = await unitOfWork.Outbox.GetAsync(id);
        if (message is not { Status: OutboxMessageStatus.Pending }) return;

        try
        {
            // Handlers resolve from THIS scope: one injected into dispatchers own scope
            // would hold a different DbContext and its writes would not co-commit
            var handler = sp.GetServices<IOutboxHandler>().FirstOrDefault(h => h.Type == message.Type)
                          ?? throw new InvalidOperationException($"No handler for '{message.Type}'");

            // external I/O. No open transactions
            await handler.Handle(message, ct);

            message.Status = OutboxMessageStatus.Processed;
            message.ProcessedAt = DateTimeOffset.UtcNow;

            // handler's DB writes + Status, single commit
            await unitOfWork.CompleteAsync();
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            logger.LogWarning(e, "Outbox {id} was already delivered (duplicate key); marking processed", id);
            await MarkProcessedAsync(id, ct);
        }
        catch (Exception ex)
        {
            await RecordFailureAsync(id, ex, maxAttempts, ct);
        }
    }

    private async Task MarkProcessedAsync(Guid id, CancellationToken ct)
    {
        // Fresh scope. Caller's context is poisoned by a write that just failed
        using var scope = scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var message = await unitOfWork.Outbox.GetAsync(id);
        if (message is not { Status: OutboxMessageStatus.Pending }) return;

        message.Status = OutboxMessageStatus.Processed;
        message.ProcessedAt = DateTimeOffset.UtcNow;
        await unitOfWork.CompleteAsync();
    }

    private async Task RecordFailureAsync(Guid id, Exception ex, int maxAttempts, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var message = await unitOfWork.Outbox.GetAsync(id);

        // status guard. do not stomp attempts/last error onto a row that succeeded if
        // lease lapsed and another dispatcher delivered while we were failing
        if (message is not { Status: OutboxMessageStatus.Pending }) return;

        message.LastError = ex.Message;

        if (message.Attempts >= maxAttempts)
        {
            message.Status = OutboxMessageStatus.DeadLettered;
            logger.LogError(ex, "Outbox {id} dead-lettered after {N} receives", id, message.Attempts);
        }
        else
        {
            message.VisibleAt = DateTimeOffset.UtcNow + Backoff(message.Attempts);
            logger.LogWarning(ex, "Outbox {id} failed (receive {N}), retrying at {At}", id, message.Attempts, message.VisibleAt);
        }
        await unitOfWork.CompleteAsync();
    }

    private static TimeSpan Backoff(int attempts) => TimeSpan.FromMinutes(Math.Pow(2, attempts));
}
