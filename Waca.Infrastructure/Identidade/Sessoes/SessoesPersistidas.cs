using Microsoft.EntityFrameworkCore;

namespace Waca.Infrastructure.Identidade.Sessoes;

/// <summary>Sessão recém-registrada (ainda sujeita ao commit da transação do chamador).</summary>
public sealed record SessaoCriada(Guid Id, Guid PessoaId, DateTimeOffset ExpiraAbsolutaEm);

/// <summary>
/// Estado lido para validar uma requisição autenticada. Contém dados da conta; <see cref="ToString"/> não os expõe.
/// </summary>
public sealed class EstadoDaSessao(
    Guid sessaoId,
    Guid pessoaId,
    string? email,
    bool emailConfirmado,
    EstadoConta estadoConta,
    string? securityStamp,
    DateTimeOffset expiraAbsolutaEm,
    DateTimeOffset? revogadaEm)
{
    public Guid SessaoId { get; } = sessaoId;

    public Guid PessoaId { get; } = pessoaId;

    public string? Email { get; } = email;

    public bool EmailConfirmado { get; } = emailConfirmado;

    public EstadoConta EstadoConta { get; } = estadoConta;

    public string? SecurityStamp { get; } = securityStamp;

    public DateTimeOffset ExpiraAbsolutaEm { get; } = expiraAbsolutaEm;

    public DateTimeOffset? RevogadaEm { get; } = revogadaEm;

    /// <summary>Sessão utilizável no instante informado: não revogada, dentro do teto e com conta ativa e confirmada.</summary>
    public bool PermiteAcessoEm(DateTimeOffset agora) =>
        RevogadaEm is null
        && agora < ExpiraAbsolutaEm
        && EstadoConta == EstadoConta.Ativa
        && EmailConfirmado;

    /// <summary>Não expõe e-mail nem security stamp.</summary>
    public override string ToString() => $"EstadoDaSessao(sessão {SessaoId})";
}

/// <summary>
/// Registro e consulta de sessões em <c>identidade.sessoes</c>, sempre no contexto (e na transação) do chamador.
/// </summary>
/// <remarks>
/// Não há acesso síncrono ao banco. A criação participa da transação de confirmação do código, junto com o consumo
/// do desafio e a criação da conta; nada é confirmado aqui.
/// </remarks>
public sealed class SessoesPersistidas
{
    private readonly OpcoesDeSessao _opcoes;

    /// <exception cref="InvalidOperationException">Opções inválidas.</exception>
    public SessoesPersistidas(OpcoesDeSessao opcoes)
    {
        _opcoes = opcoes ?? throw new ArgumentNullException(nameof(opcoes));
        _opcoes.Validar();
    }

    /// <summary>Grava uma nova sessão no contexto informado, na transação que estiver aberta nele.</summary>
    public async Task<SessaoCriada> CriarAsync(WacaIdentidadeDbContext contexto, Guid pessoaId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        if (pessoaId == Guid.Empty)
        {
            throw new ArgumentException("A sessão precisa de uma pessoa.", nameof(pessoaId));
        }

        var sessao = new SessaoRegistro
        {
            Id = Guid.CreateVersion7(agora),
            PessoaId = pessoaId,
            CriadaEm = agora,
            ExpiraAbsolutaEm = agora + _opcoes.TetoAbsoluto,
        };

        contexto.Sessoes.Add(sessao);
        await contexto.SaveChangesAsync(cancellationToken);
        return new SessaoCriada(sessao.Id, sessao.PessoaId, sessao.ExpiraAbsolutaEm);
    }

    /// <summary>Lê, em uma única consulta, a sessão e a conta a que ela pertence; nulo se não corresponderem.</summary>
    public Task<EstadoDaSessao?> ObterAsync(WacaIdentidadeDbContext contexto, Guid sessaoId, Guid pessoaId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        return (from sessao in contexto.Sessoes.AsNoTracking()
                join conta in contexto.Users.AsNoTracking() on sessao.PessoaId equals conta.Id
                where sessao.Id == sessaoId && sessao.PessoaId == pessoaId
                select new EstadoDaSessao(
                    sessao.Id,
                    conta.Id,
                    conta.Email,
                    conta.EmailConfirmed,
                    conta.Estado,
                    conta.SecurityStamp,
                    sessao.ExpiraAbsolutaEm,
                    sessao.RevogadaEm))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Revoga a sessão, se ainda não estiver revogada. Devolve se alguma linha foi alterada.</summary>
    public async Task<bool> RevogarAsync(WacaIdentidadeDbContext contexto, Guid sessaoId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var afetadas = await contexto.Sessoes
            .Where(s => s.Id == sessaoId && s.RevogadaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevogadaEm, agora), cancellationToken);
        return afetadas == 1;
    }
}
