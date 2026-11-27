using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;
using static Waca.Infrastructure.Tests.Identidade.IdentidadeNoBancoDeTeste;

namespace Waca.Infrastructure.Tests.Identidade.Desafios;

[Collection(ColecaoPostgres.Nome)]
public class MigrationDeDesafiosTests(ServidorPostgres servidor)
{
    private const string MigrationInicial = "20261126132912_CriacaoInicialIdentidade";
    private const string MigrationDosDesafios = "20261126155304_DesafiosDeAcesso";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tabela_tem_as_colunas_aprovadas()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();

        var colunas = await banco.ConsultarAsync(
            """
            SELECT column_name, data_type, is_nullable, column_default
            FROM information_schema.columns
            WHERE table_schema = 'identidade' AND table_name = 'desafios_acesso'
            ORDER BY ordinal_position
            """);

        Assert.Equal(
            [
                ("id", "uuid", "NO"),
                ("finalidade", "character varying", "NO"),
                ("email_normalizado", "character varying", "NO"),
                ("pessoa_id", "uuid", "YES"),
                ("segredo_hash", "bytea", "NO"),
                ("versao_chave", "smallint", "NO"),
                ("criado_em", "timestamp with time zone", "NO"),
                ("expira_em", "timestamp with time zone", "NO"),
                ("tentativas", "integer", "NO"),
                ("max_tentativas", "integer", "NO"),
                ("consumido_em", "timestamp with time zone", "YES"),
                ("invalidado_em", "timestamp with time zone", "YES"),
            ],
            colunas.Select(c => ((string)c[0]!, (string)c[1]!, (string)c[2]!)));
        Assert.All(colunas, c => Assert.Null(c[3]));
    }

    [Fact]
    public async Task Restricoes_e_indices_esperados_existem()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();

        var restricoes = (await banco.ConsultarAsync(
            """
            SELECT conname, contype::text FROM pg_constraint
            WHERE conrelid = 'identidade.desafios_acesso'::regclass
            """)).Select(r => ((string)r[0]!, (string)r[1]!)).Order().ToArray();
        var indices = (await banco.ConsultarAsync(
            "SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = 'identidade' AND tablename = 'desafios_acesso'"))
            .ToDictionary(i => (string)i[0]!, i => (string)i[1]!);

        Assert.Equal(
            [
                ("ck_desafios_acesso_consumo", "c"),
                ("ck_desafios_acesso_email", "c"),
                ("ck_desafios_acesso_esgotado", "c"),
                ("ck_desafios_acesso_estado_final", "c"),
                ("ck_desafios_acesso_finalidade", "c"),
                ("ck_desafios_acesso_id", "c"),
                ("ck_desafios_acesso_invalidacao", "c"),
                ("ck_desafios_acesso_pessoa", "c"),
                ("ck_desafios_acesso_segredo", "c"),
                ("ck_desafios_acesso_tentativas", "c"),
                ("ck_desafios_acesso_validade", "c"),
                ("ck_desafios_acesso_versao_chave", "c"),
                ("pk_desafios_acesso", "p"),
            ],
            restricoes);

        Assert.Equal(
            ["ix_desafios_acesso_cota", "ix_desafios_acesso_expira_em", "pk_desafios_acesso", "ux_desafios_acesso_aberto"],
            indices.Keys.Order(StringComparer.Ordinal));
        Assert.StartsWith("CREATE UNIQUE INDEX ux_desafios_acesso_aberto", indices["ux_desafios_acesso_aberto"]);
        Assert.Contains("(email_normalizado, finalidade)", indices["ux_desafios_acesso_aberto"]);
        Assert.Contains("WHERE ((consumido_em IS NULL) AND (invalidado_em IS NULL))", indices["ux_desafios_acesso_aberto"]);
        Assert.Contains("(email_normalizado, finalidade, criado_em)", indices["ix_desafios_acesso_cota"]);
        Assert.DoesNotContain("UNIQUE", indices["ix_desafios_acesso_cota"]);
        Assert.Contains("(expira_em)", indices["ix_desafios_acesso_expira_em"]);
    }

    [Fact]
    public async Task Historico_da_identidade_tem_as_migrations_em_ordem_e_o_das_vaquinhas_nao_muda()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        await using var identidade = banco.CriarContextoDeIdentidade();
        await using var vaquinhas = banco.CriarContexto();

        var historico = (await banco.ConsultarAsync("SELECT \"MigrationId\" FROM identidade.\"__EFMigrationsHistory\" ORDER BY 1"))
            .Select(l => (string)l[0]!).ToArray();
        var historicoPublico = (await banco.ConsultarAsync("SELECT \"MigrationId\" FROM public.\"__EFMigrationsHistory\" ORDER BY 1"))
            .Select(l => (string)l[0]!).ToArray();

        // MVP-04C1 acrescentou a migration de sessões; a contagem passa a vir do contexto.
        Assert.Equal(identidade.Database.GetMigrations().Count(), historico.Length);
        Assert.Equal(MigrationInicial, historico[0]);
        Assert.EndsWith("_DesafiosDeAcesso", historico[1]);
        Assert.Equal(identidade.Database.GetMigrations(), historico);
        Assert.Equal(vaquinhas.Database.GetMigrations(), historicoPublico);
        Assert.Empty(await identidade.Database.GetPendingMigrationsAsync(Ct));
        Assert.False(identidade.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Reaplicar_as_migrations_nao_altera_o_schema()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var identidadeAntes = await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "identidade");
        var publicAntes = await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "public");

        await banco.AplicarMigrationsDeIdentidadeAsync();
        await banco.AplicarMigrationsAsync();

        Assert.Equal(identidadeAntes, await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "identidade"));
        Assert.Equal(publicAntes, await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "public"));

        await using var contexto = banco.CriarContextoDeIdentidade();
        Assert.Equal(
            contexto.Database.GetMigrations().Count(),
            await banco.ContarAsync("SELECT count(*) FROM identidade.\"__EFMigrationsHistory\""));
    }

    [Fact]
    public async Task Nova_migration_preserva_usuarios_chaves_e_o_schema_public()
    {
        await using var banco = await servidor.CriarBancoAsync();
        await using (var contexto = banco.CriarContextoDeIdentidade())
        {
            await contexto.GetService<IMigrator>().MigrateAsync(MigrationInicial, Ct);
        }

        var conta = NovaConta("existente@exemplo.com");
        await using (var contexto = banco.CriarContextoDeIdentidade())
        {
            contexto.Users.Add(conta);
            await contexto.SaveChangesAsync(Ct);
        }

        await banco.ExecutarAsync("INSERT INTO identidade.\"DataProtectionKeys\" (\"FriendlyName\", \"Xml\") VALUES ('teste', '<chave-de-teste/>')");

        var usuariosAntes = await LinhasAsync(banco, "SELECT * FROM identidade.\"AspNetUsers\"");
        var chavesAntes = await LinhasAsync(banco, "SELECT * FROM identidade.\"DataProtectionKeys\"");
        var schemaAntes = await DescreverTabelasExistentesAsync(banco);
        var publicAntes = await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "public");

        // Aplica exatamente a migration dos desafios (as posteriores têm testes próprios).
        await using (var contexto = banco.CriarContextoDeIdentidade())
        {
            await contexto.GetService<IMigrator>().MigrateAsync(MigrationDosDesafios, Ct);
        }

        Assert.Equal(usuariosAntes, await LinhasAsync(banco, "SELECT * FROM identidade.\"AspNetUsers\""));
        Assert.Equal(chavesAntes, await LinhasAsync(banco, "SELECT * FROM identidade.\"DataProtectionKeys\""));
        Assert.Equal(schemaAntes, await DescreverTabelasExistentesAsync(banco));
        Assert.Equal(publicAntes, await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "public"));
        Assert.Equal(0, await banco.ContarAsync("SELECT count(*) FROM identidade.desafios_acesso"));
    }

    [Fact]
    public async Task Tabela_de_desafios_nao_tem_chaves_estrangeiras_nem_gatilhos()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();

        Assert.Equal(0, await banco.ContarAsync(
            "SELECT count(*) FROM pg_constraint WHERE contype = 'f' AND (conrelid = 'identidade.desafios_acesso'::regclass OR confrelid = 'identidade.desafios_acesso'::regclass)"));
        Assert.Equal(0, await banco.ContarAsync(
            "SELECT count(*) FROM pg_trigger WHERE tgrelid = 'identidade.desafios_acesso'::regclass AND NOT tgisinternal"));
    }

    /// <summary>Descrição das tabelas da identidade anteriores aos desafios (usuários, claims, logins, tokens, chaves).</summary>
    private static async Task<string[]> DescreverTabelasExistentesAsync(BancoDeTeste banco) =>
        (await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "identidade"))
        .Where(l => !l.Contains("desafios_acesso", StringComparison.Ordinal) && !l.Contains("__EFMigrationsHistory", StringComparison.Ordinal))
        .ToArray();

    private static async Task<string[]> LinhasAsync(BancoDeTeste banco, string sql) =>
        (await banco.ConsultarAsync(sql))
        .Select(l => string.Join("|", l.Select(v => v switch
        {
            null => "∅",
            DateTime d => d.ToString("O"),
            _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
        })))
        .Order(StringComparer.Ordinal)
        .ToArray();
}
