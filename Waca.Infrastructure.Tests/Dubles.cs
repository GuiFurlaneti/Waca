using Waca.Application.Comum;

namespace Waca.Infrastructure.Tests;

internal sealed class AtorFixo(Guid? pessoaId) : IAtorAtual
{
    public Guid? PessoaId { get; } = pessoaId;
}

internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}

internal sealed class GeradorFixo(params Guid[] ids) : IGeradorDeIdentificadores
{
    private readonly Queue<Guid> _ids = new(ids);

    public Guid NovoId() => _ids.Dequeue();
}
