using Waca.Domain.ObjetosDeValor;

namespace Waca.Domain.Participacoes;

/// <summary>
/// Dados registrados de uma <see cref="AlteracaoCota"/>, usados apenas para reconstituir uma
/// <see cref="Participacao"/> por meio de <see cref="Participacao.Reconstituir"/>.
/// </summary>
/// <remarks>
/// É um transporte sem regras: qualquer combinação pode ser criada, e toda a validação acontece na
/// reconstituição. Não é contrato HTTP nem esquema de banco de dados. Os campos correspondem um a um
/// às propriedades públicas de <see cref="AlteracaoCota"/>.
/// </remarks>
public sealed record EstadoAlteracaoCota(
    Guid Id,
    ValorMonetario ValorAnterior,
    ValorMonetario NovoValor,
    Guid AlteradoPor,
    DateTimeOffset AlteradoEm);
