namespace Waca.Application.Comum;

/// <summary>
/// A operação exige um ator identificado, e nenhum foi fornecido.
/// </summary>
public sealed class AtorNaoIdentificadoException : Exception
{
    public AtorNaoIdentificadoException()
        : base("A operação exige um ator identificado.")
    {
    }
}
