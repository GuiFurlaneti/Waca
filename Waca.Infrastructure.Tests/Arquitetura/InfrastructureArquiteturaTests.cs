using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.EntityFrameworkCore;
using Waca.Application.Vaquinhas;
using Waca.Infrastructure.Persistencia;
using Waca.Infrastructure.Persistencia.Repositorios;
using Xunit;

namespace Waca.Infrastructure.Tests.Arquitetura;

/// <summary>
/// Verificações que não precisam de banco: dependências e modelo do EF Core.
/// </summary>
public class InfrastructureArquiteturaTests
{
    private static readonly Assembly Infrastructure = typeof(RepositorioVaquinhas).Assembly;

    [Fact]
    public void Infrastructure_depende_de_Application_e_Domain_e_nao_da_Api()
    {
        var waca = Infrastructure.GetReferencedAssemblies()
            .Select(r => r.Name)
            .OfType<string>()
            .Where(n => n.StartsWith("Waca.", StringComparison.Ordinal))
            .Order()
            .ToArray();

        Assert.Equal(["Waca.Application", "Waca.Domain"], waca);
    }

    [Fact]
    public void Application_continua_sem_referencia_a_Infrastructure()
    {
        var referencias = typeof(IRepositorioVaquinhas).Assembly.GetReferencedAssemblies().Select(r => r.Name);

        Assert.DoesNotContain("Waca.Infrastructure", referencias);
    }

    [Fact]
    public void Repositorio_implementa_o_contrato_da_Application()
    {
        Assert.True(typeof(IRepositorioVaquinhas).IsAssignableFrom(typeof(RepositorioVaquinhas)));
    }

    [Fact]
    public void Infrastructure_usa_somente_a_nova_Vaquinha_do_dominio()
    {
        var tiposDoDominio = ReferenciasDeTipo(Infrastructure)
            .Where(t => t.Namespace.StartsWith("Waca.Domain", StringComparison.Ordinal))
            .Distinct()
            .ToArray();

        Assert.Equal([("Waca.Domain.Vaquinhas", "Vaquinha")], tiposDoDominio);
    }

    [Fact]
    public void Modelo_do_EF_contem_apenas_a_tabela_de_vaquinhas_com_as_colunas_aprovadas()
    {
        using var contexto = new WacaDbContextFactory().CreateDbContext([]);

        var entidade = Assert.Single(contexto.Model.GetEntityTypes());
        Assert.Equal("vaquinhas", entidade.GetTableName());
        Assert.Equal(
            ["criada_em", "id", "nome", "organizador_id"],
            entidade.GetProperties().Select(p => p.GetColumnName()).Order());
        Assert.Equal("id", Assert.Single(entidade.FindPrimaryKey()!.Properties).GetColumnName());
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never, entidade.FindProperty("Id")!.ValueGenerated);
        Assert.Empty(entidade.GetForeignKeys());
    }

    [Fact]
    public void Fabrica_de_design_nao_contem_connection_string()
    {
        using var contexto = new WacaDbContextFactory().CreateDbContext([]);

        Assert.True(string.IsNullOrEmpty(contexto.Database.GetConnectionString()));
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
}
