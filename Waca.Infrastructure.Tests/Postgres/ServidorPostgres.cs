using System.Net;
using System.Runtime.ExceptionServices;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Waca.Infrastructure.Tests.Postgres;

/// <summary>
/// PostgreSQL descartável em contêiner, criado pelo Testcontainers e compartilhado pelos testes da
/// coleção <see cref="ColecaoPostgres"/>. É o único servidor que os testes de integração usam.
/// </summary>
/// <remarks>
/// <para>
/// Não há servidor alternativo nem leitura de variável de ambiente para escolher outro banco. Sem Docker,
/// os testes que dependem de banco falham.
/// </para>
/// <para>
/// Restrições de rede: o Docker precisa ser local (socket Unix, named pipe ou TCP em loopback). A porta
/// do PostgreSQL é publicada com número aleatório e só em 127.0.0.1; isso é exigido antes da criação do
/// contêiner e conferido depois de iniciado, inspecionando o contêiner. Qualquer divergência interrompe os testes.
/// </para>
/// </remarks>
public sealed class ServidorPostgres : IAsyncLifetime
{
    /// <summary>Imagem oficial PostgreSQL 16.13, fixada pelo digest do índice multiplataforma.</summary>
    public const string Imagem = "postgres:16.13@sha256:5d143123fdf80462d1778cd4f24b9f7ca13c87174bca19141fb194c5a1ebca59";

    internal const string PortaDoPostgres = "5432/tcp";
    internal const string Loopback = "127.0.0.1";

    private PostgreSqlContainer? _conteiner;
    private string? _conexaoAdministrativa;
    private readonly GestorDeBancosDeTeste _gestor;

    public ServidorPostgres()
    {
        _gestor = new GestorDeBancosDeTeste(ExecutarComandoAdministrativoAsync, LimparPoolDoBanco);
    }

    internal GestorDeBancosDeTeste Gestor => _gestor;

    public async ValueTask InitializeAsync()
    {
        PostgreSqlContainer? conteiner = null;
        try
        {
            conteiner = new PostgreSqlBuilder(Imagem)
                .WithPassword(Guid.NewGuid().ToString("N"))
                .WithCreateParameterModifier(RestringirPublicacaoAoLoopback)
                .Build();

            ExigirDockerLocal(TestcontainersSettings.OS.DockerEndpointAuthConfig?.Endpoint);

            await conteiner.StartAsync(TestContext.Current.CancellationToken);

            ExigirHostLoopback(conteiner.Hostname);
            await ConferirPublicacaoNoLoopbackAsync(conteiner.Id, TestContext.Current.CancellationToken);

            _conexaoAdministrativa = conteiner.GetConnectionString();
            _conteiner = conteiner;
        }
        catch (Exception falhaOriginal)
        {
            // Não depende do xUnit chamar DisposeAsync depois de uma falha na inicialização.
            if (conteiner is not null)
            {
                try
                {
                    await conteiner.DisposeAsync();
                }
                catch (Exception falhaNaLimpeza)
                {
                    throw new AggregateException(
                        "O contêiner PostgreSQL de testes não pôde ser iniciado nem descartado; ele pode continuar existindo até a limpeza do Testcontainers.",
                        falhaOriginal,
                        falhaNaLimpeza);
                }
            }

            ExceptionDispatchInfo.Capture(falhaOriginal).Throw();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        if (_conteiner is not null)
        {
            await _conteiner.DisposeAsync();
        }
    }

    /// <summary>Cria um banco vazio, exclusivo e registrado; opcionalmente aplica as migrations.</summary>
    public async Task<BancoDeTeste> CriarBancoAsync(bool aplicarMigrations = true)
    {
        BancoDeTeste? banco = null;
        await _gestor.CriarAsync(
            async nome =>
            {
                banco = new BancoDeTeste(this, nome);
                if (aplicarMigrations)
                {
                    await banco.AplicarMigrationsAsync();
                }
            },
            TestContext.Current.CancellationToken);

        return banco!;
    }

    internal bool PertenceAExecucao(string nome) => _gestor.PertenceAExecucao(nome);

    internal Task RemoverBancoAsync(string nome) => _gestor.RemoverAsync(nome);

    /// <summary>Connection string de um banco desta execução, no contêiner iniciado.</summary>
    internal string ConnectionStringDo(string nome)
    {
        NomeDeBancoDeTeste.Validar(nome);
        if (!PertenceAExecucao(nome))
        {
            throw new InvalidOperationException($"O banco {nome} não foi criado por esta execução.");
        }

        return new NpgsqlConnectionStringBuilder(ExigirConexaoAdministrativa()) { Database = nome }.ConnectionString;
    }

    /// <summary>Publica todas as portas apenas em 127.0.0.1; falha se a porta do PostgreSQL não estiver configurada.</summary>
    /// <exception cref="InvalidOperationException">Não há configuração de portas a restringir.</exception>
    internal static void RestringirPublicacaoAoLoopback(CreateContainerParameters parametros)
    {
        var portas = parametros.HostConfig?.PortBindings
            ?? throw new InvalidOperationException("O contêiner não tem configuração de publicação de portas; a restrição ao loopback não pôde ser aplicada.");

        if (!portas.TryGetValue(PortaDoPostgres, out var vinculos) || vinculos is null || vinculos.Count == 0)
        {
            throw new InvalidOperationException($"A porta {PortaDoPostgres} não está configurada para publicação; a restrição ao loopback não pôde ser aplicada.");
        }

        foreach (var vinculo in portas.Values.Where(v => v is not null).SelectMany(v => v))
        {
            if (!string.IsNullOrEmpty(vinculo.HostPort) && vinculo.HostPort != "0")
            {
                throw new InvalidOperationException("A porta do host deve ser escolhida dinamicamente, não fixada.");
            }

            vinculo.HostIP = Loopback;
        }
    }

    /// <summary>Aceita apenas Docker local: socket Unix, named pipe ou TCP em endereço de loopback.</summary>
    /// <exception cref="InvalidOperationException">O Docker é remoto; publicar em 127.0.0.1 do host remoto não equivale ao loopback desta máquina.</exception>
    internal static void ExigirDockerLocal(Uri? endpoint)
    {
        if (endpoint is null || EhEndpointLocal(endpoint))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Docker remoto não é suportado pelos testes de integração (endpoint {endpoint.Scheme}://{endpoint.Host}): a porta seria publicada no loopback do host remoto.");
    }

    internal static bool EhEndpointLocal(Uri endpoint) =>
        endpoint.Scheme switch
        {
            "unix" or "npipe" => true,
            "tcp" or "http" or "https" => EhLoopback(endpoint.Host),
            _ => false,
        };

    /// <exception cref="InvalidOperationException">O contêiner não está acessível por loopback.</exception>
    internal static void ExigirHostLoopback(string? hostname)
    {
        if (!EhLoopback(hostname))
        {
            throw new InvalidOperationException($"O contêiner de testes não está acessível por loopback (host {hostname}).");
        }
    }

    /// <summary>Confere, nas portas efetivamente publicadas, que a do PostgreSQL só está em 127.0.0.1.</summary>
    /// <exception cref="InvalidOperationException">A porta não foi publicada ou está publicada fora do loopback.</exception>
    internal static void ExigirPublicacaoSomenteNoLoopback(IDictionary<string, IList<PortBinding>>? portasPublicadas)
    {
        if (portasPublicadas is null
            || !portasPublicadas.TryGetValue(PortaDoPostgres, out var vinculos)
            || vinculos is null
            || vinculos.Count == 0)
        {
            throw new InvalidOperationException($"A porta {PortaDoPostgres} não aparece publicada no contêiner iniciado.");
        }

        foreach (var vinculo in vinculos)
        {
            if (vinculo.HostIP != Loopback)
            {
                throw new InvalidOperationException(
                    $"A porta {PortaDoPostgres} está publicada em '{vinculo.HostIP}', fora de {Loopback}.");
            }
        }
    }

    private static bool EhLoopback(string? host) =>
        host is not null
        && (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host.Trim('[', ']'), out var endereco) && IPAddress.IsLoopback(endereco)));

    private static async Task ConferirPublicacaoNoLoopbackAsync(string idDoConteiner, CancellationToken cancellationToken)
    {
        var configuracao = TestcontainersSettings.OS.DockerEndpointAuthConfig
            ?? throw new InvalidOperationException("A configuração do Docker não está disponível para conferir as portas publicadas.");

        using var cliente = configuracao.GetDockerClientBuilder(Guid.NewGuid()).Build();
        var inspecao = await cliente.Containers.InspectContainerAsync(idDoConteiner, cancellationToken);

        ExigirPublicacaoSomenteNoLoopback(inspecao.NetworkSettings?.Ports);
    }

    private string ExigirConexaoAdministrativa() =>
        _conexaoAdministrativa ?? throw new InvalidOperationException("O contêiner PostgreSQL de testes não foi iniciado.");

    private async Task ExecutarComandoAdministrativoAsync(string sql, CancellationToken cancellationToken)
    {
        await using var conexao = new NpgsqlConnection(ExigirConexaoAdministrativa());
        await conexao.OpenAsync(cancellationToken);
        await using var comando = new NpgsqlCommand(sql, conexao);
        await comando.ExecuteNonQueryAsync(cancellationToken);
    }

    private void LimparPoolDoBanco(string nome)
    {
        if (_conexaoAdministrativa is null)
        {
            return;
        }

        using var conexao = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(_conexaoAdministrativa) { Database = NomeDeBancoDeTeste.Validar(nome) }.ConnectionString);
        NpgsqlConnection.ClearPool(conexao);
    }
}

[CollectionDefinition(Nome)]
public sealed class ColecaoPostgres : ICollectionFixture<ServidorPostgres>
{
    public const string Nome = "PostgreSQL";
}
