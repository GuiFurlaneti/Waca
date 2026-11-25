using System.Text.RegularExpressions;

namespace Waca.Infrastructure.Tests.Postgres;

/// <summary>
/// Geração, validação e comandos SQL dos nomes de bancos de teste.
/// </summary>
/// <remarks>
/// Todo nome usado em <c>CREATE DATABASE</c> ou <c>DROP DATABASE</c> passa por <see cref="Validar"/>:
/// só <c>waca_teste_</c> seguido de 32 dígitos hexadecimais minúsculos é aceito, o que exclui bancos
/// administrativos e qualquer caractere que exija escape.
/// </remarks>
internal static partial class NomeDeBancoDeTeste
{
    public const string Prefixo = "waca_teste_";

    private static readonly string[] Reservados = ["postgres", "template0", "template1"];

    // Equivale a ^waca_teste_[0-9a-f]{32}$, mas com \z: no .NET, $ também aceita uma quebra de linha final.
    [GeneratedRegex(@"^waca_teste_[0-9a-f]{32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Padrao();

    /// <summary>Novo nome, derivado de um Guid novo.</summary>
    public static string Gerar() => Prefixo + Guid.NewGuid().ToString("N");

    public static bool EhValido(string? nome) =>
        nome is not null
        && Padrao().IsMatch(nome)
        && !Reservados.Contains(nome, StringComparer.OrdinalIgnoreCase);

    /// <exception cref="ArgumentException">O nome não segue o padrão dos bancos de teste.</exception>
    public static string Validar(string? nome)
    {
        if (!EhValido(nome))
        {
            throw new ArgumentException("O nome não corresponde a um banco de teste gerado por esta execução.", nameof(nome));
        }

        return nome!;
    }

    /// <summary>Identificador delimitado por aspas duplas, depois da validação.</summary>
    public static string Delimitar(string nome) => "\"" + Validar(nome) + "\"";

    public static string ComandoCriar(string nome) => $"CREATE DATABASE {Delimitar(nome)}";

    public static string ComandoExcluir(string nome) => $"DROP DATABASE IF EXISTS {Delimitar(nome)} WITH (FORCE)";
}
