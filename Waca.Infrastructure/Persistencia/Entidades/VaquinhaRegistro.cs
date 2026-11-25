namespace Waca.Infrastructure.Persistencia.Entidades;

/// <summary>
/// Linha da tabela <c>vaquinhas</c>. Modelo interno de persistência, sem regras.
/// </summary>
/// <remarks>
/// Nunca sai da Infrastructure: a conversão para o agregado <see cref="Waca.Domain.Vaquinhas.Vaquinha"/>
/// passa por <see cref="Mapeamento.VaquinhaMapeamento"/>, que reaplica as validações do domínio.
/// </remarks>
internal sealed class VaquinhaRegistro
{
    public Guid Id { get; set; }

    public string Nome { get; set; } = null!;

    public Guid OrganizadorId { get; set; }

    public DateTimeOffset CriadaEm { get; set; }
}
