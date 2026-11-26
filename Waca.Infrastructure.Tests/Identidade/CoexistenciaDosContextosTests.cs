using Microsoft.EntityFrameworkCore;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;

namespace Waca.Infrastructure.Tests.Identidade;

/// <summary>
/// Vaquinhas e identidade no mesmo banco descartável, cada contexto com schema e histórico próprios.
/// </summary>
[Collection(ColecaoPostgres.Nome)]
public class CoexistenciaDosContextosTests(ServidorPostgres servidor)
{
    private static readonly string[] TabelasDeVaquinhas = ["__EFMigrationsHistory", "vaquinhas"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Vaquinhas_e_depois_identidade_mantem_os_dois_modelos_integros()
    {
        await using var banco = await servidor.CriarBancoAsync(aplicarMigrations: false);

        await banco.AplicarMigrationsAsync();
        var vaquinhasAntes = await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "public");

        await banco.AplicarMigrationsDeIdentidadeAsync();

        Assert.Equal(vaquinhasAntes, await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "public"));
        await ExigirModelosIntegrosAsync(banco);
    }

    [Fact]
    public async Task Identidade_e_depois_vaquinhas_mantem_os_dois_modelos_integros()
    {
        await using var banco = await servidor.CriarBancoAsync(aplicarMigrations: false);

        await banco.AplicarMigrationsDeIdentidadeAsync();
        var identidadeAntes = await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "identidade");

        await banco.AplicarMigrationsAsync();

        Assert.Equal(identidadeAntes, await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "identidade"));
        await ExigirModelosIntegrosAsync(banco);
    }

    [Fact]
    public async Task Reaplicar_as_migrations_dos_dois_contextos_nao_altera_nenhum_schema()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var publicAntes = await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "public");
        var identidadeAntes = await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "identidade");

        await banco.AplicarMigrationsAsync();
        await banco.AplicarMigrationsDeIdentidadeAsync();
        await banco.AplicarMigrationsAsync();

        Assert.Equal(publicAntes, await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "public"));
        Assert.Equal(identidadeAntes, await MigrationsDeIdentidadeTests.DescreverSchemaAsync(banco, "identidade"));
        await ExigirModelosIntegrosAsync(banco);
    }

    [Fact]
    public async Task Nenhum_outro_schema_de_aplicacao_e_criado()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();

        var schemas = await banco.ConsultarAsync(
            """
            SELECT DISTINCT table_schema FROM information_schema.tables
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
            """);

        Assert.Equal(["identidade", "public"], schemas.Select(s => (string)s[0]!).Order(StringComparer.Ordinal));
    }

    private static async Task ExigirModelosIntegrosAsync(BancoDeTeste banco)
    {
        Assert.Equal(TabelasDeVaquinhas, await MigrationsDeIdentidadeTests.TabelasDoSchemaAsync(banco, "public"));
        Assert.Equal(MigrationsDeIdentidadeTests.TabelasDaIdentidade, await MigrationsDeIdentidadeTests.TabelasDoSchemaAsync(banco, "identidade"));

        await using var vaquinhas = banco.CriarContexto();
        await using var identidade = banco.CriarContextoDeIdentidade();

        var historicoDeVaquinhas = await banco.ConsultarAsync("SELECT \"MigrationId\" FROM public.\"__EFMigrationsHistory\" ORDER BY 1");
        var historicoDeIdentidade = await banco.ConsultarAsync("SELECT \"MigrationId\" FROM identidade.\"__EFMigrationsHistory\" ORDER BY 1");

        Assert.Equal(vaquinhas.Database.GetMigrations(), historicoDeVaquinhas.Select(l => (string)l[0]!));
        Assert.Equal(identidade.Database.GetMigrations(), historicoDeIdentidade.Select(l => (string)l[0]!));
        Assert.Empty(vaquinhas.Database.GetMigrations().Intersect(identidade.Database.GetMigrations()));

        Assert.Empty(await vaquinhas.Database.GetPendingMigrationsAsync(Ct));
        Assert.Empty(await identidade.Database.GetPendingMigrationsAsync(Ct));
        Assert.False(vaquinhas.Database.HasPendingModelChanges());
        Assert.False(identidade.Database.HasPendingModelChanges());
    }
}
