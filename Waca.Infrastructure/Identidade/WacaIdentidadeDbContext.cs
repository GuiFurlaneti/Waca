using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Waca.Infrastructure.Identidade.Configuracoes;

namespace Waca.Infrastructure.Identidade;

/// <summary>
/// Contexto do EF Core para as contas do ASP.NET Core Identity e as chaves de Data Protection,
/// no schema <c>identidade</c> do PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// É separado do contexto das vaquinhas: tem schema, migrations e histórico próprios
/// (<c>identidade.__EFMigrationsHistory</c>). Usa <see cref="IdentityUserContext{TUser, TKey}"/>, sem papéis.
/// </para>
/// <para>
/// Duas verificações impedem divergência entre design-time, testes e produção: o contexto recusa ser
/// configurado com outra tabela de histórico, e o modelo recusa ser construído sem as opções de
/// <see cref="OpcoesDaIdentidade"/> (o Identity escolheria outra versão de schema).
/// </para>
/// <para>
/// A tabela <c>DataProtectionKeys</c> só prepara a persistência das chaves. Quando a API passar a usar
/// Data Protection persistida, a produção deverá proteger as chaves em repouso (certificado ou mecanismo
/// seguro equivalente): gravar as chaves em banco desliga a criptografia em repouso padrão.
/// </para>
/// <para>Nenhuma migration é aplicada automaticamente; não há connection string no código.</para>
/// </remarks>
public sealed class WacaIdentidadeDbContext(DbContextOptions<WacaIdentidadeDbContext> options)
    : IdentityUserContext<UsuarioWaca, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>Schema PostgreSQL de todas as tabelas deste contexto.</summary>
    public const string Schema = "identidade";

    /// <summary>Nome da tabela de histórico de migrations, dentro de <see cref="Schema"/>.</summary>
    public const string TabelaDeHistorico = "__EFMigrationsHistory";

    private static readonly IServiceProvider ServicosForaDaAplicacao = new ServiceCollection()
        .AddOptions()
        .Configure<IdentityOptions>(OpcoesDaIdentidade.Configurar)
        .BuildServiceProvider();

    /// <inheritdoc />
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    /// <summary>
    /// Configura o provedor PostgreSQL com o histórico de migrations próprio da identidade.
    /// </summary>
    /// <remarks>
    /// É o ponto único de configuração do provedor. A futura API deve usá-lo no registro do contexto,
    /// junto com <c>AddIdentityCore</c> configurado por <see cref="OpcoesDaIdentidade.Configurar"/>.
    /// </remarks>
    /// <param name="opcoes">Construtor de opções do contexto.</param>
    /// <param name="connectionString">Connection string fornecida por configuração segura, ou <see langword="null"/> para gerar migrations.</param>
    public static DbContextOptionsBuilder UsarPostgres(DbContextOptionsBuilder opcoes, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(opcoes);

        return connectionString is null
            ? opcoes.UseNpgsql(ConfigurarNpgsql)
            : opcoes.UseNpgsql(connectionString, ConfigurarNpgsql);
    }

    /// <summary>
    /// Opções para uso fora da aplicação (ferramentas de design-time e testes), já com as opções do Identity.
    /// </summary>
    /// <param name="connectionString">Connection string, ou <see langword="null"/> para gerar migrations.</param>
    public static DbContextOptions<WacaIdentidadeDbContext> CriarOpcoesForaDaAplicacao(string? connectionString)
    {
        var opcoes = new DbContextOptionsBuilder<WacaIdentidadeDbContext>();
        UsarPostgres(opcoes, connectionString);
        opcoes.UseApplicationServiceProvider(ServicosForaDaAplicacao);
        return opcoes.Options;
    }

    /// <exception cref="InvalidOperationException">O provedor não foi configurado por <see cref="UsarPostgres"/>.</exception>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var relacional = optionsBuilder.Options.Extensions.OfType<RelationalOptionsExtension>().SingleOrDefault();
        if (relacional is null
            || relacional.MigrationsHistoryTableSchema != Schema
            || relacional.MigrationsHistoryTableName != TabelaDeHistorico)
        {
            throw new InvalidOperationException(
                $"O contexto de identidade deve ser configurado por {nameof(UsarPostgres)}, com histórico de migrations em {Schema}.{TabelaDeHistorico}.");
        }

        base.OnConfiguring(optionsBuilder);
    }

    /// <exception cref="InvalidOperationException">As opções do Identity não são as de <see cref="OpcoesDaIdentidade"/>.</exception>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        OpcoesDaIdentidade.ExigirArmazenamentoCompativel(OpcoesDeArmazenamento());

        builder.HasDefaultSchema(Schema);
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new UsuarioWacaConfiguracao());
    }

    private static void ConfigurarNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(TabelaDeHistorico, Schema);

    // Mesma fonte que o IdentityUserContext usa para escolher a versão do schema.
    private StoreOptions? OpcoesDeArmazenamento() =>
        this.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()
            ?.ApplicationServiceProvider
            ?.GetService<IOptions<IdentityOptions>>()
            ?.Value
            ?.Stores;
}
