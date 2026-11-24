using System.Globalization;

namespace Waca.Domain.ObjetosDeValor;

/// <summary>
/// Quantia monetária em reais (BRL), não negativa e com precisão exata de centavos.
/// </summary>
/// <remarks>
/// É um <c>readonly record struct</c>: imutável, com igualdade por valor gerada pelo compilador
/// e sem alocação. O valor padrão (<c>default</c> ou <c>new ValorMonetario()</c>) representa
/// R$ 0,00, que é um estado válido; por isso a estrutura não pode ser construída em estado inválido.
/// A propriedade <see cref="Valor"/> não tem <c>init</c>, então expressões <c>with</c> não
/// conseguem contornar a validação de <see cref="Criar"/>.
/// </remarks>
public readonly record struct ValorMonetario
{
    /// <summary>R$ 0,00.</summary>
    public static ValorMonetario Zero => default;

    /// <summary>
    /// Maior quantia que <see cref="decimal"/> consegue representar com resolução de centavos
    /// (mantissa de 96 bits com duas casas decimais: R$ 792.281.625.142.643.375.935.439.503,35).
    /// </summary>
    /// <remarks>
    /// Não é um limite de negócio: acima dele, <see cref="decimal"/> não distingue centavos e
    /// somas seriam arredondadas silenciosamente. O limite de negócio/banco de dados ainda será definido.
    /// </remarks>
    public static ValorMonetario MaximoRepresentavel { get; } = new(new decimal(-1, -1, -1, false, 2));

    private ValorMonetario(decimal valor) => Valor = valor;

    /// <summary>Quantia em reais.</summary>
    public decimal Valor { get; }

    /// <summary>
    /// Cria uma quantia validada. Nenhum arredondamento é aplicado à entrada.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A quantia é negativa ou maior que <see cref="MaximoRepresentavel"/>.
    /// </exception>
    /// <exception cref="ArgumentException">A quantia tem fração de centavo.</exception>
    public static ValorMonetario Criar(decimal valor)
    {
        if (valor < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(valor), valor, "O valor monetário não pode ser negativo.");
        }

        if (valor > MaximoRepresentavel.Valor)
        {
            throw new ArgumentOutOfRangeException(
                nameof(valor), valor, "O valor monetário excede a maior quantia representável com precisão de centavos.");
        }

        if (decimal.Round(valor, 2) != valor)
        {
            throw new ArgumentException("O valor monetário não pode conter frações de centavo.", nameof(valor));
        }

        return new ValorMonetario(valor);
    }

    /// <summary>Soma exata de duas quantias.</summary>
    /// <exception cref="OverflowException">
    /// O resultado excede <see cref="MaximoRepresentavel"/>; nenhuma das quantias é alterada.
    /// </exception>
    public ValorMonetario Somar(ValorMonetario outro)
    {
        // Ambas as parcelas são <= MaximoRepresentavel, então a soma nunca estoura decimal.MaxValue.
        // Se a soma exata couber em MaximoRepresentavel, decimal a representa sem arredondamento;
        // se não couber, o resultado (arredondado ou não) é necessariamente maior que o máximo.
        var soma = Valor + outro.Valor;

        if (soma > MaximoRepresentavel.Valor)
        {
            throw new OverflowException("A soma excede a maior quantia representável com precisão de centavos.");
        }

        return new ValorMonetario(soma);
    }

    public static ValorMonetario operator +(ValorMonetario esquerda, ValorMonetario direita) => esquerda.Somar(direita);

    /// <summary>Representação técnica, independente da cultura (ex.: <c>10.50</c>). Não é formatação de interface.</summary>
    public override string ToString() => Valor.ToString(CultureInfo.InvariantCulture);
}
