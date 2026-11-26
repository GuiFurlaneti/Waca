using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Identidade.Desafios;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;

namespace Waca.Infrastructure.Tests.Identidade.Desafios;

/// <summary>Relógio controlado pelo teste, sempre em UTC.</summary>
internal sealed class RelogioDeTeste(DateTimeOffset inicio) : TimeProvider
{
    private readonly Lock _trava = new();
    private DateTimeOffset _agora = inicio;

    public static DateTimeOffset Inicio { get; } = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

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

/// <summary>Dublê de entrega: guarda os códigos em memória. Existe só no projeto de testes.</summary>
internal sealed class CaixaDeEntregaEmMemoria : IEntregaDeCodigoDeAcesso
{
    private readonly ConcurrentQueue<CodigoParaEntrega> _entregas = new();

    /// <summary>Executado antes de registrar a entrega; uma exceção aqui simula falha do mecanismo.</summary>
    public Func<CodigoParaEntrega, Task>? AoEntregar { get; set; }

    /// <summary>Entregas tentadas, inclusive as que falharam.</summary>
    public ConcurrentQueue<CodigoParaEntrega> Tentativas { get; } = new();

    public IReadOnlyList<CodigoParaEntrega> Entregas => [.. _entregas];

    public async Task EntregarAsync(CodigoParaEntrega entrega, CancellationToken cancellationToken)
    {
        Tentativas.Enqueue(entrega);
        if (AoEntregar is not null)
        {
            await AoEntregar(entrega);
        }

        _entregas.Enqueue(entrega);
    }

    public string CodigoDo(Guid? desafioId) =>
        Tentativas.Single(e => e.DesafioId == desafioId).Codigo;
}

/// <summary>Registro de log em memória, para provar que códigos e endereços não são registrados.</summary>
internal sealed class LogEmMemoria
{
    public ConcurrentQueue<string> Linhas { get; } = new();

    public ILogger<T> Para<T>() => new Registrador<T>(this);

    private sealed class Registrador<T>(LogEmMemoria destino) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var valores = state is IEnumerable<KeyValuePair<string, object?>> pares
                ? string.Join(";", pares.Select(p => $"{p.Key}={p.Value}"))
                : state?.ToString();
            destino.Linhas.Enqueue($"{logLevel}|{formatter(state, exception)}|{valores}|{exception}");
        }
    }
}

/// <summary>Fábrica de contextos sobre um banco descartável da fixture; cada chamada abre um contexto novo.</summary>
internal sealed class FabricaDeContextosDeTeste(BancoDeTeste banco) : IDbContextFactory<WacaIdentidadeDbContext>
{
    public WacaIdentidadeDbContext CreateDbContext() => banco.CriarContextoDeIdentidade();
}

/// <summary>Fábrica que nunca abre conexão: só para construir serviços em testes sem banco.</summary>
internal sealed class FabricaSemBanco : IDbContextFactory<WacaIdentidadeDbContext>
{
    public WacaIdentidadeDbContext CreateDbContext() =>
        throw new InvalidOperationException("Este teste não deveria acessar banco.");
}

/// <summary>Serviços de desafio ligados a um banco descartável, com chave aleatória gerada para o teste.</summary>
internal sealed class AmbienteDeDesafios
{
    public AmbienteDeDesafios(BancoDeTeste banco, OpcoesDeDesafios? opcoes = null)
    {
        Banco = banco;
        Opcoes = opcoes ?? new OpcoesDeDesafios();
        Chave = NovaChave();
        Chaveiro = new ChaveiroDeDesafios(1, new Dictionary<short, byte[]> { [1] = Chave });
        Fabrica = new FabricaDeContextosDeTeste(banco);
        Emissor = new EmissorDeDesafios(Fabrica, Chaveiro, Opcoes, Normalizador, Caixa, Relogio, Log.Para<EmissorDeDesafios>());
        Verificador = CriarVerificador(Chaveiro);
        Limpeza = new LimpezaDeDesafios(Fabrica, Opcoes, Relogio);
    }

    public static UpperInvariantLookupNormalizer Normalizador { get; } = new();

    public BancoDeTeste Banco { get; }

    public OpcoesDeDesafios Opcoes { get; }

    public byte[] Chave { get; }

    public ChaveiroDeDesafios Chaveiro { get; }

    public RelogioDeTeste Relogio { get; } = new(RelogioDeTeste.Inicio);

    public CaixaDeEntregaEmMemoria Caixa { get; } = new();

    public LogEmMemoria Log { get; } = new();

    public FabricaDeContextosDeTeste Fabrica { get; }

    public EmissorDeDesafios Emissor { get; }

    public VerificadorDeDesafios Verificador { get; }

    public LimpezaDeDesafios Limpeza { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static byte[] NovaChave() => RandomNumberGenerator.GetBytes(32);

    public VerificadorDeDesafios CriarVerificador(ChaveiroDeDesafios chaveiro) =>
        new(Fabrica, chaveiro, Opcoes, Normalizador, Relogio, Log.Para<VerificadorDeDesafios>());

    /// <summary>Emite e devolve o código capturado pela caixa (vazio quando nada foi entregue).</summary>
    public async Task<(ResultadoDaEmissao Resultado, string Codigo)> EmitirAsync(
        string email,
        FinalidadeDesafio finalidade = FinalidadeDesafio.Acesso,
        Guid? pessoaId = null)
    {
        var resultado = await Emissor.EmitirAsync(new SolicitacaoDeEmissao(finalidade, email, pessoaId), Ct);
        var codigo = resultado.DesafioId is { } id && Caixa.Tentativas.Any(e => e.DesafioId == id)
            ? Caixa.CodigoDo(id)
            : string.Empty;
        return (resultado, codigo);
    }

    public Task<ResultadoDaVerificacao> VerificarAsync(
        Guid? desafioId,
        string email,
        string codigo,
        FinalidadeDesafio finalidade = FinalidadeDesafio.Acesso) =>
        Verificador.VerificarEConsumirEmTransacaoPropriaAsync(new PedidoDeVerificacao(desafioId!.Value, finalidade, email, codigo), Ct);

    /// <summary>Estado de um desafio lido por uma conexão nova, sem passar pelo EF.</summary>
    public async Task<EstadoDoDesafio?> LerAsync(Guid? desafioId)
    {
        await using var conexao = new NpgsqlConnection(Banco.ConnectionString);
        await conexao.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand(
            """
            SELECT finalidade, email_normalizado, pessoa_id, segredo_hash, versao_chave, criado_em, expira_em,
                   tentativas, max_tentativas, consumido_em, invalidado_em
            FROM identidade.desafios_acesso WHERE id = @id
            """,
            conexao);
        comando.Parameters.AddWithValue("id", desafioId!.Value);
        await using var leitor = await comando.ExecuteReaderAsync(Ct);
        if (!await leitor.ReadAsync(Ct))
        {
            return null;
        }

        return new EstadoDoDesafio(
            leitor.GetString(0),
            leitor.GetString(1),
            leitor.IsDBNull(2) ? null : leitor.GetGuid(2),
            (byte[])leitor.GetValue(3),
            leitor.GetInt16(4),
            leitor.GetFieldValue<DateTimeOffset>(5),
            leitor.GetFieldValue<DateTimeOffset>(6),
            leitor.GetInt32(7),
            leitor.GetInt32(8),
            leitor.IsDBNull(9) ? null : leitor.GetFieldValue<DateTimeOffset>(9),
            leitor.IsDBNull(10) ? null : leitor.GetFieldValue<DateTimeOffset>(10));
    }

    public Task<long> ContarDesafiosAsync(string condicao = "true") =>
        Banco.ContarAsync($"SELECT count(*) FROM identidade.desafios_acesso WHERE {condicao}");
}

internal sealed record EstadoDoDesafio(
    string Finalidade,
    string EmailNormalizado,
    Guid? PessoaId,
    byte[] SegredoHash,
    short VersaoChave,
    DateTimeOffset CriadoEm,
    DateTimeOffset ExpiraEm,
    int Tentativas,
    int MaxTentativas,
    DateTimeOffset? ConsumidoEm,
    DateTimeOffset? InvalidadoEm)
{
    public bool Aberto => ConsumidoEm is null && InvalidadoEm is null;
}
