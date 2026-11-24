using System.Reflection;
using Waca.Domain.ObjetosDeValor;
using Waca.Domain.Participacoes;
using Waca.Domain.Vaquinhas;
using Xunit;

namespace Waca.Domain.Tests.Vaquinhas;

public class VaquinhaTests
{
    private static readonly Guid Id = Guid.Parse("6f1c2a44-8d1b-4c55-9a0e-1b2c3d4e5f60");
    private static readonly Guid Organizador = Guid.Parse("0a9b8c7d-6e5f-4a3b-8c2d-1e0f9a8b7c6d");
    private static readonly DateTimeOffset Criacao = new(2026, 11, 24, 15, 30, 0, TimeSpan.Zero);

    // ---------- Construção válida ----------

    [Fact]
    public void Criar_com_dados_validos_preserva_todos_os_campos()
    {
        var vaquinha = new Vaquinha(Id, "Viagem da galera", Organizador, Criacao);

        Assert.Equal(Id, vaquinha.Id);
        Assert.Equal("Viagem da galera", vaquinha.Nome);
        Assert.Equal(Organizador, vaquinha.OrganizadorId);
        Assert.Equal(Criacao, vaquinha.CriadaEm);
        Assert.Equal(TimeSpan.Zero, vaquinha.CriadaEm.Offset);
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(5, 30)]
    [InlineData(14, 0)]
    public void Instante_com_offset_e_normalizado_para_UTC_preservando_o_momento(int horas, int minutos)
    {
        var offset = new TimeSpan(horas, minutos, 0);
        var informado = new DateTimeOffset(2026, 11, 24, 12, 0, 0, offset);

        var vaquinha = new Vaquinha(Id, "Viagem da galera", Organizador, informado);

        Assert.Equal(TimeSpan.Zero, vaquinha.CriadaEm.Offset);
        Assert.Equal(informado.UtcDateTime, vaquinha.CriadaEm.UtcDateTime);
        Assert.Equal(informado.UtcTicks, vaquinha.CriadaEm.UtcTicks);
    }

    [Theory]
    [InlineData("  Viagem da galera", "Viagem da galera")]
    [InlineData("Viagem da galera   ", "Viagem da galera")]
    [InlineData(" \t Viagem  da   galera \r\n", "Viagem  da   galera")]
    [InlineData(" Formatura 2026 – Turma B ", "Formatura 2026 – Turma B")]
    public void Nome_tem_espacos_das_extremidades_removidos_e_conteudo_interno_preservado(string informado, string esperado)
    {
        var vaquinha = new Vaquinha(Id, informado, Organizador, Criacao);

        Assert.Equal(esperado, vaquinha.Nome);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("Churrasco do time ⚽")]
    [InlineData("Presente da Ana & do Bruno (2026)")]
    public void Nome_sem_espacos_nas_extremidades_e_preservado_exatamente(string nome)
    {
        var vaquinha = new Vaquinha(Id, nome, Organizador, Criacao);

        Assert.Equal(nome, vaquinha.Nome);
    }

    [Fact]
    public void Nome_longo_e_aceito_porque_nenhum_limite_foi_aprovado()
    {
        var nome = new string('a', 10_000);

        var vaquinha = new Vaquinha(Id, nome, Organizador, Criacao);

        Assert.Equal(nome, vaquinha.Nome);
    }

    [Fact]
    public void Vaquinhas_diferentes_podem_ter_o_mesmo_nome_e_o_mesmo_organizador()
    {
        var outroId = Guid.Parse("11111111-2222-4333-8444-555555555555");

        var primeira = new Vaquinha(Id, "Viagem da galera", Organizador, Criacao);
        var segunda = new Vaquinha(outroId, "Viagem da galera", Organizador, Criacao);

        Assert.Equal(primeira.Nome, segunda.Nome);
        Assert.Equal(primeira.OrganizadorId, segunda.OrganizadorId);
        Assert.NotEqual(primeira.Id, segunda.Id);
    }

    [Fact]
    public void Instantes_passados_ou_futuros_sao_aceitos_porque_nenhuma_regra_de_data_foi_aprovada()
    {
        var passado = new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var futuro = new DateTimeOffset(2100, 12, 31, 23, 59, 59, TimeSpan.Zero);

        Assert.Equal(passado, new Vaquinha(Id, "Viagem", Organizador, passado).CriadaEm);
        Assert.Equal(futuro, new Vaquinha(Id, "Viagem", Organizador, futuro).CriadaEm);
    }

    [Fact]
    public void Criar_nao_gera_participantes_pagamentos_nem_valores()
    {
        var propriedades = typeof(Vaquinha)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(p => p.Name)
            .Order()
            .ToArray();

        Assert.Equal(["CriadaEm", "Id", "Nome", "OrganizadorId"], propriedades);
        Assert.DoesNotContain(
            typeof(Vaquinha).GetFields(BindingFlags.NonPublic | BindingFlags.Instance),
            campo => campo.FieldType != typeof(Guid)
                && campo.FieldType != typeof(string)
                && campo.FieldType != typeof(DateTimeOffset));
    }

    // ---------- Construção inválida ----------

    [Fact]
    public void Rejeita_id_vazio()
    {
        AssertArgumento("id", () => new Vaquinha(Guid.Empty, "Viagem da galera", Organizador, Criacao));
    }

    [Fact]
    public void Rejeita_organizador_vazio()
    {
        AssertArgumento("organizadorId", () => new Vaquinha(Id, "Viagem da galera", Guid.Empty, Criacao));
    }

    [Fact]
    public void Rejeita_nome_nulo()
    {
        var excecao = Assert.Throws<ArgumentNullException>(() => new Vaquinha(Id, null!, Organizador, Criacao));

        Assert.Equal("nome", excecao.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData(" ")]
    [InlineData("  　")]
    public void Rejeita_nome_vazio_ou_somente_com_espacos(string nome)
    {
        AssertArgumento("nome", () => new Vaquinha(Id, nome, Organizador, Criacao));
    }

    [Fact]
    public void Rejeita_instante_nao_informado()
    {
        AssertArgumento("criadaEm", () => new Vaquinha(Id, "Viagem da galera", Organizador, default));
        AssertArgumento("criadaEm", () => new Vaquinha(Id, "Viagem da galera", Organizador, DateTimeOffset.MinValue));
    }

    [Fact]
    public void Nenhuma_instancia_e_devolvida_quando_a_construcao_falha()
    {
        Vaquinha? resultado = null;

        Assert.ThrowsAny<ArgumentException>(() => resultado = new Vaquinha(Guid.Empty, "Viagem", Organizador, Criacao));
        Assert.ThrowsAny<ArgumentException>(() => resultado = new Vaquinha(Id, " ", Organizador, Criacao));
        Assert.ThrowsAny<ArgumentException>(() => resultado = new Vaquinha(Id, null!, Organizador, Criacao));
        Assert.ThrowsAny<ArgumentException>(() => resultado = new Vaquinha(Id, "Viagem", Guid.Empty, Criacao));
        Assert.ThrowsAny<ArgumentException>(() => resultado = new Vaquinha(Id, "Viagem", Organizador, default));

        Assert.Null(resultado);
    }

    // ---------- Encapsulamento ----------

    [Fact]
    public void Propriedades_nao_tem_setters_nem_init()
    {
        Assert.All(
            typeof(Vaquinha).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            propriedade => Assert.Null(propriedade.GetSetMethod(nonPublic: true)));
    }

    [Fact]
    public void Expoe_apenas_o_construtor_validado_e_nenhuma_operacao()
    {
        var tipo = typeof(Vaquinha);

        Assert.True(tipo.IsSealed);
        var construtor = Assert.Single(tipo.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(
            [typeof(Guid), typeof(string), typeof(Guid), typeof(DateTimeOffset)],
            construtor.GetParameters().Select(p => p.ParameterType));
        Assert.Empty(tipo.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));

        // Sem operações financeiras, de convite, de encerramento ou de renomeação: só os getters.
        var metodosPublicos = tipo
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);
        Assert.Empty(metodosPublicos);
    }

    [Fact]
    public void Pertence_ao_dominio_e_so_expoe_tipos_do_sistema()
    {
        var tipo = typeof(Vaquinha);

        Assert.Equal("Waca.Domain.Vaquinhas", tipo.Namespace);
        Assert.Equal("Waca.Domain", tipo.Assembly.GetName().Name);
        Assert.Same(typeof(Participacao).Assembly, tipo.Assembly);
        Assert.All(
            tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            propriedade => Assert.Equal("System", propriedade.PropertyType.Namespace));
        Assert.DoesNotContain(
            tipo.Assembly.GetReferencedAssemblies(),
            referencia => referencia.Name is "Waca.Application" or "Waca.Infrastructure" or "Waca.Api");
    }

    // ---------- Compatibilidade ----------

    [Fact]
    public void Entidade_legada_continua_disponivel_e_distinta()
    {
        var legada = typeof(Waca.Domain.Entidades.Vaquinha);

        Assert.NotEqual(typeof(Vaquinha), legada);
        Assert.Equal("Waca.Domain.Entidades", legada.Namespace);
        Assert.Equal(typeof(int), legada.GetProperty("Id")!.PropertyType);
        Assert.Equal(typeof(Guid), typeof(Vaquinha).GetProperty("Id")!.PropertyType);
        Assert.Same(legada.Assembly, typeof(Vaquinha).Assembly);
    }

    [Fact]
    public void Identificador_e_compativel_com_o_dominio_financeiro()
    {
        var vaquinha = new Vaquinha(Id, "Viagem da galera", Organizador, Criacao);
        var participante = Guid.Parse("99999999-8888-4777-8666-555555555555");

        var participacao = new Participacao(
            Guid.Parse("12345678-1234-4234-8234-123456789012"), vaquinha.Id, participante, ValorMonetario.Criar(100m));

        Assert.Equal(vaquinha.Id, participacao.VaquinhaId);
    }

    // ---------- Auxiliares ----------

    private static void AssertArgumento(string parametro, Action acao)
    {
        var excecao = Assert.Throws<ArgumentException>(acao);
        Assert.Equal(parametro, excecao.ParamName);
    }
}
