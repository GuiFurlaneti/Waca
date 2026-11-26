using Microsoft.EntityFrameworkCore;
using Npgsql;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;
using static Waca.Infrastructure.Tests.Identidade.IdentidadeNoBancoDeTeste;

namespace Waca.Infrastructure.Tests.Identidade;

[Collection(ColecaoPostgres.Nome)]
public class UsuarioWacaPersistenciaTests(ServidorPostgres servidor)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Guid_v7_e_persistido_e_recuperado_sem_alteracao()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var conta = NovaConta("ivo@exemplo.com");
        Assert.Equal(7, conta.Id.Version);

        await AdicionarAsync(banco, conta);

        var lida = await LerAsync(banco, conta.Id);
        Assert.Equal(conta.Id, lida.Id);
        Assert.Equal(7, lida.Id.Version);
        Assert.Equal(UsuarioWaca.NomeDeUsuarioPara(conta.Id), lida.UserName);
        Assert.Equal(conta.Id, Assert.Single(await banco.ConsultarAsync("SELECT \"Id\" FROM identidade.\"AspNetUsers\""))[0]);
    }

    [Fact]
    public async Task Email_confirmado_e_nao_confirmado_sao_diferenciados()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var confirmada = NovaConta("julia@exemplo.com", emailConfirmado: true);
        var naoConfirmada = NovaConta("kaio@exemplo.com", emailConfirmado: false);
        await AdicionarAsync(banco, confirmada);
        await AdicionarAsync(banco, naoConfirmada);

        var lidaConfirmada = await LerAsync(banco, confirmada.Id);
        var lidaNaoConfirmada = await LerAsync(banco, naoConfirmada.Id);

        Assert.True(lidaConfirmada.EmailConfirmed);
        Assert.False(lidaNaoConfirmada.EmailConfirmed);
        Assert.True(lidaConfirmada.PermiteAcesso());
        Assert.False(lidaNaoConfirmada.PermiteAcesso());
    }

    [Theory]
    [InlineData(EstadoConta.Ativa, "Ativa")]
    [InlineData(EstadoConta.Desativada, "Desativada")]
    public async Task Estado_valido_e_persistido_como_texto(EstadoConta estado, string gravado)
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var conta = NovaConta("lara@exemplo.com", estado);

        await AdicionarAsync(banco, conta);

        Assert.Equal(estado, (await LerAsync(banco, conta.Id)).Estado);
        Assert.Equal(gravado, Assert.Single(await banco.ConsultarAsync("SELECT \"Estado\" FROM identidade.\"AspNetUsers\""))[0]);
    }

    [Fact]
    public async Task Conta_desativada_continua_sem_acesso_mesmo_com_email_confirmado_e_sem_bloqueio_temporario()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var conta = NovaConta("mara@exemplo.com", EstadoConta.Desativada, emailConfirmado: true);
        conta.LockoutEnd = null;

        await AdicionarAsync(banco, conta);

        var lida = await LerAsync(banco, conta.Id);
        Assert.Null(lida.LockoutEnd);
        Assert.Equal(EstadoConta.Desativada, lida.Estado);
        Assert.False(lida.PermiteAcesso());
    }

    [Theory]
    [InlineData("Bloqueada")]
    [InlineData("ativa")]
    [InlineData("")]
    [InlineData("1")]
    public async Task Estado_invalido_e_recusado_pelo_banco(string estado)
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();

        var excecao = await Assert.ThrowsAsync<PostgresException>(
            () => banco.InserirContaPorSqlAsync(Guid.CreateVersion7(), "nina@exemplo.com", "NINA@EXEMPLO.COM", estado));

        ExigirViolacao(excecao, PostgresErrorCodes.CheckViolation, "CK_AspNetUsers_Estado");
    }

    [Fact]
    public async Task Estado_nao_atribuido_e_recusado_pelo_banco_ao_gravar_pelo_EF()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var conta = NovaConta("otto@exemplo.com");
        conta.Estado = default;

        var excecao = await Assert.ThrowsAsync<DbUpdateException>(() => AdicionarAsync(banco, conta));

        ExigirViolacao(excecao, PostgresErrorCodes.CheckViolation, "CK_AspNetUsers_Estado");
        Assert.Equal(0, await banco.ContarAsync("SELECT count(*) FROM identidade.\"AspNetUsers\""));
    }

    [Theory]
    [InlineData(null, "PAULA@EXEMPLO.COM", "Ativa")]
    [InlineData("paula@exemplo.com", null, "Ativa")]
    [InlineData("paula@exemplo.com", "PAULA@EXEMPLO.COM", null)]
    public async Task Dados_obrigatorios_nao_podem_ser_nulos(string? email, string? emailNormalizado, string? estado)
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();

        var excecao = await Assert.ThrowsAsync<PostgresException>(
            () => banco.InserirContaPorSqlAsync(Guid.CreateVersion7(), email, emailNormalizado, estado));

        ExigirViolacao(excecao, PostgresErrorCodes.NotNullViolation);
    }

    [Theory]
    [InlineData("", "QUITA@EXEMPLO.COM")]
    [InlineData("quita@exemplo.com", "")]
    public async Task Email_vazio_e_recusado_pelo_banco(string email, string emailNormalizado)
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();

        var excecao = await Assert.ThrowsAsync<PostgresException>(
            () => banco.InserirContaPorSqlAsync(Guid.CreateVersion7(), email, emailNormalizado, "Ativa"));

        ExigirViolacao(excecao, PostgresErrorCodes.CheckViolation, "CK_AspNetUsers_Email_NaoVazio");
    }

    [Fact]
    public async Task Identificador_vazio_e_recusado_pelo_banco()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();

        var excecao = await Assert.ThrowsAsync<PostgresException>(
            () => banco.InserirContaPorSqlAsync(Guid.Empty, "rui@exemplo.com", "RUI@EXEMPLO.COM", "Ativa"));

        ExigirViolacao(excecao, PostgresErrorCodes.CheckViolation, "CK_AspNetUsers_Id_NaoVazio");
    }

    [Fact]
    public async Task Criar_contas_nao_cria_vaquinha_participacao_nem_outras_tabelas()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var tabelasAntes = await TodasAsTabelasAsync(banco);

        await AdicionarAsync(banco, NovaConta("sara@exemplo.com"));
        await AdicionarAsync(banco, NovaConta("tiago@exemplo.com", EstadoConta.Desativada, emailConfirmado: false));

        Assert.Equal(tabelasAntes, await TodasAsTabelasAsync(banco));
        Assert.Equal(0, await banco.ContarAsync("SELECT count(*) FROM public.vaquinhas"));
        Assert.Equal(0, await banco.ContarAsync(
            "SELECT count(*) FROM information_schema.tables WHERE table_name ILIKE '%particip%' OR table_name ILIKE '%membro%'"));
        Assert.Equal(0, await banco.ContarAsync(
            "SELECT count(*) FROM pg_constraint WHERE contype = 'f' AND (confrelid = 'public.vaquinhas'::regclass OR conrelid = 'public.vaquinhas'::regclass)"));
        Assert.Equal(2, await banco.ContarAsync("SELECT count(*) FROM identidade.\"AspNetUsers\""));
        foreach (var tabela in new[] { "AspNetUserClaims", "AspNetUserLogins", "AspNetUserTokens", "DataProtectionKeys" })
        {
            Assert.Equal(0, await banco.ContarAsync($"SELECT count(*) FROM identidade.\"{tabela}\""));
        }
    }

    private static async Task AdicionarAsync(BancoDeTeste banco, UsuarioWaca conta)
    {
        await using var contexto = banco.CriarContextoDeIdentidade();
        contexto.Users.Add(conta);
        await contexto.SaveChangesAsync(Ct);
    }

    private static async Task<UsuarioWaca> LerAsync(BancoDeTeste banco, Guid id)
    {
        await using var contexto = banco.CriarContextoDeIdentidade();
        return await contexto.Users.AsNoTracking().SingleAsync(u => u.Id == id, Ct);
    }

    private static async Task<string[]> TodasAsTabelasAsync(BancoDeTeste banco) =>
        (await banco.ConsultarAsync(
            "SELECT table_schema || '.' || table_name FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema')"))
        .Select(l => (string)l[0]!)
        .Order(StringComparer.Ordinal)
        .ToArray();
}
