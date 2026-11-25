namespace Waca.Application.Comum;

/// <summary>
/// Gera Guid versão 7 (RFC 9562), ordenável pelo instante de criação.
/// </summary>
/// <remarks>
/// A ordenação favorece índices de banco de dados; o identificador não deve ser usado como fonte
/// de data nem como segredo.
/// </remarks>
public sealed class GeradorDeIdentificadoresV7 : IGeradorDeIdentificadores
{
    public Guid NovoId() => Guid.CreateVersion7();
}
