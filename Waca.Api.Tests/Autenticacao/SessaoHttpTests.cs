using System.Net;
using System.Net.Http.Json;
using Waca.Api.Autenticacao;
using Waca.Api.Tests.Infraestrutura;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;

namespace Waca.Api.Tests.Autenticacao;

[Collection(ColecaoPostgresDaApi.Nome)]
public class SessaoHttpTests(ServidorPostgres servidor)
{
    private const string Email = "carla@exemplo.com";

    [Fact]
    public async Task Cookies_tem_os_atributos_exigidos()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.ObterCsrfAsync();
        var referencia = await cliente.SolicitarCodigoAsync(Email);

        await cliente.ConfirmarAsync(referencia, Email, ambiente.Caixa.CodigoDo(referencia));

        var sessao = Atributos(cliente.UltimosSetCookie.Single(c => c.StartsWith(ClienteWaca.CookieDeSessao + "=", StringComparison.Ordinal)));
        Assert.Contains("secure", sessao);
        Assert.Contains("httponly", sessao);
        Assert.Contains("samesite=strict", sessao);
        Assert.Contains("path=/", sessao);
        Assert.DoesNotContain(sessao, a => a.StartsWith("domain", StringComparison.Ordinal));
        Assert.DoesNotContain(sessao, a => a.StartsWith("expires", StringComparison.Ordinal) || a.StartsWith("max-age", StringComparison.Ordinal));

        var csrf = Atributos(cliente.UltimosSetCookie.Single(c => c.StartsWith(ProtecaoContraCsrf.CookieDoTokenDeRequisicao + "=", StringComparison.Ordinal)));
        Assert.Contains("secure", csrf);
        Assert.Contains("samesite=strict", csrf);
        Assert.Contains("path=/", csrf);
        Assert.DoesNotContain("httponly", csrf);

        Assert.True(cliente.Cookies.ContainsKey(ProtecaoContraCsrf.CookieDoTokenDeCookie));
    }

    [Fact]
    public async Task Eu_autenticado_devolve_somente_a_propria_identidade_e_anonimo_recebe_401()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        var api = ambiente.CriarApi();
        using var autenticado = api.CriarCliente();
        using var anonimo = api.CriarCliente();
        await autenticado.EntrarAsync(Email);

        var eu = await autenticado.EuAsync();
        var pessoa = Assert.Single(await ambiente.Banco.ConsultarAsync("SELECT \"Id\" FROM identidade.\"AspNetUsers\""))[0];

        Assert.Equal(pessoa, eu!.PessoaId);
        Assert.Equal(Email, eu.Email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/autenticacao/eu")).StatusCode);
    }

    [Fact]
    public async Task Cookie_adulterado_nao_autentica()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.EntrarAsync(Email);

        var original = cliente.Cookies[ClienteWaca.CookieDeSessao];
        var posicao = original.Length / 2;
        cliente.Cookies[ClienteWaca.CookieDeSessao] = original[..posicao] + (original[posicao] == 'A' ? 'B' : 'A') + original[(posicao + 1)..];

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/autenticacao/eu")).StatusCode);
    }

    [Fact]
    public async Task Sessao_inativa_por_mais_de_8_horas_e_recusada()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.EntrarAsync(Email);

        ambiente.Relogio.Avancar(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/autenticacao/eu")).StatusCode);
    }

    [Fact]
    public async Task Teto_absoluto_de_7_dias_vale_mesmo_com_atividade_continua()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.EntrarAsync(Email);

        var decorrido = TimeSpan.Zero;
        while (decorrido + TimeSpan.FromHours(5) < TimeSpan.FromDays(7))
        {
            ambiente.Relogio.Avancar(TimeSpan.FromHours(5));
            decorrido += TimeSpan.FromHours(5);
            Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/autenticacao/eu")).StatusCode);
        }

        ambiente.Relogio.Avancar(TimeSpan.FromDays(7) - decorrido + TimeSpan.FromMinutes(1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/autenticacao/eu")).StatusCode);
    }

    [Theory]
    [InlineData("UPDATE identidade.\"AspNetUsers\" SET \"Estado\" = 'Desativada'")]
    [InlineData("UPDATE identidade.sessoes SET revogada_em = criada_em")]
    [InlineData("UPDATE identidade.\"AspNetUsers\" SET \"SecurityStamp\" = 'outro-stamp'")]
    [InlineData("DELETE FROM identidade.\"AspNetUsers\"")]
    public async Task Alteracao_no_banco_derruba_a_sessao_na_requisicao_seguinte(string alteracao)
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.EntrarAsync(Email);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/autenticacao/eu")).StatusCode);

        await ambiente.Banco.ExecutarAsync(alteracao);

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/autenticacao/eu")).StatusCode);
    }

    [Fact]
    public async Task PessoaId_enviado_pelo_cliente_nao_substitui_a_sessao()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        var api = ambiente.CriarApi();
        using var ana = api.CriarCliente();
        using var bia = api.CriarCliente();
        await ana.EntrarAsync("ana@exemplo.com");
        await bia.EntrarAsync("bia@exemplo.com");
        var idDaBia = (await bia.EuAsync())!.PessoaId;
        var idDaAna = (await ana.EuAsync())!.PessoaId;

        var resposta = await ana.GetAsync($"/api/autenticacao/eu?pessoaId={idDaBia}", r =>
        {
            r.Headers.Add("X-User-Id", idDaBia.ToString());
            r.Headers.Add("X-Pessoa-Id", idDaBia.ToString());
        });
        var eu = await resposta.Content.ReadFromJsonAsync<RespostaDaIdentidade>(TestContext.Current.CancellationToken);

        Assert.NotEqual(idDaAna, idDaBia);
        Assert.Equal(idDaAna, eu!.PessoaId);
        Assert.Equal("ana@exemplo.com", eu.Email);
    }

    [Fact]
    public async Task Renovacao_do_principal_pelo_security_stamp_preserva_sid_e_amr()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.EntrarAsync(Email);
        var cookieOriginal = cliente.Cookies[ClienteWaca.CookieDeSessao];

        // Passado o intervalo do SecurityStampValidator (30 min), o Identity reconstrói o principal e renova o cookie.
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/autenticacao/eu")).StatusCode);
        var renovado = cliente.Cookies[ClienteWaca.CookieDeSessao];
        Assert.NotEqual(cookieOriginal, renovado);

        // O cookie renovado só passa no ValidadorDeSessao se ainda tiver sid e amr.
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/autenticacao/eu")).StatusCode);
    }

    [Fact]
    public async Task Cookie_continua_valido_apos_reinicio_com_as_mesmas_chaves_e_banco()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        var primeira = ambiente.CriarApi();
        using var cliente = primeira.CriarCliente();
        await cliente.EntrarAsync(Email);
        var cookies = new Dictionary<string, string>(cliente.Cookies);
        await primeira.DisposeAsync();

        using var depois = ambiente.CriarApi().CriarCliente();
        foreach (var (nome, valor) in cookies)
        {
            depois.Cookies[nome] = valor;
        }

        Assert.Equal(HttpStatusCode.OK, (await depois.GetAsync("/api/autenticacao/eu")).StatusCode);
        Assert.Equal(1, await ambiente.Banco.ContarAsync("SELECT count(*) FROM identidade.\"DataProtectionKeys\""));
    }

    private static string[] Atributos(string setCookie) =>
        setCookie.Split(';').Skip(1).Select(a => a.Trim().ToLowerInvariant()).ToArray();
}
