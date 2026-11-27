using Npgsql;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Identidade.Sessoes;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;
using static Waca.Infrastructure.Tests.Identidade.IdentidadeNoBancoDeTeste;

namespace Waca.Infrastructure.Tests.Identidade.Sessoes;

/// <summary>Opções de sessão, sem banco.</summary>
public class OpcoesDeSessaoTests
{
    [Fact]
    public void Padroes_sao_os_parametros_provisorios()
    {
        var opcoes = new OpcoesDeSessao();

        opcoes.Validar();
        Assert.Equal(TimeSpan.FromHours(8), opcoes.Inatividade);
        Assert.Equal(TimeSpan.FromDays(7), opcoes.TetoAbsoluto);
    }

    [Theory]
    [InlineData(4, 7 * 24 * 60)]
    [InlineData(25 * 60, 7 * 24 * 60)]
    [InlineData(8 * 60, 7 * 60)]
    [InlineData(8 * 60, 31 * 24 * 60)]
    public void Tempos_invalidos_sao_recusados(int inatividadeEmMinutos, int tetoEmMinutos)
    {
        var opcoes = new OpcoesDeSessao
        {
            Inatividade = TimeSpan.FromMinutes(inatividadeEmMinutos),
            TetoAbsoluto = TimeSpan.FromMinutes(tetoEmMinutos),
        };

        Assert.Throws<InvalidOperationException>(opcoes.Validar);
        Assert.Throws<InvalidOperationException>(() => new SessoesPersistidas(opcoes));
    }

    [Fact]
    public void Estado_da_sessao_so_permite_acesso_com_conta_ativa_confirmada_dentro_do_teto_e_sem_revogacao()
    {
        var agora = DateTimeOffset.UtcNow;
        EstadoDaSessao Estado(EstadoConta estado = EstadoConta.Ativa, bool confirmado = true, DateTimeOffset? teto = null, DateTimeOffset? revogada = null) =>
            new(Guid.CreateVersion7(), Guid.CreateVersion7(), "a@exemplo.com", confirmado, estado, "stamp", teto ?? agora.AddHours(1), revogada);

        Assert.True(Estado().PermiteAcessoEm(agora));
        Assert.False(Estado(EstadoConta.Desativada).PermiteAcessoEm(agora));
        Assert.False(Estado(confirmado: false).PermiteAcessoEm(agora));
        Assert.False(Estado(teto: agora).PermiteAcessoEm(agora));
        Assert.False(Estado(revogada: agora).PermiteAcessoEm(agora));
        Assert.DoesNotContain("a@exemplo.com", Estado().ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("stamp", Estado().ToString(), StringComparison.Ordinal);
    }
}

[Collection(ColecaoPostgres.Nome)]
public class SessoesPersistidasTests(ServidorPostgres servidor)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tabela_restricoes_indice_e_chave_estrangeira_seguem_o_modelo()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();

        var colunas = await banco.ConsultarAsync(
            """
            SELECT column_name, data_type, is_nullable FROM information_schema.columns
            WHERE table_schema = 'identidade' AND table_name = 'sessoes' ORDER BY ordinal_position
            """);
        var restricoes = (await banco.ConsultarAsync(
            "SELECT conname, pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = 'identidade.sessoes'::regclass"))
            .ToDictionary(r => (string)r[0]!, r => (string)r[1]!);

        Assert.Equal(
            [
                ("id", "uuid", "NO"),
                ("pessoa_id", "uuid", "NO"),
                ("criada_em", "timestamp with time zone", "NO"),
                ("expira_absoluta_em", "timestamp with time zone", "NO"),
                ("revogada_em", "timestamp with time zone", "YES"),
            ],
            colunas.Select(c => ((string)c[0]!, (string)c[1]!, (string)c[2]!)));
        Assert.Equal(
            ["ck_sessoes_id", "ck_sessoes_revogacao", "ck_sessoes_validade", "fk_sessoes_pessoa", "pk_sessoes"],
            restricoes.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("REFERENCES identidade.\"AspNetUsers\"(\"Id\") ON DELETE CASCADE", restricoes["fk_sessoes_pessoa"]);
        Assert.Equal(1, await banco.ContarAsync(
            "SELECT count(*) FROM pg_indexes WHERE schemaname = 'identidade' AND tablename = 'sessoes' AND indexname = 'ix_sessoes_pessoa_id'"));
    }

    [Theory]
    [InlineData("id-vazio", "ck_sessoes_id")]
    [InlineData("teto-antes-da-criacao", "ck_sessoes_validade")]
    [InlineData("revogada-antes-da-criacao", "ck_sessoes_revogacao")]
    public async Task Linhas_inconsistentes_sao_recusadas(string caso, string restricao)
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var conta = await CriarContaAsync(banco);
        var criada = DateTimeOffset.UtcNow;

        var excecao = await Assert.ThrowsAsync<PostgresException>(() => InserirAsync(
            banco,
            caso == "id-vazio" ? Guid.Empty : Guid.CreateVersion7(),
            conta.Id,
            criada,
            caso == "teto-antes-da-criacao" ? criada.AddSeconds(-1) : criada.AddDays(7),
            caso == "revogada-antes-da-criacao" ? criada.AddSeconds(-1) : null));

        ExigirViolacao(excecao, PostgresErrorCodes.CheckViolation, restricao);
    }

    [Fact]
    public async Task Sessao_de_pessoa_inexistente_e_recusada_e_excluir_a_conta_exclui_as_sessoes()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var criada = DateTimeOffset.UtcNow;

        var semConta = await Assert.ThrowsAsync<PostgresException>(() => InserirAsync(banco, Guid.CreateVersion7(), Guid.CreateVersion7(), criada, criada.AddDays(7), null));
        ExigirViolacao(semConta, PostgresErrorCodes.ForeignKeyViolation, "fk_sessoes_pessoa");

        var conta = await CriarContaAsync(banco);
        await InserirAsync(banco, Guid.CreateVersion7(), conta.Id, criada, criada.AddDays(7), null);
        await banco.ExecutarAsync($"DELETE FROM identidade.\"AspNetUsers\" WHERE \"Id\" = '{conta.Id}'");

        Assert.Equal(0, await banco.ContarAsync("SELECT count(*) FROM identidade.sessoes"));
    }

    [Fact]
    public async Task Criar_ler_e_revogar_usam_o_contexto_e_a_transacao_do_chamador()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var conta = await CriarContaAsync(banco);
        var sessoes = new SessoesPersistidas(new OpcoesDeSessao());
        var agora = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

        await using (var contexto = banco.CriarContextoDeIdentidade())
        await using (var transacao = await contexto.Database.BeginTransactionAsync(Ct))
        {
            await sessoes.CriarAsync(contexto, conta.Id, agora, Ct);
            await transacao.RollbackAsync(Ct);
        }

        Assert.Equal(0, await banco.ContarAsync("SELECT count(*) FROM identidade.sessoes"));

        SessaoCriada criada;
        await using (var contexto = banco.CriarContextoDeIdentidade())
        {
            criada = await sessoes.CriarAsync(contexto, conta.Id, agora, Ct);
        }

        Assert.Equal(agora.AddDays(7), criada.ExpiraAbsolutaEm);

        await using (var contexto = banco.CriarContextoDeIdentidade())
        {
            var estado = await sessoes.ObterAsync(contexto, criada.Id, conta.Id, Ct);
            Assert.NotNull(estado);
            Assert.Equal((conta.Id, conta.Email, EstadoConta.Ativa), (estado.PessoaId, estado.Email, estado.EstadoConta));
            Assert.True(estado.PermiteAcessoEm(agora));
            Assert.Null(await sessoes.ObterAsync(contexto, criada.Id, Guid.CreateVersion7(), Ct));

            Assert.True(await sessoes.RevogarAsync(contexto, criada.Id, agora.AddMinutes(1), Ct));
            Assert.False(await sessoes.RevogarAsync(contexto, criada.Id, agora.AddMinutes(2), Ct));
            Assert.False((await sessoes.ObterAsync(contexto, criada.Id, conta.Id, Ct))!.PermiteAcessoEm(agora.AddMinutes(1)));
        }
    }

    private static async Task<UsuarioWaca> CriarContaAsync(BancoDeTeste banco)
    {
        var conta = NovaConta($"pessoa{Guid.NewGuid():N}@exemplo.com");
        await using var contexto = banco.CriarContextoDeIdentidade();
        contexto.Users.Add(conta);
        await contexto.SaveChangesAsync(Ct);
        return conta;
    }

    private static async Task InserirAsync(BancoDeTeste banco, Guid id, Guid pessoa, DateTimeOffset criada, DateTimeOffset teto, DateTimeOffset? revogada)
    {
        await using var conexao = new NpgsqlConnection(banco.ConnectionString);
        await conexao.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand(
            "INSERT INTO identidade.sessoes (id, pessoa_id, criada_em, expira_absoluta_em, revogada_em) VALUES (@id, @pessoa, @criada, @teto, @revogada)",
            conexao);
        comando.Parameters.AddWithValue("id", id);
        comando.Parameters.AddWithValue("pessoa", pessoa);
        comando.Parameters.AddWithValue("criada", criada);
        comando.Parameters.AddWithValue("teto", teto);
        comando.Parameters.Add(new NpgsqlParameter("revogada", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = (object?)revogada ?? DBNull.Value });
        await comando.ExecuteNonQueryAsync(Ct);
    }
}
