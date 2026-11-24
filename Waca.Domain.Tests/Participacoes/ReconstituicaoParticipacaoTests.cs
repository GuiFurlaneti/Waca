using System.Collections;
using Waca.Domain.ObjetosDeValor;
using Waca.Domain.Participacoes;
using Xunit;

namespace Waca.Domain.Tests.Participacoes;

public class ReconstituicaoParticipacaoTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly Guid VaquinhaId = Guid.NewGuid();
    private static readonly Guid Participante = Guid.NewGuid();
    private static readonly Guid Organizador = Guid.NewGuid();
    private static readonly DateTimeOffset Declaracao = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Decisao = new(2026, 11, 25, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset InstanteAlteracao = new(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);

    // ---------- Estados válidos ----------

    [Fact]
    public void Sem_pagamentos_e_sem_alteracoes_preserva_identificadores_e_cota()
    {
        var participacao = Participacao.Reconstituir(Id, VaquinhaId, Participante, Reais(150m), [], []);

        Assert.Equal(Id, participacao.Id);
        Assert.Equal(VaquinhaId, participacao.VaquinhaId);
        Assert.Equal(Participante, participacao.ParticipanteId);
        Assert.Equal(Reais(150m), participacao.Cota);
        Assert.Empty(participacao.Pagamentos);
        Assert.Empty(participacao.AlteracoesCota);
        AssertTotais(participacao, confirmado: 0m, pendente: 0m, comprometido: 0m, disponivel: 150m, restante: 150m);
    }

    [Fact]
    public void Pagamento_pendente_e_preservado_sem_dados_de_decisao()
    {
        var estado = Pendente(30m);

        var participacao = Reconstituir(100m, [estado]);

        var pagamento = Assert.Single(participacao.Pagamentos);
        AssertPagamento(estado, pagamento);
        Assert.Equal(StatusPagamento.AguardandoConfirmacao, pagamento.Status);
        Assert.Null(pagamento.DecididoPor);
        Assert.Null(pagamento.DecididoEm);
        Assert.Null(pagamento.Justificativa);
        AssertTotais(participacao, confirmado: 0m, pendente: 30m, comprometido: 30m, disponivel: 70m, restante: 100m);
    }

    [Fact]
    public void Pagamento_confirmado_e_preservado_com_decisor_e_instante()
    {
        var estado = Confirmado(60m);

        var participacao = Reconstituir(100m, [estado]);

        var pagamento = Assert.Single(participacao.Pagamentos);
        AssertPagamento(estado, pagamento);
        Assert.Equal(StatusPagamento.Confirmado, pagamento.Status);
        Assert.Equal(Organizador, pagamento.DecididoPor);
        Assert.Equal(Decisao, pagamento.DecididoEm);
        Assert.Null(pagamento.Justificativa);
        AssertTotais(participacao, confirmado: 60m, pendente: 0m, comprometido: 60m, disponivel: 40m, restante: 40m);
    }

    [Fact]
    public void Pagamento_rejeitado_e_preservado_com_justificativa_e_nao_ocupa_a_cota()
    {
        var estado = Rejeitado(40m, "Comprovante ilegível");

        var participacao = Reconstituir(100m, [estado]);

        var pagamento = Assert.Single(participacao.Pagamentos);
        AssertPagamento(estado, pagamento);
        Assert.Equal(StatusPagamento.Rejeitado, pagamento.Status);
        Assert.Equal(Organizador, pagamento.DecididoPor);
        Assert.Equal(Decisao, pagamento.DecididoEm);
        Assert.Equal("Comprovante ilegível", pagamento.Justificativa);
        AssertTotais(participacao, confirmado: 0m, pendente: 0m, comprometido: 0m, disponivel: 100m, restante: 100m);
    }

    [Fact]
    public void Varios_pagamentos_em_estados_diferentes_preservam_a_ordem_e_derivam_os_totais()
    {
        // Bruno T0: cota 180, 60 confirmados, 80 em análise e uma declaração de 40 recusada.
        EstadoPagamento[] estados = [Confirmado(60m), Rejeitado(40m, "Valor divergente"), Pendente(80m)];

        var participacao = Reconstituir(180m, estados);

        Assert.Equal(estados.Length, participacao.Pagamentos.Count);
        for (var i = 0; i < estados.Length; i++)
        {
            AssertPagamento(estados[i], participacao.Pagamentos[i]);
        }

        AssertTotais(participacao, confirmado: 60m, pendente: 80m, comprometido: 140m, disponivel: 40m, restante: 120m);
    }

    [Fact]
    public void Uma_alteracao_de_cota_e_preservada()
    {
        var estado = Mudanca(100m, 150m);

        var participacao = Reconstituir(150m, alteracoes: [estado]);

        var alteracao = Assert.Single(participacao.AlteracoesCota);
        AssertAlteracao(estado, alteracao);
        Assert.Equal(Reais(150m), participacao.Cota);
    }

    [Fact]
    public void Historico_encadeado_de_varias_alteracoes_e_preservado_na_ordem()
    {
        EstadoAlteracaoCota[] estados = [Mudanca(100m, 150m), Mudanca(150m, 120m), Mudanca(120m, 180m)];

        var participacao = Reconstituir(180m, [Confirmado(60m), Pendente(80m)], estados);

        Assert.Equal(estados.Length, participacao.AlteracoesCota.Count);
        for (var i = 0; i < estados.Length; i++)
        {
            AssertAlteracao(estados[i], participacao.AlteracoesCota[i]);
        }

        AssertTotais(participacao, confirmado: 60m, pendente: 80m, comprometido: 140m, disponivel: 40m, restante: 120m);
    }

    [Fact]
    public void Comprometido_igual_a_cota_e_aceito()
    {
        var participacao = Reconstituir(100m, [Confirmado(60m), Pendente(40m)]);

        AssertTotais(participacao, confirmado: 60m, pendente: 40m, comprometido: 100m, disponivel: 0m, restante: 40m);
    }

    [Fact]
    public void Rejeitados_nao_contam_para_o_limite_da_cota()
    {
        var participacao = Reconstituir(100m, [Confirmado(60m), Rejeitado(90m), Pendente(40m), Rejeitado(100m)]);

        AssertTotais(participacao, confirmado: 60m, pendente: 40m, comprometido: 100m, disponivel: 0m, restante: 40m);
    }

    [Fact]
    public void Instantes_com_offset_sao_convertidos_para_UTC_preservando_o_momento()
    {
        var fuso = TimeSpan.FromHours(-3);
        var declaradoEm = new DateTimeOffset(2026, 11, 24, 9, 0, 0, fuso);
        var decididoEm = new DateTimeOffset(2026, 11, 24, 18, 15, 0, fuso);
        var alteradoEm = new DateTimeOffset(2026, 11, 24, 21, 30, 0, fuso);
        var pagamentoEstado = Confirmado(60m) with { DeclaradoEm = declaradoEm, DecididoEm = decididoEm };
        var alteracaoEstado = Mudanca(100m, 120m) with { AlteradoEm = alteradoEm };

        var participacao = Reconstituir(120m, [pagamentoEstado], [alteracaoEstado]);

        var pagamento = Assert.Single(participacao.Pagamentos);
        Assert.Equal(TimeSpan.Zero, pagamento.DeclaradoEm.Offset);
        Assert.Equal(declaradoEm.UtcDateTime, pagamento.DeclaradoEm.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, pagamento.DecididoEm!.Value.Offset);
        Assert.Equal(decididoEm.UtcDateTime, pagamento.DecididoEm.Value.UtcDateTime);

        var alteracao = Assert.Single(participacao.AlteracoesCota);
        Assert.Equal(TimeSpan.Zero, alteracao.AlteradoEm.Offset);
        Assert.Equal(alteradoEm.UtcDateTime, alteracao.AlteradoEm.UtcDateTime);
    }

    [Fact]
    public void Ordem_recebida_e_preservada_sem_exigir_ordem_cronologica()
    {
        // Instantes fora de ordem, e decisão anterior à declaração: nenhuma regra cronológica foi estabelecida.
        var tardio = Pendente(10m) with { DeclaradoEm = Declaracao.AddDays(5) };
        var antigo = Confirmado(20m) with { DeclaradoEm = Declaracao, DecididoEm = Declaracao.AddDays(-1) };
        var segundaAlteracao = Mudanca(150m, 120m) with { AlteradoEm = InstanteAlteracao.AddDays(-10) };
        var primeiraAlteracao = Mudanca(100m, 150m) with { AlteradoEm = InstanteAlteracao };

        var participacao = Reconstituir(120m, [tardio, antigo], [primeiraAlteracao, segundaAlteracao]);

        Assert.Equal([tardio.Id, antigo.Id], participacao.Pagamentos.Select(p => p.Id));
        Assert.Equal([primeiraAlteracao.Id, segundaAlteracao.Id], participacao.AlteracoesCota.Select(a => a.Id));
        AssertPagamento(antigo, participacao.Pagamentos[1]);
        AssertAlteracao(segundaAlteracao, participacao.AlteracoesCota[1]);
    }

    [Fact]
    public void Ida_e_volta_a_partir_de_participacao_operada_preserva_todos_os_dados()
    {
        var original = new Participacao(Id, VaquinhaId, Participante, Reais(100m));
        original.AlterarCota(Guid.NewGuid(), Reais(180m), Organizador, InstanteAlteracao);
        var confirmado = original.DeclararPagamento(Guid.NewGuid(), Reais(60m), Participante, Declaracao).Id;
        original.ConfirmarPagamento(confirmado, Organizador, Decisao);
        var rejeitado = original.DeclararPagamento(Guid.NewGuid(), Reais(50m), Participante, Declaracao.AddHours(1)).Id;
        original.RejeitarPagamento(rejeitado, Organizador, "  Valor divergente  ", Decisao.AddHours(1));
        original.DeclararPagamento(Guid.NewGuid(), Reais(80m), Participante, Declaracao.AddHours(2));
        Assert.False(original.AlterarCota(Guid.NewGuid(), Reais(180.00m), Organizador, InstanteAlteracao.AddHours(1)));
        original.AlterarCota(Guid.NewGuid(), Reais(150m), Organizador, InstanteAlteracao.AddHours(2));

        var reconstituida = Participacao.Reconstituir(
            original.Id,
            original.VaquinhaId,
            original.ParticipanteId,
            original.Cota,
            original.Pagamentos.Select(EstadoDe),
            original.AlteracoesCota.Select(EstadoDe));

        Assert.Equal(original.Id, reconstituida.Id);
        Assert.Equal(original.VaquinhaId, reconstituida.VaquinhaId);
        Assert.Equal(original.ParticipanteId, reconstituida.ParticipanteId);
        Assert.Equal(original.Cota, reconstituida.Cota);
        Assert.Equal(original.Pagamentos.Select(EstadoDe), reconstituida.Pagamentos.Select(EstadoDe));
        Assert.Equal(original.AlteracoesCota.Select(EstadoDe), reconstituida.AlteracoesCota.Select(EstadoDe));
        Assert.Equal(2, reconstituida.AlteracoesCota.Count);
        AssertTotais(
            reconstituida,
            original.Confirmado.Valor,
            original.Pendente.Valor,
            original.Comprometido.Valor,
            original.Disponivel.Valor,
            original.Restante.Valor);
    }

    [Fact]
    public void Reconstituicao_nao_cria_registros_alem_dos_informados()
    {
        var participacao = Reconstituir(180m, [Pendente(80m)], [Mudanca(100m, 180m)]);

        Assert.Single(participacao.Pagamentos);
        Assert.Single(participacao.AlteracoesCota);
    }

    // ---------- Coleções ----------

    [Fact]
    public void Alterar_as_colecoes_informadas_depois_nao_afeta_a_participacao()
    {
        var confirmado = Confirmado(60m);
        var pendente = Pendente(80m);
        var alteracao = Mudanca(100m, 180m);
        var pagamentos = new List<EstadoPagamento> { confirmado, pendente };
        var alteracoes = new EstadoAlteracaoCota[] { alteracao };

        var participacao = Participacao.Reconstituir(Id, VaquinhaId, Participante, Reais(180m), pagamentos, alteracoes);
        pagamentos.Add(Pendente(40m));
        pagamentos.RemoveAt(0);
        alteracoes[0] = Mudanca(100m, 999m);

        Assert.Equal([confirmado.Id, pendente.Id], participacao.Pagamentos.Select(p => p.Id));
        AssertAlteracao(alteracao, Assert.Single(participacao.AlteracoesCota));
        AssertTotais(participacao, confirmado: 60m, pendente: 80m, comprometido: 140m, disponivel: 40m, restante: 120m);
    }

    [Fact]
    public void Colecoes_expostas_apos_reconstituir_sao_somente_leitura()
    {
        var participacao = Reconstituir(180m, [Pendente(80m)], [Mudanca(100m, 180m)]);

        Assert.Throws<NotSupportedException>(() => ((ICollection<Pagamento>)participacao.Pagamentos).Clear());
        Assert.Throws<NotSupportedException>(() => ((ICollection<AlteracaoCota>)participacao.AlteracoesCota).Clear());
    }

    [Fact]
    public void Colecoes_informadas_sao_lidas_uma_unica_vez()
    {
        var pagamentos = new EnumeravelContado<EstadoPagamento>([Confirmado(60m), Pendente(80m)]);
        var alteracoes = new EnumeravelContado<EstadoAlteracaoCota>([Mudanca(100m, 180m)]);

        var participacao = Participacao.Reconstituir(Id, VaquinhaId, Participante, Reais(180m), pagamentos, alteracoes);

        Assert.Equal(1, pagamentos.Enumeracoes);
        Assert.Equal(1, alteracoes.Enumeracoes);
        Assert.Equal(2, participacao.Pagamentos.Count);
    }

    // ---------- Operações após a reconstituição ----------

    [Fact]
    public void Declarar_apos_reconstituir_respeita_o_disponivel()
    {
        var participacao = BrunoT0();

        Assert.Throws<InvalidOperationException>(
            () => participacao.DeclararPagamento(Guid.NewGuid(), Reais(40.01m), Participante, Declaracao));
        AssertTotais(participacao, confirmado: 60m, pendente: 80m, comprometido: 140m, disponivel: 40m, restante: 120m);

        participacao.DeclararPagamento(Guid.NewGuid(), Reais(40m), Participante, Declaracao);

        Assert.Equal(4, participacao.Pagamentos.Count);
        AssertTotais(participacao, confirmado: 60m, pendente: 120m, comprometido: 180m, disponivel: 0m, restante: 120m);
    }

    [Fact]
    public void Confirmar_e_rejeitar_pendentes_reconstituidos_funcionam()
    {
        var primeiro = Pendente(30m);
        var segundo = Pendente(50m);
        var participacao = Reconstituir(100m, [primeiro, segundo]);

        participacao.ConfirmarPagamento(primeiro.Id, Organizador, Decisao);
        participacao.RejeitarPagamento(segundo.Id, Organizador, "Sem comprovação", Decisao);

        Assert.Equal(StatusPagamento.Confirmado, participacao.Pagamentos[0].Status);
        Assert.Equal(StatusPagamento.Rejeitado, participacao.Pagamentos[1].Status);
        AssertTotais(participacao, confirmado: 30m, pendente: 0m, comprometido: 30m, disponivel: 70m, restante: 70m);
    }

    [Fact]
    public void Pagamentos_ja_decididos_continuam_sem_nova_decisao_apos_reconstituir()
    {
        var confirmado = Confirmado(60m);
        var rejeitado = Rejeitado(40m);
        var participacao = Reconstituir(100m, [confirmado, rejeitado]);

        Assert.Throws<InvalidOperationException>(() => participacao.ConfirmarPagamento(confirmado.Id, Organizador, Decisao));
        Assert.Throws<InvalidOperationException>(() => participacao.RejeitarPagamento(confirmado.Id, Organizador, "x", Decisao));
        Assert.Throws<InvalidOperationException>(() => participacao.ConfirmarPagamento(rejeitado.Id, Organizador, Decisao));
        Assert.Throws<InvalidOperationException>(() => participacao.RejeitarPagamento(rejeitado.Id, Organizador, "x", Decisao));
        AssertPagamento(confirmado, participacao.Pagamentos[0]);
        AssertPagamento(rejeitado, participacao.Pagamentos[1]);
    }

    [Fact]
    public void Identificadores_reconstituidos_continuam_reservados()
    {
        var rejeitado = Rejeitado(40m);
        var alteracao = Mudanca(100m, 180m);
        var participacao = Reconstituir(180m, [rejeitado], [alteracao]);

        Assert.Throws<InvalidOperationException>(
            () => participacao.DeclararPagamento(rejeitado.Id, Reais(10m), Participante, Declaracao));
        Assert.Throws<InvalidOperationException>(
            () => participacao.AlterarCota(alteracao.Id, Reais(200m), Organizador, InstanteAlteracao));
        Assert.Single(participacao.Pagamentos);
        Assert.Single(participacao.AlteracoesCota);
    }

    [Fact]
    public void Alterar_cota_apos_reconstituir_respeita_o_comprometido_e_continua_o_historico()
    {
        var anterior = Mudanca(100m, 180m);
        var participacao = Participacao.Reconstituir(
            Id, VaquinhaId, Participante, Reais(180m), [Confirmado(60m), Pendente(80m)], [anterior]);

        Assert.Throws<InvalidOperationException>(
            () => participacao.AlterarCota(Guid.NewGuid(), Reais(139.99m), Organizador, InstanteAlteracao));
        Assert.False(participacao.AlterarCota(Guid.NewGuid(), Reais(180.000m), Organizador, InstanteAlteracao));
        Assert.True(participacao.AlterarCota(Guid.NewGuid(), Reais(140m), Organizador, InstanteAlteracao));

        Assert.Equal(2, participacao.AlteracoesCota.Count);
        AssertAlteracao(anterior, participacao.AlteracoesCota[0]);
        Assert.Equal(Reais(180m), participacao.AlteracoesCota[1].ValorAnterior);
        Assert.Equal(Reais(140m), participacao.AlteracoesCota[1].NovoValor);
        AssertTotais(participacao, confirmado: 60m, pendente: 80m, comprometido: 140m, disponivel: 0m, restante: 80m);
    }

    // ---------- 1. Identificadores vazios ----------

    [Fact]
    public void Rejeita_identificadores_vazios_da_participacao()
    {
        AssertArgumento("id", () => Participacao.Reconstituir(Guid.Empty, VaquinhaId, Participante, Reais(100m), [], []));
        AssertArgumento("vaquinhaId", () => Participacao.Reconstituir(Id, Guid.Empty, Participante, Reais(100m), [], []));
        AssertArgumento("participanteId", () => Participacao.Reconstituir(Id, VaquinhaId, Guid.Empty, Reais(100m), [], []));
    }

    [Fact]
    public void Rejeita_identificadores_vazios_dos_pagamentos()
    {
        AssertPagamentoInvalido(Pendente(10m) with { Id = Guid.Empty }, "pagamentoId");
        AssertPagamentoInvalido(Pendente(10m) with { DeclaradoPor = Guid.Empty }, "declaradoPor");
        AssertPagamentoInvalido(Confirmado(10m) with { DecididoPor = Guid.Empty }, "decididoPor");
        AssertPagamentoInvalido(Rejeitado(10m) with { DecididoPor = Guid.Empty }, "decididoPor");
    }

    [Fact]
    public void Rejeita_identificadores_vazios_das_alteracoes()
    {
        AssertAlteracaoInvalida(Mudanca(50m, 100m) with { Id = Guid.Empty }, "alteracaoId");
        AssertAlteracaoInvalida(Mudanca(50m, 100m) with { AlteradoPor = Guid.Empty }, "alteradoPor");
    }

    [Fact]
    public void Rejeita_instantes_nao_informados()
    {
        AssertPagamentoInvalido(Pendente(10m) with { DeclaradoEm = default }, "declaradoEm");
        AssertPagamentoInvalido(Confirmado(10m) with { DecididoEm = default(DateTimeOffset) }, "decididoEm");
        AssertPagamentoInvalido(Rejeitado(10m) with { DecididoEm = default(DateTimeOffset) }, "decididoEm");
        AssertAlteracaoInvalida(Mudanca(50m, 100m) with { AlteradoEm = default }, "alteradoEm");
    }

    // ---------- 2 e 3. Cota zero e valores monetários ----------

    [Fact]
    public void Rejeita_cota_zero_inclusive_valor_padrao()
    {
        foreach (var cota in new[] { ValorMonetario.Zero, default(ValorMonetario) })
        {
            var excecao = Assert.Throws<ArgumentOutOfRangeException>(
                () => Participacao.Reconstituir(Id, VaquinhaId, Participante, cota, [], []));

            Assert.Equal("cota", excecao.ParamName);
        }
    }

    [Fact]
    public void Rejeita_pagamento_de_valor_zero_inclusive_valor_padrao()
    {
        AssertPagamentoInvalido(Pendente(10m) with { Valor = ValorMonetario.Zero }, "valor");
        AssertPagamentoInvalido(Confirmado(10m) with { Valor = default }, "valor");
    }

    [Fact]
    public void Valores_negativos_ou_com_fracao_de_centavo_nao_sao_representaveis_no_estado()
    {
        // O estado transporta ValorMonetario: o adaptador precisa criá-lo por ValorMonetario.Criar,
        // que rejeita esses valores antes de qualquer reconstituição.
        Assert.Throws<ArgumentOutOfRangeException>(() => Pendente(10m) with { Valor = ValorMonetario.Criar(-0.01m) });
        Assert.Throws<ArgumentException>(() => Pendente(10m) with { Valor = ValorMonetario.Criar(10.001m) });
        Assert.Throws<ArgumentException>(() => Mudanca(50m, 100m) with { NovoValor = ValorMonetario.Criar(100.005m) });
    }

    // ---------- 4 e 5. Identificadores duplicados ----------

    [Fact]
    public void Rejeita_pagamentos_com_identificador_duplicado()
    {
        var primeiro = Confirmado(10m);
        var repetido = Pendente(20m) with { Id = primeiro.Id };

        var excecao = AssertInvalido("pagamentos", () => Reconstituir(100m, [primeiro, repetido]));

        Assert.Contains(primeiro.Id.ToString(), excecao.Message);
    }

    [Fact]
    public void Rejeita_alteracoes_com_identificador_duplicado()
    {
        var primeira = Mudanca(100m, 150m);
        var repetida = Mudanca(150m, 120m) with { Id = primeira.Id };

        var excecao = AssertInvalido("alteracoesCota", () => Reconstituir(120m, alteracoes: [primeira, repetida]));

        Assert.Contains(primeira.Id.ToString(), excecao.Message);
    }

    // ---------- 6. Estado não reconhecido ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(99)]
    public void Rejeita_pagamento_em_estado_nao_reconhecido(int status)
    {
        var excecao = AssertInvalido(
            "pagamentos", () => Reconstituir(100m, [Pendente(10m) with { Status = (StatusPagamento)status }]));

        Assert.IsType<ArgumentOutOfRangeException>(excecao.InnerException);
    }

    // ---------- 7. Pendente com decisão ----------

    [Fact]
    public void Rejeita_pendente_com_qualquer_dado_de_decisao()
    {
        AssertPagamentoInvalido(Pendente(10m) with { DecididoPor = Organizador }, "estado");
        AssertPagamentoInvalido(Pendente(10m) with { DecididoEm = Decisao }, "estado");
        AssertPagamentoInvalido(Pendente(10m) with { Justificativa = "Motivo" }, "estado");
        AssertPagamentoInvalido(Pendente(10m) with { Justificativa = "" }, "estado");
    }

    // ---------- 8. Confirmado incompleto ----------

    [Fact]
    public void Rejeita_confirmado_sem_decisor_ou_instante_ou_com_justificativa()
    {
        AssertPagamentoInvalido(Confirmado(10m) with { DecididoPor = null }, "estado");
        AssertPagamentoInvalido(Confirmado(10m) with { DecididoEm = null }, "estado");
        AssertPagamentoInvalido(Confirmado(10m) with { Justificativa = "Motivo" }, "estado");
    }

    // ---------- 9. Rejeitado incompleto ----------

    [Fact]
    public void Rejeita_rejeitado_sem_decisor_instante_ou_justificativa_valida()
    {
        AssertPagamentoInvalido(Rejeitado(10m) with { DecididoPor = null }, "estado");
        AssertPagamentoInvalido(Rejeitado(10m) with { DecididoEm = null }, "estado");
        AssertPagamentoInvalido(Rejeitado(10m) with { Justificativa = null }, "estado");
        AssertPagamentoInvalido(Rejeitado(10m) with { Justificativa = "" }, "estado");
        AssertPagamentoInvalido(Rejeitado(10m) with { Justificativa = "   " }, "estado");
    }

    [Fact]
    public void Rejeita_justificativa_com_espacos_nas_extremidades_em_vez_de_alterar_o_dado()
    {
        AssertPagamentoInvalido(Rejeitado(10m) with { Justificativa = " Motivo" }, "estado");
        AssertPagamentoInvalido(Rejeitado(10m) with { Justificativa = "Motivo\n" }, "estado");
    }

    // ---------- 10. Comprometido acima da cota ----------

    [Fact]
    public void Rejeita_confirmado_mais_pendente_acima_da_cota()
    {
        AssertInvalido("pagamentos", () => Reconstituir(100m, [Confirmado(60m), Pendente(40.01m)]));
        AssertInvalido("pagamentos", () => Reconstituir(100m, [Pendente(40.01m), Confirmado(60m)]));
        AssertInvalido("pagamentos", () => Reconstituir(100m, [Confirmado(100.01m)]));
        AssertInvalido("pagamentos", () => Reconstituir(100m, [Pendente(100.01m)]));
    }

    [Fact]
    public void Soma_acima_do_maximo_representavel_e_rejeitada_como_inconsistencia_e_nao_como_estouro()
    {
        var maximo = ValorMonetario.MaximoRepresentavel;
        var primeiro = Pendente(10m) with { Valor = maximo };
        var segundo = Confirmado(10m) with { Valor = maximo };

        var excecao = Assert.Throws<ArgumentException>(
            () => Participacao.Reconstituir(Id, VaquinhaId, Participante, maximo, [primeiro, segundo], []));

        Assert.Equal("pagamentos", excecao.ParamName);
    }

    // ---------- 11. Alterações sem valor positivo ou sem mudança ----------

    [Fact]
    public void Rejeita_alteracao_com_valor_zero()
    {
        AssertAlteracaoInvalida(Mudanca(50m, 100m) with { ValorAnterior = ValorMonetario.Zero }, "valorAnterior");
        AssertAlteracaoInvalida(Mudanca(50m, 100m) with { NovoValor = default }, "novoValor", cota: 100m);
    }

    [Fact]
    public void Rejeita_alteracao_sem_mudanca_real()
    {
        AssertAlteracaoInvalida(Mudanca(100m, 100m), "novoValor");
        AssertAlteracaoInvalida(Mudanca(100m, 100.00m) with { NovoValor = Reais(100.000m) }, "novoValor");
    }

    // ---------- 12 e 13. Cadeia do histórico ----------

    [Fact]
    public void Rejeita_historico_com_cadeia_quebrada()
    {
        var excecao = AssertInvalido(
            "alteracoesCota", () => Reconstituir(180m, alteracoes: [Mudanca(100m, 150m), Mudanca(140m, 180m)]));

        Assert.Contains("posição 1", excecao.Message);
    }

    [Fact]
    public void Rejeita_historico_em_ordem_diferente_da_cadeia()
    {
        AssertInvalido(
            "alteracoesCota", () => Reconstituir(180m, alteracoes: [Mudanca(150m, 180m), Mudanca(100m, 150m)]));
    }

    [Fact]
    public void Rejeita_ultima_alteracao_diferente_da_cota_atual()
    {
        AssertInvalido("alteracoesCota", () => Reconstituir(180m, alteracoes: [Mudanca(100m, 150m)]));
        AssertInvalido("alteracoesCota", () => Reconstituir(180m, alteracoes: [Mudanca(100m, 180m), Mudanca(180m, 150m)]));
    }

    // ---------- 14. Nulos ----------

    [Fact]
    public void Rejeita_colecoes_nulas()
    {
        Assert.Throws<ArgumentNullException>(
            "pagamentos", () => Participacao.Reconstituir(Id, VaquinhaId, Participante, Reais(100m), null!, []));
        Assert.Throws<ArgumentNullException>(
            "alteracoesCota", () => Participacao.Reconstituir(Id, VaquinhaId, Participante, Reais(100m), [], null!));
    }

    [Fact]
    public void Rejeita_itens_nulos_nas_colecoes()
    {
        AssertInvalido("pagamentos", () => Reconstituir(100m, [Pendente(10m), null!]));
        AssertInvalido("alteracoesCota", () => Reconstituir(100m, alteracoes: [Mudanca(50m, 100m), null!]));
    }

    // ---------- Integridade ----------

    [Fact]
    public void Falha_nao_devolve_participacao_nem_altera_as_colecoes_informadas()
    {
        var valido = Pendente(30m);
        var excedente = Pendente(80m);
        var alteracao = Mudanca(50m, 100m);
        var pagamentos = new List<EstadoPagamento> { valido, excedente };
        var alteracoes = new List<EstadoAlteracaoCota> { alteracao };
        Participacao? resultado = null;

        Assert.Throws<ArgumentException>(
            () => resultado = Participacao.Reconstituir(Id, VaquinhaId, Participante, Reais(100m), pagamentos, alteracoes));

        Assert.Null(resultado);
        Assert.Equal([valido, excedente], pagamentos);
        Assert.Equal([alteracao], alteracoes);

        pagamentos.Remove(excedente);
        var corrigida = Participacao.Reconstituir(Id, VaquinhaId, Participante, Reais(100m), pagamentos, alteracoes);

        AssertPagamento(valido, Assert.Single(corrigida.Pagamentos));
        AssertAlteracao(alteracao, Assert.Single(corrigida.AlteracoesCota));
        AssertTotais(corrigida, confirmado: 0m, pendente: 30m, comprometido: 30m, disponivel: 70m, restante: 100m);
    }

    // ---------- Auxiliares ----------

    private static ValorMonetario Reais(decimal valor) => ValorMonetario.Criar(valor);

    private static EstadoPagamento Pendente(decimal valor) =>
        new(Guid.NewGuid(), Reais(valor), StatusPagamento.AguardandoConfirmacao, Participante, Declaracao, null, null, null);

    private static EstadoPagamento Confirmado(decimal valor) =>
        new(Guid.NewGuid(), Reais(valor), StatusPagamento.Confirmado, Participante, Declaracao, Organizador, Decisao, null);

    private static EstadoPagamento Rejeitado(decimal valor, string justificativa = "Comprovante ilegível") =>
        new(Guid.NewGuid(), Reais(valor), StatusPagamento.Rejeitado, Participante, Declaracao, Organizador, Decisao, justificativa);

    private static EstadoAlteracaoCota Mudanca(decimal anterior, decimal novo) =>
        new(Guid.NewGuid(), Reais(anterior), Reais(novo), Organizador, InstanteAlteracao);

    private static Participacao Reconstituir(
        decimal cota,
        IEnumerable<EstadoPagamento>? pagamentos = null,
        IEnumerable<EstadoAlteracaoCota>? alteracoes = null) =>
        Participacao.Reconstituir(Id, VaquinhaId, Participante, Reais(cota), pagamentos ?? [], alteracoes ?? []);

    private static Participacao BrunoT0() =>
        Reconstituir(180m, [Confirmado(60m), Rejeitado(40m), Pendente(80m)], [Mudanca(150m, 180m)]);

    private static EstadoPagamento EstadoDe(Pagamento p) =>
        new(p.Id, p.Valor, p.Status, p.DeclaradoPor, p.DeclaradoEm, p.DecididoPor, p.DecididoEm, p.Justificativa);

    private static EstadoAlteracaoCota EstadoDe(AlteracaoCota a) =>
        new(a.Id, a.ValorAnterior, a.NovoValor, a.AlteradoPor, a.AlteradoEm);

    private static void AssertPagamento(EstadoPagamento esperado, Pagamento atual)
    {
        Assert.Equal(esperado, EstadoDe(atual));
        Assert.Equal(TimeSpan.Zero, atual.DeclaradoEm.Offset);
        if (atual.DecididoEm is { } decididoEm)
        {
            Assert.Equal(TimeSpan.Zero, decididoEm.Offset);
        }
    }

    private static void AssertAlteracao(EstadoAlteracaoCota esperado, AlteracaoCota atual)
    {
        Assert.Equal(esperado, EstadoDe(atual));
        Assert.Equal(TimeSpan.Zero, atual.AlteradoEm.Offset);
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
        var excecao = Assert.Throws<ArgumentException>(acao);
        Assert.Equal(parametro, excecao.ParamName);
    }

    private static ArgumentException AssertInvalido(string colecao, Action acao)
    {
        var excecao = Assert.Throws<ArgumentException>(acao);
        Assert.Equal(colecao, excecao.ParamName);
        return excecao;
    }

    private static void AssertPagamentoInvalido(EstadoPagamento estado, string causa)
    {
        var excecao = AssertInvalido("pagamentos", () => Reconstituir(100m, [estado]));
        var interna = Assert.IsAssignableFrom<ArgumentException>(excecao.InnerException);
        Assert.Equal(causa, interna.ParamName);
    }

    private static void AssertAlteracaoInvalida(EstadoAlteracaoCota estado, string causa, decimal cota = 100m)
    {
        var excecao = AssertInvalido("alteracoesCota", () => Reconstituir(cota, alteracoes: [estado]));
        var interna = Assert.IsAssignableFrom<ArgumentException>(excecao.InnerException);
        Assert.Equal(causa, interna.ParamName);
    }

    private sealed class EnumeravelContado<T>(IEnumerable<T> itens) : IEnumerable<T>
    {
        public int Enumeracoes { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            Enumeracoes++;
            return itens.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
