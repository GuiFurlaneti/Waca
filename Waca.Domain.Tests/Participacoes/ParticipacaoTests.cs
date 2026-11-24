using System.Reflection;
using Waca.Domain.ObjetosDeValor;
using Waca.Domain.Participacoes;
using Xunit;

namespace Waca.Domain.Tests.Participacoes;

public class ParticipacaoTests
{
    private static readonly Guid Participante = Guid.NewGuid();
    private static readonly Guid Organizador = Guid.NewGuid();
    private static readonly DateTimeOffset Declaracao = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Decisao = new(2026, 11, 25, 9, 30, 0, TimeSpan.Zero);

    // ---------- Criação ----------

    [Fact]
    public void Criar_com_cota_positiva_inicia_sem_pagamentos()
    {
        var id = Guid.NewGuid();
        var vaquinhaId = Guid.NewGuid();

        var participacao = new Participacao(id, vaquinhaId, Participante, Reais(100m));

        Assert.Equal(id, participacao.Id);
        Assert.Equal(vaquinhaId, participacao.VaquinhaId);
        Assert.Equal(Participante, participacao.ParticipanteId);
        Assert.Equal(Reais(100m), participacao.Cota);
        Assert.Empty(participacao.Pagamentos);
        AssertTotais(participacao, confirmado: 0m, pendente: 0m, comprometido: 0m, disponivel: 100m, restante: 100m);
    }

    [Fact]
    public void Criar_rejeita_cota_zero_inclusive_valor_padrao()
    {
        foreach (var cota in new[] { ValorMonetario.Zero, default(ValorMonetario), ValorMonetario.Criar(0m) })
        {
            var excecao = Assert.Throws<ArgumentOutOfRangeException>(
                () => new Participacao(Guid.NewGuid(), Guid.NewGuid(), Participante, cota));

            Assert.Equal("cota", excecao.ParamName);
        }
    }

    [Fact]
    public void Criar_rejeita_identificadores_vazios()
    {
        AssertArgumento("id", () => new Participacao(Guid.Empty, Guid.NewGuid(), Participante, Reais(100m)));
        AssertArgumento("vaquinhaId", () => new Participacao(Guid.NewGuid(), Guid.Empty, Participante, Reais(100m)));
        AssertArgumento("participanteId", () => new Participacao(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Reais(100m)));
    }

    // ---------- Declaração ----------

    [Fact]
    public void Declarar_pagamento_parcial_fica_aguardando_e_compromete_a_cota()
    {
        var participacao = NovaParticipacao(100m);
        var pagamentoId = Guid.NewGuid();

        var pagamento = participacao.DeclararPagamento(pagamentoId, Reais(30m), Participante, Declaracao);

        Assert.Equal(pagamentoId, pagamento.Id);
        Assert.Equal(Reais(30m), pagamento.Valor);
        Assert.Equal(StatusPagamento.AguardandoConfirmacao, pagamento.Status);
        Assert.Null(pagamento.DecididoPor);
        Assert.Null(pagamento.DecididoEm);
        Assert.Null(pagamento.Justificativa);
        Assert.Same(pagamento, Assert.Single(participacao.Pagamentos));
        AssertTotais(participacao, confirmado: 0m, pendente: 30m, comprometido: 30m, disponivel: 70m, restante: 100m);
    }

    [Fact]
    public void Declarar_varios_pagamentos_parciais_acumula_o_comprometido()
    {
        var participacao = NovaParticipacao(100m);

        Declarar(participacao, 30m);
        Declarar(participacao, 50m);

        Assert.Equal(2, participacao.Pagamentos.Count);
        AssertTotais(participacao, confirmado: 0m, pendente: 80m, comprometido: 80m, disponivel: 20m, restante: 100m);
    }

    [Fact]
    public void Declarar_exatamente_o_disponivel_e_aceito()
    {
        var participacao = NovaParticipacao(100m);
        Declarar(participacao, 80m);

        Declarar(participacao, 20m);

        AssertTotais(participacao, confirmado: 0m, pendente: 100m, comprometido: 100m, disponivel: 0m, restante: 100m);
    }

    [Fact]
    public void Declarar_acima_do_disponivel_e_rejeitado_sem_alterar_o_estado()
    {
        var participacao = NovaParticipacao(100m);
        Declarar(participacao, 80m);
        var antes = Fotografar(participacao);

        Assert.Throws<InvalidOperationException>(
            () => participacao.DeclararPagamento(Guid.NewGuid(), Reais(20.01m), Participante, Declaracao));

        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Declarar_acima_da_cota_em_participacao_vazia_e_rejeitado()
    {
        var participacao = NovaParticipacao(100m);

        Assert.Throws<InvalidOperationException>(
            () => participacao.DeclararPagamento(Guid.NewGuid(), Reais(100.01m), Participante, Declaracao));

        Assert.Empty(participacao.Pagamentos);
    }

    [Fact]
    public void Declarar_pagamento_zero_e_rejeitado_sem_alterar_o_estado()
    {
        var participacao = NovaParticipacao(100m);
        Declarar(participacao, 10m);
        var antes = Fotografar(participacao);

        var excecao = Assert.Throws<ArgumentOutOfRangeException>(
            () => participacao.DeclararPagamento(Guid.NewGuid(), ValorMonetario.Zero, Participante, Declaracao));

        Assert.Equal("valor", excecao.ParamName);
        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Declarar_com_identificadores_vazios_e_rejeitado_sem_alterar_o_estado()
    {
        var participacao = NovaParticipacao(100m);
        var antes = Fotografar(participacao);

        AssertArgumento("pagamentoId",
            () => participacao.DeclararPagamento(Guid.Empty, Reais(10m), Participante, Declaracao));
        AssertArgumento("declaradoPor",
            () => participacao.DeclararPagamento(Guid.NewGuid(), Reais(10m), Guid.Empty, Declaracao));

        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Declarar_com_instante_nao_informado_e_rejeitado()
    {
        var participacao = NovaParticipacao(100m);

        AssertArgumento("declaradoEm",
            () => participacao.DeclararPagamento(Guid.NewGuid(), Reais(10m), Participante, default));

        Assert.Empty(participacao.Pagamentos);
    }

    [Fact]
    public void Declarar_com_identificador_duplicado_e_rejeitado_sem_alterar_o_estado()
    {
        var participacao = NovaParticipacao(100m);
        var pagamentoId = Declarar(participacao, 10m);
        var antes = Fotografar(participacao);

        Assert.Throws<InvalidOperationException>(
            () => participacao.DeclararPagamento(pagamentoId, Reais(5m), Participante, Declaracao));

        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Declarar_reutilizando_identificador_de_pagamento_rejeitado_e_rejeitado()
    {
        var participacao = NovaParticipacao(100m);
        var pagamentoId = Declarar(participacao, 40m);
        participacao.RejeitarPagamento(pagamentoId, Organizador, "Comprovante inválido", Decisao);
        var antes = Fotografar(participacao);

        // A cota foi liberada pela rejeição: a falha esperada é de unicidade, não de limite.
        Assert.Equal(Reais(100m), participacao.Disponivel);

        Assert.Throws<InvalidOperationException>(
            () => participacao.DeclararPagamento(pagamentoId, Reais(40m), Participante, Declaracao));

        var rejeitado = Assert.Single(participacao.Pagamentos);
        Assert.Equal(pagamentoId, rejeitado.Id);
        Assert.Equal(StatusPagamento.Rejeitado, rejeitado.Status);
        Assert.Equal("Comprovante inválido", rejeitado.Justificativa);
        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Declarar_registra_declarante_e_instante_em_utc()
    {
        var participacao = NovaParticipacao(100m);
        var horarioDeBrasilia = new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.FromHours(-3));

        var pagamento = participacao.DeclararPagamento(Guid.NewGuid(), Reais(10m), Participante, horarioDeBrasilia);

        Assert.Equal(Participante, pagamento.DeclaradoPor);
        Assert.Equal(TimeSpan.Zero, pagamento.DeclaradoEm.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero), pagamento.DeclaradoEm);
    }

    // ---------- Confirmação ----------

    [Fact]
    public void Confirmar_pagamento_pendente_move_o_valor_para_confirmado()
    {
        var participacao = NovaParticipacao(100m);
        var pagamentoId = Declarar(participacao, 30m);

        participacao.ConfirmarPagamento(pagamentoId, Organizador, Decisao);

        var pagamento = Assert.Single(participacao.Pagamentos);
        Assert.Equal(StatusPagamento.Confirmado, pagamento.Status);
        Assert.Equal(Organizador, pagamento.DecididoPor);
        Assert.Equal(Decisao, pagamento.DecididoEm);
        Assert.Null(pagamento.Justificativa);
        AssertTotais(participacao, confirmado: 30m, pendente: 0m, comprometido: 30m, disponivel: 70m, restante: 70m);
    }

    [Fact]
    public void Confirmar_duas_vezes_e_rejeitado_sem_alterar_o_estado()
    {
        var participacao = NovaParticipacao(100m);
        var pagamentoId = Declarar(participacao, 30m);
        participacao.ConfirmarPagamento(pagamentoId, Organizador, Decisao);
        var antes = Fotografar(participacao);

        Assert.Throws<InvalidOperationException>(
            () => participacao.ConfirmarPagamento(pagamentoId, Guid.NewGuid(), Decisao.AddHours(1)));

        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Confirmar_pagamento_rejeitado_e_rejeitado_sem_alterar_o_estado()
    {
        var participacao = NovaParticipacao(100m);
        var pagamentoId = Declarar(participacao, 30m);
        participacao.RejeitarPagamento(pagamentoId, Organizador, "Comprovante ilegível", Decisao);
        var antes = Fotografar(participacao);

        Assert.Throws<InvalidOperationException>(
            () => participacao.ConfirmarPagamento(pagamentoId, Organizador, Decisao));

        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Confirmar_pagamento_inexistente_ou_com_identificador_vazio_e_rejeitado()
    {
        var participacao = NovaParticipacao(100m);
        Declarar(participacao, 30m);
        var antes = Fotografar(participacao);

        Assert.Throws<InvalidOperationException>(
            () => participacao.ConfirmarPagamento(Guid.NewGuid(), Organizador, Decisao));
        AssertArgumento("pagamentoId",
            () => participacao.ConfirmarPagamento(Guid.Empty, Organizador, Decisao));

        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Confirmar_com_decisor_ou_instante_invalidos_mantem_o_pagamento_pendente()
    {
        var participacao = NovaParticipacao(100m);
        var pagamentoId = Declarar(participacao, 30m);
        var antes = Fotografar(participacao);

        AssertArgumento("decididoPor", () => participacao.ConfirmarPagamento(pagamentoId, Guid.Empty, Decisao));
        AssertArgumento("decididoEm", () => participacao.ConfirmarPagamento(pagamentoId, Organizador, default));

        Assert.Equal(antes, Fotografar(participacao));
    }

    // ---------- Rejeição ----------

    [Fact]
    public void Rejeitar_com_justificativa_libera_a_cota_e_preserva_o_historico()
    {
        var participacao = NovaParticipacao(100m);
        Declarar(participacao, 50m);
        var rejeitadoId = Declarar(participacao, 50m);

        participacao.RejeitarPagamento(rejeitadoId, Organizador, "  Valor não recebido  ", Decisao);

        var rejeitado = participacao.Pagamentos.Single(p => p.Id == rejeitadoId);
        Assert.Equal(StatusPagamento.Rejeitado, rejeitado.Status);
        Assert.Equal("Valor não recebido", rejeitado.Justificativa);
        Assert.Equal(Reais(50m), rejeitado.Valor);
        Assert.Equal(2, participacao.Pagamentos.Count);
        AssertTotais(participacao, confirmado: 0m, pendente: 50m, comprometido: 50m, disponivel: 50m, restante: 100m);

        Declarar(participacao, 50m);

        Assert.Equal(3, participacao.Pagamentos.Count);
        AssertTotais(participacao, confirmado: 0m, pendente: 100m, comprometido: 100m, disponivel: 0m, restante: 100m);
    }

    [Fact]
    public void Rejeitar_com_decisor_vazio_mantem_o_pagamento_pendente_e_sem_decisao()
    {
        var participacao = NovaParticipacao(100m);
        var pagamentoId = Declarar(participacao, 30m);
        var antes = Fotografar(participacao);

        AssertArgumento("decididoPor",
            () => participacao.RejeitarPagamento(pagamentoId, Guid.Empty, "Valor não recebido", Decisao));

        AssertPendenteSemDecisao(participacao, pagamentoId);
        Assert.Equal(Reais(30m), participacao.Comprometido);
        Assert.Equal(Reais(70m), participacao.Disponivel);
        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Rejeitar_com_instante_nao_informado_mantem_o_pagamento_pendente_e_sem_decisao()
    {
        var participacao = NovaParticipacao(100m);
        var pagamentoId = Declarar(participacao, 30m);
        var antes = Fotografar(participacao);

        AssertArgumento("decididoEm",
            () => participacao.RejeitarPagamento(pagamentoId, Organizador, "Valor não recebido", default(DateTimeOffset)));

        AssertPendenteSemDecisao(participacao, pagamentoId);
        AssertTotais(participacao, confirmado: 0m, pendente: 30m, comprometido: 30m, disponivel: 70m, restante: 100m);
        Assert.Equal(antes, Fotografar(participacao));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejeitar_sem_justificativa_mantem_o_pagamento_pendente(string? justificativa)
    {
        var participacao = NovaParticipacao(100m);
        var pagamentoId = Declarar(participacao, 30m);
        var antes = Fotografar(participacao);

        AssertArgumento("justificativa",
            () => participacao.RejeitarPagamento(pagamentoId, Organizador, justificativa!, Decisao));

        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Rejeitar_pagamento_confirmado_e_rejeitado_sem_alterar_o_estado()
    {
        var participacao = NovaParticipacao(100m);
        var pagamentoId = Declarar(participacao, 30m);
        participacao.ConfirmarPagamento(pagamentoId, Organizador, Decisao);
        var antes = Fotografar(participacao);

        Assert.Throws<InvalidOperationException>(
            () => participacao.RejeitarPagamento(pagamentoId, Organizador, "Engano", Decisao));

        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Rejeitar_pagamento_inexistente_e_rejeitado()
    {
        var participacao = NovaParticipacao(100m);
        Declarar(participacao, 30m);
        var antes = Fotografar(participacao);

        Assert.Throws<InvalidOperationException>(
            () => participacao.RejeitarPagamento(Guid.NewGuid(), Organizador, "Motivo", Decisao));

        Assert.Equal(antes, Fotografar(participacao));
    }

    [Fact]
    public void Rejeitar_registra_decisor_e_instante_em_utc()
    {
        var participacao = NovaParticipacao(100m);
        var pagamentoId = Declarar(participacao, 30m);
        var decisaoEmBrasilia = new DateTimeOffset(2026, 11, 25, 6, 30, 0, TimeSpan.FromHours(-3));

        participacao.RejeitarPagamento(pagamentoId, Organizador, "Duplicado", decisaoEmBrasilia);

        var pagamento = Assert.Single(participacao.Pagamentos);
        Assert.Equal(Organizador, pagamento.DecididoPor);
        Assert.Equal(TimeSpan.Zero, pagamento.DecididoEm!.Value.Offset);
        Assert.Equal(Decisao, pagamento.DecididoEm);
        Assert.Equal(Participante, pagamento.DeclaradoPor);
        Assert.Equal(Declaracao, pagamento.DeclaradoEm);
    }

    // ---------- Totais e rastreabilidade ----------

    [Fact]
    public void Totais_com_pagamentos_em_varios_estados_sao_coerentes_e_sem_dupla_contagem()
    {
        var participacao = NovaParticipacao(100m);
        var confirmado1 = Declarar(participacao, 10m);
        Declarar(participacao, 20m);
        var rejeitado = Declarar(participacao, 30m);
        var confirmado2 = Declarar(participacao, 15m);

        participacao.ConfirmarPagamento(confirmado1, Organizador, Decisao);
        participacao.RejeitarPagamento(rejeitado, Organizador, "Não identificado", Decisao);
        participacao.ConfirmarPagamento(confirmado2, Organizador, Decisao);

        AssertTotais(participacao, confirmado: 25m, pendente: 20m, comprometido: 45m, disponivel: 55m, restante: 75m);

        var somaNaoRejeitados = participacao.Pagamentos
            .Where(p => p.Status != StatusPagamento.Rejeitado)
            .Sum(p => p.Valor.Valor);
        Assert.Equal(participacao.Comprometido.Valor, somaNaoRejeitados);
        Assert.Equal(4, participacao.Pagamentos.Count);
    }

    [Fact]
    public void Organizador_declara_e_confirma_o_proprio_pagamento_com_rastreabilidade()
    {
        var participacaoDoOrganizador = new Participacao(Guid.NewGuid(), Guid.NewGuid(), Organizador, Reais(200m));
        var pagamentoId = Guid.NewGuid();

        participacaoDoOrganizador.DeclararPagamento(pagamentoId, Reais(200m), Organizador, Declaracao);
        participacaoDoOrganizador.ConfirmarPagamento(pagamentoId, Organizador, Decisao);

        var pagamento = Assert.Single(participacaoDoOrganizador.Pagamentos);
        Assert.Equal(StatusPagamento.Confirmado, pagamento.Status);
        Assert.Equal(Organizador, pagamento.DeclaradoPor);
        Assert.Equal(Declaracao, pagamento.DeclaradoEm);
        Assert.Equal(Organizador, pagamento.DecididoPor);
        Assert.Equal(Decisao, pagamento.DecididoEm);
        AssertTotais(participacaoDoOrganizador, confirmado: 200m, pendente: 0m, comprometido: 200m, disponivel: 0m, restante: 0m);
    }

    // ---------- Encapsulamento ----------

    [Fact]
    public void Colecao_de_pagamentos_nao_pode_ser_modificada_externamente()
    {
        var participacao = NovaParticipacao(100m);
        Declarar(participacao, 10m);

        Assert.False(participacao.Pagamentos is List<Pagamento>);
        var comoLista = Assert.IsAssignableFrom<IList<Pagamento>>(participacao.Pagamentos);
        Assert.True(comoLista.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => comoLista.Clear());
        Assert.Throws<NotSupportedException>(() => comoLista.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => comoLista[0] = comoLista[0]);

        Assert.Single(participacao.Pagamentos);
        AssertTotais(participacao, confirmado: 0m, pendente: 10m, comprometido: 10m, disponivel: 90m, restante: 100m);
    }

    [Fact]
    public void Pagamento_nao_expoe_construtor_transicoes_nem_setters_publicos()
    {
        var tipo = typeof(Pagamento);

        Assert.Empty(tipo.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(tipo.GetMethod("Confirmar", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(tipo.GetMethod("Rejeitar", BindingFlags.Public | BindingFlags.Instance));
        Assert.All(
            tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            propriedade => Assert.Null(propriedade.GetSetMethod(nonPublic: false)));
        Assert.All(
            typeof(Participacao).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            propriedade => Assert.Null(propriedade.GetSetMethod(nonPublic: false)));
    }

    // ---------- Auxiliares ----------

    private static ValorMonetario Reais(decimal valor) => ValorMonetario.Criar(valor);

    private static Participacao NovaParticipacao(decimal cota) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Participante, Reais(cota));

    private static Guid Declarar(Participacao participacao, decimal valor)
    {
        var pagamentoId = Guid.NewGuid();
        participacao.DeclararPagamento(pagamentoId, Reais(valor), Participante, Declaracao);
        return pagamentoId;
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

    private static void AssertPendenteSemDecisao(Participacao participacao, Guid pagamentoId)
    {
        var pagamento = participacao.Pagamentos.Single(p => p.Id == pagamentoId);
        Assert.Equal(StatusPagamento.AguardandoConfirmacao, pagamento.Status);
        Assert.Null(pagamento.DecididoPor);
        Assert.Null(pagamento.DecididoEm);
        Assert.Null(pagamento.Justificativa);
    }

    private static void AssertArgumento(string parametro, Action acao)
    {
        var excecao = Assert.ThrowsAny<ArgumentException>(acao);
        Assert.Equal(parametro, excecao.ParamName);
    }

    /// <summary>Estado financeiro e histórico completos, comparáveis por valor.</summary>
    private sealed record Fotografia(
        ValorMonetario Cota,
        ValorMonetario Confirmado,
        ValorMonetario Pendente,
        ValorMonetario Comprometido,
        ValorMonetario Disponivel,
        ValorMonetario Restante,
        string Pagamentos);

    private static Fotografia Fotografar(Participacao participacao) => new(
        participacao.Cota,
        participacao.Confirmado,
        participacao.Pendente,
        participacao.Comprometido,
        participacao.Disponivel,
        participacao.Restante,
        string.Join(" | ", participacao.Pagamentos.Select(p =>
            $"{p.Id};{p.Valor};{p.Status};{p.DeclaradoPor};{p.DeclaradoEm:O};{p.DecididoPor};{p.DecididoEm:O};{p.Justificativa}")));
}
