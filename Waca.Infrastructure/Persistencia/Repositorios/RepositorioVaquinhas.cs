using Microsoft.EntityFrameworkCore;
using Waca.Application.Vaquinhas;
using Waca.Domain.Vaquinhas;
using Waca.Infrastructure.Persistencia.Mapeamento;

namespace Waca.Infrastructure.Persistencia.Repositorios;

/// <summary>
/// Implementação de <see cref="IRepositorioVaquinhas"/> com EF Core e PostgreSQL.
/// </summary>
/// <remarks>
/// Falhas do banco são propagadas sem conversão; a Application decide como tratá-las.
/// </remarks>
public sealed class RepositorioVaquinhas : IRepositorioVaquinhas
{
    private readonly WacaDbContext _contexto;

    public RepositorioVaquinhas(WacaDbContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        _contexto = contexto;
    }

    /// <summary>
    /// Insere a vaquinha e só conclui depois que o banco confirma a gravação.
    /// </summary>
    /// <remarks>
    /// Se a gravação falhar (inclusive por identificador repetido), o registro deixa de ser acompanhado
    /// pelo contexto, para que uma nova chamada de <c>SaveChanges</c> não tente inseri-lo outra vez.
    /// </remarks>
    public async Task AdicionarAsync(Vaquinha vaquinha, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vaquinha);

        var entrada = _contexto.Vaquinhas.Add(VaquinhaMapeamento.ParaRegistro(vaquinha));
        try
        {
            await _contexto.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            entrada.State = EntityState.Detached;
            throw;
        }
    }

    /// <summary>
    /// Vaquinhas do organizador informado. O filtro é aplicado na consulta SQL.
    /// </summary>
    /// <exception cref="InvalidOperationException">Uma linha não representa uma vaquinha válida.</exception>
    public async Task<IReadOnlyList<Vaquinha>> ListarPorOrganizadorAsync(Guid organizadorId, CancellationToken cancellationToken)
    {
        var registros = await _contexto.Vaquinhas
            .AsNoTracking()
            .Where(v => v.OrganizadorId == organizadorId)
            .ToListAsync(cancellationToken);

        return registros.Select(VaquinhaMapeamento.ParaDominio).ToArray();
    }
}
