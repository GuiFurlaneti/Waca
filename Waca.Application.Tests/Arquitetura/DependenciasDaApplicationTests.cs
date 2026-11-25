using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Waca.Application.Vaquinhas;
using Waca.Domain.Vaquinhas;
using Xunit;

namespace Waca.Application.Tests.Arquitetura;

/// <summary>
/// Garante a direção das dependências: Application depende apenas de Domain, e Domain de nenhum projeto.
/// As referências de tipo são lidas dos metadados do assembly compilado, o que inclui os tipos usados
/// dentro dos corpos dos métodos.
/// </summary>
public class DependenciasDaApplicationTests
{
    private static readonly Assembly Application = typeof(CriarVaquinha).Assembly;
    private static readonly Assembly Domain = typeof(Vaquinha).Assembly;

    private static readonly string[] PrefixosDeInfraestrutura =
        ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "System.Data", "Microsoft.Data"];

    [Fact]
    public void Application_referencia_Domain_e_nenhum_outro_projeto_Waca()
    {
        var waca = Application.GetReferencedAssemblies()
            .Select(r => r.Name)
            .OfType<string>()
            .Where(n => n.StartsWith("Waca.", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(["Waca.Domain"], waca);
    }

    [Fact]
    public void Application_nao_referencia_assemblies_de_persistencia_ou_web()
    {
        var referencias = Application.GetReferencedAssemblies().Select(r => r.Name).OfType<string>();

        Assert.DoesNotContain(referencias, n => PrefixosDeInfraestrutura.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Csproj_da_Application_declara_somente_Domain_e_nenhum_pacote()
    {
        var csproj = XDocument.Load(LocalizarNaSolucao("Waca.Application", "Waca.Application.csproj"));

        var projetos = csproj.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(((string?)e.Attribute("Include") ?? "").Replace('\\', '/')))
            .ToArray();
        Assert.Equal(["Waca.Domain"], projetos);
        Assert.DoesNotContain(csproj.Descendants(), e => e.Name.LocalName == "PackageReference");
    }

    [Fact]
    public void Domain_continua_independente()
    {
        var waca = Domain.GetReferencedAssemblies()
            .Select(r => r.Name)
            .OfType<string>()
            .Where(n => n.StartsWith("Waca.", StringComparison.OrdinalIgnoreCase));
        var csproj = XDocument.Load(LocalizarNaSolucao("Waca.Domain", "Waca.Domain.csproj"));

        Assert.Empty(waca);
        Assert.DoesNotContain(csproj.Descendants(), e => e.Name.LocalName == "ProjectReference");
    }

    [Fact]
    public void Application_usa_somente_a_nova_Vaquinha_do_dominio()
    {
        var tiposDoDominio = ReferenciasDeTipo(Application)
            .Where(t => t.Namespace.StartsWith("Waca.Domain", StringComparison.Ordinal))
            .Distinct()
            .ToArray();

        Assert.Equal([("Waca.Domain.Vaquinhas", "Vaquinha")], tiposDoDominio);
    }

    [Fact]
    public void Application_nao_usa_entidades_legadas_nem_o_dominio_financeiro()
    {
        var namespaces = ReferenciasDeTipo(Application).Select(t => t.Namespace).Distinct().ToArray();

        Assert.DoesNotContain("Waca.Domain.Entidades", namespaces);
        Assert.DoesNotContain("Waca.Domain.Participacoes", namespaces);
        Assert.DoesNotContain("Waca.Domain.ObjetosDeValor", namespaces);
    }

    [Fact]
    public void Contratos_da_Application_nao_dependem_de_tipos_de_persistencia_ou_web()
    {
        var namespaces = ReferenciasDeTipo(Application).Select(t => t.Namespace).Distinct();

        Assert.DoesNotContain(namespaces, n => PrefixosDeInfraestrutura.Any(p => n.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public void Verificador_de_metadados_enxerga_tipos_usados_dentro_de_metodos()
    {
        // Controle: este assembly de testes usa Vaquinha e MetadataReader dentro de métodos;
        // se o verificador não os enxergasse, as verificações negativas acima não provariam nada.
        var referencias = ReferenciasDeTipo(typeof(DependenciasDaApplicationTests).Assembly).ToArray();

        Assert.Contains(("Waca.Domain.Vaquinhas", "Vaquinha"), referencias);
        Assert.Contains(("System.Reflection.Metadata", "MetadataReader"), referencias);
    }

    private static IEnumerable<(string Namespace, string Nome)> ReferenciasDeTipo(Assembly assembly)
    {
        using var arquivo = File.OpenRead(assembly.Location);
        using var pe = new PEReader(arquivo);
        var metadados = pe.GetMetadataReader();

        return metadados.TypeReferences
            .Select(h => metadados.GetTypeReference(h))
            .Select(t => (metadados.GetString(t.Namespace), metadados.GetString(t.Name)))
            .ToArray();
    }

    private static string LocalizarNaSolucao(string pastaDoProjeto, string arquivo)
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            if (File.Exists(Path.Combine(pasta.FullName, "Waca.slnx")))
            {
                var caminho = Path.Combine(pasta.FullName, pastaDoProjeto, arquivo);
                return File.Exists(caminho) ? caminho : throw new FileNotFoundException("Arquivo não encontrado.", caminho);
            }
        }

        throw new DirectoryNotFoundException($"Waca.slnx não encontrado acima de '{AppContext.BaseDirectory}'.");
    }
}
