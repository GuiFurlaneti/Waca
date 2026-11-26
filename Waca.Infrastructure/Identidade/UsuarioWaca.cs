using Microsoft.AspNetCore.Identity;

namespace Waca.Infrastructure.Identidade;

/// <summary>
/// Conta de usuário do Waca, persistida pelo ASP.NET Core Identity no schema <c>identidade</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IdentityUser{TKey}.Id"/> é o <c>PessoaId</c> que a Application recebe por <c>IAtorAtual</c>.
/// Ele é fornecido por quem cria a conta (Guid v7 é o formato previsto), nunca é gerado pelo banco
/// e nunca muda. O e-mail pode mudar e, por isso, não identifica a pessoa.
/// </para>
/// <para>
/// O nome de usuário do Identity é derivado do identificador (32 dígitos hexadecimais), não do e-mail.
/// A normalização de e-mail e nome é responsabilidade do Identity (<see cref="ILookupNormalizer"/>);
/// esta classe não normaliza nada.
/// </para>
/// <para>
/// Não há relação com vaquinhas ou participações: criar uma conta não cria organizador, membro nem
/// participação financeira.
/// </para>
/// </remarks>
public sealed class UsuarioWaca : IdentityUser<Guid>
{
    /// <summary>Usado pelo EF Core ao materializar a conta.</summary>
    private UsuarioWaca()
    {
    }

    /// <summary>Prepara uma conta nova, com e-mail ainda não confirmado.</summary>
    /// <param name="id">Identificador interno estável; não pode ser <see cref="Guid.Empty"/>.</param>
    /// <param name="email">Endereço informado; é gravado como recebido.</param>
    /// <param name="estado">Estado administrativo inicial, que deve ser escolhido explicitamente.</param>
    /// <exception cref="ArgumentException">Identificador vazio, e-mail em branco ou estado inexistente.</exception>
    public UsuarioWaca(Guid id, string email, EstadoConta estado)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("O identificador da conta não pode ser vazio.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        if (!Enum.IsDefined(estado))
        {
            throw new ArgumentException("Estado de conta inexistente.", nameof(estado));
        }

        Id = id;
        UserName = NomeDeUsuarioPara(id);
        Email = email;
        Estado = estado;
    }

    /// <summary>Estado administrativo; independente do bloqueio temporário do Identity.</summary>
    public EstadoConta Estado { get; set; }

    /// <summary>
    /// Indica se o estado administrativo e a confirmação de e-mail permitem acesso.
    /// </summary>
    /// <remarks>
    /// Exige <see cref="EstadoConta.Ativa"/> <b>e</b> e-mail confirmado: uma conta não verificada nunca é tratada
    /// como habilitada. Não considera o bloqueio temporário do Identity, que deve ser verificado à parte
    /// antes de qualquer emissão de sessão (fora do escopo desta entrega).
    /// </remarks>
    public bool PermiteAcesso() => Estado == EstadoConta.Ativa && EmailConfirmed;

    /// <summary>Nome de usuário do Identity derivado do identificador, sem relação com o e-mail.</summary>
    public static string NomeDeUsuarioPara(Guid id) => id.ToString("N");
}
