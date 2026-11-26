using System.Globalization;
using System.Security.Cryptography;

namespace Waca.Infrastructure.Identidade.Desafios;

/// <summary>
/// Geração de códigos numéricos com o gerador criptográfico do .NET (<see cref="RandomNumberGenerator"/>).
/// </summary>
/// <remarks>
/// Cada valor de 0 a 10ⁿ − 1 tem a mesma probabilidade; zeros à esquerda são preservados
/// (por exemplo, <c>00042137</c>). Nenhuma outra fonte (relógio, identificadores, <see cref="Random"/>) é usada.
/// </remarks>
public static class GeradorDeCodigo
{
    /// <exception cref="ArgumentOutOfRangeException">Quantidade de dígitos fora do intervalo aceito.</exception>
    public static string Gerar(int digitos)
    {
        ValidarDigitos(digitos);
        return Formatar(RandomNumberGenerator.GetInt32(0, LimiteExclusivo(digitos)), digitos);
    }

    /// <summary>Formata o valor com exatamente <paramref name="digitos"/> dígitos, preenchendo com zeros à esquerda.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Valor ou quantidade de dígitos fora do intervalo.</exception>
    public static string Formatar(int valor, int digitos)
    {
        ValidarDigitos(digitos);
        ArgumentOutOfRangeException.ThrowIfNegative(valor);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(valor, LimiteExclusivo(digitos));

        return valor.ToString("D" + digitos.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>Exatamente <paramref name="digitos"/> dígitos ASCII, sem sinais, espaços ou outros caracteres.</summary>
    public static bool TemFormatoValido(string? codigo, int digitos) =>
        codigo is not null && codigo.Length == digitos && codigo.All(char.IsAsciiDigit);

    private static int LimiteExclusivo(int digitos)
    {
        var limite = 1;
        for (var i = 0; i < digitos; i++)
        {
            limite *= 10;
        }

        return limite;
    }

    private static void ValidarDigitos(int digitos)
    {
        if (digitos is < OpcoesDeDesafios.MinimoDeDigitos or > OpcoesDeDesafios.MaximoDeDigitos)
        {
            throw new ArgumentOutOfRangeException(nameof(digitos), digitos,
                $"A quantidade de dígitos deve estar entre {OpcoesDeDesafios.MinimoDeDigitos} e {OpcoesDeDesafios.MaximoDeDigitos}.");
        }
    }
}
