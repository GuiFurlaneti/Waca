using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Waca.Application.Comum;
using Waca.Domain.Vaquinhas;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Persistencia;
using Xunit;

namespace Waca.Infrastructure.Tests.Identidade;

/// <summary>
/// Dependências da identidade: confinada à Infrastructure, sem login, sessão ou endpoints nesta entrega.
/// </summary>
public class ArquiteturaDaIdentidadeTests
{
    private static readonly Assembly Domain = typeof(Vaquinha).Assembly;
    private static readonly Assembly Application = typeof(IAtorAtual).Assembly;
    private static readonly Assembly Infrastructure = typeof(WacaIdentidadeDbContext).Assembly;

    private static readonly string[] PrefixosDeIdentidade =
    [
        "Microsoft.AspNetCore.Identity",
        "Microsoft.Extensions.Identity",
        "Microsoft.AspNetCore.DataProtection",
    ];

    /// <summary>Assemblies de autenticação HTTP, sessão e endpoints que não podem aparecer na Infrastructure.</summary>
    private static readonly string[] AssembliesProibidosNaInfrastructure =
    [
        "Microsoft.AspNetCore.Identity",
        "Microsoft.AspNetCore.Authentication",
        "Microsoft.AspNetCore.Authentication.Abstractions",
        "Microsoft.AspNetCore.Authentication.Cookies",
        "Microsoft.AspNetCore.Authentication.Core",
        "Microsoft.AspNetCore.Authorization",
        "Microsoft.AspNetCore.Antiforgery",
        "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Http.Abstractions",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.AspNetCore.Mvc.Core",
        "Waca.Api",
    ];

    [Fact]
    public void Domain_nao_depende_de_Identity_nem_de_Data_Protection()
    {
        Assert.DoesNotContain(NomesReferenciados(Domain), n => PrefixosDeIdentidade.Any(p => n.StartsWith(p, StringComparison.Ordinal)));
        Assert.DoesNotContain(ReferenciasDeTipo(Domain), t => PrefixosDeIdentidade.Any(p => t.Namespace.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public void Application_nao_depende_de_Identity_nem_da_Infrastructure()
    {
        var nomes = NomesReferenciados(Application);

        Assert.DoesNotContain(nomes, n => PrefixosDeIdentidade.Any(p => n.StartsWith(p, StringComparison.Ordinal)));
        Assert.DoesNotContain("Waca.Infrastructure", nomes);
        Assert.DoesNotContain(ReferenciasDeTipo(Application), t => PrefixosDeIdentidade.Any(p => t.Namespace.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public void Infrastructure_continua_sem_depender_da_Api_e_sem_assemblies_de_autenticacao_HTTP()
    {
        var nomes = NomesReferenciados(Infrastructure);

        Assert.Empty(nomes.Intersect(AssembliesProibidosNaInfrastructure));
        Assert.DoesNotContain(nomes, n => n.StartsWith("Microsoft.AspNetCore.Authentication", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_nao_contem_emissores_de_sessao_nem_endpoints()
    {
        var tiposReferenciados = ReferenciasDeTipo(Infrastructure).Select(t => t.Nome).ToHashSet(StringComparer.Ordinal);

        foreach (var proibido in new[]
                 {
                     "SignInManager`1", "UserManager`1", "IAuthenticationService", "CookieAuthenticationOptions",
                     "HttpContext", "IEndpointRouteBuilder", "ControllerBase", "IAntiforgery", "IDataProtectionProvider",
                 })
        {
            Assert.DoesNotContain(proibido, tiposReferenciados);
        }
    }

    [Fact]
    public void Tipos_de_identidade_ficam_somente_no_namespace_de_identidade()
    {
        var foraDoLugar = Infrastructure.GetTypes()
            .Where(t => t.Namespace is not null
                && !t.Namespace.StartsWith("Waca.Infrastructure.Identidade", StringComparison.Ordinal)
                && UsaIdentity(t))
            .Select(t => t.FullName)
            .ToArray();

        Assert.Empty(foraDoLugar);
    }

    [Fact]
    public void Contrato_IAtorAtual_permanece_inalterado()
    {
        var propriedade = Assert.Single(typeof(IAtorAtual).GetProperties());

        Assert.Equal("PessoaId", propriedade.Name);
        Assert.Equal(typeof(Guid?), propriedade.PropertyType);
        Assert.Null(propriedade.SetMethod);
        Assert.DoesNotContain(typeof(IAtorAtual).GetMethods(), m => !m.IsSpecialName);
    }

    [Fact]
    public void Contexto_das_vaquinhas_nao_conhece_a_identidade()
    {
        Assert.DoesNotContain(
            typeof(WacaDbContext).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericArguments().Any(UsaIdentityOuIdentidade));
        Assert.False(UsaIdentity(typeof(WacaDbContext)));
    }

    private static bool UsaIdentityOuIdentidade(Type tipo) =>
        UsaIdentity(tipo) || (tipo.Namespace?.StartsWith("Waca.Infrastructure.Identidade", StringComparison.Ordinal) ?? false);

    private static bool UsaIdentity(Type tipo)
    {
        for (var atual = tipo; atual is not null; atual = atual.BaseType)
        {
            if (EhDeIdentity(atual) || atual.GetInterfaces().Any(EhDeIdentity))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EhDeIdentity(Type tipo) =>
        PrefixosDeIdentidade.Any(p => tipo.Namespace?.StartsWith(p, StringComparison.Ordinal) ?? false);

    private static string[] NomesReferenciados(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(r => r.Name).OfType<string>().ToArray();

    private static IReadOnlyList<(string Namespace, string Nome)> ReferenciasDeTipo(Assembly assembly)
    {
        using var arquivo = File.OpenRead(assembly.Location);
        using var pe = new PEReader(arquivo);
        var metadados = pe.GetMetadataReader();

        return metadados.TypeReferences
            .Select(h => metadados.GetTypeReference(h))
            .Select(t => (metadados.GetString(t.Namespace), metadados.GetString(t.Name)))
            .ToArray();
    }
}
