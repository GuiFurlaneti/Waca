using Microsoft.EntityFrameworkCore;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;

namespace Waca.Infrastructure.Tests.Persistencia;

[Collection(ColecaoPostgres.Nome)]
public class MigrationTests(ServidorPostgres servidor)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Banco_vazio_aceita_as_migrations_e_nada_fica_pendente()
    {
        await using var banco = await servidor.CriarBancoAsync(aplicarMigrations: false);
        await using var contexto = banco.CriarContexto();

        Assert.NotEmpty(await contexto.Database.GetPendingMigrationsAsync(Ct));

        await contexto.Database.MigrateAsync(Ct);

        Assert.Empty(await contexto.Database.GetPendingMigrationsAsync(Ct));
        Assert.Equal(contexto.Database.GetMigrations(), await contexto.Database.GetAppliedMigrationsAsync(Ct));
        Assert.False(contexto.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Reaplicar_as_migrations_nao_altera_o_banco()
    {
        await using var banco = await servidor.CriarBancoAsync();

        await banco.AplicarMigrationsAsync();

        Assert.Equal(1, await banco.ContarAsync("SELECT count(*) FROM \"__EFMigrationsHistory\""));
    }

    [Fact]
    public async Task Somente_a_tabela_de_vaquinhas_e_o_historico_sao_criados()
    {
        await using var banco = await servidor.CriarBancoAsync();

        var tabelas = await banco.ConsultarAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name");

        Assert.Equal(["__EFMigrationsHistory", "vaquinhas"], tabelas.Select(l => (string)l[0]!));
    }

    [Fact]
    public async Task Colunas_seguem_o_modelo_aprovado()
    {
        await using var banco = await servidor.CriarBancoAsync();

        var colunas = await banco.ConsultarAsync(
            """
            SELECT column_name, data_type, is_nullable, column_default, character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'vaquinhas'
            ORDER BY ordinal_position
            """);

        Assert.Equal(
            [
                ("id", "uuid", "NO"),
                ("nome", "text", "NO"),
                ("organizador_id", "uuid", "NO"),
                ("criada_em", "timestamp with time zone", "NO"),
            ],
            colunas.Select(c => ((string)c[0]!, (string)c[1]!, (string)c[2]!)));
        Assert.All(colunas, c => Assert.Null(c[3]));
        Assert.All(colunas, c => Assert.Null(c[4]));
    }

    [Fact]
    public async Task Chave_primaria_e_indice_por_organizador_existem_sem_unicidade_adicional()
    {
        await using var banco = await servidor.CriarBancoAsync();

        var restricoes = await banco.ConsultarAsync(
            """
            SELECT conname, contype, pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE conrelid = 'public.vaquinhas'::regclass
            ORDER BY conname
            """);
        var indices = await banco.ConsultarAsync(
            "SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'vaquinhas' ORDER BY indexname");

        var restricao = Assert.Single(restricoes);
        Assert.Equal("pk_vaquinhas", restricao[0]);
        Assert.Equal('p', restricao[1]);
        Assert.Equal("PRIMARY KEY (id)", restricao[2]);

        Assert.Equal(["ix_vaquinhas_organizador_id", "pk_vaquinhas"], indices.Select(i => (string)i[0]!));
        var indiceOrganizador = (string)indices[0][1]!;
        Assert.Contains("(organizador_id)", indiceOrganizador);
        Assert.DoesNotContain("UNIQUE", indiceOrganizador);
    }

    [Fact]
    public async Task Tabela_nao_tem_chaves_estrangeiras_nem_gatilhos()
    {
        await using var banco = await servidor.CriarBancoAsync();

        Assert.Equal(0, await banco.ContarAsync(
            "SELECT count(*) FROM pg_constraint WHERE contype = 'f' AND connamespace = 'public'::regnamespace"));
        Assert.Equal(0, await banco.ContarAsync(
            "SELECT count(*) FROM pg_trigger WHERE tgrelid = 'public.vaquinhas'::regclass AND NOT tgisinternal"));
    }
}
