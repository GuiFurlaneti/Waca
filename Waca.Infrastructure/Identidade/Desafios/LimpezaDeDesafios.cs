using Microsoft.EntityFrameworkCore;

namespace Waca.Infrastructure.Identidade.Desafios;

/// <summary>
/// Remoção física de desafios antigos. Operação idempotente, sem agendamento (fica para a fase de hospedagem).
/// </summary>
/// <remarks>
/// Remove apenas desafios cuja expiração ocorreu há mais de <see cref="OpcoesDeDesafios.Retencao"/>. Como a
/// retenção cobre a janela longa de cota, nenhuma emissão ainda contada é apagada. A remoção nunca reabre um
/// desafio: um desafio ausente é recusado na verificação.
/// </remarks>
public sealed class LimpezaDeDesafios
{
    private readonly IDbContextFactory<WacaIdentidadeDbContext> _contextos;
    private readonly OpcoesDeDesafios _opcoes;
    private readonly TimeProvider _relogio;

    /// <exception cref="InvalidOperationException">Opções inválidas.</exception>
    public LimpezaDeDesafios(IDbContextFactory<WacaIdentidadeDbContext> contextos, OpcoesDeDesafios opcoes, TimeProvider relogio)
    {
        _contextos = contextos ?? throw new ArgumentNullException(nameof(contextos));
        _opcoes = opcoes ?? throw new ArgumentNullException(nameof(opcoes));
        _relogio = relogio ?? throw new ArgumentNullException(nameof(relogio));
        _opcoes.Validar();
    }

    /// <summary>Remove os desafios expirados há mais que a retenção e devolve quantos foram removidos.</summary>
    public async Task<int> RemoverAntigosAsync(CancellationToken cancellationToken)
    {
        var limite = _relogio.GetUtcNow() - _opcoes.Retencao;

        await using var contexto = await _contextos.CreateDbContextAsync(cancellationToken);
        return await contexto.DesafiosDeAcesso
            .Where(d => d.ExpiraEm < limite)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
