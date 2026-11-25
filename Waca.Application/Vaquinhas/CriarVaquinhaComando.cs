namespace Waca.Application.Vaquinhas;

/// <summary>
/// Dados informados pelo cliente para criar uma vaquinha.
/// </summary>
/// <remarks>
/// Contém apenas o nome. Organizador, identificador e instante de criação não são aceitos do cliente:
/// vêm do ator atual, do gerador de identificadores e do relógio.
/// </remarks>
public sealed record CriarVaquinhaComando(string Nome);
