using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Identidade.Sessoes;

namespace Waca.Api.Autenticacao;

/// <summary>
/// Validação assíncrona do cookie em toda requisição autenticada, antes dos endpoints.
/// </summary>
/// <remarks>
/// <para>
/// Primeiro executa o <see cref="SecurityStampValidator"/> do Identity. Depois, com uma consulta por chave primária,
/// confere que a sessão existe, não foi revogada, está dentro do teto absoluto, pertence à pessoa do cookie, que a
/// conta está ativa e confirmada e que o security stamp do cookie é o atual. Qualquer falha rejeita o principal e
/// apaga o cookie: o endpoint protegido responde 401.
/// </para>
/// <para>
/// Só com tudo válido a identidade é publicada como <see cref="ISessaoValidada"/>. Falha de banco não vira acesso
/// anônimo bem-sucedido: a exceção interrompe a requisição.
/// </para>
/// </remarks>
public sealed class ValidadorDeSessao(
    SessoesPersistidas sessoes,
    WacaIdentidadeDbContext contexto,
    TimeProvider relogio,
    IOptions<IdentityOptions> opcoesDaIdentidade) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);

        var principal = context.Principal;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            await RejeitarAsync(context);
            return;
        }

        var tiposDeClaim = opcoesDaIdentidade.Value.ClaimsIdentity;
        var stamp = principal.FindFirst(tiposDeClaim.SecurityStampClaimType)?.Value;
        if (!Guid.TryParse(principal.FindFirst(tiposDeClaim.UserIdClaimType)?.Value, out var pessoaId)
            || !Guid.TryParse(principal.FindFirst(ClaimsDaSessao.Sessao)?.Value, out var sessaoId)
            || principal.FindFirst(ClaimsDaSessao.Metodo)?.Value != ClaimsDaSessao.CodigoPorEmail
            || string.IsNullOrEmpty(stamp))
        {
            await RejeitarAsync(context);
            return;
        }

        var estado = await sessoes.ObterAsync(contexto, sessaoId, pessoaId, context.HttpContext.RequestAborted);
        if (estado is null
            || !estado.PermiteAcessoEm(relogio.GetUtcNow())
            || !string.Equals(estado.SecurityStamp, stamp, StringComparison.Ordinal))
        {
            await RejeitarAsync(context);
            return;
        }

        context.HttpContext.Features.Set<ISessaoValidada>(new SessaoValidada(estado.PessoaId, estado.SessaoId, estado.Email ?? string.Empty));
    }

    /// <summary>API: sem redirecionamento para página de login.</summary>
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private static async Task RejeitarAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }
}
