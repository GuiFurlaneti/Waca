namespace Waca.Domain.Entidades;

public class Membro
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public decimal CotaEsperada { get; set; }

    public int VaquinhaId { get; set; }
    public Vaquinha Vaquinha { get; set; } = null!;
}