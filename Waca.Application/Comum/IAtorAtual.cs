namespace Waca.Application.Comum;

/// <summary>
/// Identidade de quem executa a operação, fornecida por uma fonte confiável.
/// </summary>
/// <remarks>
/// <para>
/// A implementação de produção deverá obter a identidade de uma sessão autenticada; ela nunca vem
/// do corpo da requisição. Esta interface não autentica ninguém: a Application apenas confia no valor
/// entregue pela implementação registrada.
/// </para>
/// <para>
/// <see langword="null"/> significa que não há ator identificado. <see cref="Guid.Empty"/> também é
/// tratado como ausência de identidade pelos casos de uso.
/// </para>
/// </remarks>
public interface IAtorAtual
{
    /// <summary>Identificador da pessoa autenticada, ou <see langword="null"/> se não houver.</summary>
    Guid? PessoaId { get; }
}
