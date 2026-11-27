using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Waca.Api.Autenticacao;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Identidade.Desafios;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;

namespace Waca.Api.Tests.Infraestrutura;

[CollectionDefinition(Nome)]
public sealed class ColecaoPostgresDaApi : ICollectionFixture<ServidorPostgres>
{
    public const string Nome = "PostgreSQL da API";
}

/// <summary>Relógio controlado pelo teste, sempre em UTC.</summary>
public sealed class RelogioDeTeste : TimeProvider
{
    private readonly Lock _trava = new();
    private DateTimeOffset _agora = DateTimeOffset.UtcNow.AddTicks(-(DateTimeOffset.UtcNow.Ticks % TimeSpan.TicksPerSecond));

    public override DateTimeOffset GetUtcNow()
    {
        lock (_trava)
        {
            return _agora;
        }
    }

    public void Avancar(TimeSpan intervalo)
    {
        lock (_trava)
        {
            _agora += intervalo;
        }
    }
}

/// <summary>Dublê de entrega dos códigos: existe só no projeto de testes.</summary>
public sealed class CaixaDeTeste : IEntregaDeCodigoDeAcesso
{
    private readonly ConcurrentDictionary<Guid, CodigoParaEntrega> _entregas = new();

    public IReadOnlyCollection<CodigoParaEntrega> Entregas => [.. _entregas.Values];

    public Task EntregarAsync(CodigoParaEntrega entrega, CancellationToken cancellationToken)
    {
        _entregas[entrega.DesafioId] = entrega;
        return Task.CompletedTask;
    }

    public string CodigoDo(Guid desafioId) => _entregas[desafioId].Codigo;

    public bool Recebeu(Guid desafioId) => _entregas.ContainsKey(desafioId);
}

/// <summary>Registro de logs em memória (mensagem formatada, valores estruturados e exceções).</summary>
public sealed class LogsDeTeste : ILoggerProvider
{
    public ConcurrentQueue<string> Linhas { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Registrador(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Registrador(LogsDeTeste destino, string categoria) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var valores = state is IEnumerable<KeyValuePair<string, object?>> pares
                ? string.Join(";", pares.Select(p => $"{p.Key}={p.Value}"))
                : state?.ToString();
            destino.Linhas.Enqueue($"{logLevel}|{categoria}|{formatter(state, exception)}|{valores}|{exception}");
        }
    }
}

/// <summary>
/// API real (Identity, cookies, antiforgery, rate limiting) sobre um banco descartável da fixture Testcontainers.
/// Só o relógio, a entrega de códigos e o destino dos logs são substituídos.
/// </summary>
public sealed class FabricaDaApi(
    string connectionString,
    string chaveHmacBase64,
    RelogioDeTeste relogio,
    CaixaDeTeste caixa,
    LogsDeTeste logs,
    int permissoesPorIp = 1000) : WebApplicationFactory<Program>
{
    public RelogioDeTeste Relogio { get; } = relogio;

    public CaixaDeTeste Caixa { get; } = caixa;

    public LogsDeTeste Logs { get; } = logs;

    public ClienteWaca CriarCliente() =>
        new(CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        }), Caixa);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Identidade", connectionString);
        builder.UseSetting("Waca:Desafios:Chave:VersaoAtual", "1");
        builder.UseSetting("Waca:Desafios:Chave:Chaves:1", chaveHmacBase64);
        builder.UseSetting("Waca:LimitePorIp:PermissoesPorJanela", permissoesPorIp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Waca:LimitePorIp:Janela", "00:01:00");

        builder.ConfigureTestServices(servicos =>
        {
            servicos.RemoveAll<TimeProvider>();
            servicos.AddSingleton<TimeProvider>(Relogio);
            servicos.RemoveAll<IEntregaDeCodigoDeAcesso>();
            servicos.AddSingleton<IEntregaDeCodigoDeAcesso>(Caixa);
            servicos.AddSingleton<ILoggerProvider>(Logs);
        });
    }
}

/// <summary>Banco descartável com as migrations de vaquinhas e de identidade, e uma chave HMAC aleatória.</summary>
public sealed class AmbienteHttp : IAsyncDisposable
{
    private readonly List<FabricaDaApi> _fabricas = [];

    private AmbienteHttp(BancoDeTeste banco)
    {
        Banco = banco;
    }

    public BancoDeTeste Banco { get; }

    public string ChaveHmac { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public RelogioDeTeste Relogio { get; } = new();

    public CaixaDeTeste Caixa { get; } = new();

    public LogsDeTeste Logs { get; } = new();

    public static async Task<AmbienteHttp> CriarAsync(ServidorPostgres servidor)
    {
        var banco = await servidor.CriarBancoAsync();
        try
        {
            await using var contexto = CriarContexto(banco);
            await contexto.Database.MigrateAsync(TestContext.Current.CancellationToken);
            return new AmbienteHttp(banco);
        }
        catch
        {
            await banco.DisposeAsync();
            throw;
        }
    }

    /// <summary>Nova instância da API sobre o mesmo banco, relógio, caixa e chave (simula reinício ou outra instância).</summary>
    public FabricaDaApi CriarApi(int permissoesPorIp = 1000)
    {
        var fabrica = new FabricaDaApi(Banco.ConnectionString, ChaveHmac, Relogio, Caixa, Logs, permissoesPorIp);
        _fabricas.Add(fabrica);
        return fabrica;
    }

    public WacaIdentidadeDbContext CriarContexto() => CriarContexto(Banco);

    public async ValueTask DisposeAsync()
    {
        foreach (var fabrica in _fabricas)
        {
            await fabrica.DisposeAsync();
        }

        await Banco.DisposeAsync();
    }

    private static WacaIdentidadeDbContext CriarContexto(BancoDeTeste banco) =>
        new(WacaIdentidadeDbContext.CriarOpcoesForaDaAplicacao(banco.ConnectionString));
}
