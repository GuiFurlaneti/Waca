using Microsoft.AspNetCore.Antiforgery;

namespace Waca.Api.Autenticacao;

/// <summary>
/// Proteção CSRF explícita para todos os métodos que alteram estado, inclusive endpoints anônimos de autenticação.
/// </summary>
/// <remarks>
/// <para>
/// Convenção do desenho aprovado: o token de requisição vai no cookie <c>XSRF-TOKEN</c> (legível pelo JavaScript da
/// mesma origem) e volta no cabeçalho <c>X-XSRF-TOKEN</c>. O token do cookie fica em <c>__Host-waca-antiforgery</c>
/// (<c>HttpOnly</c>). Os tokens são ligados à identidade: um token obtido antes do login (ou por outra pessoa) é
/// recusado depois dele.
/// </para>
/// <para>
/// A validação é feita por middleware, e não por endpoint, para que nenhum endpoint novo que altere estado fique
/// sem proteção por esquecimento. Obter o token não autentica ninguém.
/// </para>
/// </remarks>
public static class ProtecaoContraCsrf
{
    public const string CabecalhoDoToken = "X-XSRF-TOKEN";
    public const string CookieDoTokenDeRequisicao = "XSRF-TOKEN";
    public const string CookieDoTokenDeCookie = "__Host-waca-antiforgery";

    public static IServiceCollection AdicionarProtecaoContraCsrf(this IServiceCollection servicos) =>
        servicos.AddAntiforgery(opcoes =>
        {
            opcoes.HeaderName = CabecalhoDoToken;
            opcoes.FormFieldName = "__waca_csrf";
            opcoes.Cookie.Name = CookieDoTokenDeCookie;
            opcoes.Cookie.HttpOnly = true;
            opcoes.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            opcoes.Cookie.SameSite = SameSiteMode.Strict;
            opcoes.Cookie.Path = "/";
            opcoes.Cookie.Domain = null;
        });

    /// <summary>Recusa (400) toda requisição POST, PUT, PATCH ou DELETE sem tokens válidos para a identidade atual.</summary>
    public static IApplicationBuilder UsarProtecaoContraCsrf(this IApplicationBuilder app) =>
        app.Use(async (contexto, proximo) =>
        {
            var metodo = contexto.Request.Method;
            if (HttpMethods.IsPost(metodo) || HttpMethods.IsPut(metodo) || HttpMethods.IsPatch(metodo) || HttpMethods.IsDelete(metodo))
            {
                var antiforgery = contexto.RequestServices.GetRequiredService<IAntiforgery>();
                try
                {
                    await antiforgery.ValidateRequestAsync(contexto);
                }
                catch (AntiforgeryValidationException)
                {
                    contexto.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await contexto.Response.WriteAsJsonAsync(new { erro = "requisicao_invalida" });
                    return;
                }
            }

            await proximo(contexto);
        });

    /// <summary>Gera tokens para a identidade em <see cref="HttpContext.User"/> e grava o cookie legível <c>XSRF-TOKEN</c>.</summary>
    public static void EmitirTokens(HttpContext contexto)
    {
        var antiforgery = contexto.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(contexto);
        contexto.Response.Cookies.Append(
            CookieDoTokenDeRequisicao,
            tokens.RequestToken!,
            new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                IsEssential = true,
            });
    }
}
