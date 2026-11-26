using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;

namespace Waca.Infrastructure.Tests.Identidade;

/// <summary>
/// Acesso ao contexto de identidade dentro de um <see cref="BancoDeTeste"/> da fixture.
/// </summary>
/// <remarks>
/// Usa somente a connection string do banco descartável criado e registrado pela execução; não há outra
/// conexão possível. Os contextos são configurados pelos mesmos pontos de entrada da Infrastructure
/// (<see cref="WacaIdentidadeDbContext.CriarOpcoesForaDaAplicacao"/> e <see cref="WacaIdentidadeDbContext.UsarPostgres"/>).
/// </remarks>
internal static class IdentidadeNoBancoDeTeste
{
    public static readonly UpperInvariantLookupNormalizer Normalizador = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static WacaIdentidadeDbContext CriarContextoDeIdentidade(this BancoDeTeste banco) =>
        new(WacaIdentidadeDbContext.CriarOpcoesForaDaAplicacao(banco.ConnectionString));

    public static async Task AplicarMigrationsDeIdentidadeAsync(this BancoDeTeste banco)
    {
        await using var contexto = banco.CriarContextoDeIdentidade();
        await contexto.Database.MigrateAsync(Ct);
    }

    /// <summary>Banco novo com as migrations de vaquinhas e de identidade aplicadas.</summary>
    public static async Task<BancoDeTeste> CriarBancoComIdentidadeAsync(this ServidorPostgres servidor)
    {
        var banco = await servidor.CriarBancoAsync();
        try
        {
            await banco.AplicarMigrationsDeIdentidadeAsync();
            return banco;
        }
        catch
        {
            await banco.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Serviços como a futura API os registraria: contexto por <see cref="WacaIdentidadeDbContext.UsarPostgres"/>
    /// e <c>AddIdentityCore</c> com <see cref="OpcoesDaIdentidade.Configurar"/>. Sem login, cookies ou tokens.
    /// </summary>
    public static ServiceProvider CriarServicosDeIdentidade(this BancoDeTeste banco) =>
        CriarServicosDeIdentidade(banco.ConnectionString);

    /// <summary>
    /// Mesma composição, com a connection string de um banco da fixture ou <see langword="null"/>
    /// (sem banco: só para conferir a composição e o modelo, sem abrir conexão).
    /// </summary>
    public static ServiceProvider CriarServicosDeIdentidade(string? connectionString)
    {
        var servicos = new ServiceCollection();
        servicos.AddLogging();
        servicos.AddDbContext<WacaIdentidadeDbContext>(opcoes => WacaIdentidadeDbContext.UsarPostgres(opcoes, connectionString));
        servicos.AddIdentityCore<UsuarioWaca>(OpcoesDaIdentidade.Configurar)
            .AddEntityFrameworkStores<WacaIdentidadeDbContext>();

        return servicos.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    /// <summary>Conta nova com Guid v7 e e-mail normalizado pelo normalizador oficial do Identity.</summary>
    public static UsuarioWaca NovaConta(string email, EstadoConta estado = EstadoConta.Ativa, bool emailConfirmado = true)
    {
        var usuario = new UsuarioWaca(Guid.CreateVersion7(), email, estado)
        {
            EmailConfirmed = emailConfirmado,
        };
        usuario.NormalizedEmail = Normalizador.NormalizeEmail(usuario.Email);
        usuario.NormalizedUserName = Normalizador.NormalizeName(usuario.UserName);
        return usuario;
    }

    /// <summary>
    /// Insere uma linha em <c>identidade.AspNetUsers</c> por SQL parametrizado, sem passar pelo EF nem pelo Identity.
    /// </summary>
    public static async Task InserirContaPorSqlAsync(
        this BancoDeTeste banco,
        Guid id,
        string? email,
        string? emailNormalizado,
        string? estado,
        bool emailConfirmado = false)
    {
        await using var conexao = new NpgsqlConnection(banco.ConnectionString);
        await conexao.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand(
            """
            INSERT INTO identidade."AspNetUsers"
                ("Id", "Estado", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed",
                 "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES (@id, @estado, @nome, @nomeNormalizado, @email, @emailNormalizado, @confirmado, false, false, true, 0)
            """,
            conexao);
        var nome = UsuarioWaca.NomeDeUsuarioPara(id);
        comando.Parameters.AddWithValue("id", id);
        comando.Parameters.AddWithValue("estado", (object?)estado ?? DBNull.Value);
        comando.Parameters.AddWithValue("nome", nome);
        comando.Parameters.AddWithValue("nomeNormalizado", Normalizador.NormalizeName(nome));
        comando.Parameters.AddWithValue("email", (object?)email ?? DBNull.Value);
        comando.Parameters.AddWithValue("emailNormalizado", (object?)emailNormalizado ?? DBNull.Value);
        comando.Parameters.AddWithValue("confirmado", emailConfirmado);
        await comando.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>Exige que a falha seja a violação PostgreSQL esperada (SQLSTATE e, se informado, nome da restrição).</summary>
    public static PostgresException ExigirViolacao(Exception excecao, string sqlState, string? restricao = null)
    {
        var postgres = excecao as PostgresException
            ?? excecao.InnerException as PostgresException
            ?? throw new Xunit.Sdk.XunitException($"Esperava PostgresException {sqlState}, veio {excecao.GetType().Name}: {excecao.Message}");

        Assert.Equal(sqlState, postgres.SqlState);
        if (restricao is not null)
        {
            Assert.Equal(restricao, postgres.ConstraintName);
        }

        return postgres;
    }
}
