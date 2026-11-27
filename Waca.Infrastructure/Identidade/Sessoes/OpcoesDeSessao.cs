namespace Waca.Infrastructure.Identidade.Sessoes;

/// <summary>
/// Tempos das sessões. Os valores padrão são <b>provisórios</b> (planejamento da MVP-04): 8 horas de inatividade
/// e teto absoluto de 7 dias, sem "lembrar-me".
/// </summary>
public sealed class OpcoesDeSessao
{
    /// <summary>Expiração por inatividade (janela deslizante do cookie). Padrão: 8 horas.</summary>
    public TimeSpan Inatividade { get; init; } = TimeSpan.FromHours(8);

    /// <summary>Teto absoluto a partir da criação; nenhuma renovação o ultrapassa. Padrão: 7 dias.</summary>
    public TimeSpan TetoAbsoluto { get; init; } = TimeSpan.FromDays(7);

    /// <exception cref="InvalidOperationException">Tempos fora dos limites aceitos.</exception>
    public void Validar()
    {
        if (Inatividade < TimeSpan.FromMinutes(5) || Inatividade > TimeSpan.FromHours(24))
        {
            throw new InvalidOperationException("Configuração de sessão inválida: Inatividade deve estar entre 5 minutos e 24 horas.");
        }

        if (TetoAbsoluto < Inatividade || TetoAbsoluto > TimeSpan.FromDays(30))
        {
            throw new InvalidOperationException("Configuração de sessão inválida: TetoAbsoluto deve cobrir a Inatividade e não passar de 30 dias.");
        }
    }
}
