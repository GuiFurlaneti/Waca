using System.Security.Claims;

namespace Waca.Api.Autenticacao;

/// <summary>Claims próprias do Waca gravadas no cookie de sessão.</summary>
public static class ClaimsDaSessao
{
    /// <summary>Identificador da linha em <c>identidade.sessoes</c>.</summary>
    public const string Sessao = "sid";

    /// <summary>Método de autenticação.</summary>
    public const string Metodo = "amr";

    /// <summary>Único método aceito nesta fase: código de uso único enviado por e-mail.</summary>
    public const string CodigoPorEmail = "otp_email";

    internal static readonly string[] Preservadas = [Sessao, Metodo];

    /// <summary>
    /// Copia <c>sid</c> e <c>amr</c> do principal atual para o novo, quando o Identity reconstrói o principal
    /// ao validar o security stamp (a reconstrução descarta claims adicionadas no login).
    /// </summary>
    public static void Preservar(ClaimsPrincipal atual, ClaimsPrincipal novo)
    {
        ArgumentNullException.ThrowIfNull(atual);
        ArgumentNullException.ThrowIfNull(novo);

        if (novo.Identity is not ClaimsIdentity identidade)
        {
            return;
        }

        foreach (var tipo in Preservadas)
        {
            if (identidade.HasClaim(c => c.Type == tipo))
            {
                continue;
            }

            foreach (var claim in atual.FindAll(tipo))
            {
                identidade.AddClaim(new Claim(claim.Type, claim.Value));
            }
        }
    }
}
