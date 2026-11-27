using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Waca.Infrastructure.Identidade.Desafios;

/// <summary>Código apresentado para um desafio.</summary>
/// <param name="DesafioId">Desafio informado pelo solicitante.</param>
/// <param name="Finalidade">Finalidade esperada pela operação que verifica.</param>
/// <param name="Email">Endereço do destinatário, sem normalização.</param>
/// <param name="Codigo">Código digitado. Não registrar.</param>
public sealed record PedidoDeVerificacao(Guid DesafioId, FinalidadeDesafio Finalidade, string Email, string Codigo)
{
    /// <summary>Não expõe o código.</summary>
    public override string ToString() => $"PedidoDeVerificacao(desafio {DesafioId}, {Finalidade})";
}

/// <summary>Motivo interno da recusa. Não deve ser revelado ao usuário final.</summary>
public enum MotivoDaRecusa
{
    NaoEncontrado = 1,
    JaConsumido = 2,
    Invalidado = 3,
    Expirado = 4,
    TentativasEsgotadas = 5,
    ChaveIndisponivel = 6,
    CodigoIncorreto = 7,
}

/// <summary>Resultado da verificação. Em caso de sucesso, identifica o destinatário comprovado.</summary>
public sealed record ResultadoDaVerificacao(
    bool Consumido,
    MotivoDaRecusa? Motivo,
    Guid? DesafioId,
    string? EmailNormalizado,
    Guid? PessoaId,
    int? TentativasRestantes)
{
    internal static ResultadoDaVerificacao Recusado(MotivoDaRecusa motivo, int? tentativasRestantes = null) =>
        new(false, motivo, null, null, null, tentativasRestantes);

    /// <summary>Não expõe o e-mail normalizado nem a pessoa.</summary>
    public override string ToString() => $"ResultadoDaVerificacao(consumido: {Consumido}, motivo: {Motivo?.ToString() ?? "-"})";
}

/// <summary>
/// Verifica e consome desafios com bloqueio de linha no PostgreSQL (<c>SELECT ... FOR UPDATE</c>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="VerificarEConsumirAsync"/> trabalha dentro da transação do chamador: o consumo só se torna
/// definitivo no commit dessa transação e é desfeito se ela for revertida. Isso permite, na MVP-04C, consumir o
/// desafio, criar a conta e registrar a sessão na mesma transação.
/// </para>
/// <para>
/// Uma recusa por código incorreto incrementa <c>tentativas</c> nessa mesma transação; o chamador deve
/// <b>confirmá-la</b> (e não revertê-la) para que a tentativa conte. <see cref="VerificarEConsumirEmTransacaoPropriaAsync"/>
/// faz isso sozinho e é a forma recomendada quando não há outra operação na transação.
/// </para>
/// <para>
/// Com <c>READ COMMITTED</c>, verificações simultâneas do mesmo desafio esperam o bloqueio da linha; a segunda lê
/// a versão já consumida e é recusada. As tentativas incorretas não chamam <c>AccessFailedAsync</c>: nenhuma conta
/// é bloqueada por quem só conhece o e-mail.
/// </para>
/// </remarks>
public sealed class VerificadorDeDesafios
{
    private readonly IDbContextFactory<WacaIdentidadeDbContext> _contextos;
    private readonly ChaveiroDeDesafios _chaveiro;
    private readonly OpcoesDeDesafios _opcoes;
    private readonly ILookupNormalizer _normalizador;
    private readonly TimeProvider _relogio;
    private readonly ILogger<VerificadorDeDesafios> _log;

    /// <exception cref="InvalidOperationException">Opções inválidas.</exception>
    public VerificadorDeDesafios(
        IDbContextFactory<WacaIdentidadeDbContext> contextos,
        ChaveiroDeDesafios chaveiro,
        OpcoesDeDesafios opcoes,
        ILookupNormalizer normalizador,
        TimeProvider relogio,
        ILogger<VerificadorDeDesafios> log)
    {
        _contextos = contextos ?? throw new ArgumentNullException(nameof(contextos));
        _chaveiro = chaveiro ?? throw new ArgumentNullException(nameof(chaveiro));
        _opcoes = opcoes ?? throw new ArgumentNullException(nameof(opcoes));
        _normalizador = normalizador ?? throw new ArgumentNullException(nameof(normalizador));
        _relogio = relogio ?? throw new ArgumentNullException(nameof(relogio));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _opcoes.Validar();
    }

    /// <summary>Verifica e consome em uma transação própria, sempre confirmada (sucesso ou tentativa registrada).</summary>
    public async Task<ResultadoDaVerificacao> VerificarEConsumirEmTransacaoPropriaAsync(PedidoDeVerificacao pedido, CancellationToken cancellationToken)
    {
        await using var contexto = await _contextos.CreateDbContextAsync(cancellationToken);
        await using var transacao = await contexto.Database.BeginTransactionAsync(cancellationToken);

        var resultado = await VerificarEConsumirAsync(contexto, pedido, cancellationToken);

        await transacao.CommitAsync(cancellationToken);
        return resultado;
    }

    /// <summary>
    /// Verifica e consome dentro da transação já aberta em <paramref name="contexto"/>. Nada é confirmado aqui.
    /// </summary>
    /// <exception cref="InvalidOperationException">Não há transação aberta, ou o consumo não afetou exatamente uma linha.</exception>
    public async Task<ResultadoDaVerificacao> VerificarEConsumirAsync(
        WacaIdentidadeDbContext contexto,
        PedidoDeVerificacao pedido,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(pedido);

        if (contexto.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A verificação de desafio exige uma transação aberta pelo chamador.");
        }

        var id = pedido.DesafioId;

        var linhas = await contexto.DesafiosDeAcesso
            .FromSql($"SELECT * FROM identidade.desafios_acesso WHERE id = {id} FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Lido só depois de obter o bloqueio da linha: um código que vence enquanto a verificação espera outra
        // transação é recusado. O mesmo valor é usado na decisão e nos UPDATEs de tentativa e de consumo.
        var agora = _relogio.GetUtcNow();

        if (linhas.Count == 0)
        {
            return Recusar(id, MotivoDaRecusa.NaoEncontrado);
        }

        var desafio = linhas[0];
        if (desafio.ConsumidoEm is not null)
        {
            return Recusar(id, MotivoDaRecusa.JaConsumido);
        }

        if (desafio.InvalidadoEm is not null)
        {
            return Recusar(id, MotivoDaRecusa.Invalidado);
        }

        if (desafio.ExpiraEm <= agora)
        {
            return Recusar(id, MotivoDaRecusa.Expirado);
        }

        if (desafio.Tentativas >= desafio.MaxTentativas)
        {
            return Recusar(id, MotivoDaRecusa.TentativasEsgotadas);
        }

        var emailNormalizado = _normalizador.NormalizeEmail(pedido.Email ?? string.Empty) ?? string.Empty;
        var codigo = pedido.Codigo ?? string.Empty;

        var calculado = _chaveiro.CalcularSegredo(desafio.VersaoChave, desafio.Id, pedido.Finalidade, emailNormalizado, codigo);
        if (calculado is null)
        {
            _log.LogWarning("Desafio {DesafioId} usa versão de chave {VersaoChave} indisponível.", id, desafio.VersaoChave);
            return Recusar(id, MotivoDaRecusa.ChaveIndisponivel);
        }

        // Finalidade e destinatário estão dentro do HMAC; as comparações explícitas só tornam a regra legível.
        var confere = ProtecaoDoCodigo.Confere(desafio.SegredoHash, calculado)
            && desafio.Finalidade == pedido.Finalidade
            && string.Equals(desafio.EmailNormalizado, emailNormalizado, StringComparison.Ordinal)
            && GeradorDeCodigo.TemFormatoValido(codigo, _opcoes.DigitosDoCodigo);

        if (!confere)
        {
            var tentativas = desafio.Tentativas + 1;
            var afetadas = await contexto.DesafiosDeAcesso
                .Where(d => d.Id == id && d.ConsumidoEm == null && d.InvalidadoEm == null)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(d => d.Tentativas, d => d.Tentativas + 1)
                        .SetProperty(d => d.InvalidadoEm, d => d.Tentativas + 1 >= d.MaxTentativas ? agora : d.InvalidadoEm),
                    cancellationToken);
            ExigirUmaLinha(afetadas);

            var restantes = Math.Max(0, desafio.MaxTentativas - tentativas);
            _log.LogInformation("Código incorreto para o desafio {DesafioId}; restam {Restantes} tentativas.", id, restantes);
            return ResultadoDaVerificacao.Recusado(MotivoDaRecusa.CodigoIncorreto, restantes);
        }

        var consumidas = await contexto.DesafiosDeAcesso
            .Where(d => d.Id == id && d.ConsumidoEm == null && d.InvalidadoEm == null && d.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConsumidoEm, agora), cancellationToken);
        ExigirUmaLinha(consumidas);

        _log.LogInformation("Desafio {DesafioId} consumido (pendente de commit da transação).", id);
        return new ResultadoDaVerificacao(true, null, desafio.Id, desafio.EmailNormalizado, desafio.PessoaId, null);
    }

    private ResultadoDaVerificacao Recusar(Guid id, MotivoDaRecusa motivo)
    {
        _log.LogInformation("Desafio {DesafioId} recusado: {Motivo}.", id, motivo);
        return ResultadoDaVerificacao.Recusado(motivo);
    }

    private static void ExigirUmaLinha(int afetadas)
    {
        if (afetadas != 1)
        {
            throw new InvalidOperationException($"A atualização do desafio afetou {afetadas} linhas; era esperada exatamente uma.");
        }
    }
}
