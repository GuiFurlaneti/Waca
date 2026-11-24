using Waca.Domain.ObjetosDeValor;

namespace Waca.Domain.Participacoes;

/// <summary>
/// Dados registrados de um <see cref="Pagamento"/>, usados apenas para reconstituir uma
/// <see cref="Participacao"/> por meio de <see cref="Participacao.Reconstituir"/>.
/// </summary>
/// <remarks>
/// <para>
/// É um transporte sem regras: qualquer combinação pode ser criada, e toda a validação acontece na
/// reconstituição. Não é contrato HTTP nem esquema de banco de dados.
/// </para>
/// <para>
/// Os campos correspondem um a um às propriedades públicas de <see cref="Pagamento"/>.
/// <see cref="DecididoPor"/>, <see cref="DecididoEm"/> e <see cref="Justificativa"/> são nulos
/// quando o dado não existe no estado informado.
/// </para>
/// </remarks>
public sealed record EstadoPagamento(
    Guid Id,
    ValorMonetario Valor,
    StatusPagamento Status,
    Guid DeclaradoPor,
    DateTimeOffset DeclaradoEm,
    Guid? DecididoPor,
    DateTimeOffset? DecididoEm,
    string? Justificativa);
