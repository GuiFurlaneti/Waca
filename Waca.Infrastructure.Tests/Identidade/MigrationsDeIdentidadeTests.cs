using Microsoft.EntityFrameworkCore;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;

namespace Waca.Infrastructure.Tests.Identidade;

[Collection(ColecaoPostgres.Nome)]
public class MigrationsDeIdentidadeTests(ServidorPostgres servidor)
{
    internal static readonly string[] TabelasDaIdentidade =
    [
        "AspNetUserClaims",
        "AspNetUserLogins",
        "AspNetUserTokens",
        "AspNetUsers",
        "DataProtectionKeys",
        "__EFMigrationsHistory",
        "desafios_acesso",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Banco_vazio_aceita_as_migrations_de_identidade_e_nada_fica_pendente()
    {
        await using var banco = await servidor.CriarBancoAsync(aplicarMigrations: false);
        await using var contexto = banco.CriarContextoDeIdentidade();

        Assert.NotEmpty(await contexto.Database.GetPendingMigrationsAsync(Ct));

        await contexto.Database.MigrateAsync(Ct);

        Assert.Empty(await contexto.Database.GetPendingMigrationsAsync(Ct));
        Assert.Equal(contexto.Database.GetMigrations(), await contexto.Database.GetAppliedMigrationsAsync(Ct));
        Assert.False(contexto.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Schema_identidade_contem_somente_as_tabelas_do_Identity_e_do_Data_Protection()
    {
        await using var banco = await servidor.CriarBancoAsync(aplicarMigrations: false);
        await banco.AplicarMigrationsDeIdentidadeAsync();

        Assert.Equal(1, await banco.ContarAsync("SELECT count(*) FROM pg_namespace WHERE nspname = 'identidade'"));
        Assert.Equal(TabelasDaIdentidade, await TabelasDoSchemaAsync(banco, "identidade"));

        // Só a identidade foi aplicada: nada no schema public.
        Assert.Empty(await TabelasDoSchemaAsync(banco, "public"));
    }

    [Fact]
    public async Task Nao_existem_tabelas_de_papeis_nem_de_passkeys()
    {
        await using var banco = await servidor.CriarBancoAsync(aplicarMigrations: false);
        await banco.AplicarMigrationsDeIdentidadeAsync();

        Assert.Equal(0, await banco.ContarAsync(
            """
            SELECT count(*)
            FROM information_schema.tables
            WHERE table_schema = 'identidade'
              AND table_type = 'BASE TABLE'
              AND (
                  table_name ILIKE '%role%'
                  OR table_name ILIKE '%passkey%'
              )
            """));
    }

    [Fact]
    public async Task Historico_proprio_fica_no_schema_identidade_e_contem_so_a_migration_da_identidade()
    {
        await using var banco = await servidor.CriarBancoAsync(aplicarMigrations: false);
        await banco.AplicarMigrationsDeIdentidadeAsync();
        await using var contexto = banco.CriarContextoDeIdentidade();

        var historico = await banco.ConsultarAsync("SELECT \"MigrationId\" FROM identidade.\"__EFMigrationsHistory\" ORDER BY \"MigrationId\"");

        Assert.Equal(contexto.Database.GetMigrations(), historico.Select(l => (string)l[0]!));
        Assert.Equal(0, await banco.ContarAsync(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '__EFMigrationsHistory'"));
    }

    [Fact]
    public async Task Reaplicar_as_migrations_de_identidade_nao_altera_o_banco()
    {
        await using var banco = await servidor.CriarBancoAsync(aplicarMigrations: false);
        await banco.AplicarMigrationsDeIdentidadeAsync();
        var antes = await DescreverSchemaAsync(banco, "identidade");

        await banco.AplicarMigrationsDeIdentidadeAsync();

        Assert.Equal(antes, await DescreverSchemaAsync(banco, "identidade"));
        Assert.Equal(1, await banco.ContarAsync("SELECT count(*) FROM identidade.\"__EFMigrationsHistory\""));
    }

    [Fact]
    public async Task Colunas_de_email_e_estado_sao_obrigatorias_com_os_comprimentos_do_Identity()
    {
        await using var banco = await servidor.CriarBancoAsync(aplicarMigrations: false);
        await banco.AplicarMigrationsDeIdentidadeAsync();

        var colunas = (await banco.ConsultarAsync(
            """
            SELECT column_name, data_type, is_nullable, character_maximum_length, column_default
            FROM information_schema.columns
            WHERE table_schema = 'identidade' AND table_name = 'AspNetUsers'
            """)).ToDictionary(c => (string)c[0]!);

        Assert.Equal(("uuid", "NO"), ((string)colunas["Id"][1]!, (string)colunas["Id"][2]!));
        Assert.Null(colunas["Id"][4]);

        foreach (var (nome, comprimento) in new[] { ("Email", 256), ("NormalizedEmail", 256), ("Estado", 20) })
        {
            Assert.Equal("character varying", colunas[nome][1]);
            Assert.Equal("NO", colunas[nome][2]);
            Assert.Equal(comprimento, Convert.ToInt32(colunas[nome][3]));
            Assert.Null(colunas[nome][4]);
        }

        Assert.Equal("YES", colunas["UserName"][2]);
        Assert.Equal("YES", colunas["NormalizedUserName"][2]);
        Assert.Equal("NO", colunas["EmailConfirmed"][2]);
    }

    [Fact]
    public async Task EmailIndex_e_unico_e_e_o_unico_indice_sobre_o_email_normalizado()
    {
        await using var banco = await servidor.CriarBancoAsync(aplicarMigrations: false);
        await banco.AplicarMigrationsDeIdentidadeAsync();

        var indices = (await banco.ConsultarAsync(
            "SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = 'identidade' AND tablename = 'AspNetUsers'"))
            .ToDictionary(i => (string)i[0]!, i => (string)i[1]!);

        Assert.Equal(["EmailIndex", "PK_AspNetUsers", "UserNameIndex"], indices.Keys.Order(StringComparer.Ordinal));
        Assert.StartsWith("CREATE UNIQUE INDEX \"EmailIndex\"", indices["EmailIndex"]);
        Assert.EndsWith("(\"NormalizedEmail\")", indices["EmailIndex"]);
        Assert.StartsWith("CREATE UNIQUE INDEX \"UserNameIndex\"", indices["UserNameIndex"]);
        Assert.EndsWith("(\"NormalizedUserName\")", indices["UserNameIndex"]);
        Assert.Single(indices.Values, d => d.Contains("NormalizedEmail", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Restricoes_de_estado_identificador_e_email_existem_no_banco()
    {
        await using var banco = await servidor.CriarBancoAsync(aplicarMigrations: false);
        await banco.AplicarMigrationsDeIdentidadeAsync();

        var restricoes = (await banco.ConsultarAsync(
            """
            SELECT conname, pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE conrelid = 'identidade."AspNetUsers"'::regclass AND contype = 'c'
            """)).ToDictionary(r => (string)r[0]!, r => (string)r[1]!);

        Assert.Equal(
            ["CK_AspNetUsers_Email_NaoVazio", "CK_AspNetUsers_Estado", "CK_AspNetUsers_Id_NaoVazio"],
            restricoes.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("'Ativa'", restricoes["CK_AspNetUsers_Estado"]);
        Assert.Contains("'Desativada'", restricoes["CK_AspNetUsers_Estado"]);
        Assert.Contains("00000000-0000-0000-0000-000000000000", restricoes["CK_AspNetUsers_Id_NaoVazio"]);
    }

    [Fact]
    public async Task Tabela_de_chaves_do_Data_Protection_existe_com_as_colunas_esperadas_e_vazia()
    {
        await using var banco = await servidor.CriarBancoAsync(aplicarMigrations: false);
        await banco.AplicarMigrationsDeIdentidadeAsync();

        var colunas = await banco.ConsultarAsync(
            """
            SELECT column_name, data_type
            FROM information_schema.columns
            WHERE table_schema = 'identidade' AND table_name = 'DataProtectionKeys'
            ORDER BY ordinal_position
            """);

        Assert.Equal(
            [("Id", "integer"), ("FriendlyName", "text"), ("Xml", "text")],
            colunas.Select(c => ((string)c[0]!, (string)c[1]!)));
        Assert.Equal(0, await banco.ContarAsync("SELECT count(*) FROM identidade.\"DataProtectionKeys\""));
    }

    [Fact]
    public async Task Chaves_estrangeiras_ligam_apenas_tabelas_da_identidade_aos_usuarios()
    {
        await using var banco = await servidor.CriarBancoAsync(aplicarMigrations: false);
        await banco.AplicarMigrationsDeIdentidadeAsync();

        var chaves = await banco.ConsultarAsync(
            """
            SELECT conrelid::regclass::text, confrelid::regclass::text
            FROM pg_constraint
            WHERE contype = 'f' AND connamespace = 'identidade'::regnamespace
            ORDER BY 1
            """);

        Assert.Equal(
            [
                ("identidade.\"AspNetUserClaims\"", "identidade.\"AspNetUsers\""),
                ("identidade.\"AspNetUserLogins\"", "identidade.\"AspNetUsers\""),
                ("identidade.\"AspNetUserTokens\"", "identidade.\"AspNetUsers\""),
            ],
            chaves.Select(c => ((string)c[0]!, (string)c[1]!)).Order());
    }

    internal static async Task<string[]> TabelasDoSchemaAsync(BancoDeTeste banco, string schema) =>
        (await banco.ConsultarAsync(
            $"SELECT table_name FROM information_schema.tables WHERE table_schema = '{schema}'"))
        .Select(l => (string)l[0]!)
        .Order(StringComparer.Ordinal)
        .ToArray();

    /// <summary>Descrição textual de tabelas, colunas, índices e restrições de um schema, para comparar antes e depois.</summary>
    internal static async Task<string[]> DescreverSchemaAsync(BancoDeTeste banco, string schema)
    {
        var colunas = await banco.ConsultarAsync(
            $"""
            SELECT table_name || '.' || column_name || ':' || data_type || ':' || is_nullable || ':' || coalesce(column_default, '')
            FROM information_schema.columns WHERE table_schema = '{schema}'
            """);
        var indices = await banco.ConsultarAsync(
            $"SELECT indexdef FROM pg_indexes WHERE schemaname = '{schema}'");
        var restricoes = await banco.ConsultarAsync(
            $"""
            SELECT conrelid::regclass::text || ':' || conname || ':' || pg_get_constraintdef(oid)
            FROM pg_constraint WHERE connamespace = '{schema}'::regnamespace
            """);

        return colunas.Concat(indices).Concat(restricoes)
            .Select(l => (string)l[0]!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
