using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Waca.Infrastructure.Tests.Postgres;

/// <summary>
/// Registra o texto SQL dos comandos de leitura enviados pelo EF Core.
/// </summary>
public sealed class ComandosCapturados : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _comandos = new();

    public IReadOnlyCollection<string> Comandos => _comandos.ToArray();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _comandos.Enqueue(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
