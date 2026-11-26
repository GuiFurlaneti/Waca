namespace Waca.Infrastructure.Identidade.Desafios;

/// <summary>
/// Mecanismo interno que leva o código até o destinatário (por exemplo, um provedor de e-mail).
/// </summary>
/// <remarks>
/// <para>
/// É o único ponto que recebe o código em texto puro. É chamado somente depois de o desafio estar confirmado
/// no banco. A implementação não pode registrar o código em log nem gravá-lo em texto puro (inclusive em filas).
/// </para>
/// <para>
/// Retornar sem exceção significa que o mecanismo <b>aceitou</b> a entrega, não que a mensagem chegou.
/// Esta entrega não inclui implementação de produção; o projeto de testes usa uma caixa em memória.
/// </para>
/// </remarks>
public interface IEntregaDeCodigoDeAcesso
{
    Task EntregarAsync(CodigoParaEntrega entrega, CancellationToken cancellationToken);
}

/// <summary>Dados entregues ao <see cref="IEntregaDeCodigoDeAcesso"/>. Não é um DTO público da API.</summary>
public sealed class CodigoParaEntrega
{
    internal CodigoParaEntrega(Guid desafioId, FinalidadeDesafio finalidade, string emailDestino, string codigo, DateTimeOffset expiraEm)
    {
        DesafioId = desafioId;
        Finalidade = finalidade;
        EmailDestino = emailDestino;
        Codigo = codigo;
        ExpiraEm = expiraEm;
    }

    public Guid DesafioId { get; }

    public FinalidadeDesafio Finalidade { get; }

    /// <summary>Endereço exatamente como foi informado na solicitação.</summary>
    public string EmailDestino { get; }

    /// <summary>Código em texto puro. Não registrar, não persistir.</summary>
    public string Codigo { get; }

    public DateTimeOffset ExpiraEm { get; }

    /// <summary>Não expõe o código nem o endereço.</summary>
    public override string ToString() => $"CodigoParaEntrega(desafio {DesafioId}, {Finalidade})";
}
