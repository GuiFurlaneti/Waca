namespace Waca.Domain.Participacoes;

/// <summary>
/// Estados de um <see cref="Pagamento"/> implementados nesta versão do domínio.
/// </summary>
/// <remarks>
/// Os valores começam em 1 para que <c>default(StatusPagamento)</c> não represente um estado válido.
/// A anulação de pagamentos confirmados ainda não foi implementada.
/// </remarks>
public enum StatusPagamento
{
    /// <summary>Declarado e ainda não decidido; compromete a cota.</summary>
    AguardandoConfirmacao = 1,

    /// <summary>Recebimento confirmado; conta como arrecadado e compromete a cota.</summary>
    Confirmado = 2,

    /// <summary>Recusado com justificativa; permanece no histórico e não ocupa a cota.</summary>
    Rejeitado = 3,
}
