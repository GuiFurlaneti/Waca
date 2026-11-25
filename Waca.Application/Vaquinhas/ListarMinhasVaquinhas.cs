using Waca.Application.Comum;
using Waca.Domain.Vaquinhas;

namespace Waca.Application.Vaquinhas;

/// <summary>
/// Caso de uso: o ator atual consulta as vaquinhas que ele organiza.
/// </summary>
/// <remarks>
/// <para>
/// O organizador consultado é sempre o ator atual; nenhum dado do cliente escolhe de quem são as vaquinhas.
/// Como defesa adicional, qualquer vaquinha devolvida pelo repositório com outro organizador é descartada.
/// </para>
/// <para>
/// A ordem é a devolvida pelo repositório. Não há paginação: ela deverá ser definida antes de uma API de produção.
/// </para>
/// </remarks>
public sealed class ListarMinhasVaquinhas
{
    private readonly IAtorAtual _atorAtual;
    private readonly IRepositorioVaquinhas _repositorio;

    public ListarMinhasVaquinhas(IAtorAtual atorAtual, IRepositorioVaquinhas repositorio)
    {
        ArgumentNullException.ThrowIfNull(atorAtual);
        ArgumentNullException.ThrowIfNull(repositorio);

        _atorAtual = atorAtual;
        _repositorio = repositorio;
    }

    /// <returns>As vaquinhas do ator; lista vazia se não houver nenhuma.</returns>
    /// <exception cref="AtorNaoIdentificadoException">Não há ator identificado.</exception>
    /// <exception cref="FalhaNoRepositorioException">A consulta não foi concluída.</exception>
    /// <exception cref="OperationCanceledException">A operação foi cancelada.</exception>
    public async Task<IReadOnlyList<VaquinhaResumo>> ExecutarAsync(CancellationToken cancellationToken)
    {
        var organizadorId = _atorAtual.ExigirPessoaId();

        IReadOnlyList<Vaquinha> vaquinhas;
        try
        {
            vaquinhas = await _repositorio.ListarPorOrganizadorAsync(organizadorId, cancellationToken);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            throw new FalhaNoRepositorioException("Não foi possível consultar as vaquinhas.", excecao);
        }

        if (vaquinhas is null)
        {
            throw new FalhaNoRepositorioException("O repositório não devolveu resultado.", null);
        }

        return vaquinhas
            .Where(v => v is not null && v.OrganizadorId == organizadorId)
            .Select(v => new VaquinhaResumo(v.Id, v.Nome, v.CriadaEm))
            .ToArray();
    }
}
