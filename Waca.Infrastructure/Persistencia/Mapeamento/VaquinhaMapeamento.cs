using Waca.Domain.Vaquinhas;
using Waca.Infrastructure.Persistencia.Entidades;

namespace Waca.Infrastructure.Persistencia.Mapeamento;

/// <summary>
/// Conversão explícita entre <see cref="Vaquinha"/> e <see cref="VaquinhaRegistro"/>.
/// </summary>
internal static class VaquinhaMapeamento
{
    public static VaquinhaRegistro ParaRegistro(Vaquinha vaquinha) => new()
    {
        Id = vaquinha.Id,
        Nome = vaquinha.Nome,
        OrganizadorId = vaquinha.OrganizadorId,
        CriadaEm = vaquinha.CriadaEm,
    };

    /// <summary>
    /// Reconstrói a vaquinha pelo construtor validado do domínio.
    /// </summary>
    /// <remarks>
    /// Uma linha que o domínio rejeitaria, ou que ele alteraria ao normalizar (por exemplo, nome com espaços
    /// nas extremidades), não é corrigida: gera <see cref="InvalidOperationException"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A linha não representa uma vaquinha válida.</exception>
    public static Vaquinha ParaDominio(VaquinhaRegistro registro)
    {
        Vaquinha vaquinha;
        try
        {
            vaquinha = new Vaquinha(registro.Id, registro.Nome, registro.OrganizadorId, registro.CriadaEm);
        }
        catch (ArgumentException excecao)
        {
            throw new InvalidOperationException($"O registro de vaquinha {registro.Id} é inválido: {excecao.Message}", excecao);
        }

        if (vaquinha.Nome != registro.Nome || vaquinha.CriadaEm != registro.CriadaEm || vaquinha.CriadaEm.Offset != registro.CriadaEm.Offset)
        {
            throw new InvalidOperationException(
                $"O registro de vaquinha {registro.Id} não está na forma gravada pelo domínio.");
        }

        return vaquinha;
    }
}
