using System.Net;
using Waca.Api.Tests.Infraestrutura;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;

namespace Waca.Api.Tests.Autenticacao;

/// <summary>
/// Atomicidade da confirmação: falhas reais do PostgreSQL, provocadas por gatilhos criados só no banco descartável
/// do teste, desfazem consumo, conta e sessão juntos.
/// </summary>
[Collection(ColecaoPostgresDaApi.Nome)]
public class TransacoesHttpTests(ServidorPostgres servidor)
{
    private const string Email = "bruno@exemplo.com";

    [Fact]
    public async Task Falha_ao_criar_a_conta_desfaz_o_consumo_do_desafio()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        await CriarGatilhoQueFalhaAsync(ambiente, "\"AspNetUsers\"");
        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.ObterCsrfAsync();
        var referencia = await cliente.SolicitarCodigoAsync(Email);
        var codigo = ambiente.Caixa.CodigoDo(referencia);

        var resposta = await cliente.ConfirmarAsync(referencia, Email, codigo);

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        Assert.False(cliente.Cookies.ContainsKey(ClienteWaca.CookieDeSessao));
        await ExigirNadaGravadoAsync(ambiente, referencia);

        // Sem a falha, o mesmo código ainda vale: o consumo foi desfeito.
        await ambiente.Banco.ExecutarAsync("DROP TRIGGER teste_falha ON identidade.\"AspNetUsers\"");
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.ConfirmarAsync(referencia, Email, codigo)).StatusCode);
    }

    [Fact]
    public async Task Falha_ao_gravar_a_sessao_desfaz_a_conta_e_o_consumo()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);
        await CriarGatilhoQueFalhaAsync(ambiente, "sessoes");
        using var cliente = ambiente.CriarApi().CriarCliente();
        await cliente.ObterCsrfAsync();
        var referencia = await cliente.SolicitarCodigoAsync(Email);
        var codigo = ambiente.Caixa.CodigoDo(referencia);

        var resposta = await cliente.ConfirmarAsync(referencia, Email, codigo);

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        Assert.False(cliente.Cookies.ContainsKey(ClienteWaca.CookieDeSessao));
        await ExigirNadaGravadoAsync(ambiente, referencia);

        await ambiente.Banco.ExecutarAsync("DROP TRIGGER teste_falha ON identidade.sessoes");
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.ConfirmarAsync(referencia, Email, codigo)).StatusCode);
        Assert.Equal(1, await ambiente.Banco.ContarAsync("SELECT count(*) FROM identidade.sessoes"));
    }

    [Fact]
    public async Task Conta_criada_ao_mesmo_tempo_por_outra_operacao_viola_EmailIndex_e_a_transacao_e_repetida_uma_vez()
    {
        await using var ambiente = await AmbienteHttp.CriarAsync(servidor);

        // Na primeira inserção, o gatilho grava antes outra conta com o mesmo e-mail normalizado (como faria uma
        // operação concorrente): a inserção original viola EmailIndex (23505). A sequência não é transacional,
        // então a repetição não dispara o gatilho de novo.
        await ambiente.Banco.ExecutarAsync(
            """
            CREATE SEQUENCE identidade.teste_conflito;
            CREATE FUNCTION identidade.teste_conflito() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF nextval('identidade.teste_conflito') = 1 THEN
                    INSERT INTO identidade."AspNetUsers"
                        ("Id", "Estado", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed",
                         "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                    VALUES (gen_random_uuid(), 'Ativa', md5(random()::text), upper(md5(random()::text)), NEW."Email",
                            NEW."NormalizedEmail", true, false, false, true, 0);
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER teste_conflito BEFORE INSERT ON identidade."AspNetUsers"
                FOR EACH ROW EXECUTE FUNCTION identidade.teste_conflito();
            """);
        using var cliente = ambiente.CriarApi().CriarCliente();

        await cliente.EntrarAsync(Email);

        Assert.True(await ambiente.Banco.ContarAsync("SELECT last_value FROM identidade.teste_conflito") >= 2);
        var conta = Assert.Single(await ambiente.Banco.ConsultarAsync("SELECT \"Id\" FROM identidade.\"AspNetUsers\""));
        var sessao = Assert.Single(await ambiente.Banco.ConsultarAsync("SELECT pessoa_id FROM identidade.sessoes"));
        Assert.Equal(conta[0], sessao[0]);
        Assert.Equal(conta[0], (await cliente.EuAsync())!.PessoaId);
    }

    private static Task CriarGatilhoQueFalhaAsync(AmbienteHttp ambiente, string tabela) =>
        ambiente.Banco.ExecutarAsync(
            $"""
            CREATE OR REPLACE FUNCTION identidade.teste_falha() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'falha provocada pelo teste';
            END $$;
            CREATE TRIGGER teste_falha BEFORE INSERT ON identidade.{tabela}
                FOR EACH ROW EXECUTE FUNCTION identidade.teste_falha();
            """);

    private static async Task ExigirNadaGravadoAsync(AmbienteHttp ambiente, Guid referencia)
    {
        Assert.Equal(0, await ambiente.Banco.ContarAsync("SELECT count(*) FROM identidade.\"AspNetUsers\""));
        Assert.Equal(0, await ambiente.Banco.ContarAsync("SELECT count(*) FROM identidade.sessoes"));
        Assert.Equal(1, await ambiente.Banco.ContarAsync(
            $"SELECT count(*) FROM identidade.desafios_acesso WHERE id = '{referencia}' AND consumido_em IS NULL AND invalidado_em IS NULL AND tentativas = 0"));
    }
}
