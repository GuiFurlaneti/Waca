using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Waca.Infrastructure.Persistencia;
using Xunit;

namespace Waca.Infrastructure.Tests.Postgres;

/// <summary>
/// Banco exclusivo de um teste, criado por <see cref="ServidorPostgres.CriarBancoAsync"/>.
/// É removido (com as conexões abertas) ao ser descartado.
/// </summary>
/// <remarks>
/// Só pode representar um banco criado e registrado pela execução atual: o construtor recusa qualquer outro
/// nome, e a remoção passa pelo registro do <see cref="GestorDeBancosDeTeste"/>.
/// </remarks>
public sealed class BancoDeTeste : IAsyncDisposable
{
    private readonly ServidorPostgres _servidor;

    /// <exception cref="ArgumentException">O nome não segue o padrão dos bancos de teste.</exception>
    /// <exception cref="InvalidOperationException">O banco não foi criado por esta execução.</exception>
    internal BancoDeTeste(ServidorPostgres servidor, string nome)
    {
        ArgumentNullException.ThrowIfNull(servidor);
        NomeDeBancoDeTeste.Validar(nome);
        if (!servidor.PertenceAExecucao(nome))
        {
            throw new InvalidOperationException($"O banco {nome} não foi criado por esta execução.");
        }

        _servidor = servidor;
        Nome = nome;
        ConnectionString = servidor.ConnectionStringDo(nome);
    }

    public string Nome { get; }

    public string ConnectionString { get; }

    /// <summary>Novo contexto, como uma requisição faria. Quem chama descarta.</summary>
    public WacaDbContext CriarContexto(params IInterceptor[] interceptadores) =>
        new(new DbContextOptionsBuilder<WacaDbContext>()
            .UseNpgsql(ConnectionString)
            .AddInterceptors(interceptadores)
            .Options);

    public async Task AplicarMigrationsAsync()
    {
        await using var contexto = CriarContexto();
        await contexto.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public async Task ExecutarAsync(string sql)
    {
        await using var conexao = new NpgsqlConnection(ConnectionString);
        await conexao.OpenAsync(TestContext.Current.CancellationToken);
        await using var comando = new NpgsqlCommand(sql, conexao);
        await comando.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Executa uma consulta e devolve todas as linhas, cada uma como um vetor de valores.</summary>
    public async Task<IReadOnlyList<object?[]>> ConsultarAsync(string sql)
    {
        await using var conexao = new NpgsqlConnection(ConnectionString);
        await conexao.OpenAsync(TestContext.Current.CancellationToken);
        await using var comando = new NpgsqlCommand(sql, conexao);
        await using var leitor = await comando.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var linhas = new List<object?[]>();
        while (await leitor.ReadAsync(TestContext.Current.CancellationToken))
        {
            var valores = new object?[leitor.FieldCount];
            for (var i = 0; i < leitor.FieldCount; i++)
            {
                valores[i] = leitor.IsDBNull(i) ? null : leitor.GetValue(i);
            }

            linhas.Add(valores);
        }

        return linhas;
    }

    public async Task<long> ContarAsync(string sql) => Convert.ToInt64((await ConsultarAsync(sql))[0][0]);

    /// <summary>Remove o banco pelo registro da execução. Repetir não tem efeito.</summary>
    public async ValueTask DisposeAsync() => await _servidor.RemoverBancoAsync(Nome);
}
