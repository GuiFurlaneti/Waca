namespace Waca.Domain.Entidades;

public class Aporte
{
    public int Id { get; set; }
    public decimal Valor { get; set; }
    public DateTime Data { get; set; } = DateTime.Now;

    public int MembroId { get; set; }
    public Membro Membro { get; set; } = null!;
}