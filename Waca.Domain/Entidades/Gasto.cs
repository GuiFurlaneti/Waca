namespace Waca.Domain.Entidades;

public class Gasto
{
    public int Id { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public DateTime Data { get; set; } = DateTime.Now;

    public int VaquinhaId { get; set; }
    public Vaquinha Vaquinha { get; set; } = null!;
}