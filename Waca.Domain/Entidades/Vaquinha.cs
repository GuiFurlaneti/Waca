namespace Waca.Domain.Entidades;

public class Vaquinha
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public decimal Meta { get; set; }
    public TipoVaquinha Tipo { get; set; }
    public bool Ativa { get; set; } = true;
    public DateTime DataCriacao { get; set; } = DateTime.Now;
}

public enum TipoVaquinha
{
    Unica,
    Recorrente
}