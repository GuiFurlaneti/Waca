using Waca.Application.Comum;
using Waca.Domain.Vaquinhas;

namespace Waca.Application.Vaquinhas;

/// <summary>
/// Caso de uso: o ator atual cria uma vaquinha da qual será o único organizador.
/// </summary>
/// <remarks>
/// <para>
/// O organizador é sempre o ator atual; identificador e instante são gerados aqui. A criação não gera
/// participação financeira para o organizador nem qualquer valor.
/// </para>
/// <para>
/// O resultado só é devolvido depois que o repositório confirma a gravação.
/// </para>
/// </remarks>
public sealed class CriarVaquinha
{
    private readonly IAtorAtual _atorAtual;
    private readonly IRepositorioVaquinhas _repositorio;
    private readonly IGeradorDeIdentificadores _geradorDeIdentificadores;
    private readonly TimeProvider _relogio;

    public CriarVaquinha(
        IAtorAtual atorAtual,
        IRepositorioVaquinhas repositorio,
        IGeradorDeIdentificadores geradorDeIdentificadores,
        TimeProvider relogio)
    {
        ArgumentNullException.ThrowIfNull(atorAtual);
        ArgumentNullException.ThrowIfNull(repositorio);
        ArgumentNullException.ThrowIfNull(geradorDeIdentificadores);
        ArgumentNullException.ThrowIfNull(relogio);

        _atorAtual = atorAtual;
        _repositorio = repositorio;
        _geradorDeIdentificadores = geradorDeIdentificadores;
        _relogio = relogio;
    }

    /// <exception cref="AtorNaoIdentificadoException">Não há ator identificado.</exception>
    /// <exception cref="EntradaInvalidaException">O nome foi rejeitado pelo domínio.</exception>
    /// <exception cref="FalhaNoRepositorioException">A gravação não foi concluída.</exception>
    /// <exception cref="OperationCanceledException">A operação foi cancelada.</exception>
    public async Task<VaquinhaResumo> ExecutarAsync(CriarVaquinhaComando comando, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var organizadorId = _atorAtual.ExigirPessoaId();

        // O domínio valida o nome no construtor, que também exige identificador e instante.
        // Um identificador gerado para um nome inválido é apenas descartado.
        Vaquinha vaquinha;
        try
        {
            vaquinha = new Vaquinha(
                _geradorDeIdentificadores.NovoId(),
                comando.Nome,
                organizadorId,
                _relogio.GetUtcNow());
        }
        catch (ArgumentException excecao) when (excecao.ParamName == "nome")
        {
            throw new EntradaInvalidaException(nameof(comando.Nome), excecao.Message, excecao);
        }

        try
        {
            await _repositorio.AdicionarAsync(vaquinha, cancellationToken);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            throw new FalhaNoRepositorioException("Não foi possível gravar a vaquinha.", excecao);
        }

        return new VaquinhaResumo(vaquinha.Id, vaquinha.Nome, vaquinha.CriadaEm);
    }
}
