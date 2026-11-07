using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ClothingStore.Api;

public sealed class StoreException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;

    public static StoreException Conflict(string message) => new(409, message);
    public static StoreException NotFound(string message) => new(404, message);
}

public sealed class StoreExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        var problem = exception switch
        {
            StoreException error => (error.Status, error.Message),
            DbUpdateConcurrencyException => (409, "Запись уже изменена. Обновите данные и повторите запрос"),
            PostgresException pg => DatabaseProblem(pg),
            DbUpdateException { InnerException: PostgresException pg } => DatabaseProblem(pg),
            _ => default
        };

        if (problem == default)
            return false;

        await Results.Problem(statusCode: problem.Item1, title: problem.Item2).ExecuteAsync(context);
        return true;
    }

    private static (int, string) DatabaseProblem(PostgresException error) => error.SqlState switch
    {
        PostgresErrorCodes.UniqueViolation => (409, "Запись с такими данными уже существует"),
        PostgresErrorCodes.ForeignKeyViolation => (409, "Связанная запись отсутствует или используется"),
        PostgresErrorCodes.NumericValueOutOfRange => (409, "Превышен допустимый объём операции"),
        _ => default
    };
}
