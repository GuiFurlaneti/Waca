using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Waca.Infrastructure.Identidade.Desafios;

/// <summary>Pedido interno de emissão. Não contém código.</summary>
/// <param name="Finalidade">Finalidade do desafio.</param>
/// <param name="Email">Endereço informado, sem normalização.</param>
/// <param name="PessoaId">Pessoa vinculada: obrigatória para <see cref="FinalidadeDesafio.TrocaDeEmail"/> e ausente para <see cref="FinalidadeDesafio.Acesso"/>.</param>
public sealed record SolicitacaoDeEmissao(FinalidadeDesafio Finalidade, string Email, Guid? PessoaId = null)
{
    /// <summary>Não expõe o endereço nem a pessoa (evita vazamento por log automático de objetos).</summary>
    public override string ToString() => $"SolicitacaoDeEmissao({Finalidade})";
}

/// <summary>Resultado da emissão. Nunca contém o código.</summary>
public enum SituacaoDaEmissao
{
    /// <summary>Desafio confirmado no banco e entrega aceita pelo mecanismo de entrega (não garante recebimento).</summary>
    Emitido = 1,

    /// <summary>Desafio confirmado, mas o mecanismo de entrega falhou; o desafio foi invalidado e a cota foi consumida.</summary>
    EntregaFalhou = 2,

    /// <summary>Nenhum desafio foi criado porque um limite de emissão foi atingido.</summary>
    LimiteAtingido = 3,

    /// <summary>E-mail ou combinação de finalidade e pessoa inválidos; nada foi gravado.</summary>
    EntradaInvalida = 4,
}

/// <summary>Limite que impediu a emissão.</summary>
public enum LimiteDeEmissao
{
    IntervaloMinimo = 1,
    JanelaCurta = 2,
    JanelaLonga = 3,
}

/// <summary>Resultado de <see cref="EmissorDeDesafios.EmitirAsync"/>.</summary>
public sealed record ResultadoDaEmissao(SituacaoDaEmissao Situacao, Guid? DesafioId, DateTimeOffset? ExpiraEm, LimiteDeEmissao? Limite);

/// <summary>
/// Emite desafios de acesso: confere cotas persistidas, invalida o desafio aberto anterior, grava apenas o HMAC
/// e, depois do commit, entrega o código ao <see cref="IEntregaDeCodigoDeAcesso"/>.
/// </summary>
/// <remarks>
/// <para>
/// Emissões para o mesmo e-mail e finalidade são serializadas por <c>pg_advisory_xact_lock</c>, um bloqueio do
/// PostgreSQL liberado no fim da transação. Assim, cota e invalidação são decididas por uma única transação de
/// cada vez, em qualquer quantidade de instâncias. O índice único parcial é uma segunda proteção.
/// </para>
/// <para>
/// <b>Falha na entrega:</b> o código só é entregue depois do commit. Se a entrega falhar, o desafio é
/// invalidado em outra transação (nenhum código não entregue fica utilizável), a emissão continua contando
/// para a cota (evita repetição abusiva) e o resultado é <see cref="SituacaoDaEmissao.EntregaFalhou"/>.
/// Não há fila nem cópia do código em texto puro.
/// </para>
/// </remarks>
public sealed class EmissorDeDesafios
{
    private const int TamanhoMaximoDoEmail = 256;

    private readonly IDbContextFactory<WacaIdentidadeDbContext> _contextos;
    private readonly ChaveiroDeDesafios _chaveiro;
    private readonly OpcoesDeDesafios _opcoes;
    private readonly ILookupNormalizer _normalizador;
    private readonly IEntregaDeCodigoDeAcesso _entrega;
    private readonly TimeProvider _relogio;
    private readonly ILogger<EmissorDeDesafios> _log;

    /// <exception cref="InvalidOperationException">Opções inválidas.</exception>
    public EmissorDeDesafios(
        IDbContextFactory<WacaIdentidadeDbContext> contextos,
        ChaveiroDeDesafios chaveiro,
        OpcoesDeDesafios opcoes,
        ILookupNormalizer normalizador,
        IEntregaDeCodigoDeAcesso entrega,
        TimeProvider relogio,
        ILogger<EmissorDeDesafios> log)
    {
        _contextos = contextos ?? throw new ArgumentNullException(nameof(contextos));
        _chaveiro = chaveiro ?? throw new ArgumentNullException(nameof(chaveiro));
        _opcoes = opcoes ?? throw new ArgumentNullException(nameof(opcoes));
        _normalizador = normalizador ?? throw new ArgumentNullException(nameof(normalizador));
        _entrega = entrega ?? throw new ArgumentNullException(nameof(entrega));
        _relogio = relogio ?? throw new ArgumentNullException(nameof(relogio));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _opcoes.Validar();
    }

    public async Task<ResultadoDaEmissao> EmitirAsync(SolicitacaoDeEmissao solicitacao, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solicitacao);

        if (!EntradaValida(solicitacao, out var emailNormalizado))
        {
            return new ResultadoDaEmissao(SituacaoDaEmissao.EntradaInvalida, null, null, null);
        }

        var codigo = GeradorDeCodigo.Gerar(_opcoes.DigitosDoCodigo);
        DesafioDeAcessoRegistro registro;

        await using (var contexto = await _contextos.CreateDbContextAsync(cancellationToken))
        await using (var transacao = await contexto.Database.BeginTransactionAsync(cancellationToken))
        {
            var chaveDoBloqueio = $"waca:desafio:{solicitacao.Finalidade}:{emailNormalizado}";
            await contexto.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({chaveDoBloqueio}, 0))",
                cancellationToken);

            // Lido só depois de obter o bloqueio: a espera por outra emissão não pode produzir um horário
            // anterior ao do desafio que ela gravou (cotas, criação, expiração e invalidação usam este valor).
            var agora = _relogio.GetUtcNow();

            var limite = await VerificarCotaAsync(contexto, solicitacao.Finalidade, emailNormalizado, agora, cancellationToken);
            if (limite is not null)
            {
                await transacao.RollbackAsync(cancellationToken);
                _log.LogInformation("Emissão de desafio recusada por limite {Limite}.", limite);
                return new ResultadoDaEmissao(SituacaoDaEmissao.LimiteAtingido, null, null, limite);
            }

            await contexto.DesafiosDeAcesso
                .Where(d => d.EmailNormalizado == emailNormalizado
                    && d.Finalidade == solicitacao.Finalidade
                    && d.ConsumidoEm == null
                    && d.InvalidadoEm == null)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.InvalidadoEm, agora), cancellationToken);

            var id = Guid.CreateVersion7(agora);
            registro = new DesafioDeAcessoRegistro
            {
                Id = id,
                Finalidade = solicitacao.Finalidade,
                EmailNormalizado = emailNormalizado,
                PessoaId = solicitacao.PessoaId,
                SegredoHash = _chaveiro.CalcularSegredo(_chaveiro.VersaoAtual, id, solicitacao.Finalidade, emailNormalizado, codigo)!,
                VersaoChave = _chaveiro.VersaoAtual,
                CriadoEm = agora,
                ExpiraEm = agora + _opcoes.Validade,
                Tentativas = 0,
                MaxTentativas = _opcoes.MaximoDeTentativas,
            };

            contexto.DesafiosDeAcesso.Add(registro);
            await contexto.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }

        // Só depois do commit: o código nunca sai antes de o desafio existir de forma definitiva.
        try
        {
            await _entrega.EntregarAsync(
                new CodigoParaEntrega(registro.Id, registro.Finalidade, solicitacao.Email, codigo, registro.ExpiraEm),
                cancellationToken);
        }
        catch (Exception falha)
        {
            // Não registra a mensagem da exceção: o mecanismo de entrega pode incluir dados do destinatário.
            _log.LogWarning("Entrega do desafio {DesafioId} falhou ({TipoDaFalha}); o desafio será invalidado.", registro.Id, falha.GetType().Name);
            await InvalidarAposFalhaNaEntregaAsync(registro.Id);

            if (falha is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ResultadoDaEmissao(SituacaoDaEmissao.EntregaFalhou, registro.Id, null, null);
        }

        _log.LogInformation("Desafio {DesafioId} emitido e aceito pelo mecanismo de entrega.", registro.Id);
        return new ResultadoDaEmissao(SituacaoDaEmissao.Emitido, registro.Id, registro.ExpiraEm, null);
    }

    private async Task<LimiteDeEmissao?> VerificarCotaAsync(
        WacaIdentidadeDbContext contexto,
        FinalidadeDesafio finalidade,
        string emailNormalizado,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        var anteriores = contexto.DesafiosDeAcesso.Where(d => d.EmailNormalizado == emailNormalizado && d.Finalidade == finalidade);

        var ultimo = await anteriores.MaxAsync(d => (DateTimeOffset?)d.CriadoEm, cancellationToken);
        if (ultimo is not null && ultimo > agora - _opcoes.IntervaloMinimoEntreEmissoes)
        {
            return LimiteDeEmissao.IntervaloMinimo;
        }

        var inicioCurta = agora - _opcoes.JanelaCurta;
        if (await anteriores.CountAsync(d => d.CriadoEm > inicioCurta, cancellationToken) >= _opcoes.MaximoDeEmissoesNaJanelaCurta)
        {
            return LimiteDeEmissao.JanelaCurta;
        }

        var inicioLonga = agora - _opcoes.JanelaLonga;
        if (await anteriores.CountAsync(d => d.CriadoEm > inicioLonga, cancellationToken) >= _opcoes.MaximoDeEmissoesNaJanelaLonga)
        {
            return LimiteDeEmissao.JanelaLonga;
        }

        return null;
    }

    private async Task InvalidarAposFalhaNaEntregaAsync(Guid desafioId)
    {
        var agora = _relogio.GetUtcNow();
        try
        {
            await using var contexto = await _contextos.CreateDbContextAsync(CancellationToken.None);
            await contexto.DesafiosDeAcesso
                .Where(d => d.Id == desafioId && d.ConsumidoEm == null && d.InvalidadoEm == null)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.InvalidadoEm, agora), CancellationToken.None);
        }
        catch (Exception falha)
        {
            // O código não foi entregue e não está gravado; o desafio expira sozinho na validade configurada.
            _log.LogError("Não foi possível invalidar o desafio {DesafioId} após falha na entrega ({TipoDaFalha}).", desafioId, falha.GetType().Name);
        }
    }

    private bool EntradaValida(SolicitacaoDeEmissao solicitacao, out string emailNormalizado)
    {
        emailNormalizado = string.Empty;

        var pessoaValida = solicitacao.Finalidade switch
        {
            FinalidadeDesafio.Acesso => solicitacao.PessoaId is null,
            FinalidadeDesafio.TrocaDeEmail => solicitacao.PessoaId is { } pessoa && pessoa != Guid.Empty,
            _ => false,
        };

        var email = solicitacao.Email;
        if (!pessoaValida
            || string.IsNullOrEmpty(email)
            || email.Length > TamanhoMaximoDoEmail
            || char.IsWhiteSpace(email[0])
            || char.IsWhiteSpace(email[^1])
            || !new EmailAddressAttribute().IsValid(email))
        {
            return false;
        }

        var normalizado = _normalizador.NormalizeEmail(email);
        if (string.IsNullOrEmpty(normalizado) || normalizado.Length > TamanhoMaximoDoEmail)
        {
            return false;
        }

        emailNormalizado = normalizado;
        return true;
    }
}
