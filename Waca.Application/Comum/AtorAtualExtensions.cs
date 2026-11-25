namespace Waca.Application.Comum;

internal static class AtorAtualExtensions
{
    /// <summary>
    /// Identificador do ator, ou <see cref="AtorNaoIdentificadoException"/> se ausente ou vazio.
    /// </summary>
    public static Guid ExigirPessoaId(this IAtorAtual atorAtual)
    {
        var pessoaId = atorAtual.PessoaId;
        if (pessoaId is not { } id || id == Guid.Empty)
        {
            throw new AtorNaoIdentificadoException();
        }

        return id;
    }
}
