using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Waca.Infrastructure.Identidade.Desafios;

/// <summary>
/// Cálculo do segredo gravado em <c>segredo_hash</c>: HMAC-SHA256 com chave secreta independente do banco.
/// </summary>
/// <remarks>
/// <para>
/// A mensagem autenticada é um rótulo de domínio seguido de quatro campos, cada um prefixado pelo seu tamanho
/// (32 bits, big-endian): identificador do desafio (16 bytes), finalidade, e-mail normalizado e código (UTF-8).
/// O prefixo de tamanho torna a codificação não ambígua: nenhuma combinação diferente de campos produz a
/// mesma mensagem.
/// </para>
/// <para>
/// Um código de 8 dígitos tem só 10⁸ valores; por isso nunca se usa SHA-256 simples, que permitiria testar
/// todos os códigos fora do servidor a partir de uma cópia do banco.
/// </para>
/// </remarks>
public static class ProtecaoDoCodigo
{
    /// <summary>Tamanho do HMAC-SHA256, também exigido pelo banco.</summary>
    public const int TamanhoDoSegredo = 32;

    private static readonly byte[] Rotulo = Encoding.ASCII.GetBytes("waca/desafio-de-acesso/v1");
    private static readonly UTF8Encoding Utf8Estrito = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <exception cref="ArgumentException">Chave vazia ou finalidade inexistente.</exception>
    public static byte[] Calcular(ReadOnlySpan<byte> chave, Guid desafioId, FinalidadeDesafio finalidade, string emailNormalizado, string codigo)
    {
        if (chave.IsEmpty)
        {
            throw new ArgumentException("A chave HMAC não pode ser vazia.", nameof(chave));
        }

        if (!Enum.IsDefined(finalidade))
        {
            throw new ArgumentException("Finalidade inexistente.", nameof(finalidade));
        }

        ArgumentNullException.ThrowIfNull(emailNormalizado);
        ArgumentNullException.ThrowIfNull(codigo);

        Span<byte> id = stackalloc byte[16];
        desafioId.TryWriteBytes(id, bigEndian: true, out _);

        using var mensagem = new MemoryStream();
        mensagem.Write(Rotulo);
        EscreverCampo(mensagem, id);
        EscreverCampo(mensagem, Utf8Estrito.GetBytes(finalidade.ToString()));
        EscreverCampo(mensagem, Utf8Estrito.GetBytes(emailNormalizado));
        EscreverCampo(mensagem, Utf8Estrito.GetBytes(codigo));

        return HMACSHA256.HashData(chave, mensagem.ToArray());
    }

    /// <summary>Compara em tempo constante; tamanhos diferentes nunca conferem.</summary>
    public static bool Confere(ReadOnlySpan<byte> esperado, ReadOnlySpan<byte> calculado) =>
        esperado.Length == TamanhoDoSegredo && CryptographicOperations.FixedTimeEquals(esperado, calculado);

    private static void EscreverCampo(Stream destino, ReadOnlySpan<byte> campo)
    {
        Span<byte> tamanho = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(tamanho, campo.Length);
        destino.Write(tamanho);
        destino.Write(campo);
    }
}
