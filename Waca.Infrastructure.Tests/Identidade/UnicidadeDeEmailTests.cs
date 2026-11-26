using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;
using static Waca.Infrastructure.Tests.Identidade.IdentidadeNoBancoDeTeste;

namespace Waca.Infrastructure.Tests.Identidade;

/// <summary>
/// Unicidade do e-mail normalizado: a validação do Identity (<c>RequireUniqueEmail</c>) é só de aplicação;
/// a garantia é o índice único <c>EmailIndex</c> no PostgreSQL.
/// </summary>
[Collection(ColecaoPostgres.Nome)]
public class UnicidadeDeEmailTests(ServidorPostgres servidor)
{
    private const int Concorrentes = 8;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Dois_usuarios_com_o_mesmo_email_normalizado_sao_recusados_pelo_banco()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        await AdicionarAsync(banco, NovaConta("ana@exemplo.com"));

        var excecao = await Assert.ThrowsAsync<DbUpdateException>(() => AdicionarAsync(banco, NovaConta("ana@exemplo.com")));

        ExigirViolacao(excecao, PostgresErrorCodes.UniqueViolation, "EmailIndex");
        Assert.Equal(1, await banco.ContarAsync("SELECT count(*) FROM identidade.\"AspNetUsers\""));
    }

    [Fact]
    public async Task Diferenca_de_maiusculas_e_minusculas_nao_cria_outra_conta()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        await AdicionarAsync(banco, NovaConta("Ana.Silva@Exemplo.com"));

        var excecao = await Assert.ThrowsAsync<DbUpdateException>(() => AdicionarAsync(banco, NovaConta("ana.silva@EXEMPLO.COM")));

        ExigirViolacao(excecao, PostgresErrorCodes.UniqueViolation, "EmailIndex");
        var gravado = Assert.Single(await banco.ConsultarAsync("SELECT \"Email\", \"NormalizedEmail\" FROM identidade.\"AspNetUsers\""));
        Assert.Equal("Ana.Silva@Exemplo.com", gravado[0]);
        Assert.Equal("ANA.SILVA@EXEMPLO.COM", gravado[1]);
    }

    [Fact]
    public async Task Enderecos_diferentes_continuam_permitidos()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();

        await AdicionarAsync(banco, NovaConta("ana@exemplo.com"));
        await AdicionarAsync(banco, NovaConta("bruno@exemplo.com"));
        await AdicionarAsync(banco, NovaConta("ana@exemplo.com.br"));

        Assert.Equal(3, await banco.ContarAsync("SELECT count(*) FROM identidade.\"AspNetUsers\""));
    }

    [Fact]
    public async Task UserManager_normaliza_pelo_mecanismo_oficial_e_a_validacao_de_aplicacao_recusa_duplicado()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        await using var servicos = banco.CriarServicosDeIdentidade();

        var primeiro = new UsuarioWaca(Guid.CreateVersion7(), "Carla@Exemplo.com", EstadoConta.Ativa);
        Assert.True((await CriarPeloUserManagerAsync(servicos, primeiro)).Succeeded);

        var duplicado = new UsuarioWaca(Guid.CreateVersion7(), "carla@EXEMPLO.com", EstadoConta.Ativa);
        var resultado = await CriarPeloUserManagerAsync(servicos, duplicado);

        Assert.False(resultado.Succeeded);
        Assert.Equal("DuplicateEmail", Assert.Single(resultado.Errors).Code);

        var gravado = Assert.Single(await banco.ConsultarAsync("SELECT \"Id\", \"NormalizedEmail\" FROM identidade.\"AspNetUsers\""));
        Assert.Equal(primeiro.Id, gravado[0]);
        Assert.Equal("CARLA@EXEMPLO.COM", gravado[1]);

        await using var escopo = servicos.CreateAsyncScope();
        var gerenciador = escopo.ServiceProvider.GetRequiredService<UserManager<UsuarioWaca>>();
        Assert.Equal(primeiro.Id, (await gerenciador.FindByEmailAsync("CARLA@exemplo.COM"))?.Id);
    }

    [Fact]
    public async Task Violacao_direta_do_indice_produz_erro_23505()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        await banco.InserirContaPorSqlAsync(Guid.CreateVersion7(), "dora@exemplo.com", "DORA@EXEMPLO.COM", "Ativa");

        var excecao = await Assert.ThrowsAsync<PostgresException>(
            () => banco.InserirContaPorSqlAsync(Guid.CreateVersion7(), "Dora@Exemplo.com", "DORA@EXEMPLO.COM", "Ativa"));

        ExigirViolacao(excecao, "23505", "EmailIndex");
    }

    [Fact]
    public async Task Atualizar_o_email_para_um_endereco_ja_usado_e_recusado_pelo_banco()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        await AdicionarAsync(banco, NovaConta("elisa@exemplo.com"));
        var outra = NovaConta("fabio@exemplo.com");
        await AdicionarAsync(banco, outra);

        await using (var contexto = banco.CriarContextoDeIdentidade())
        {
            var conta = await contexto.Users.SingleAsync(u => u.Id == outra.Id, Ct);
            conta.Email = "Elisa@Exemplo.com";
            conta.NormalizedEmail = Normalizador.NormalizeEmail(conta.Email);

            var excecao = await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Ct));
            ExigirViolacao(excecao, PostgresErrorCodes.UniqueViolation, "EmailIndex");
        }

        var emails = await banco.ConsultarAsync("SELECT \"NormalizedEmail\" FROM identidade.\"AspNetUsers\" ORDER BY 1");
        Assert.Equal(["ELISA@EXEMPLO.COM", "FABIO@EXEMPLO.COM"], emails.Select(e => (string)e[0]!));
    }

    [Fact]
    public async Task Insercoes_concorrentes_pelo_store_do_Identity_confirmam_uma_unica_conta()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var contas = Enumerable.Range(0, Concorrentes)
            .Select(i => NovaConta(i % 2 == 0 ? "gabi@exemplo.com" : "GABI@Exemplo.COM"))
            .ToArray();
        var largada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tarefas = contas.Select(async conta =>
        {
            await using var contexto = banco.CriarContextoDeIdentidade();
            await contexto.Database.OpenConnectionAsync(Ct);
            using var store = new UserOnlyStore<UsuarioWaca, WacaIdentidadeDbContext, Guid>(contexto);

            await largada.Task;
            try
            {
                return (Conta: conta, Resultado: (IdentityResult?)await store.CreateAsync(conta, Ct), Falha: (Exception?)null);
            }
            catch (DbUpdateException falha)
            {
                return (Conta: conta, Resultado: null, Falha: falha);
            }
        }).ToArray();

        largada.SetResult();
        var resultados = await Task.WhenAll(tarefas);

        var vencedor = Assert.Single(resultados, r => r.Resultado?.Succeeded == true);
        var perdedores = resultados.Where(r => r.Resultado?.Succeeded != true).ToArray();
        Assert.Equal(Concorrentes - 1, perdedores.Length);
        Assert.All(perdedores, p => ExigirViolacao(p.Falha!, PostgresErrorCodes.UniqueViolation, "EmailIndex"));

        await ExigirSemInconsistenciaAsync(banco, vencedor.Conta.Id);
    }

    [Fact]
    public async Task Cadastros_concorrentes_pelo_UserManager_confirmam_uma_unica_conta()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        await using var servicos = banco.CriarServicosDeIdentidade();
        var largada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tarefas = Enumerable.Range(0, Concorrentes).Select(async i =>
        {
            var conta = new UsuarioWaca(Guid.CreateVersion7(), i % 2 == 0 ? "hugo@exemplo.com" : "Hugo@EXEMPLO.com", EstadoConta.Ativa);
            await using var escopo = servicos.CreateAsyncScope();
            var gerenciador = escopo.ServiceProvider.GetRequiredService<UserManager<UsuarioWaca>>();
            await escopo.ServiceProvider.GetRequiredService<WacaIdentidadeDbContext>().Database.OpenConnectionAsync(Ct);

            await largada.Task;
            try
            {
                return (Conta: conta, Resultado: (IdentityResult?)await gerenciador.CreateAsync(conta), Falha: (Exception?)null);
            }
            catch (DbUpdateException falha)
            {
                return (Conta: conta, Resultado: null, Falha: falha);
            }
        }).ToArray();

        largada.SetResult();
        var resultados = await Task.WhenAll(tarefas);

        var vencedor = Assert.Single(resultados, r => r.Resultado?.Succeeded == true);
        Assert.All(
            resultados.Where(r => r.Resultado?.Succeeded != true),
            r =>
            {
                // Quem consulta depois do commit do vencedor é recusado pela validação de aplicação;
                // quem passa por ela ao mesmo tempo é recusado pelo índice único.
                if (r.Falha is not null)
                {
                    ExigirViolacao(r.Falha, PostgresErrorCodes.UniqueViolation, "EmailIndex");
                }
                else
                {
                    Assert.Equal("DuplicateEmail", Assert.Single(r.Resultado!.Errors).Code);
                }
            });

        await ExigirSemInconsistenciaAsync(banco, vencedor.Conta.Id);
    }

    private static async Task AdicionarAsync(BancoDeTeste banco, UsuarioWaca conta)
    {
        await using var contexto = banco.CriarContextoDeIdentidade();
        contexto.Users.Add(conta);
        await contexto.SaveChangesAsync(Ct);
    }

    private static async Task<IdentityResult> CriarPeloUserManagerAsync(IServiceProvider servicos, UsuarioWaca conta)
    {
        await using var escopo = servicos.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<UserManager<UsuarioWaca>>().CreateAsync(conta);
    }

    /// <summary>Depois da disputa: só o vencedor existe, nada ficou pendurado e o banco segue aceitando contas.</summary>
    private static async Task ExigirSemInconsistenciaAsync(BancoDeTeste banco, Guid vencedor)
    {
        var linha = Assert.Single(await banco.ConsultarAsync("SELECT \"Id\" FROM identidade.\"AspNetUsers\""));
        Assert.Equal(vencedor, linha[0]);

        foreach (var tabela in new[] { "AspNetUserClaims", "AspNetUserLogins", "AspNetUserTokens" })
        {
            Assert.Equal(0, await banco.ContarAsync($"SELECT count(*) FROM identidade.\"{tabela}\""));
        }

        Assert.Equal(0, await banco.ContarAsync(
            $"SELECT count(*) FROM pg_stat_activity WHERE datname = '{banco.Nome}' AND state LIKE 'idle in transaction%'"));

        await AdicionarAsync(banco, NovaConta("depois.da.disputa@exemplo.com"));
        Assert.Equal(2, await banco.ContarAsync("SELECT count(*) FROM identidade.\"AspNetUsers\""));
    }
}
