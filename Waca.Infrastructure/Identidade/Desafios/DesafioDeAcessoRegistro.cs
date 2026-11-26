namespace Waca.Infrastructure.Identidade.Desafios;

/// <summary>
/// Linha de <c>identidade.desafios_acesso</c>. Só guarda o HMAC do código, nunca o código.
/// </summary>
/// <remarks>
/// Estados: aberto (<see cref="ConsumidoEm"/> e <see cref="InvalidadoEm"/> nulos), consumido ou invalidado.
/// Os dois estados finais são exclusivos e definitivos. A expiração é verificada pelo horário, mesmo antes
/// da limpeza física.
/// </remarks>
internal sealed class DesafioDeAcessoRegistro
{
    public Guid Id { get; set; }

    public FinalidadeDesafio Finalidade { get; set; }

    public string EmailNormalizado { get; set; } = string.Empty;

    public Guid? PessoaId { get; set; }

    public byte[] SegredoHash { get; set; } = [];

    public short VersaoChave { get; set; }

    public DateTimeOffset CriadoEm { get; set; }

    public DateTimeOffset ExpiraEm { get; set; }

    public int Tentativas { get; set; }

    public int MaxTentativas { get; set; }

    public DateTimeOffset? ConsumidoEm { get; set; }

    public DateTimeOffset? InvalidadoEm { get; set; }
}
