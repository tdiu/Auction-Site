using Microsoft.EntityFrameworkCore.Storage;

namespace API.Tests.Fakes;

/// <summary>
/// A no-op <see cref="IDbContextTransaction"/> that records which way it was closed, so a test can
/// assert the rotation rolled back rather than committed when it lost the claim. Hand-written rather
/// than substituted because the service disposes it with <c>await using</c>, and a default
/// <c>DisposeAsync</c> from a mock is easy to get subtly wrong.
/// </summary>
public class RecordingTransaction : IDbContextTransaction
{
    public Guid TransactionId { get; } = Guid.NewGuid();
    public bool Committed { get; private set; }
    public bool RolledBack { get; private set; }
    public bool Disposed { get; private set; }

    public void Commit() => Committed = true;

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Committed = true;
        return Task.CompletedTask;
    }

    public void Rollback() => RolledBack = true;

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        RolledBack = true;
        return Task.CompletedTask;
    }

    public bool SupportsSavepoints => true;

    public List<string> Savepoints { get; } = [];

    public List<string> SavepointRollbacks { get; } = [];

    public void CreateSavepoint(string name) => Savepoints.Add(name);

    public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default)
    {
        Savepoints.Add(name);
        return Task.CompletedTask;
    }

    public void RollbackToSavepoint(string name) => SavepointRollbacks.Add(name);

    public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default)
    {
        SavepointRollbacks.Add(name);
        return Task.CompletedTask;
    }

    public void ReleaseSavepoint(string name) { }

    public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Dispose() => Disposed = true;

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
