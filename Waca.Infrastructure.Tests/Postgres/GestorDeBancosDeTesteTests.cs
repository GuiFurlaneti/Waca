using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Waca.Infrastructure.Tests.Postgres;

/// <summary>
/// Propriedade, compensação e limpeza dos bancos de teste, com um executor simulado. Não usa Docker nem banco.
/// </summary>
public class GestorDeBancosDeTesteTests
{
    private static readonly Regex ComandoPermitido = new(
        @"^(CREATE DATABASE|DROP DATABASE IF EXISTS) ""waca_teste_[0-9a-f]{32}""( WITH \(FORCE\))?\z");

    [Fact]
    public async Task Banco_so_e_registrado_depois_do_CREATE_confirmado()
    {
        var executor = new ExecutorSimulado();
        var gestor = new GestorDeBancosDeTeste(executor.ExecutarAsync);
        string? registradoDuranteAPreparacao = null;

        var nome = await gestor.CriarAsync(
            n =>
            {
                registradoDuranteAPreparacao = gestor.PertenceAExecucao(n) ? n : null;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(nome, registradoDuranteAPreparacao);
        Assert.Equal([$"CREATE DATABASE \"{nome}\""], executor.Comandos);
        Assert.Equal([nome], gestor.Ativos);
        Assert.True(gestor.PertenceAExecucao(nome));
    }

    [Fact]
    public async Task Falha_no_CREATE_nao_registra_nem_remove_nada()
    {
        var falha = new InvalidOperationException("create falhou");
        var executor = new ExecutorSimulado { FalharEm = c => c.StartsWith("CREATE", StringComparison.Ordinal) ? falha : null };
        var gestor = new GestorDeBancosDeTeste(executor.ExecutarAsync);
        var preparou = false;

        var excecao = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gestor.CriarAsync(_ => { preparou = true; return Task.CompletedTask; }, CancellationToken.None));

        Assert.Same(falha, excecao);
        Assert.False(preparou);
        Assert.Empty(gestor.Ativos);
        Assert.DoesNotContain(executor.Comandos, c => c.StartsWith("DROP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Falha_na_preparacao_remove_o_banco_criado_e_preserva_a_falha_original()
    {
        var executor = new ExecutorSimulado();
        var gestor = new GestorDeBancosDeTeste(executor.ExecutarAsync);
        var falhaDaMigration = new InvalidOperationException("migration falhou");
        string? nome = null;

        var excecao = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gestor.CriarAsync(n => { nome = n; throw falhaDaMigration; }, CancellationToken.None));

        Assert.Same(falhaDaMigration, excecao);
        Assert.Equal([$"CREATE DATABASE \"{nome}\"", $"DROP DATABASE IF EXISTS \"{nome}\" WITH (FORCE)"], executor.Comandos);
        Assert.Empty(gestor.Ativos);
    }

    [Fact]
    public async Task Falha_na_preparacao_e_na_remocao_relata_as_duas_e_nao_da_o_banco_como_removido()
    {
        var falhaNoDrop = new InvalidOperationException("drop falhou");
        var executor = new ExecutorSimulado { FalharEm = c => c.StartsWith("DROP", StringComparison.Ordinal) ? falhaNoDrop : null };
        var gestor = new GestorDeBancosDeTeste(executor.ExecutarAsync);
        var falhaDaMigration = new TimeoutException("migration falhou");
        string? nome = null;

        var excecao = await Assert.ThrowsAsync<AggregateException>(
            () => gestor.CriarAsync(n => { nome = n; throw falhaDaMigration; }, CancellationToken.None));

        Assert.Equal([falhaDaMigration, falhaNoDrop], excecao.InnerExceptions);
        Assert.Contains(nome!, excecao.Message);
        Assert.Equal([nome!], gestor.Ativos);
    }

    [Fact]
    public async Task Remocao_de_banco_nao_criado_pela_execucao_e_recusada_sem_executar_SQL()
    {
        var executor = new ExecutorSimulado();
        var gestor = new GestorDeBancosDeTeste(executor.ExecutarAsync);

        await Assert.ThrowsAsync<InvalidOperationException>(() => gestor.RemoverAsync(NomeDeBancoDeTeste.Gerar()));

        Assert.Empty(executor.Comandos);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("template0")]
    [InlineData("template1")]
    [InlineData("waca_teste_")]
    [InlineData("qualquer")]
    public async Task Remocao_de_nome_administrativo_ou_invalido_e_recusada_sem_executar_SQL(string nome)
    {
        var executor = new ExecutorSimulado();
        var gestor = new GestorDeBancosDeTeste(executor.ExecutarAsync);

        await Assert.ThrowsAsync<ArgumentException>(() => gestor.RemoverAsync(nome));

        Assert.Empty(executor.Comandos);
    }

    [Fact]
    public async Task Remocao_e_idempotente_para_o_banco_da_execucao()
    {
        var executor = new ExecutorSimulado();
        var gestor = new GestorDeBancosDeTeste(executor.ExecutarAsync);
        var nome = await gestor.CriarAsync(_ => Task.CompletedTask, CancellationToken.None);

        await gestor.RemoverAsync(nome);
        await gestor.RemoverAsync(nome);

        Assert.Single(executor.Comandos, c => c.StartsWith("DROP", StringComparison.Ordinal));
        Assert.Empty(gestor.Ativos);
        Assert.True(gestor.PertenceAExecucao(nome));
    }

    [Fact]
    public async Task Remocao_que_falhou_pode_ser_repetida_e_o_banco_continua_ativo_ate_la()
    {
        var tentativas = 0;
        var executor = new ExecutorSimulado
        {
            FalharEm = c => c.StartsWith("DROP", StringComparison.Ordinal) && tentativas++ == 0 ? new IOException("rede") : null,
        };
        var gestor = new GestorDeBancosDeTeste(executor.ExecutarAsync);
        var nome = await gestor.CriarAsync(_ => Task.CompletedTask, CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => gestor.RemoverAsync(nome));
        Assert.Equal([nome], gestor.Ativos);

        await gestor.RemoverAsync(nome);
        Assert.Empty(gestor.Ativos);
    }

    [Fact]
    public async Task Remocao_ignora_o_cancelamento_do_teste_e_limpa_o_pool_antes()
    {
        var executor = new ExecutorSimulado();
        var limpos = new List<string>();
        var gestor = new GestorDeBancosDeTeste(executor.ExecutarAsync, n => limpos.Add(n));
        using var fonte = new CancellationTokenSource();
        var nome = await gestor.CriarAsync(_ => Task.CompletedTask, fonte.Token);
        await fonte.CancelAsync();

        await gestor.RemoverAsync(nome);

        Assert.Equal([nome], limpos);
        Assert.False(executor.Tokens[^1].CanBeCanceled);
    }

    [Fact]
    public async Task Todo_SQL_emitido_e_CREATE_ou_DROP_de_banco_de_teste_validado()
    {
        var executor = new ExecutorSimulado();
        var gestor = new GestorDeBancosDeTeste(executor.ExecutarAsync);

        var nomes = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            nomes.Add(await gestor.CriarAsync(_ => Task.CompletedTask, CancellationToken.None));
        }

        await Assert.ThrowsAnyAsync<Exception>(
            () => gestor.CriarAsync(_ => throw new InvalidOperationException("falha"), CancellationToken.None));
        foreach (var nome in nomes)
        {
            await gestor.RemoverAsync(nome);
        }

        Assert.Equal(12, executor.Comandos.Count);
        Assert.All(executor.Comandos, c => Assert.Matches(ComandoPermitido, c));
    }

    [Fact]
    public void Gestor_nao_oferece_listagem_do_servidor_nem_remocao_por_prefixo()
    {
        var metodos = typeof(GestorDeBancosDeTeste)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && !m.Name.Contains('<'))
            .Select(m => m.Name)
            .Order()
            .ToArray();

        Assert.Equal(["CriarAsync", "PertenceAExecucao", "RemoverAsync"], metodos);
    }

    private sealed class ExecutorSimulado
    {
        public List<string> Comandos { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Func<string, Exception?>? FalharEm { get; init; }

        public Task ExecutarAsync(string sql, CancellationToken cancellationToken)
        {
            Comandos.Add(sql);
            Tokens.Add(cancellationToken);
            return FalharEm?.Invoke(sql) is { } falha ? Task.FromException(falha) : Task.CompletedTask;
        }
    }
}
