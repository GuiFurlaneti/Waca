using System.Security.Cryptography;

namespace Waca.Infrastructure.Identidade.Desafios;

/// <summary>
/// Configuração das chaves HMAC dos desafios, como chega de uma fonte segura (variável de ambiente ou cofre).
/// </summary>
/// <remarks>
/// Não há valor padrão nem chave de desenvolvimento: sem configuração válida, o <see cref="ChaveiroDeDesafios"/>
/// não é criado e os serviços de desafio não funcionam.
/// </remarks>
public sealed class OpcoesDaChaveDeDesafios
{
    /// <summary>Versão usada para emitir novos desafios.</summary>
    public short VersaoAtual { get; set; }

    /// <summary>Chaves em Base64 por versão. Versões antigas continuam aceitas na verificação.</summary>
    public Dictionary<short, string> Chaves { get; set; } = [];

    /// <summary>Nunca expõe o material das chaves.</summary>
    public override string ToString() => $"OpcoesDaChaveDeDesafios(versão atual {VersaoAtual}, {Chaves.Count} chave(s))";
}

/// <summary>
/// Chaves secretas, independentes do banco, usadas para calcular o HMAC-SHA256 dos códigos.
/// </summary>
/// <remarks>
/// <para>
/// Cada chave precisa ter de <see cref="TamanhoMinimoDaChave"/> a <see cref="TamanhoMaximoDaChave"/> bytes e não
/// pode ser trivial (todos os bytes iguais) nem repetida entre versões. A versão gravada em cada desafio
/// permite trocar a chave sem invalidar desafios em andamento; versão desconhecida é recusada.
/// </para>
/// <para>As chaves nunca são gravadas no banco nem expostas por <see cref="ToString"/>.</para>
/// </remarks>
public sealed class ChaveiroDeDesafios
{
    public const int TamanhoMinimoDaChave = 32;
    public const int TamanhoMaximoDaChave = 64;

    private readonly Dictionary<short, byte[]> _chaves;

    /// <exception cref="ArgumentException">Chaves ausentes, fracas, repetidas ou versão atual inexistente.</exception>
    public ChaveiroDeDesafios(short versaoAtual, IReadOnlyDictionary<short, byte[]> chaves)
    {
        ArgumentNullException.ThrowIfNull(chaves);

        if (versaoAtual <= 0)
        {
            throw new ArgumentException("A versão atual da chave deve ser positiva.", nameof(versaoAtual));
        }

        if (chaves.Count == 0)
        {
            throw new ArgumentException("Nenhuma chave de desafio foi configurada.", nameof(chaves));
        }

        _chaves = [];
        foreach (var (versao, chave) in chaves)
        {
            if (versao <= 0)
            {
                throw new ArgumentException("As versões de chave devem ser positivas.", nameof(chaves));
            }

            ValidarMaterial(chave, versao);
            if (_chaves.Values.Any(existente => CryptographicOperations.FixedTimeEquals(existente, chave)))
            {
                throw new ArgumentException("A mesma chave não pode ser usada em duas versões.", nameof(chaves));
            }

            _chaves[versao] = chave.ToArray();
        }

        if (!_chaves.ContainsKey(versaoAtual))
        {
            throw new ArgumentException($"Não há chave configurada para a versão atual {versaoAtual}.", nameof(versaoAtual));
        }

        VersaoAtual = versaoAtual;
    }

    public short VersaoAtual { get; }

    /// <summary>Cria o chaveiro a partir da configuração, exigindo Base64 válido.</summary>
    /// <exception cref="ArgumentException">Configuração ausente ou inválida.</exception>
    public static ChaveiroDeDesafios DeConfiguracao(OpcoesDaChaveDeDesafios? opcoes)
    {
        if (opcoes is null)
        {
            throw new ArgumentException("A configuração das chaves de desafio é obrigatória.", nameof(opcoes));
        }

        var chaves = new Dictionary<short, byte[]>();
        foreach (var (versao, base64) in opcoes.Chaves ?? [])
        {
            if (string.IsNullOrWhiteSpace(base64))
            {
                throw new ArgumentException($"A chave da versão {versao} está vazia.", nameof(opcoes));
            }

            var destino = new byte[base64.Length];
            if (!Convert.TryFromBase64String(base64, destino, out var escritos))
            {
                throw new ArgumentException($"A chave da versão {versao} não está em Base64 válido.", nameof(opcoes));
            }

            chaves[versao] = destino[..escritos];
            CryptographicOperations.ZeroMemory(destino);
        }

        return new ChaveiroDeDesafios(opcoes.VersaoAtual, chaves);
    }

    /// <summary>HMAC do código com a chave da versão informada, ou <see langword="null"/> se a versão não existir.</summary>
    internal byte[]? CalcularSegredo(short versao, Guid desafioId, FinalidadeDesafio finalidade, string emailNormalizado, string codigo) =>
        _chaves.TryGetValue(versao, out var chave)
            ? ProtecaoDoCodigo.Calcular(chave, desafioId, finalidade, emailNormalizado, codigo)
            : null;

    /// <summary>Nunca expõe o material das chaves.</summary>
    public override string ToString() => $"ChaveiroDeDesafios(versão atual {VersaoAtual}, {_chaves.Count} chave(s))";

    private static void ValidarMaterial(byte[]? chave, short versao)
    {
        if (chave is null || chave.Length < TamanhoMinimoDaChave || chave.Length > TamanhoMaximoDaChave)
        {
            throw new ArgumentException(
                $"A chave da versão {versao} deve ter de {TamanhoMinimoDaChave} a {TamanhoMaximoDaChave} bytes.");
        }

        if (chave.AsSpan().IndexOfAnyExcept(chave[0]) < 0)
        {
            throw new ArgumentException($"A chave da versão {versao} é trivial (todos os bytes iguais).");
        }
    }
}
