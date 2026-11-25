namespace Waca.Application.Comum;

/// <summary>
/// O repositório não concluiu a operação; nada pode ser dado como gravado ou consultado.
/// </summary>
/// <remarks>
/// A causa original fica em <see cref="Exception.InnerException"/>. Cancelamentos
/// (<see cref="OperationCanceledException"/>) não são convertidos nesta exceção.
/// </remarks>
public sealed class FalhaNoRepositorioException : Exception
{
    public FalhaNoRepositorioException(string mensagem, Exception? causa)
        : base(mensagem, causa)
    {
    }
}
