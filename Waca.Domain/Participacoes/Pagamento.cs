using Waca.Domain.ObjetosDeValor;

namespace Waca.Domain.Participacoes;

/// <summary>
/// Pagamento declarado contra a cota de uma <see cref="Participacao"/>.
/// </summary>
/// <remarks>
/// <para>
/// O construtor e as transições são <c>internal</c>: código de fora do assembly <c>Waca.Domain</c>
/// (Application, API, testes) só cria e altera pagamentos por meio de <see cref="Participacao"/>.
/// </para>
/// <para>
/// <c>internal</c> restringe o acesso ao assembly, não à classe <see cref="Participacao"/>: outro tipo do
/// próprio <c>Waca.Domain</c> poderia chamar esses membros. Por isso cada transição valida as próprias
/// pré-condições, e a regra de limite da cota permanece em <see cref="Participacao"/>.
/// </para>
/// <para>
/// Valor e dados da declaração são imutáveis. Dados da decisão são gravados uma única vez.
/// Instantes são armazenados em UTC (offset zero), preservando o mesmo momento recebido.
/// </para>
/// </remarks>
public sealed class Pagamento
{
    internal Pagamento(Guid pagamentoId, ValorMonetario valor, Guid declaradoPor, DateTimeOffset declaradoEm)
    {
        ExigirId(pagamentoId, nameof(pagamentoId));
        if (valor == ValorMonetario.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(valor), valor, "O valor do pagamento deve ser maior que zero.");
        }

        ExigirId(declaradoPor, nameof(declaradoPor));
        ExigirInstante(declaradoEm, nameof(declaradoEm));

        Id = pagamentoId;
        Valor = valor;
        DeclaradoPor = declaradoPor;
        DeclaradoEm = declaradoEm.ToUniversalTime();
        Status = StatusPagamento.AguardandoConfirmacao;
    }

    public Guid Id { get; }

    public ValorMonetario Valor { get; }

    public StatusPagamento Status { get; private set; }

    public Guid DeclaradoPor { get; }

    public DateTimeOffset DeclaradoEm { get; }

    /// <summary>Quem confirmou ou rejeitou; nulo enquanto aguarda confirmação.</summary>
    public Guid? DecididoPor { get; private set; }

    /// <summary>Quando foi confirmado ou rejeitado (UTC); nulo enquanto aguarda confirmação.</summary>
    public DateTimeOffset? DecididoEm { get; private set; }

    /// <summary>Justificativa da rejeição, sem espaços nas extremidades; nula nos demais estados.</summary>
    public string? Justificativa { get; private set; }

    internal void Confirmar(Guid decididoPor, DateTimeOffset decididoEm)
    {
        ExigirId(decididoPor, nameof(decididoPor));
        ExigirInstante(decididoEm, nameof(decididoEm));
        ExigirAguardandoConfirmacao("confirmado");

        Status = StatusPagamento.Confirmado;
        DecididoPor = decididoPor;
        DecididoEm = decididoEm.ToUniversalTime();
    }

    internal void Rejeitar(Guid decididoPor, string justificativa, DateTimeOffset decididoEm)
    {
        ExigirId(decididoPor, nameof(decididoPor));
        ExigirInstante(decididoEm, nameof(decididoEm));
        if (string.IsNullOrWhiteSpace(justificativa))
        {
            throw new ArgumentException("A justificativa da rejeição é obrigatória.", nameof(justificativa));
        }

        ExigirAguardandoConfirmacao("rejeitado");

        Status = StatusPagamento.Rejeitado;
        DecididoPor = decididoPor;
        DecididoEm = decididoEm.ToUniversalTime();
        Justificativa = justificativa.Trim();
    }

    private void ExigirAguardandoConfirmacao(string acao)
    {
        if (Status != StatusPagamento.AguardandoConfirmacao)
        {
            throw new InvalidOperationException(
                $"O pagamento {Id} está {Status} e não pode ser {acao}; apenas pagamentos aguardando confirmação podem.");
        }
    }

    private static void ExigirId(Guid id, string parametro)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("O identificador não pode ser vazio.", parametro);
        }
    }

    private static void ExigirInstante(DateTimeOffset instante, string parametro)
    {
        if (instante == default)
        {
            throw new ArgumentException("O instante não foi informado.", parametro);
        }
    }
}
