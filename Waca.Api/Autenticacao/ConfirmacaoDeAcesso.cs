using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Identidade.Desafios;
using Waca.Infrastructure.Identidade.Sessoes;

namespace Waca.Api.Autenticacao;

/// <summary>Resultado da confirmação. Não contém o código.</summary>
public sealed record ResultadoDaConfirmacao(bool Aceita, UsuarioWaca? Conta, SessaoCriada? Sessao, bool ContaCriada)
{
    internal static ResultadoDaConfirmacao Recusada { get; } = new(false, null, null, false);

    /// <summary>Não expõe dados da conta.</summary>
    public override string ToString() => $"ResultadoDaConfirmacao(aceita: {Aceita})";
}

/// <summary>
/// Confirmação do código: consumo do desafio, identificação ou criação da conta e registro da sessão em uma única
/// transação PostgreSQL, no mesmo <see cref="WacaIdentidadeDbContext"/> usado pelo <see cref="UserManager{TUser}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Recusa do código: a transação é <b>confirmada</b> para gravar a tentativa (contrato da MVP-04B); nada mais é
/// criado. Conta desativada ou bloqueada temporariamente: o desafio fica consumido, nenhuma sessão é criada.
/// </para>
/// <para>
/// Qualquer falha ao criar a conta ou a sessão desfaz tudo, inclusive o consumo. Se outra operação criar uma conta
/// com o mesmo e-mail ao mesmo tempo (<c>EmailIndex</c>), a transação é desfeita e repetida uma única vez; na
/// repetição a conta existente é encontrada. Outros erros de banco são propagados.
/// </para>
/// <para>O cookie só é emitido pelo chamador depois do commit.</para>
/// </remarks>
public sealed class ConfirmacaoDeAcesso(
    WacaIdentidadeDbContext contexto,
    VerificadorDeDesafios verificador,
    UserManager<UsuarioWaca> usuarios,
    SessoesPersistidas sessoes,
    TimeProvider relogio,
    ILogger<ConfirmacaoDeAcesso> log)
{
    private const int TentativasDaTransacao = 2;

    public async Task<ResultadoDaConfirmacao> ConfirmarAsync(Guid referencia, string email, string codigo, CancellationToken cancellationToken)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            await using var transacao = await contexto.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var resultado = await ExecutarAsync(referencia, email, codigo, transacao.CommitAsync, cancellationToken);
                if (resultado is not null)
                {
                    return resultado;
                }
            }
            catch (DbUpdateException falha) when (tentativa < TentativasDaTransacao && EhEmailDuplicado(falha))
            {
                log.LogInformation("Conta criada por outra operação durante a confirmação; a transação será repetida.");
            }

            await transacao.RollbackAsync(cancellationToken);
            contexto.ChangeTracker.Clear();

            if (tentativa >= TentativasDaTransacao)
            {
                throw new InvalidOperationException("A confirmação não pôde ser concluída por conflito repetido na criação da conta.");
            }
        }
    }

    /// <summary>Uma passada na transação aberta. Nulo indica conflito de e-mail resolvível por repetição.</summary>
    private async Task<ResultadoDaConfirmacao?> ExecutarAsync(
        Guid referencia,
        string email,
        string codigo,
        Func<CancellationToken, Task> confirmar,
        CancellationToken cancellationToken)
    {
        var verificacao = await verificador.VerificarEConsumirAsync(
            contexto,
            new PedidoDeVerificacao(referencia, FinalidadeDesafio.Acesso, email, codigo),
            cancellationToken);

        if (!verificacao.Consumido)
        {
            // Grava a tentativa incorreta (ou nada, se o desafio não existir/estiver fechado).
            await confirmar(cancellationToken);
            return ResultadoDaConfirmacao.Recusada;
        }

        var agora = relogio.GetUtcNow();
        var contaCriada = false;
        var conta = await usuarios.FindByEmailAsync(verificacao.EmailNormalizado!);

        if (conta is null)
        {
            conta = new UsuarioWaca(Guid.CreateVersion7(agora), email, EstadoConta.Ativa) { EmailConfirmed = true };
            var criacao = await usuarios.CreateAsync(conta);
            if (!criacao.Succeeded)
            {
                if (criacao.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateEmail)))
                {
                    return null;
                }

                throw new InvalidOperationException(
                    $"A conta não pôde ser criada: {string.Join(", ", criacao.Errors.Select(e => e.Code))}.");
            }

            contaCriada = true;
        }
        else
        {
            if (conta.Estado != EstadoConta.Ativa || await usuarios.IsLockedOutAsync(conta))
            {
                // O desafio fica consumido; a conta não recebe sessão.
                await confirmar(cancellationToken);
                log.LogInformation("Confirmação recusada: conta sem permissão de acesso.");
                return ResultadoDaConfirmacao.Recusada;
            }

            if (!conta.EmailConfirmed)
            {
                conta.EmailConfirmed = true;
                ExigirSucesso(await usuarios.UpdateAsync(conta), "confirmar o e-mail");
            }

            if (string.IsNullOrEmpty(conta.SecurityStamp))
            {
                ExigirSucesso(await usuarios.UpdateSecurityStampAsync(conta), "gerar o security stamp");
            }
        }

        var sessao = await sessoes.CriarAsync(contexto, conta.Id, agora, cancellationToken);
        await confirmar(cancellationToken);
        return new ResultadoDaConfirmacao(true, conta, sessao, contaCriada);
    }

    private static void ExigirSucesso(IdentityResult resultado, string operacao)
    {
        if (!resultado.Succeeded)
        {
            throw new InvalidOperationException(
                $"Não foi possível {operacao}: {string.Join(", ", resultado.Errors.Select(e => e.Code))}.");
        }
    }

    private static bool EhEmailDuplicado(DbUpdateException falha) =>
        falha.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "EmailIndex" };
}
