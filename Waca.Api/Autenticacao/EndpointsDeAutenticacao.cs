using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Identidade.Desafios;
using Waca.Infrastructure.Identidade.Sessoes;

namespace Waca.Api.Autenticacao;

/// <summary>Pedido de código. O <see cref="ToString"/> não expõe o e-mail.</summary>
public sealed record PedidoDeCodigo(string? Email)
{
    public override string ToString() => nameof(PedidoDeCodigo);
}

/// <summary>Confirmação de código. O <see cref="ToString"/> não expõe e-mail nem código.</summary>
public sealed record PedidoDeConfirmacao(Guid? Referencia, string? Email, string? Codigo)
{
    public override string ToString() => nameof(PedidoDeConfirmacao);
}

/// <summary>Resposta pública do pedido de código: só uma referência opaca para a confirmação.</summary>
public sealed record RespostaDoPedidoDeCodigo(Guid Referencia);

/// <summary>Dados mínimos da própria identidade autenticada.</summary>
public sealed record RespostaDaIdentidade(Guid PessoaId, string Email);

/// <summary>Endpoints de autenticação sem senha (<c>/api/autenticacao</c>).</summary>
public static class EndpointsDeAutenticacao
{
    public const string Prefixo = "/api/autenticacao";
    public const string PoliticaDeLimite = "autenticacao";

    private static readonly object ErroGenerico = new { erro = "codigo_invalido_ou_expirado" };

    public static IEndpointRouteBuilder MapearAutenticacao(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup(Prefixo).RequireRateLimiting(PoliticaDeLimite);

        grupo.MapGet("/antiforgery", ObterAntiforgery).AllowAnonymous();
        grupo.MapPost("/codigo", SolicitarCodigoAsync).AllowAnonymous();
        grupo.MapPost("/confirmar", ConfirmarAsync).AllowAnonymous();
        grupo.MapGet("/eu", Eu).RequireAuthorization();

        return rotas;
    }

    /// <summary>Emite os tokens CSRF para a identidade atual (anônima ou não). Não autentica ninguém.</summary>
    internal static IResult ObterAntiforgery(HttpContext contexto)
    {
        ProtecaoContraCsrf.EmitirTokens(contexto);
        return Results.NoContent();
    }

    /// <summary>
    /// Sempre 202 com uma referência opaca, exista ou não uma conta para o e-mail (a emissão não consulta contas).
    /// Quando nenhum desafio é emitido (cota atingida), a referência é um identificador aleatório no mesmo formato,
    /// que não corresponde a desafio algum: a confirmação com ele falha como qualquer código inválido.
    /// </summary>
    internal static async Task<IResult> SolicitarCodigoAsync(
        PedidoDeCodigo? pedido,
        EmissorDeDesafios emissor,
        TimeProvider relogio,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pedido?.Email))
        {
            return Results.BadRequest(new { erro = "email_invalido" });
        }

        var resultado = await emissor.EmitirAsync(new SolicitacaoDeEmissao(FinalidadeDesafio.Acesso, pedido.Email), cancellationToken);
        if (resultado.Situacao == SituacaoDaEmissao.EntradaInvalida)
        {
            return Results.BadRequest(new { erro = "email_invalido" });
        }

        var referencia = resultado.Situacao == SituacaoDaEmissao.Emitido && resultado.DesafioId is { } id
            ? id
            : Guid.CreateVersion7(relogio.GetUtcNow());

        return Results.Accepted(value: new RespostaDoPedidoDeCodigo(referencia));
    }

    /// <summary>
    /// Confirma o código. Em sucesso, a transação (consumo, conta e sessão) já foi confirmada quando o cookie é
    /// emitido; os tokens CSRF são renovados para a nova identidade. Qualquer recusa recebe a mesma resposta 400.
    /// </summary>
    internal static async Task<IResult> ConfirmarAsync(
        PedidoDeConfirmacao? pedido,
        HttpContext contexto,
        ConfirmacaoDeAcesso confirmacao,
        SignInManager<UsuarioWaca> entrada,
        SessoesPersistidas sessoes,
        WacaIdentidadeDbContext banco,
        TimeProvider relogio,
        ILoggerFactory logs,
        CancellationToken cancellationToken)
    {
        if (pedido?.Referencia is not { } referencia || string.IsNullOrWhiteSpace(pedido.Email) || string.IsNullOrWhiteSpace(pedido.Codigo))
        {
            return Results.BadRequest(ErroGenerico);
        }

        var resultado = await confirmacao.ConfirmarAsync(referencia, pedido.Email, pedido.Codigo, cancellationToken);
        if (!resultado.Aceita || resultado.Conta is null || resultado.Sessao is null)
        {
            return Results.BadRequest(ErroGenerico);
        }

        try
        {
            var principal = await entrada.CreateUserPrincipalAsync(resultado.Conta);
            var identidade = (ClaimsIdentity)principal.Identity!;
            identidade.AddClaim(new Claim(ClaimsDaSessao.Sessao, resultado.Sessao.Id.ToString()));
            identidade.AddClaim(new Claim(ClaimsDaSessao.Metodo, ClaimsDaSessao.CodigoPorEmail));

            await contexto.SignInAsync(IdentityConstants.ApplicationScheme, principal, new AuthenticationProperties { IsPersistent = false });

            contexto.User = principal;
            ProtecaoContraCsrf.EmitirTokens(contexto);
        }
        catch (Exception falha)
        {
            // O commit já ocorreu: não há rollback. A sessão gravada, cujo sid não chegou a ninguém, é revogada.
            logs.CreateLogger(typeof(EndpointsDeAutenticacao)).LogError(
                "Falha ao emitir o cookie da sessão {SessaoId} ({TipoDaFalha}); a sessão será revogada.",
                resultado.Sessao.Id,
                falha.GetType().Name);
            await sessoes.RevogarAsync(banco, resultado.Sessao.Id, relogio.GetUtcNow(), CancellationToken.None);
            throw;
        }

        return Results.NoContent();
    }

    /// <summary>Dados mínimos da identidade validada; nada vem do cliente.</summary>
    internal static IResult Eu(HttpContext contexto)
    {
        var sessao = contexto.Features.Get<ISessaoValidada>();
        return sessao is null
            ? Results.Unauthorized()
            : Results.Ok(new RespostaDaIdentidade(sessao.PessoaId, sessao.Email));
    }
}
