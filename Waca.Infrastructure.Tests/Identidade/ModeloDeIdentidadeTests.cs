using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Persistencia;
using Xunit;

namespace Waca.Infrastructure.Tests.Identidade;

/// <summary>Modelo do EF Core e configuração do contexto de identidade, sem banco.</summary>
public class ModeloDeIdentidadeTests
{
    [Fact]
    public void Fabrica_de_design_nao_contem_connection_string()
    {
        using var contexto = new WacaIdentidadeDbContextFactory().CreateDbContext([]);

        Assert.True(string.IsNullOrEmpty(contexto.Database.GetConnectionString()));
    }

    [Fact]
    public void Historico_de_migrations_fica_em_identidade()
    {
        using var contexto = new WacaIdentidadeDbContextFactory().CreateDbContext([]);

        var relacional = contexto.GetService<IDbContextOptions>().Extensions.OfType<RelationalOptionsExtension>().Single();

        Assert.Equal("identidade", relacional.MigrationsHistoryTableSchema);
        Assert.Equal("__EFMigrationsHistory", relacional.MigrationsHistoryTableName);
    }

    [Fact]
    public void Modelo_contem_somente_usuarios_claims_logins_tokens_e_chaves_no_schema_identidade()
    {
        using var contexto = new WacaIdentidadeDbContextFactory().CreateDbContext([]);

        var entidades = contexto.Model.GetEntityTypes().ToDictionary(e => e.ClrType);

        Assert.Equal(
            [
                typeof(DataProtectionKey),
                typeof(IdentityUserClaim<Guid>),
                typeof(IdentityUserLogin<Guid>),
                typeof(IdentityUserToken<Guid>),
                typeof(UsuarioWaca),
            ],
            entidades.Keys.OrderBy(t => t.Name, StringComparer.Ordinal).ThenBy(t => t.FullName, StringComparer.Ordinal));
        Assert.All(entidades.Values, e => Assert.Equal("identidade", e.GetSchema()));
        Assert.Equal(
            ["AspNetUserClaims", "AspNetUserLogins", "AspNetUserTokens", "AspNetUsers", "DataProtectionKeys"],
            entidades.Values.Select(e => e.GetTableName()!).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(entidades.Keys, t => t.Name.Contains("Role", StringComparison.Ordinal) || t.Name.Contains("Passkey", StringComparison.Ordinal));
    }

    [Fact]
    public void EmailIndex_e_o_indice_do_Identity_tornado_unico_sem_duplicata()
    {
        using var contexto = new WacaIdentidadeDbContextFactory().CreateDbContext([]);
        var usuario = contexto.Model.FindEntityType(typeof(UsuarioWaca))!;

        var indicesDeEmail = usuario.GetIndexes()
            .Where(i => i.Properties.Any(p => p.Name == nameof(UsuarioWaca.NormalizedEmail)))
            .ToArray();

        var indice = Assert.Single(indicesDeEmail);
        Assert.Equal("EmailIndex", indice.GetDatabaseName());
        Assert.True(indice.IsUnique);
        Assert.Equal([nameof(UsuarioWaca.NormalizedEmail)], indice.Properties.Select(p => p.Name));

        var indiceDeNome = Assert.Single(usuario.GetIndexes(), i => i.GetDatabaseName() == "UserNameIndex");
        Assert.True(indiceDeNome.IsUnique);
        Assert.Equal([nameof(UsuarioWaca.NormalizedUserName)], indiceDeNome.Properties.Select(p => p.Name));
        Assert.Equal(2, usuario.GetIndexes().Count());
    }

    [Fact]
    public void Email_estado_e_identificador_seguem_as_regras_do_Waca()
    {
        using var contexto = new WacaIdentidadeDbContextFactory().CreateDbContext([]);
        var usuario = contexto.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(UsuarioWaca))!;

        foreach (var nome in new[] { nameof(UsuarioWaca.Email), nameof(UsuarioWaca.NormalizedEmail) })
        {
            var propriedade = usuario.FindProperty(nome)!;
            Assert.False(propriedade.IsNullable);
            Assert.Equal(256, propriedade.GetMaxLength());
        }

        var estado = usuario.FindProperty(nameof(UsuarioWaca.Estado))!;
        Assert.False(estado.IsNullable);
        Assert.Equal(typeof(string), estado.GetRelationalTypeMapping().Converter?.ProviderClrType);
        Assert.Equal("character varying(20)", estado.GetRelationalTypeMapping().StoreType);
        Assert.Equal(20, estado.GetMaxLength());
        Assert.Null(estado.FindAnnotation(RelationalAnnotationNames.DefaultValue));
        Assert.Null(estado.GetDefaultValueSql());

        var id = usuario.FindProperty(nameof(UsuarioWaca.Id))!;
        Assert.Equal(ValueGenerated.Never, id.ValueGenerated);
        Assert.Equal("Id", Assert.Single(usuario.FindPrimaryKey()!.Properties).Name);

        Assert.Equal(
            ["CK_AspNetUsers_Email_NaoVazio", "CK_AspNetUsers_Estado", "CK_AspNetUsers_Id_NaoVazio"],
            usuario.GetCheckConstraints().Select(c => c.Name!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Contexto_configurado_sem_o_historico_proprio_e_recusado()
    {
        using var contexto = new WacaIdentidadeDbContext(
            new DbContextOptionsBuilder<WacaIdentidadeDbContext>().UseNpgsql().Options);

        var excecao = Assert.Throws<InvalidOperationException>(() => contexto.Model);

        Assert.Contains(nameof(WacaIdentidadeDbContext.UsarPostgres), excecao.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Modelo_nao_e_construido_sem_as_opcoes_do_Identity()
    {
        var opcoes = new DbContextOptionsBuilder<WacaIdentidadeDbContext>();
        WacaIdentidadeDbContext.UsarPostgres(opcoes, connectionString: null);
        opcoes.ReplaceService<IModelCacheKeyFactory, ChaveDeModeloSemCache>();
        using var contexto = new WacaIdentidadeDbContext(opcoes.Options);

        var excecao = Assert.Throws<InvalidOperationException>(() => contexto.Model);

        Assert.Contains("OpcoesDaIdentidade.Configurar", excecao.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Modelo_nao_e_construido_com_outra_versao_de_schema_do_Identity()
    {
        using var servicos = new ServiceCollection()
            .AddOptions()
            .Configure<IdentityOptions>(o =>
            {
                OpcoesDaIdentidade.Configurar(o);
                o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .BuildServiceProvider();
        var opcoes = new DbContextOptionsBuilder<WacaIdentidadeDbContext>();
        WacaIdentidadeDbContext.UsarPostgres(opcoes, connectionString: null);
        opcoes.UseApplicationServiceProvider(servicos);
        opcoes.ReplaceService<IModelCacheKeyFactory, ChaveDeModeloSemCache>();
        using var contexto = new WacaIdentidadeDbContext(opcoes.Options);

        var excecao = Assert.Throws<InvalidOperationException>(() => contexto.Model);

        Assert.Contains("Versão de schema", excecao.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Composicao_como_a_futura_API_resolve_o_UserManager_com_o_mesmo_modelo_sem_abrir_conexao()
    {
        using var servicos = IdentidadeNoBancoDeTeste.CriarServicosDeIdentidade(connectionString: null);
        using var escopo = servicos.CreateScope();

        var gerenciador = escopo.ServiceProvider.GetRequiredService<UserManager<UsuarioWaca>>();
        var contexto = escopo.ServiceProvider.GetRequiredService<WacaIdentidadeDbContext>();

        Assert.True(gerenciador.Options.User.RequireUniqueEmail);
        Assert.True(gerenciador.SupportsUserEmail);
        Assert.True(gerenciador.SupportsUserSecurityStamp);
        Assert.False(contexto.Database.HasPendingModelChanges());

        using var fabricado = new WacaIdentidadeDbContextFactory().CreateDbContext([]);
        Assert.Equal(
            fabricado.Model.GetEntityTypes().Select(e => e.Name).Order(StringComparer.Ordinal),
            contexto.Model.GetEntityTypes().Select(e => e.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Modelos_de_vaquinhas_e_de_identidade_nao_compartilham_tabelas_nem_schemas()
    {
        using var vaquinhas = new WacaDbContextFactory().CreateDbContext([]);
        using var identidade = new WacaIdentidadeDbContextFactory().CreateDbContext([]);

        var tabelasDeVaquinhas = vaquinhas.Model.GetEntityTypes().Select(e => (e.GetSchema(), e.GetTableName())).ToArray();
        var tabelasDeIdentidade = identidade.Model.GetEntityTypes().Select(e => (e.GetSchema(), e.GetTableName())).ToArray();

        Assert.Empty(tabelasDeVaquinhas.Intersect(tabelasDeIdentidade));
        Assert.All(tabelasDeVaquinhas, t => Assert.NotEqual("identidade", t.Item1));
        Assert.False(typeof(WacaDbContext).IsAssignableTo(typeof(Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityUserContext<UsuarioWaca, Guid>)));
        Assert.Empty(vaquinhas.Database.GetMigrations().Intersect(identidade.Database.GetMigrations()));
    }

    /// <summary>Força a construção de um modelo novo, para exercitar a verificação feita em OnModelCreating.</summary>
    private sealed class ChaveDeModeloSemCache : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => new object();
    }
}
