using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Identidade.Desafios;
using Xunit;

namespace Waca.Infrastructure.Tests.Identidade.Desafios;

/// <summary>Confinamento do motor de desafios: sem fontes fracas de aleatoriedade, sem código em DTOs, sem HTTP.</summary>
public class ArquiteturaDosDesafiosTests
{
    private static readonly Assembly Infrastructure = typeof(EmissorDeDesafios).Assembly;

    [Fact]
    public void Infrastructure_nao_usa_System_Random()
    {
        Assert.DoesNotContain(("System", "Random"), ReferenciasDeTipo(Infrastructure));
    }

    [Fact]
    public void Infrastructure_nao_le_variaveis_de_ambiente_nem_configuracao()
    {
        Assert.DoesNotContain("GetEnvironmentVariable", ReferenciasDeMembro(Infrastructure));
        Assert.DoesNotContain(
            Infrastructure.GetReferencedAssemblies().Select(a => a.Name),
            n => n is not null && n.StartsWith("Microsoft.Extensions.Configuration", StringComparison.Ordinal));
    }

    [Fact]
    public void Tentativas_de_codigo_nao_acionam_o_lockout_da_conta()
    {
        var membros = ReferenciasDeMembro(Infrastructure);

        Assert.DoesNotContain("AccessFailedAsync", membros);
        Assert.DoesNotContain("SetLockoutEndDateAsync", membros);
    }

    [Fact]
    public void Resultados_e_solicitacoes_nao_carregam_o_codigo()
    {
        foreach (var tipo in new[] { typeof(ResultadoDaEmissao), typeof(ResultadoDaVerificacao), typeof(SolicitacaoDeEmissao) })
        {
            Assert.DoesNotContain(tipo.GetProperties(), p => p.Name.Contains("Codigo", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Somente_a_entrega_e_o_pedido_de_verificacao_expoem_codigo_e_a_entrega_nao_e_construivel_fora()
    {
        var tiposComCodigo = Infrastructure.GetExportedTypes()
            .Where(t => t.GetProperties().Any(p => p.Name == "Codigo"))
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["CodigoParaEntrega", "PedidoDeVerificacao"], tiposComCodigo);
        Assert.Empty(typeof(CodigoParaEntrega).GetConstructors());
    }

    [Fact]
    public void Registro_e_configuracao_dos_desafios_nao_sao_publicos()
    {
        var naoPublicos = new[] { "DesafioDeAcessoRegistro", "DesafioDeAcessoConfiguracao" };

        Assert.DoesNotContain(Infrastructure.GetExportedTypes(), t => naoPublicos.Contains(t.Name));
    }

    [Fact]
    public void Desafios_ficam_no_namespace_de_identidade_e_nao_tocam_as_vaquinhas()
    {
        var tipos = Infrastructure.GetTypes()
            .Where(t => t.Namespace == "Waca.Infrastructure.Identidade.Desafios")
            .ToArray();

        Assert.NotEmpty(tipos);
        Assert.DoesNotContain(
            ReferenciasDeTipo(Infrastructure),
            r => r.Namespace.StartsWith("Microsoft.AspNetCore.Http", StringComparison.Ordinal)
                || r.Namespace.StartsWith("Microsoft.AspNetCore.Authentication", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(WacaIdentidadeDbContext).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericArguments().Any(a => a.Namespace?.Contains("Persistencia", StringComparison.Ordinal) ?? false));
    }

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

    private static IReadOnlyList<string> ReferenciasDeMembro(Assembly assembly)
    {
        using var arquivo = File.OpenRead(assembly.Location);
        using var pe = new PEReader(arquivo);
        var metadados = pe.GetMetadataReader();

        return metadados.MemberReferences
            .Select(h => metadados.GetString(metadados.GetMemberReference(h).Name))
            .ToArray();
    }
}
