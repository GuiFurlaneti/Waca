using Waca.Application.Comum;
using Waca.Application.Vaquinhas;
using Waca.Domain.Vaquinhas;

namespace Waca.Application.Tests.Vaquinhas;

internal sealed class AtorFixo(Guid? pessoaId) : IAtorAtual
{
    public int Leituras { get; private set; }

    public Guid? PessoaId
    {
        get
        {
            Leituras++;
            return pessoaId;
        }
    }
}

internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public int Leituras { get; private set; }

    public override DateTimeOffset GetUtcNow()
    {
        Leituras++;
        return agora;
    }
}

internal sealed class GeradorFixo(params Guid[] ids) : IGeradorDeIdentificadores
{
    private readonly Queue<Guid> _ids = new(ids);

    public int Chamadas { get; private set; }

    public Guid NovoId()
    {
        Chamadas++;
        return _ids.Dequeue();
    }
}

internal sealed class RepositorioFalso : IRepositorioVaquinhas
{
    public List<Vaquinha> Gravadas { get; } = [];

    public List<Guid> OrganizadoresConsultados { get; } = [];

    public int ChamadasAdicionar { get; private set; }

    public CancellationToken UltimoToken { get; private set; }

    /// <summary>Exceção devolvida como tarefa com falha por <see cref="AdicionarAsync"/>.</summary>
    public Exception? FalhaAoAdicionar { get; set; }

    /// <summary>Exceção lançada antes de devolver a tarefa, como uma implementação síncrona faria.</summary>
    public Exception? FalhaSincronaAoAdicionar { get; set; }

    /// <summary>Se definido, a gravação só termina quando esta tarefa terminar.</summary>
    public TaskCompletionSource? Confirmacao { get; set; }

    public Exception? FalhaAoListar { get; set; }

    /// <summary>Substitui a filtragem correta, para simular um repositório defeituoso.</summary>
    public Func<Guid, IReadOnlyList<Vaquinha>>? RespostaDaListagem { get; set; }

    public Task AdicionarAsync(Vaquinha vaquinha, CancellationToken cancellationToken)
    {
        ChamadasAdicionar++;
        UltimoToken = cancellationToken;
        if (FalhaSincronaAoAdicionar is { } falhaSincrona)
        {
            throw falhaSincrona;
        }

        return GravarAsync(vaquinha);
    }

    public async Task<IReadOnlyList<Vaquinha>> ListarPorOrganizadorAsync(Guid organizadorId, CancellationToken cancellationToken)
    {
        OrganizadoresConsultados.Add(organizadorId);
        UltimoToken = cancellationToken;
        await Task.Yield();
        if (FalhaAoListar is { } falha)
        {
            throw falha;
        }

        return RespostaDaListagem is { } resposta
            ? resposta(organizadorId)
            : Gravadas.Where(v => v.OrganizadorId == organizadorId).ToArray();
    }

    private async Task GravarAsync(Vaquinha vaquinha)
    {
        if (Confirmacao is { } confirmacao)
        {
            await confirmacao.Task;
        }
        else
        {
            await Task.Yield();
        }

        if (FalhaAoAdicionar is { } falha)
        {
            throw falha;
        }

        Gravadas.Add(vaquinha);
    }
}
