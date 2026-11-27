using Waca.Application.Comum;

namespace Waca.Api.Autenticacao;

/// <summary>
/// Identidade da requisição já verificada pelo <see cref="ValidadorDeSessao"/> contra o banco.
/// </summary>
/// <remarks>
/// É publicada como feature do <see cref="HttpContext"/> somente quando todas as verificações passam. Nada que o
/// cliente envie (corpo, query string, cabeçalhos ou claims sem validação) a substitui.
/// </remarks>
public interface ISessaoValidada
{
    Guid PessoaId { get; }

    Guid SessaoId { get; }

    string Email { get; }
}

internal sealed record SessaoValidada(Guid PessoaId, Guid SessaoId, string Email) : ISessaoValidada
{
    /// <summary>Não expõe o e-mail.</summary>
    public override string ToString() => $"SessaoValidada(sessão {SessaoId})";
}

/// <summary>
/// <see cref="IAtorAtual"/> da API: lê exclusivamente a <see cref="ISessaoValidada"/> da requisição, sem acessar o
/// banco. Sem validação publicada (inclusive se o validador não estiver registrado), não há ator.
/// </summary>
public sealed class AtorAtualHttp(IHttpContextAccessor acessor) : IAtorAtual
{
    public Guid? PessoaId => acessor.HttpContext?.Features.Get<ISessaoValidada>()?.PessoaId;
}
