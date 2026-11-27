using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Waca.Api.Autenticacao;
using Waca.Api.Tests.Infraestrutura;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;

namespace Waca.Api.Tests.Autenticacao;

[Collection(ColecaoPostgresDaApi.Nome)]
public class ProtecoesHttpTests(ServidorPostgres servidor)
{
    private const string Email = "dora@exemplo.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pedido_de_codigo_sem_token_CSRF_e_recusado_sem_emitir_desafio()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        using var semNada = ambiente.CriarApi().CriarCliente();
        using var semCabecalho = ambiente.CriarApi().CriarCliente();
        await semCabecalho.ObterCsrfAsync();

        var respostaSemNada = await semNada.PostJsonAsync("/api/autenticacao/codigo", new { email = Email }, comCsrf: false);
        var respostaSemCabecalho = await semCabecalho.PostJsonAsync("/api/autenticacao/codigo", new { email = Email }, comCsrf: false);
        var respostaComTokenFalso = await semCabecalho.PostJsonAsync("/api/autenticacao/codigo", new { email = Email }, tokenCsrf: "token-falso");

        Assert.Equal(HttpStatusCode.BadRequest, respostaSemNada.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, respostaSemCabecalho.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, respostaComTokenFalso.StatusCode);
        Assert.Equal(0, await ambiente.Banco.ContarAsync("SELECT count(*) FROM identidade.desafios_acesso"));
        Assert.Empty(ambiente.Caixa.Entregas);
    }

    [Fact]
    public async Task Confirmacao_sem_token_CSRF_e_recusada_sem_consumir_o_desafio()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.ObterCsrfAsync();
        var referencia = await cliente.SolicitarCodigoAsync(Email);

        var resposta = await cliente.PostJsonAsync(
            "/api/autenticacao/confirmar",
            new { referencia, email = Email, codigo = ambiente.Caixa.CodigoDo(referencia) },
            comCsrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.False(cliente.Cookies.ContainsKey(ClienteWaca.CookieDeSessao));
        Assert.Equal(1, await ambiente.Banco.ContarAsync(
            $"SELECT count(*) FROM identidade.desafios_acesso WHERE id = '{referencia}' AND consumido_em IS NULL AND tentativas = 0"));
    }

    [Fact]
    public async Task Token_CSRF_de_outra_identidade_e_recusado()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        var api = ambiente.CriarApi();
        using var ana = api.CriarCliente();
        await ana.ObterCsrfAsync();
        var tokenAnonimo = ana.TokenCsrf!;
        await ana.EntrarAsync("ana@exemplo.com");
        using var bia = api.CriarCliente();
        await bia.EntrarAsync("bia@exemplo.com");

        // Token obtido antes do login (identidade anônima), usado depois dele.
        var comTokenAnonimo = await ana.PostJsonAsync("/api/autenticacao/codigo", new { email = Email }, tokenCsrf: tokenAnonimo);
        // Token de outra pessoa autenticada.
        var comTokenDaBia = await ana.PostJsonAsync("/api/autenticacao/codigo", new { email = Email }, tokenCsrf: bia.TokenCsrf);
        // Token renovado no login da própria Ana.
        var comTokenProprio = await ana.PostJsonAsync("/api/autenticacao/codigo", new { email = Email });

        Assert.NotEqual(tokenAnonimo, ana.TokenCsrf);
        Assert.Equal(HttpStatusCode.BadRequest, comTokenAnonimo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, comTokenDaBia.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, comTokenProprio.StatusCode);
    }

    [Fact]
    public async Task Limite_por_IP_e_aplicado_e_cabecalhos_forjados_nao_o_contornam()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        using var cliente = ambiente.CriarApi(permissoesPorIp: 3).CriarCliente();
        await cliente.ObterCsrfAsync();

        var status = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var requisicao = new HttpRequestMessage(HttpMethod.Post, "/api/autenticacao/codigo")
            {
                Content = JsonContent.Create(new { email = $"pessoa{i}@exemplo.com" }),
            };
            requisicao.Headers.Add("X-XSRF-TOKEN", cliente.TokenCsrf);
            requisicao.Headers.Add("X-Forwarded-For", $"203.0.113.{i + 1}");
            requisicao.Headers.Add("X-Real-IP", $"198.51.100.{i + 1}");
            status.Add((await cliente.EnviarAsync(requisicao)).StatusCode);
        }

        // A primeira permissão foi usada pelo GET do token; os cabeçalhos forjados não criam partições novas.
        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], status);
    }

    [Fact]
    public async Task Somente_a_lista_explicita_e_anonima_e_o_restante_exige_autenticacao()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        var api = ambiente.CriarApi();

        var endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();
        var anonimos = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => $"{string.Join(",", e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [])} {e.RoutePattern.RawText}")
            .Order(StringComparer.Ordinal);
        var politica = api.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy;

        Assert.Equal(
            ["GET /api/autenticacao/antiforgery", "POST /api/autenticacao/codigo", "POST /api/autenticacao/confirmar"],
            anonimos);
        Assert.NotNull(politica);
        Assert.Contains(politica.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);

        Assert.All(
            endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null),
            e => Assert.NotEmpty(e.Metadata.GetOrderedMetadata<IAuthorizeData>()));

        using var anonimo = api.CriarCliente();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/autenticacao/eu")).StatusCode);
        // A política padrão também cobre rotas sem endpoint: nada responde anonimamente fora da lista.
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/vaquinhas")).StatusCode);
    }

    [Fact]
    public async Task Respostas_publicas_nao_revelam_se_a_conta_existe_nem_se_a_cota_acabou()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        var api = ambiente.CriarApi();
        using var dono = api.CriarCliente();
        await dono.EntrarAsync("existente@exemplo.com");

        using var cliente = api.CriarCliente();
        await cliente.ObterCsrfAsync();
        var comConta = await cliente.PostJsonAsync("/api/autenticacao/codigo", new { email = "existente@exemplo.com" });
        var semConta = await cliente.PostJsonAsync("/api/autenticacao/codigo", new { email = "nova@exemplo.com" });
        var noLimite = await cliente.PostJsonAsync("/api/autenticacao/codigo", new { email = "nova@exemplo.com" });

        var corpos = new List<JsonElement>();
        foreach (var resposta in new[] { comConta, semConta, noLimite })
        {
            Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
            var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(["referencia"], corpo.EnumerateObject().Select(p => p.Name));
            Assert.Equal(7, corpo.GetProperty("referencia").GetGuid().Version);
            corpos.Add(corpo);
        }

        var referenciaNoLimite = corpos[2].GetProperty("referencia").GetGuid();
        Assert.Equal(0, await ambiente.Banco.ContarAsync($"SELECT count(*) FROM identidade.desafios_acesso WHERE id = '{referenciaNoLimite}'"));
        Assert.False(ambiente.Caixa.Recebeu(referenciaNoLimite));

        var confirmacao = await cliente.ConfirmarAsync(referenciaNoLimite, "nova@exemplo.com", "12345678");
        Assert.Equal(HttpStatusCode.BadRequest, confirmacao.StatusCode);
    }

    [Fact]
    public async Task Logs_nao_expoem_codigos_cookies_tokens_ou_emails()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.ObterCsrfAsync();
        var referencia = await cliente.SolicitarCodigoAsync("Eva.Lima@Exemplo.com");
        var codigo = ambiente.Caixa.CodigoDo(referencia);
        await cliente.ConfirmarAsync(referencia, "Eva.Lima@Exemplo.com", codigo == "00000000" ? "00000001" : "00000000");
        await cliente.ConfirmarAsync(referencia, "Eva.Lima@Exemplo.com", codigo);
        await cliente.GetAsync("/api/autenticacao/eu");

        var proibidos = new List<string> { codigo, "Eva.Lima@Exemplo.com", "EVA.LIMA@EXEMPLO.COM" };
        proibidos.AddRange(cliente.Cookies.Values.Where(v => v.Length >= 16));

        Assert.NotEmpty(ambiente.Logs.Linhas);
        foreach (var proibido in proibidos)
        {
            Assert.DoesNotContain(ambiente.Logs.Linhas, l => l.Contains(proibido, StringComparison.OrdinalIgnoreCase));
        }
    }
}
