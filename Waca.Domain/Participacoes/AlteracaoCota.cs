using Waca.Domain.ObjetosDeValor;

namespace Waca.Domain.Participacoes;

/// <summary>
/// Registro imutável de uma alteração efetiva da cota de uma <see cref="Participacao"/>.
/// </summary>
/// <remarks>
/// <para>
/// Só é criado por <see cref="Participacao.AlterarCota"/>: o construtor é <c>internal</c>.
/// <c>internal</c> restringe o acesso ao assembly <c>Waca.Domain</c>, não à <see cref="Participacao"/>;
/// por isso o próprio registro valida que representa uma alteração consistente
/// (identificadores preenchidos, instante informado, valores positivos e diferentes entre si).
/// </para>
/// <para>
/// O instante é armazenado em UTC (offset zero), preservando o mesmo momento recebido.
/// Nenhuma regra de ordem entre instantes de registros diferentes é aplicada.
/// </para>
/// </remarks>
public sealed class AlteracaoCota
{
    internal AlteracaoCota(
        Guid alteracaoId,
        ValorMonetario valorAnterior,
        ValorMonetario novoValor,
        Guid alteradoPor,
        DateTimeOffset alteradoEm)
    {
        if (alteracaoId == Guid.Empty)
        {
            throw new ArgumentException("O identificador não pode ser vazio.", nameof(alteracaoId));
        }

        if (valorAnterior == ValorMonetario.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(valorAnterior), valorAnterior, "A cota anterior deve ser maior que zero.");
        }

        if (novoValor == ValorMonetario.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(novoValor), novoValor, "A nova cota deve ser maior que zero.");
        }

        if (novoValor == valorAnterior)
        {
            throw new ArgumentException("Uma alteração de cota precisa mudar o valor.", nameof(novoValor));
        }

        if (alteradoPor == Guid.Empty)
        {
            throw new ArgumentException("O identificador não pode ser vazio.", nameof(alteradoPor));
        }

        if (alteradoEm == default)
        {
            throw new ArgumentException("O instante não foi informado.", nameof(alteradoEm));
        }

        Id = alteracaoId;
        ValorAnterior = valorAnterior;
        NovoValor = novoValor;
        AlteradoPor = alteradoPor;
        AlteradoEm = alteradoEm.ToUniversalTime();
    }

    public Guid Id { get; }

    public ValorMonetario ValorAnterior { get; }

    public ValorMonetario NovoValor { get; }

    /// <summary>Ator informado pelo chamador. Não implica autorização, que é responsabilidade da Application.</summary>
    public Guid AlteradoPor { get; }

    /// <summary>Instante informado pelo chamador, em UTC.</summary>
    public DateTimeOffset AlteradoEm { get; }
}
