namespace Waca.Infrastructure.Identidade.Desafios;

/// <summary>
/// Parâmetros dos desafios de acesso. Os valores padrão são <b>provisórios</b> e testáveis, não uma decisão
/// definitiva de experiência do usuário ou de política de produção.
/// </summary>
/// <remarks>
/// Os componentes chamam <see cref="Validar"/> na construção: configuração inválida impede o uso.
/// </remarks>
public sealed class OpcoesDeDesafios
{
    public const int MinimoDeDigitos = 6;
    public const int MaximoDeDigitos = 9;
    public const int MaximoDeTentativasPermitido = 10;

    /// <summary>Quantidade de dígitos do código. Padrão: 8.</summary>
    public int DigitosDoCodigo { get; init; } = 8;

    /// <summary>Validade de cada desafio. Padrão: 10 minutos.</summary>
    public TimeSpan Validade { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Tentativas por desafio antes da invalidação. Padrão: 5.</summary>
    public int MaximoDeTentativas { get; init; } = 5;

    /// <summary>Intervalo mínimo entre emissões para o mesmo e-mail e finalidade. Padrão: 60 segundos.</summary>
    public TimeSpan IntervaloMinimoEntreEmissoes { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Janela curta de cota. Padrão: 15 minutos.</summary>
    public TimeSpan JanelaCurta { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Emissões permitidas na janela curta. Padrão: 3.</summary>
    public int MaximoDeEmissoesNaJanelaCurta { get; init; } = 3;

    /// <summary>Janela longa de cota. Padrão: 24 horas.</summary>
    public TimeSpan JanelaLonga { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Emissões permitidas na janela longa. Padrão: 10.</summary>
    public int MaximoDeEmissoesNaJanelaLonga { get; init; } = 10;

    /// <summary>
    /// Tempo, contado a partir da expiração, durante o qual o desafio continua gravado. Padrão: 7 dias.
    /// Precisa cobrir a janela longa, para que a limpeza nunca devolva cota.
    /// </summary>
    public TimeSpan Retencao { get; init; } = TimeSpan.FromDays(7);

    /// <exception cref="InvalidOperationException">Algum parâmetro está fora dos limites aceitos.</exception>
    public void Validar()
    {
        Exigir(DigitosDoCodigo is >= MinimoDeDigitos and <= MaximoDeDigitos,
            $"DigitosDoCodigo deve estar entre {MinimoDeDigitos} e {MaximoDeDigitos}.");
        Exigir(Validade >= TimeSpan.FromMinutes(1) && Validade <= TimeSpan.FromHours(1),
            "Validade deve estar entre 1 minuto e 1 hora.");
        Exigir(MaximoDeTentativas is >= 1 and <= MaximoDeTentativasPermitido,
            $"MaximoDeTentativas deve estar entre 1 e {MaximoDeTentativasPermitido}.");
        Exigir(JanelaCurta > TimeSpan.Zero, "JanelaCurta deve ser positiva.");
        Exigir(JanelaLonga > JanelaCurta, "JanelaLonga deve ser maior que JanelaCurta.");
        Exigir(JanelaLonga <= TimeSpan.FromDays(7), "JanelaLonga não pode passar de 7 dias.");
        Exigir(IntervaloMinimoEntreEmissoes >= TimeSpan.Zero && IntervaloMinimoEntreEmissoes <= JanelaCurta,
            "IntervaloMinimoEntreEmissoes deve estar entre zero e JanelaCurta.");
        Exigir(MaximoDeEmissoesNaJanelaCurta >= 1, "MaximoDeEmissoesNaJanelaCurta deve ser ao menos 1.");
        Exigir(MaximoDeEmissoesNaJanelaLonga >= MaximoDeEmissoesNaJanelaCurta,
            "MaximoDeEmissoesNaJanelaLonga não pode ser menor que MaximoDeEmissoesNaJanelaCurta.");
        Exigir(Retencao >= JanelaLonga && Retencao <= TimeSpan.FromDays(365),
            "Retencao deve cobrir a JanelaLonga e não passar de 365 dias.");
    }

    private static void Exigir(bool condicao, string mensagem)
    {
        if (!condicao)
        {
            throw new InvalidOperationException($"Configuração de desafios inválida: {mensagem}");
        }
    }
}
