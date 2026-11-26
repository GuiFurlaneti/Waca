namespace Waca.Infrastructure.Identidade.Desafios;

/// <summary>
/// Finalidade de um desafio de acesso, gravada como texto na coluna <c>finalidade</c> e incluída no HMAC.
/// </summary>
/// <remarks>
/// Um código emitido para uma finalidade nunca é aceito para outra. A lista é fechada: o banco recusa
/// qualquer outro valor (<c>ck_desafios_acesso_finalidade</c>) e cada nova finalidade exige migration.
/// </remarks>
public enum FinalidadeDesafio
{
    /// <summary>Entrada por e-mail (primeiro acesso ou acesso de conta existente). Não tem pessoa vinculada.</summary>
    Acesso = 1,

    /// <summary>
    /// Confirmação de um novo endereço para uma conta existente. Exige pessoa vinculada.
    /// Nenhum fluxo usa esta finalidade ainda; ela existe para separar códigos por finalidade.
    /// </summary>
    TrocaDeEmail = 2,
}
