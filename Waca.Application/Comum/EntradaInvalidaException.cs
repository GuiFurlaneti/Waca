namespace Waca.Application.Comum;

/// <summary>
/// Um dado informado pelo cliente foi rejeitado pelas regras do domínio.
/// </summary>
/// <remarks>
/// A causa original fica em <see cref="Exception.InnerException"/>.
/// </remarks>
public sealed class EntradaInvalidaException : Exception
{
    public EntradaInvalidaException(string campo, string mensagem, Exception causa)
        : base(mensagem, causa)
    {
        Campo = campo;
    }

    /// <summary>Nome do campo da entrada que foi rejeitado.</summary>
    public string Campo { get; }
}
