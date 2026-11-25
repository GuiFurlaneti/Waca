using Waca.Domain.Vaquinhas;

namespace Waca.Application.Vaquinhas;

/// <summary>
/// Porta de persistência das vaquinhas. A implementação fica fora da Application.
/// </summary>
/// <remarks>
/// Só oferece as operações necessárias aos casos de uso atuais: não há listagem geral,
/// atualização, exclusão, encerramento nem transferência de organização.
/// </remarks>
public interface IRepositorioVaquinhas
{
    /// <summary>
    /// Grava uma nova vaquinha. Só deve concluir com sucesso depois que a gravação estiver confirmada.
    /// </summary>
    Task AdicionarAsync(Vaquinha vaquinha, CancellationToken cancellationToken);

    /// <summary>Vaquinhas cujo organizador é <paramref name="organizadorId"/>.</summary>
    Task<IReadOnlyList<Vaquinha>> ListarPorOrganizadorAsync(Guid organizadorId, CancellationToken cancellationToken);
}
