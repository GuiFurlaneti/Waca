using Waca.Domain.ObjetosDeValor;

namespace Waca.Domain.Participacoes;

/// <summary>
/// Raiz do agregado financeiro individual: a cota de um participante em uma vaquinha e os
/// pagamentos declarados contra ela.
/// </summary>
/// <remarks>
/// <para>
/// Toda alteração financeira passa por esta classe, que garante <c>Confirmado + Pendente ≤ Cota</c>.
/// Cada operação valida todas as condições antes de alterar o estado; uma falha não deixa alteração parcial.
/// </para>
/// <para>
/// As regras valem para esta instância em memória. Duas instâncias da mesma participação carregadas
/// em paralelo não se enxergam; a proteção contra concorrência entre requisições ficará a cargo da persistência.
/// </para>
/// <para>
/// Erros: argumento inválido por si só gera <see cref="ArgumentException"/> (ou derivada);
/// operação incompatível com o estado atual gera <see cref="InvalidOperationException"/>.
/// Identificadores são fornecidos pelo chamador confiável e não podem ser <see cref="Guid.Empty"/>.
/// </para>
/// </remarks>
public sealed class Participacao
{
    private readonly List<Pagamento> _pagamentos = [];

    public Participacao(Guid id, Guid vaquinhaId, Guid participanteId, ValorMonetario cota)
    {
        ExigirId(id, nameof(id));
        ExigirId(vaquinhaId, nameof(vaquinhaId));
        ExigirId(participanteId, nameof(participanteId));
        if (cota == ValorMonetario.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cota), cota, "A cota deve ser maior que zero.");
        }

        Id = id;
        VaquinhaId = vaquinhaId;
        ParticipanteId = participanteId;
        Cota = cota;
        Pagamentos = _pagamentos.AsReadOnly();
    }

    public Guid Id { get; }

    public Guid VaquinhaId { get; }

    public Guid ParticipanteId { get; }

    public ValorMonetario Cota { get; }

    /// <summary>
    /// Todos os pagamentos declarados, inclusive os rejeitados, na ordem de declaração.
    /// </summary>
    /// <remarks>
    /// É uma <see cref="System.Collections.ObjectModel.ReadOnlyCollection{T}"/> sobre a lista interna:
    /// converter para <see cref="IList{T}"/> ou <see cref="ICollection{T}"/> não permite alterá-la.
    /// </remarks>
    public IReadOnlyList<Pagamento> Pagamentos { get; }

    /// <summary>Soma dos pagamentos confirmados (arrecadado).</summary>
    public ValorMonetario Confirmado => SomarPorStatus(StatusPagamento.Confirmado);

    /// <summary>Soma dos pagamentos aguardando confirmação.</summary>
    public ValorMonetario Pendente => SomarPorStatus(StatusPagamento.AguardandoConfirmacao);

    /// <summary>Confirmado + Pendente; nunca excede a cota.</summary>
    public ValorMonetario Comprometido => Confirmado + Pendente;

    /// <summary>Cota − Comprometido: quanto ainda pode ser declarado.</summary>
    public ValorMonetario Disponivel => ValorMonetario.Criar(Cota.Valor - Comprometido.Valor);

    /// <summary>Cota − Confirmado: quanto ainda falta ser recebido.</summary>
    public ValorMonetario Restante => ValorMonetario.Criar(Cota.Valor - Confirmado.Valor);

    /// <summary>
    /// Declara um pagamento, que começa aguardando confirmação e passa a comprometer a cota.
    /// </summary>
    /// <exception cref="ArgumentException">Identificador vazio ou instante não informado.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Valor igual a zero.</exception>
    /// <exception cref="InvalidOperationException">Identificador já usado ou valor acima do disponível.</exception>
    public Pagamento DeclararPagamento(Guid pagamentoId, ValorMonetario valor, Guid declaradoPor, DateTimeOffset declaradoEm)
    {
        var pagamento = new Pagamento(pagamentoId, valor, declaradoPor, declaradoEm);

        if (_pagamentos.Exists(p => p.Id == pagamentoId))
        {
            throw new InvalidOperationException($"Já existe um pagamento com o identificador {pagamentoId} nesta participação.");
        }

        var disponivel = Disponivel;
        if (valor.Valor > disponivel.Valor)
        {
            throw new InvalidOperationException(
                $"O pagamento de {valor} excede o valor disponível de {disponivel} para esta participação.");
        }

        _pagamentos.Add(pagamento);
        return pagamento;
    }

    /// <summary>
    /// Confirma um pagamento aguardando confirmação; o valor passa de Pendente para Confirmado.
    /// </summary>
    /// <exception cref="ArgumentException">Identificador vazio ou instante não informado.</exception>
    /// <exception cref="InvalidOperationException">Pagamento inexistente ou fora do estado aguardando confirmação.</exception>
    public void ConfirmarPagamento(Guid pagamentoId, Guid decididoPor, DateTimeOffset decididoEm) =>
        ObterPagamento(pagamentoId).Confirmar(decididoPor, decididoEm);

    /// <summary>
    /// Rejeita um pagamento aguardando confirmação, liberando a cota que ele ocupava.
    /// O pagamento permanece no histórico.
    /// </summary>
    /// <exception cref="ArgumentException">Identificador vazio, instante não informado ou justificativa vazia.</exception>
    /// <exception cref="InvalidOperationException">Pagamento inexistente ou fora do estado aguardando confirmação.</exception>
    public void RejeitarPagamento(Guid pagamentoId, Guid decididoPor, string justificativa, DateTimeOffset decididoEm) =>
        ObterPagamento(pagamentoId).Rejeitar(decididoPor, justificativa, decididoEm);

    private Pagamento ObterPagamento(Guid pagamentoId)
    {
        ExigirId(pagamentoId, nameof(pagamentoId));

        return _pagamentos.Find(p => p.Id == pagamentoId)
            ?? throw new InvalidOperationException($"O pagamento {pagamentoId} não pertence a esta participação.");
    }

    private ValorMonetario SomarPorStatus(StatusPagamento status)
    {
        var total = ValorMonetario.Zero;
        foreach (var pagamento in _pagamentos)
        {
            if (pagamento.Status == status)
            {
                total += pagamento.Valor;
            }
        }

        return total;
    }

    private static void ExigirId(Guid id, string parametro)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("O identificador não pode ser vazio.", parametro);
        }
    }
}
