using System.Runtime.ExceptionServices;

namespace Waca.Infrastructure.Tests.Postgres;

/// <summary>
/// Cria e remove os bancos de teste de uma execução, registrando quais foram criados por ela.
/// </summary>
/// <remarks>
/// <para>
/// Um banco só entra no registro depois que <c>CREATE DATABASE</c> foi confirmado, e só um banco registrado
/// pode ser removido. Não há listagem de bancos do servidor nem remoção por prefixo.
/// </para>
/// <para>
/// Se a preparação de um banco recém-criado falhar, ele é removido (compensação) e a falha original é
/// relançada. Se a remoção também falhar, as duas falhas são relatadas juntas e o banco continua registrado
/// como ativo, ou seja, não é dado como descartado.
/// </para>
/// </remarks>
internal sealed class GestorDeBancosDeTeste
{
    private readonly Func<string, CancellationToken, Task> _executarComandoAdministrativo;
    private readonly Action<string>? _antesDeRemover;
    private readonly Lock _trava = new();
    private readonly HashSet<string> _ativos = new(StringComparer.Ordinal);
    private readonly HashSet<string> _removidos = new(StringComparer.Ordinal);

    /// <param name="executarComandoAdministrativo">Executa um comando na conexão administrativa do servidor de testes.</param>
    /// <param name="antesDeRemover">Chamado antes de remover um banco (por exemplo, para limpar o pool de conexões).</param>
    public GestorDeBancosDeTeste(
        Func<string, CancellationToken, Task> executarComandoAdministrativo,
        Action<string>? antesDeRemover = null)
    {
        ArgumentNullException.ThrowIfNull(executarComandoAdministrativo);
        _executarComandoAdministrativo = executarComandoAdministrativo;
        _antesDeRemover = antesDeRemover;
    }

    /// <summary>Bancos criados por esta execução e ainda não removidos.</summary>
    public IReadOnlyCollection<string> Ativos
    {
        get
        {
            lock (_trava)
            {
                return [.. _ativos];
            }
        }
    }

    /// <summary>Indica se o banco foi criado por esta execução (removido ou não).</summary>
    public bool PertenceAExecucao(string nome)
    {
        lock (_trava)
        {
            return _ativos.Contains(nome) || _removidos.Contains(nome);
        }
    }

    /// <summary>Cria um banco com nome novo e o prepara; remove-o se a preparação falhar.</summary>
    /// <returns>O nome do banco criado e preparado.</returns>
    public async Task<string> CriarAsync(Func<string, Task> preparar, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparar);

        var nome = NomeDeBancoDeTeste.Gerar();

        // Se o CREATE falhar, nada é registrado e nada é removido.
        await _executarComandoAdministrativo(NomeDeBancoDeTeste.ComandoCriar(nome), cancellationToken);

        lock (_trava)
        {
            _ativos.Add(nome);
        }

        try
        {
            await preparar(nome);
            return nome;
        }
        catch (Exception falhaOriginal)
        {
            try
            {
                await RemoverAsync(nome);
            }
            catch (Exception falhaNaLimpeza)
            {
                throw new AggregateException(
                    $"A preparação do banco de teste {nome} falhou e a remoção dele também falhou; o banco pode continuar no servidor de testes.",
                    falhaOriginal,
                    falhaNaLimpeza);
            }

            ExceptionDispatchInfo.Capture(falhaOriginal).Throw();
            throw;
        }
    }

    /// <summary>
    /// Remove um banco criado por esta execução. Chamadas repetidas para um banco já removido não fazem nada.
    /// </summary>
    /// <exception cref="ArgumentException">O nome não segue o padrão dos bancos de teste.</exception>
    /// <exception cref="InvalidOperationException">O banco não foi criado por esta execução.</exception>
    public async Task RemoverAsync(string nome)
    {
        NomeDeBancoDeTeste.Validar(nome);

        lock (_trava)
        {
            if (_removidos.Contains(nome))
            {
                return;
            }

            if (!_ativos.Contains(nome))
            {
                throw new InvalidOperationException($"O banco {nome} não foi criado por esta execução e não será removido.");
            }
        }

        _antesDeRemover?.Invoke(nome);

        // A remoção não usa o token do teste: um teste cancelado ainda precisa limpar o próprio banco.
        await _executarComandoAdministrativo(NomeDeBancoDeTeste.ComandoExcluir(nome), CancellationToken.None);

        lock (_trava)
        {
            _ativos.Remove(nome);
            _removidos.Add(nome);
        }
    }
}
