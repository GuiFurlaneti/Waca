namespace Waca.Domain.Vaquinhas;

/// <summary>
/// Fundo coletivo criado e conduzido por um único organizador responsável.
/// </summary>
/// <remarks>
/// <para>
/// Guarda apenas identidade, nome, organizador e instante de criação, todos imutáveis.
/// Não guarda meta, totais, arrecadado, saldo, despesas nem participantes: o total combinado e o
/// arrecadado serão calculados a partir das participações e dos pagamentos, fora deste agregado.
/// </para>
/// <para>
/// O organizador é o responsável pelo fundo e não se torna participante financeiro ao criá-lo.
/// Receber <see cref="OrganizadorId"/> não significa que a pessoa existe ou está autenticada;
/// isso cabe às camadas externas.
/// </para>
/// <para>
/// Identificador e instante são fornecidos pelo chamador; nada é gerado aqui. Por isso o mesmo
/// construtor serve para criar e para recuperar uma vaquinha já registrada.
/// </para>
/// <para>
/// Erros: argumento inválido gera <see cref="ArgumentException"/> (ou derivada).
/// </para>
/// </remarks>
public sealed class Vaquinha
{
    /// <exception cref="ArgumentNullException">Nome nulo.</exception>
    /// <exception cref="ArgumentException">
    /// Identificador vazio, nome vazio ou só com espaços, organizador vazio ou instante não informado.
    /// </exception>
    public Vaquinha(Guid id, string nome, Guid organizadorId, DateTimeOffset criadaEm)
    {
        ExigirId(id, nameof(id));
        if (nome is null)
        {
            throw new ArgumentNullException(nameof(nome), "O nome da vaquinha é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new ArgumentException("O nome da vaquinha é obrigatório.", nameof(nome));
        }

        ExigirId(organizadorId, nameof(organizadorId));
        if (criadaEm == default)
        {
            throw new ArgumentException("O instante não foi informado.", nameof(criadaEm));
        }

        Id = id;
        Nome = nome.Trim();
        OrganizadorId = organizadorId;
        CriadaEm = criadaEm.ToUniversalTime();
    }

    public Guid Id { get; }

    /// <summary>Nome informado, sem espaços nas extremidades.</summary>
    public string Nome { get; }

    /// <summary>Único organizador responsável pela vaquinha.</summary>
    public Guid OrganizadorId { get; }

    /// <summary>Instante de criação informado pelo chamador, em UTC.</summary>
    public DateTimeOffset CriadaEm { get; }

    private static void ExigirId(Guid id, string parametro)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("O identificador não pode ser vazio.", parametro);
        }
    }
}
