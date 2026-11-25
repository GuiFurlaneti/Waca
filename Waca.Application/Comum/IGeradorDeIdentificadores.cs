namespace Waca.Application.Comum;

/// <summary>
/// Gera identificadores para novos registros, fora dos agregados do domínio.
/// </summary>
public interface IGeradorDeIdentificadores
{
    /// <summary>Novo identificador, nunca <see cref="Guid.Empty"/>.</summary>
    Guid NovoId();
}
