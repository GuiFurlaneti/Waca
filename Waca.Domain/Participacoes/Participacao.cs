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
    private readonly List<AlteracaoCota> _alteracoesCota = [];

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
        AlteracoesCota = _alteracoesCota.AsReadOnly();
    }

    public Guid Id { get; }

    public Guid VaquinhaId { get; }

    public Guid ParticipanteId { get; }

    /// <summary>Cota atual; só muda por <see cref="AlterarCota"/>.</summary>
    public ValorMonetario Cota { get; private set; }

    /// <summary>
    /// Todos os pagamentos declarados, inclusive os rejeitados, na ordem de declaração.
    /// </summary>
    /// <remarks>
    /// É uma <see cref="System.Collections.ObjectModel.ReadOnlyCollection{T}"/> sobre a lista interna:
    /// converter para <see cref="IList{T}"/> ou <see cref="ICollection{T}"/> não permite alterá-la.
    /// </remarks>
    public IReadOnlyList<Pagamento> Pagamentos { get; }

    /// <summary>
    /// Alterações efetivas da cota, na ordem em que foram registradas.
    /// </summary>
    /// <remarks>
    /// É uma <see cref="System.Collections.ObjectModel.ReadOnlyCollection{T}"/> sobre a lista interna:
    /// converter para <see cref="IList{T}"/> ou <see cref="ICollection{T}"/> não permite alterá-la.
    /// A cota inicial, definida na criação, não gera registro.
    /// </remarks>
    public IReadOnlyList<AlteracaoCota> AlteracoesCota { get; }

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

    /// <summary>
    /// Altera a cota, registrando a mudança no histórico. Pagamentos não são modificados.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Todas as condições são verificadas antes de qualquer alteração: argumentos, identificador
    /// não utilizado, e nova cota maior ou igual ao Comprometido. Só então o registro é criado e,
    /// em seguida, o histórico e a cota são atualizados como uma única alteração lógica.
    /// </para>
    /// <para>
    /// Se a nova cota for igual à atual (depois de todas as validações), nada muda: a cota permanece,
    /// nenhum registro é criado, o identificador informado não é consumido e o método retorna <c>false</c>.
    /// </para>
    /// <para>
    /// Receber <paramref name="alteradoPor"/> não significa que o ator está autorizado; isso cabe à Application.
    /// </para>
    /// </remarks>
    /// <returns><c>true</c> se a cota mudou e o histórico recebeu um registro; <c>false</c> se o valor já era o atual.</returns>
    /// <exception cref="ArgumentException">Identificador vazio ou instante não informado.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Nova cota igual a zero.</exception>
    /// <exception cref="InvalidOperationException">Identificador já usado ou nova cota abaixo do Comprometido.</exception>
    public bool AlterarCota(Guid alteracaoId, ValorMonetario novaCota, Guid alteradoPor, DateTimeOffset alteradoEm)
    {
        ExigirId(alteracaoId, nameof(alteracaoId));
        if (novaCota == ValorMonetario.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(novaCota), novaCota, "A cota deve ser maior que zero.");
        }

        ExigirId(alteradoPor, nameof(alteradoPor));
        if (alteradoEm == default)
        {
            throw new ArgumentException("O instante não foi informado.", nameof(alteradoEm));
        }

        if (_alteracoesCota.Exists(a => a.Id == alteracaoId))
        {
            throw new InvalidOperationException($"Já existe uma alteração de cota com o identificador {alteracaoId} nesta participação.");
        }

        if (novaCota == Cota)
        {
            return false;
        }

        var comprometido = Comprometido;
        if (novaCota.Valor < comprometido.Valor)
        {
            throw new InvalidOperationException(
                $"A nova cota de {novaCota} é menor que o valor comprometido de {comprometido} nesta participação.");
        }

        var alteracao = new AlteracaoCota(alteracaoId, Cota, novaCota, alteradoPor, alteradoEm);

        _alteracoesCota.Add(alteracao);
        Cota = novaCota;
        return true;
    }

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
