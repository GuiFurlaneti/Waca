using System.Reflection;
using System.Xml.Linq;
using Waca.Domain.Entidades;
using Xunit;

namespace Waca.Domain.Tests.Arquitetura;

/// <summary>
/// Garante que Waca.Domain não depende de nenhuma outra camada da solução.
/// A verificação usa duas fontes complementares:
///  A. referências gravadas no assembly compilado (só aparecem se o código as utiliza);
///  B. ProjectReference declaradas no Waca.Domain.csproj (aparecem mesmo sem uso no código).
/// </summary>
public class DependenciasDoDominioTests
{
    [Fact]
    public void Assembly_do_Domain_nao_referencia_outros_projetos_Waca()
    {
        var referencias = typeof(Vaquinha).Assembly.GetReferencedAssemblies();

        var proibidas = VerificadorDeDependencias.AssembliesProibidos(referencias);

        Assert.Empty(proibidas);
    }

    [Fact]
    public void Csproj_do_Domain_nao_declara_ProjectReference_para_outros_projetos_Waca()
    {
        var caminho = VerificadorDeDependencias.LocalizarCsprojDoDomain();
        var csproj = XDocument.Load(caminho);

        var proibidas = VerificadorDeDependencias.ProjectReferencesProibidas(csproj);

        Assert.Empty(proibidas);
    }

    // Cenário negativo: XML sintético em memória; nenhum projeto real é alterado.
    [Theory]
    [InlineData(@"..\Waca.Infrastructure\Waca.Infrastructure.csproj", "Waca.Infrastructure")]
    [InlineData("../Waca.Application/Waca.Application.csproj", "Waca.Application")]
    [InlineData(@"..\Waca.Api\Waca.Api.csproj", "Waca.Api")]
    public void Verificador_detecta_ProjectReference_proibida(string include, string projetoEsperado)
    {
        var csproj = XDocument.Parse($"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="{include}" />
              </ItemGroup>
            </Project>
            """);

        var proibidas = VerificadorDeDependencias.ProjectReferencesProibidas(csproj);

        Assert.Equal(new[] { projetoEsperado }, proibidas);
    }

    // Controle positivo: garante que o verificador não acusa referências permitidas.
    [Fact]
    public void Verificador_aceita_csproj_sem_referencias_proibidas()
    {
        var csproj = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Waca.Infrastructure.Fake" Version="1.0.0" />
                <ProjectReference Include="..\Outro\Outro.csproj" />
              </ItemGroup>
            </Project>
            """);

        var proibidas = VerificadorDeDependencias.ProjectReferencesProibidas(csproj);

        Assert.Empty(proibidas);
    }

    // Cenário negativo da verificação A, com nomes de assembly sintéticos.
    [Fact]
    public void Verificador_detecta_assembly_proibido()
    {
        AssemblyName[] referencias = [new("System.Runtime"), new("Waca.Infrastructure")];

        var proibidas = VerificadorDeDependencias.AssembliesProibidos(referencias);

        Assert.Equal(new[] { "Waca.Infrastructure" }, proibidas);
    }
}

internal static class VerificadorDeDependencias
{
    private static readonly string[] ProjetosProibidos =
        ["Waca.Api", "Waca.Application", "Waca.Infrastructure"];

    public static string[] AssembliesProibidos(IEnumerable<AssemblyName> referencias) =>
        referencias
            .Select(r => r.Name)
            .OfType<string>()
            .Where(EhProibido)
            .ToArray();

    public static string[] ProjectReferencesProibidas(XDocument csproj) =>
        csproj.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .OfType<string>()
            .SelectMany(include => include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(caminho => Path.GetFileNameWithoutExtension(caminho.Replace('\\', '/')))
            .Where(EhProibido)
            .ToArray();

    /// <summary>
    /// Sobe a partir da pasta do assembly de testes até encontrar Waca.slnx,
    /// sem depender do diretório corrente do terminal.
    /// </summary>
    public static string LocalizarCsprojDoDomain()
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            if (!File.Exists(Path.Combine(pasta.FullName, "Waca.slnx")))
            {
                continue;
            }

            var csproj = Path.Combine(pasta.FullName, "Waca.Domain", "Waca.Domain.csproj");
            if (!File.Exists(csproj))
            {
                throw new FileNotFoundException("Waca.slnx encontrado, mas Waca.Domain.csproj não existe.", csproj);
            }

            return csproj;
        }

        throw new DirectoryNotFoundException(
            $"Waca.slnx não encontrado acima de '{AppContext.BaseDirectory}'.");
    }

    private static bool EhProibido(string nome) =>
        ProjetosProibidos.Contains(nome, StringComparer.OrdinalIgnoreCase);
}
