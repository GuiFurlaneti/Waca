namespace Waca.Application.Vaquinhas;

/// <summary>
/// Dados de uma vaquinha devolvidos ao seu organizador.
/// </summary>
/// <remarks>
/// Não contém valores financeiros, participantes nem dados de terceiros.
/// </remarks>
public sealed record VaquinhaResumo(Guid Id, string Nome, DateTimeOffset CriadaEm);
