namespace Waca.Infrastructure.Identidade.Sessoes;

/// <summary>
/// Linha de <c>identidade.sessoes</c>: uma sessão autenticada emitida para uma conta.
/// </summary>
/// <remarks>
/// O identificador é o <c>sid</c> gravado no cookie. A sessão vale até <see cref="ExpiraAbsolutaEm"/>, o teto
/// absoluto que nenhuma renovação ultrapassa, ou até ser revogada. Excluir a conta exclui suas sessões.
/// </remarks>
internal sealed class SessaoRegistro
{
    public Guid Id { get; set; }

    public Guid PessoaId { get; set; }

    public DateTimeOffset CriadaEm { get; set; }

    public DateTimeOffset ExpiraAbsolutaEm { get; set; }

    public DateTimeOffset? RevogadaEm { get; set; }
}
