namespace LMS.Api.application.abstractions.persistence;

public interface ITransactionManager
{
    Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default);

    Task ExecuteSerializableAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(action, cancellationToken);
}
