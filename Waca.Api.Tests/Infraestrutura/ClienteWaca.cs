using System.Net;
using System.Net.Http.Json;
using Microsoft.Net.Http.Headers;
using Waca.Api.Autenticacao;
using Xunit;

namespace Waca.Api.Tests.Infraestrutura;

/// <summary>
/// Cliente HTTP com cookies controlados pelo teste (para inspecionar atributos, adulterar e reaproveitar cookies).
/// </summary>
public sealed class ClienteWaca(HttpClient http, CaixaDeTeste caixa) : IDisposable
{
    public const string CookieDeSessao = ConfiguracaoDaAutenticacao.NomeDoCookieDeSessao;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public Dictionary<string, string> Cookies { get; } = new(StringComparer.Ordinal);

    /// <summary>Cabeçalhos Set-Cookie da última resposta.</summary>
    public List<string> UltimosSetCookie { get; } = [];

    public string? TokenCsrf => Cookies.GetValueOrDefault(ProtecaoContraCsrf.CookieDoTokenDeRequisicao);

    public async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage requisicao)
    {
        if (Cookies.Count > 0)
        {
            requisicao.Headers.Add("Cookie", string.Join("; ", Cookies.Select(c => $"{c.Key}={c.Value}")));
        }

        var resposta = await http.SendAsync(requisicao, Ct);
        UltimosSetCookie.Clear();
        if (resposta.Headers.TryGetValues("Set-Cookie", out var valores))
        {
            foreach (var valor in valores)
            {
                UltimosSetCookie.Add(valor);
                var cookie = SetCookieHeaderValue.Parse(valor);
                var nome = cookie.Name.ToString();
                if (cookie.Value.Length == 0 || (cookie.Expires is { } expira && expira < DateTimeOffset.UtcNow.AddYears(-1)))
                {
                    Cookies.Remove(nome);
                }
                else
                {
                    Cookies[nome] = cookie.Value.ToString();
                }
            }
        }

        return resposta;
    }

    public Task<HttpResponseMessage> GetAsync(string caminho, Action<HttpRequestMessage>? ajustar = null)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Get, caminho);
        ajustar?.Invoke(requisicao);
        return EnviarAsync(requisicao);
    }

    public Task<HttpResponseMessage> PostJsonAsync(string caminho, object corpo, bool comCsrf = true, string? tokenCsrf = null)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, caminho) { Content = JsonContent.Create(corpo) };
        if (comCsrf && (tokenCsrf ?? TokenCsrf) is { } token)
        {
            requisicao.Headers.Add(ProtecaoContraCsrf.CabecalhoDoToken, token);
        }

        return EnviarAsync(requisicao);
    }

    public async Task ObterCsrfAsync()
    {
        var resposta = await GetAsync("/api/autenticacao/antiforgery");
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.NotNull(TokenCsrf);
    }

    public async Task<Guid> SolicitarCodigoAsync(string email)
    {
        var resposta = await PostJsonAsync("/api/autenticacao/codigo", new { email });
        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<RespostaDoPedidoDeCodigo>(Ct))!.Referencia;
    }

    public Task<HttpResponseMessage> ConfirmarAsync(Guid referencia, string email, string codigo) =>
        PostJsonAsync("/api/autenticacao/confirmar", new { referencia, email, codigo });

    /// <summary>Fluxo completo: CSRF, pedido de código, confirmação. Devolve a referência usada.</summary>
    public async Task<Guid> EntrarAsync(string email)
    {
        await ObterCsrfAsync();
        var referencia = await SolicitarCodigoAsync(email);
        var resposta = await ConfirmarAsync(referencia, email, caixa.CodigoDo(referencia));
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.True(Cookies.ContainsKey(CookieDeSessao));
        return referencia;
    }

    public async Task<RespostaDaIdentidade?> EuAsync()
    {
        var resposta = await GetAsync("/api/autenticacao/eu");
        return resposta.StatusCode == HttpStatusCode.OK
            ? await resposta.Content.ReadFromJsonAsync<RespostaDaIdentidade>(Ct)
            : null;
    }

    public void Dispose() => http.Dispose();
}
