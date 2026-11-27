using System.Net;
using System.Net.Http.Json;
using Waca.Api.Autenticacao;
using Waca.Api.Tests.Infraestrutura;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;

namespace Waca.Api.Tests.Autenticacao;

[Collection(ColecaoPostgresDaApi.Nome)]
public class PrimeiroAcessoHttpTests(ServidorPostgres servidor)
{
    private const string Email = "Ana.Silva@Exemplo.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pedido_de_codigo_devolve_202_com_referencia_sem_codigo_e_sem_criar_conta()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.ObterCsrfAsync();

        var resposta = await cliente.PostJsonAsync("/api/autenticacao/codigo", new { email = Email });
        var corpo = await resposta.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        var referencia = (await resposta.Content.ReadFromJsonAsync<RespostaDoPedidoDeCodigo>(Ct))!.Referencia;
        Assert.True(ambiente.Caixa.Recebeu(referencia));
        Assert.DoesNotContain(ambiente.Caixa.CodigoDo(referencia), corpo, StringComparison.Ordinal);
        Assert.Equal(1, await ambiente.Banco.ContarAsync($"SELECT count(*) FROM identidade.desafios_acesso WHERE id = '{referencia}'"));
        Assert.Equal(0, await ambiente.Banco.ContarAsync("SELECT count(*) FROM identidade.\"AspNetUsers\""));
        Assert.Equal(0, await ambiente.Banco.ContarAsync("SELECT count(*) FROM identidade.sessoes"));
    }

    [Fact]
    public async Task Confirmacao_correta_cria_conta_confirmada_e_sessao_somente_apos_a_verificacao()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.ObterCsrfAsync();
        var referencia = await cliente.SolicitarCodigoAsync(Email);
        Assert.Equal(0, await ambiente.Banco.ContarAsync("SELECT count(*) FROM identidade.\"AspNetUsers\""));

        var resposta = await cliente.ConfirmarAsync(referencia, Email, ambiente.Caixa.CodigoDo(referencia));

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        var conta = Assert.Single(await ambiente.Banco.ConsultarAsync(
            "SELECT \"Id\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"Estado\", \"PasswordHash\", \"SecurityStamp\" FROM identidade.\"AspNetUsers\""));
        Assert.Equal((Email, "ANA.SILVA@EXEMPLO.COM", true, "Ativa"), ((string)conta[1]!, (string)conta[2]!, (bool)conta[3]!, (string)conta[4]!));
        Assert.Null(conta[5]);
        Assert.False(string.IsNullOrEmpty((string?)conta[6]));
        Assert.Equal(7, ((Guid)conta[0]!).Version);

        var sessao = Assert.Single(await ambiente.Banco.ConsultarAsync("SELECT pessoa_id, revogada_em FROM identidade.sessoes"));
        Assert.Equal(conta[0], sessao[0]);
        Assert.Null(sessao[1]);
        Assert.Equal(1, await ambiente.Banco.ContarAsync(
            $"SELECT count(*) FROM identidade.desafios_acesso WHERE id = '{referencia}' AND consumido_em IS NOT NULL"));

        var eu = await cliente.EuAsync();
        Assert.Equal(conta[0], eu!.PessoaId);
    }

    [Fact]
    public async Task Conta_existente_e_reutilizada_e_tem_o_email_confirmado()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        var existente = new UsuarioWaca(Guid.CreateVersion7(), "ana.silva@exemplo.com", EstadoConta.Ativa)
        {
            NormalizedEmail = "ANA.SILVA@EXEMPLO.COM",
            EmailConfirmed = false,
        };
        existente.NormalizedUserName = existente.UserName!.ToUpperInvariant();
        await using (var contexto = ambiente.CriarContexto())
        {
            contexto.Users.Add(existente);
            await contexto.SaveChangesAsync(Ct);
        }

        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.EntrarAsync(Email);

        var conta = Assert.Single(await ambiente.Banco.ConsultarAsync("SELECT \"Id\", \"EmailConfirmed\", \"SecurityStamp\" FROM identidade.\"AspNetUsers\""));
        Assert.Equal(existente.Id, conta[0]);
        Assert.True((bool)conta[1]!);
        Assert.False(string.IsNullOrEmpty((string?)conta[2]));
        Assert.Equal(existente.Id, (await cliente.EuAsync())!.PessoaId);
    }

    [Fact]
    public async Task Nenhuma_vaquinha_ou_participacao_e_criada()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        using var cliente = ambiente.CriarApi().CriarCliente();

        await cliente.EntrarAsync(Email);

        Assert.Equal(0, await ambiente.Banco.ContarAsync("SELECT count(*) FROM public.vaquinhas"));
        Assert.Equal(0, await ambiente.Banco.ContarAsync(
            "SELECT count(*) FROM information_schema.tables WHERE table_name ILIKE '%particip%' OR table_name ILIKE '%convite%'"));
    }

    [Fact]
    public async Task Confirmacoes_simultaneas_do_mesmo_codigo_geram_no_maximo_uma_sessao()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        var api = ambiente.CriarApi();
        using var primeiro = api.CriarCliente();
        await primeiro.ObterCsrfAsync();
        var referencia = await primeiro.SolicitarCodigoAsync(Email);
        var codigo = ambiente.Caixa.CodigoDo(referencia);

        var clientes = new List<ClienteWaca>();
        for (var i = 0; i < 6; i++)
        {
            var cliente = api.CriarCliente();
            await cliente.ObterCsrfAsync();
            clientes.Add(cliente);
        }

        var largada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tarefas = clientes.Select(async c =>
        {
            await largada.Task;
            return await c.ConfirmarAsync(referencia, Email, codigo);
        }).ToArray();
        largada.SetResult();
        var respostas = await Task.WhenAll(tarefas);

        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.All(respostas.Where(r => r.StatusCode != HttpStatusCode.NoContent), r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(1, await ambiente.Banco.ContarAsync("SELECT count(*) FROM identidade.sessoes"));
        Assert.Equal(1, await ambiente.Banco.ContarAsync("SELECT count(*) FROM identidade.\"AspNetUsers\""));
        clientes.ForEach(c => c.Dispose());
    }

    [Fact]
    public async Task Codigo_incorreto_registra_a_tentativa_sem_criar_conta_nem_sessao()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.ObterCsrfAsync();
        var referencia = await cliente.SolicitarCodigoAsync(Email);
        var errado = ambiente.Caixa.CodigoDo(referencia) == "00000000" ? "00000001" : "00000000";

        var resposta = await cliente.ConfirmarAsync(referencia, Email, errado);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.False(cliente.Cookies.ContainsKey(ClienteWaca.CookieDeSessao));
        Assert.Equal(1, await ambiente.Banco.ContarAsync($"SELECT tentativas FROM identidade.desafios_acesso WHERE id = '{referencia}'"));
        Assert.Equal(0, await ambiente.Banco.ContarAsync("SELECT count(*) FROM identidade.\"AspNetUsers\""));
        Assert.Equal(0, await ambiente.Banco.ContarAsync("SELECT count(*) FROM identidade.sessoes"));
    }

    [Fact]
    public async Task Reuso_do_codigo_e_recusado()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        var api = ambiente.CriarApi();
        using var cliente = api.CriarCliente();
        var referencia = await cliente.EntrarAsync(Email);

        using var outro = api.CriarCliente();
        await outro.ObterCsrfAsync();
        var resposta = await outro.ConfirmarAsync(referencia, Email, ambiente.Caixa.CodigoDo(referencia));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.False(outro.Cookies.ContainsKey(ClienteWaca.CookieDeSessao));
        Assert.Equal(1, await ambiente.Banco.ContarAsync("SELECT count(*) FROM identidade.sessoes"));
    }

    [Theory]
    [InlineData("desativada")]
    [InlineData("bloqueada")]
    public async Task Conta_desativada_ou_bloqueada_nao_recebe_sessao(string caso)
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        var conta = new UsuarioWaca(Guid.CreateVersion7(), Email, caso == "desativada" ? EstadoConta.Desativada : EstadoConta.Ativa)
        {
            NormalizedEmail = "ANA.SILVA@EXEMPLO.COM",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            LockoutEnabled = true,
            LockoutEnd = caso == "bloqueada" ? ambiente.Relogio.GetUtcNow().AddDays(1) : null,
        };
        conta.NormalizedUserName = conta.UserName!.ToUpperInvariant();
        await using (var contexto = ambiente.CriarContexto())
        {
            contexto.Users.Add(conta);
            await contexto.SaveChangesAsync(Ct);
        }

        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.ObterCsrfAsync();
        var referencia = await cliente.SolicitarCodigoAsync(Email);
        var resposta = await cliente.ConfirmarAsync(referencia, Email, ambiente.Caixa.CodigoDo(referencia));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.False(cliente.Cookies.ContainsKey(ClienteWaca.CookieDeSessao));
        Assert.Equal(0, await ambiente.Banco.ContarAsync("SELECT count(*) FROM identidade.sessoes"));
        Assert.Equal(1, await ambiente.Banco.ContarAsync(
            $"SELECT count(*) FROM identidade.desafios_acesso WHERE id = '{referencia}' AND consumido_em IS NOT NULL"));
    }
}
