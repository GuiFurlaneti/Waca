using Microsoft.AspNetCore.Identity;

namespace Waca.Infrastructure.Identidade;

/// <summary>
/// Configuração do ASP.NET Core Identity exigida pelo modelo persistido do Waca.
/// </summary>
/// <remarks>
/// <para>
/// A mesma configuração deve ser usada na fábrica de design-time, nos testes e na futura configuração
/// da API (por exemplo, <c>AddIdentityCore&lt;UsuarioWaca&gt;(OpcoesDaIdentidade.Configurar)</c>). O modelo do
/// EF Core recusa ser construído com opções de armazenamento diferentes; veja
/// <see cref="ExigirArmazenamentoCompativel"/>.
/// </para>
/// <para>
/// <see cref="UserOptions.RequireUniqueEmail"/> é só a validação de aplicação, que dá uma mensagem clara mas
/// não resiste a cadastros simultâneos. A garantia é o índice único <c>EmailIndex</c> no PostgreSQL.
/// </para>
/// </remarks>
public static class OpcoesDaIdentidade
{
    /// <summary>
    /// Versão do schema do Identity usada pelo modelo: usuários, claims, logins e tokens, sem passkeys.
    /// </summary>
    /// <remarks>Adotar passkeys exigirá a versão 3 e uma nova migration.</remarks>
    public static readonly Version VersaoDoSchema = IdentitySchemaVersions.Version2;

    /// <summary>Comprimento máximo das colunas usadas em chaves (provedor de login, nome de token).</summary>
    public const int ComprimentoMaximoDeChaves = 128;

    /// <summary>Aplica as opções exigidas pelo modelo e as regras mínimas de conta do Waca.</summary>
    public static void Configurar(IdentityOptions opcoes)
    {
        ArgumentNullException.ThrowIfNull(opcoes);

        opcoes.User.RequireUniqueEmail = true;
        opcoes.SignIn.RequireConfirmedEmail = true;

        opcoes.Stores.SchemaVersion = VersaoDoSchema;
        opcoes.Stores.MaxLengthForKeys = ComprimentoMaximoDeChaves;
        opcoes.Stores.ProtectPersonalData = false;
    }

    /// <summary>
    /// Confere se as opções de armazenamento produzem exatamente o modelo das migrations.
    /// </summary>
    /// <param name="opcoes">Opções resolvidas pelo provedor de serviços do contexto, ou <see langword="null"/> se não houver.</param>
    /// <exception cref="InvalidOperationException">Opções ausentes ou diferentes das aprovadas.</exception>
    public static void ExigirArmazenamentoCompativel(StoreOptions? opcoes)
    {
        if (opcoes is null)
        {
            throw new InvalidOperationException(
                "O contexto de identidade precisa das opções do Identity configuradas por OpcoesDaIdentidade.Configurar; sem elas o Identity escolheria outro schema.");
        }

        if (opcoes.SchemaVersion != VersaoDoSchema)
        {
            throw new InvalidOperationException(
                $"Versão de schema do Identity {opcoes.SchemaVersion} diferente da aprovada ({VersaoDoSchema}).");
        }

        if (opcoes.MaxLengthForKeys != ComprimentoMaximoDeChaves)
        {
            throw new InvalidOperationException(
                $"MaxLengthForKeys {opcoes.MaxLengthForKeys} diferente do aprovado ({ComprimentoMaximoDeChaves}).");
        }

        if (opcoes.ProtectPersonalData)
        {
            throw new InvalidOperationException("ProtectPersonalData altera o modelo persistido e não está aprovado.");
        }
    }
}
