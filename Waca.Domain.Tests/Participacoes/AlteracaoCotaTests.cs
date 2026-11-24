using System.Reflection;
using Waca.Domain.ObjetosDeValor;
using Waca.Domain.Participacoes;
using Xunit;

namespace Waca.Domain.Tests.Participacoes;

public class AlteracaoCotaTests
{
    private static readonly Guid Participante = Guid.NewGuid();
    private static readonly Guid Organizador = Guid.NewGuid();
    private static readonly DateTimeOffset Declaracao = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Decisao = new(2026, 11, 24, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Alteracao = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    // ---------- Alterações válidas ----------

    [Fact]
    public void Aumentar_cota_registra_historico_e_recalcula_disponivel_e_restante()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 30m, pendente: 20m);
        var alteracaoId = Guid.NewGuid();

        var alterou = participacao.AlterarCota(alteracaoId, Reais(150m), Organizador, Alteracao);

        Assert.True(alterou);
        Assert.Equal(Reais(150m), participacao.Cota);
        AssertTotais(participacao, confirmado: 30m, pendente: 20m, comprometido: 50m, disponivel: 100m, restante: 120m);

        var registro = Assert.Single(participacao.AlteracoesCota);
        Assert.Equal(alteracaoId, registro.Id);
        Assert.Equal(Reais(100m), registro.ValorAnterior);
        Assert.Equal(Reais(150m), registro.NovoValor);
    }

    [Fact]
    public void Reduzir_de_180_para_140_com_60_confirmados_e_80_pendentes_e_aceito()
    {
        var participacao = ParticipacaoCom(cota: 180m, confirmado: 60m, pendente: 80m);

        var alterou = participacao.AlterarCota(Guid.NewGuid(), Reais(140m), Organizador, Alteracao);

        Assert.True(alterou);
        Assert.Equal(Reais(140m), participacao.Cota);
        AssertTotais(participacao, confirmado: 60m, pendente: 80m, comprometido: 140m, disponivel: 0m, restante: 80m);

        var registro = Assert.Single(participacao.AlteracoesCota);
        Assert.Equal(Reais(180m), registro.ValorAnterior);
        Assert.Equal(Reais(140m), registro.NovoValor);

        // A nova cota passa a limitar declarações: não há mais valor disponível.
        Assert.Throws<InvalidOperationException>(
            () => participacao.DeclararPagamento(Guid.NewGuid(), Reais(0.01m), Participante, Declaracao));
    }

    [Fact]
    public void Reduzir_para_139_com_60_confirmados_e_80_pendentes_e_rejeitado_sem_alterar_o_estado()
    {
        var participacao = ParticipacaoCom(cota: 180m, confirmado: 60m, pendente: 80m);
        var antes = Fotografar(participacao);

        Assert.Throws<InvalidOperationException>(
            () => participacao.AlterarCota(Guid.NewGuid(), Reais(139m), Organizador, Alteracao));

        Assert.Equal(antes, Fotografar(participacao));
        Assert.Empty(participacao.AlteracoesCota);
    }

    [Fact]
    public void Reduzir_um_centavo_abaixo_do_comprometido_e_rejeitado()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 0m, pendente: 50m);
        var antes = Fotografar(participacao);

        Assert.Throws<InvalidOperationException>(
            () => participacao.AlterarCota(Guid.NewGuid(), Reais(49.99m), Organizador, Alteracao));

        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Reduzir_exatamente_ao_comprometido_e_aceito()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 0m, pendente: 50m);

        Assert.True(participacao.AlterarCota(Guid.NewGuid(), Reais(50m), Organizador, Alteracao));

        Assert.Equal(Reais(50m), participacao.Cota);
        AssertTotais(participacao, confirmado: 0m, pendente: 50m, comprometido: 50m, disponivel: 0m, restante: 50m);
    }

    [Fact]
    public void Reduzir_em_participacao_sem_pagamentos_e_aceito_ate_um_centavo()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 0m, pendente: 0m);

        Assert.True(participacao.AlterarCota(Guid.NewGuid(), Reais(0.01m), Organizador, Alteracao));

        Assert.Equal(Reais(0.01m), participacao.Cota);
        AssertTotais(participacao, confirmado: 0m, pendente: 0m, comprometido: 0m, disponivel: 0.01m, restante: 0.01m);
    }

    [Fact]
    public void Pagamento_rejeitado_nao_impede_reducao_da_cota()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 0m, pendente: 20m);
        var rejeitadoId = Guid.NewGuid();
        participacao.DeclararPagamento(rejeitadoId, Reais(60m), Participante, Declaracao);
        participacao.RejeitarPagamento(rejeitadoId, Organizador, "Não identificado", Decisao);

        Assert.True(participacao.AlterarCota(Guid.NewGuid(), Reais(20m), Organizador, Alteracao));

        Assert.Equal(Reais(20m), participacao.Cota);
        Assert.Equal(StatusPagamento.Rejeitado, participacao.Pagamentos.Single(p => p.Id == rejeitadoId).Status);
    }

    [Fact]
    public void Alteracao_valida_nao_modifica_pagamentos_nem_confirmado_e_pendente()
    {
        var participacao = ParticipacaoCom(cota: 200m, confirmado: 60m, pendente: 40m);
        var rejeitadoId = Guid.NewGuid();
        participacao.DeclararPagamento(rejeitadoId, Reais(10m), Participante, Declaracao);
        participacao.RejeitarPagamento(rejeitadoId, Organizador, "Duplicado", Decisao);
        var pagamentosAntes = DescreverPagamentos(participacao);
        var referenciasAntes = participacao.Pagamentos.ToArray();

        participacao.AlterarCota(Guid.NewGuid(), Reais(120m), Organizador, Alteracao);
        participacao.AlterarCota(Guid.NewGuid(), Reais(300m), Organizador, Alteracao.AddHours(1));

        Assert.Equal(pagamentosAntes, DescreverPagamentos(participacao));
        Assert.Equal(referenciasAntes, participacao.Pagamentos);
        Assert.Equal(Reais(60m), participacao.Confirmado);
        Assert.Equal(Reais(40m), participacao.Pendente);
    }

    // ---------- Histórico ----------

    [Fact]
    public void Historico_registra_ator_e_instante_em_utc()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 0m, pendente: 0m);
        var horarioDeBrasilia = new DateTimeOffset(2026, 11, 25, 7, 0, 0, TimeSpan.FromHours(-3));

        participacao.AlterarCota(Guid.NewGuid(), Reais(120m), Organizador, horarioDeBrasilia);

        var registro = Assert.Single(participacao.AlteracoesCota);
        Assert.Equal(Organizador, registro.AlteradoPor);
        Assert.Equal(TimeSpan.Zero, registro.AlteradoEm.Offset);
        Assert.Equal(Alteracao, registro.AlteradoEm);
    }

    [Fact]
    public void Multiplas_alteracoes_preservam_a_sequencia_do_historico()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 10m, pendente: 0m);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        participacao.AlterarCota(ids[0], Reais(150m), Organizador, Alteracao);
        participacao.AlterarCota(ids[1], Reais(120m), Participante, Alteracao.AddDays(1));
        participacao.AlterarCota(ids[2], Reais(200m), Organizador, Alteracao.AddDays(2));

        Assert.Equal(Reais(200m), participacao.Cota);
        Assert.Equal(ids, participacao.AlteracoesCota.Select(a => a.Id));
        Assert.Equal(new[] { 100m, 150m, 120m }, participacao.AlteracoesCota.Select(a => a.ValorAnterior.Valor));
        Assert.Equal(new[] { 150m, 120m, 200m }, participacao.AlteracoesCota.Select(a => a.NovoValor.Valor));
        Assert.Equal(new[] { Organizador, Participante, Organizador }, participacao.AlteracoesCota.Select(a => a.AlteradoPor));
        for (var i = 1; i < participacao.AlteracoesCota.Count; i++)
        {
            Assert.Equal(participacao.AlteracoesCota[i - 1].NovoValor, participacao.AlteracoesCota[i].ValorAnterior);
        }
    }

    [Fact]
    public void Historico_nao_pode_ser_modificado_externamente()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 0m, pendente: 0m);
        participacao.AlterarCota(Guid.NewGuid(), Reais(150m), Organizador, Alteracao);

        Assert.False(participacao.AlteracoesCota is List<AlteracaoCota>);
        var comoLista = Assert.IsAssignableFrom<IList<AlteracaoCota>>(participacao.AlteracoesCota);
        Assert.True(comoLista.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => comoLista.Clear());
        Assert.Throws<NotSupportedException>(() => comoLista.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => comoLista[0] = comoLista[0]);

        Assert.Single(participacao.AlteracoesCota);
        Assert.Equal(Reais(150m), participacao.Cota);
    }

    [Fact]
    public void AlteracaoCota_nao_expoe_construtor_nem_setters_publicos()
    {
        var tipo = typeof(AlteracaoCota);

        Assert.Empty(tipo.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.All(
            tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            propriedade => Assert.Null(propriedade.GetSetMethod(nonPublic: false)));
        Assert.Null(typeof(Participacao).GetProperty(nameof(Participacao.Cota))!.GetSetMethod(nonPublic: false));
    }

    // ---------- Mesmo valor ----------

    [Fact]
    public void Alterar_para_o_mesmo_valor_nao_altera_nada_e_retorna_false()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 30m, pendente: 10m);
        participacao.AlterarCota(Guid.NewGuid(), Reais(120m), Organizador, Alteracao);
        var antes = Fotografar(participacao);

        var alterou = participacao.AlterarCota(Guid.NewGuid(), Reais(120.00m), Organizador, Alteracao.AddHours(1));

        Assert.False(alterou);
        Assert.Equal(antes, Fotografar(participacao));
        Assert.Single(participacao.AlteracoesCota);
    }

    [Fact]
    public void Alterar_para_o_mesmo_valor_nao_consome_o_identificador()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 0m, pendente: 0m);
        var alteracaoId = Guid.NewGuid();

        Assert.False(participacao.AlterarCota(alteracaoId, Reais(100m), Organizador, Alteracao));
        Assert.True(participacao.AlterarCota(alteracaoId, Reais(110m), Organizador, Alteracao));

        Assert.Equal(alteracaoId, Assert.Single(participacao.AlteracoesCota).Id);
    }

    [Fact]
    public void Alterar_para_o_mesmo_valor_com_argumentos_invalidos_ainda_e_rejeitado()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 0m, pendente: 0m);
        var antes = Fotografar(participacao);

        AssertArgumento("alteradoPor",
            () => participacao.AlterarCota(Guid.NewGuid(), Reais(100m), Guid.Empty, Alteracao));
        AssertArgumento("alteracaoId",
            () => participacao.AlterarCota(Guid.Empty, Reais(100m), Organizador, Alteracao));
        AssertArgumento("alteradoEm",
            () => participacao.AlterarCota(Guid.NewGuid(), Reais(100m), Organizador, default(DateTimeOffset)));

        Assert.Equal(antes, Fotografar(participacao));
    }

    // ---------- Operações inválidas ----------

    [Fact]
    public void Cota_zero_e_rejeitada_inclusive_valor_padrao_sem_alterar_o_estado()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 0m, pendente: 0m);
        var antes = Fotografar(participacao);

        foreach (var novaCota in new[] { ValorMonetario.Zero, default(ValorMonetario) })
        {
            var excecao = Assert.Throws<ArgumentOutOfRangeException>(
                () => participacao.AlterarCota(Guid.NewGuid(), novaCota, Organizador, Alteracao));
            Assert.Equal("novaCota", excecao.ParamName);
        }

        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Identificador_de_alteracao_vazio_e_rejeitado_sem_alterar_o_estado()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 20m, pendente: 0m);
        var antes = Fotografar(participacao);

        AssertArgumento("alteracaoId",
            () => participacao.AlterarCota(Guid.Empty, Reais(150m), Organizador, Alteracao));

        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Identificador_de_alteracao_duplicado_e_rejeitado_sem_alterar_o_estado()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 0m, pendente: 0m);
        var alteracaoId = Guid.NewGuid();
        participacao.AlterarCota(alteracaoId, Reais(150m), Organizador, Alteracao);
        var antes = Fotografar(participacao);

        Assert.Throws<InvalidOperationException>(
            () => participacao.AlterarCota(alteracaoId, Reais(200m), Organizador, Alteracao.AddHours(1)));

        Assert.Equal(antes, Fotografar(participacao));
        Assert.Equal(Reais(150m), participacao.Cota);
    }

    [Fact]
    public void Ator_vazio_e_rejeitado_sem_alterar_o_estado()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 20m, pendente: 10m);
        var antes = Fotografar(participacao);

        AssertArgumento("alteradoPor",
            () => participacao.AlterarCota(Guid.NewGuid(), Reais(150m), Guid.Empty, Alteracao));

        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Instante_nao_informado_e_rejeitado_sem_alterar_o_estado()
    {
        var participacao = ParticipacaoCom(cota: 100m, confirmado: 20m, pendente: 10m);
        var antes = Fotografar(participacao);

        AssertArgumento("alteradoEm",
            () => participacao.AlterarCota(Guid.NewGuid(), Reais(150m), Organizador, default(DateTimeOffset)));

        Assert.Equal(antes, Fotografar(participacao));
    }

    // ---------- Auxiliares ----------

    private static ValorMonetario Reais(decimal valor) => ValorMonetario.Criar(valor);

    /// <summary>Cria uma participação com um pagamento confirmado e/ou um pendente (valores zero são omitidos).</summary>
    private static Participacao ParticipacaoCom(decimal cota, decimal confirmado, decimal pendente)
    {
        var participacao = new Participacao(Guid.NewGuid(), Guid.NewGuid(), Participante, Reais(cota));

        if (confirmado > 0m)
        {
            var confirmadoId = Guid.NewGuid();
            participacao.DeclararPagamento(confirmadoId, Reais(confirmado), Participante, Declaracao);
            participacao.ConfirmarPagamento(confirmadoId, Organizador, Decisao);
        }

        if (pendente > 0m)
        {
            participacao.DeclararPagamento(Guid.NewGuid(), Reais(pendente), Participante, Declaracao);
        }

        return participacao;
    }

    private static void AssertTotais(
        Participacao participacao, decimal confirmado, decimal pendente, decimal comprometido, decimal disponivel, decimal restante)
    {
        Assert.Equal(Reais(confirmado), participacao.Confirmado);
        Assert.Equal(Reais(pendente), participacao.Pendente);
        Assert.Equal(Reais(comprometido), participacao.Comprometido);
        Assert.Equal(Reais(disponivel), participacao.Disponivel);
        Assert.Equal(Reais(restante), participacao.Restante);
    }

    private static void AssertArgumento(string parametro, Action acao)
    {
        var excecao = Assert.ThrowsAny<ArgumentException>(acao);
        Assert.Equal(parametro, excecao.ParamName);
    }

    private static string Texto(object? valor) => valor?.ToString() ?? "<null>";

    private static string DescreverPagamentos(Participacao participacao) =>
        string.Join(" | ", participacao.Pagamentos.Select(p =>
            $"{p.Id};{p.Valor};{p.Status};{p.DeclaradoPor};{p.DeclaradoEm:O};" +
            $"{Texto(p.DecididoPor)};{Texto(p.DecididoEm?.ToString("O"))};{Texto(p.Justificativa)}"));

    /// <summary>Estado completo da participação, comparável por valor; nulos são representados explicitamente.</summary>
    private sealed record Fotografia(
        ValorMonetario Cota,
        ValorMonetario Confirmado,
        ValorMonetario Pendente,
        ValorMonetario Comprometido,
        ValorMonetario Disponivel,
        ValorMonetario Restante,
        string Pagamentos,
        string AlteracoesCota);

    private static Fotografia Fotografar(Participacao participacao) => new(
        participacao.Cota,
        participacao.Confirmado,
        participacao.Pendente,
        participacao.Comprometido,
        participacao.Disponivel,
        participacao.Restante,
        DescreverPagamentos(participacao),
        string.Join(" | ", participacao.AlteracoesCota.Select(a =>
            $"{a.Id};{a.ValorAnterior};{a.NovoValor};{a.AlteradoPor};{a.AlteradoEm:O}")));
}
