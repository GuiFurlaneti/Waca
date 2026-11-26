using Microsoft.EntityFrameworkCore;
using Npgsql;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Identidade.Desafios;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;
using static Waca.Infrastructure.Tests.Identidade.IdentidadeNoBancoDeTeste;

namespace Waca.Infrastructure.Tests.Identidade.Desafios;

[Collection(ColecaoPostgres.Nome)]
public class VerificacaoDeDesafiosTests(ServidorPostgres servidor)
{
    private const string Email = "Ana.Silva@Exemplo.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Codigo_correto_e_aceito_uma_unica_vez()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (emissao, codigo) = await ambiente.EmitirAsync(Email);
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(1));

        var primeira = await ambiente.VerificarAsync(emissao.DesafioId, "ana.silva@EXEMPLO.com", codigo);
        var segunda = await ambiente.VerificarAsync(emissao.DesafioId, Email, codigo);

        Assert.True(primeira.Consumido);
        Assert.Equal((emissao.DesafioId, "ANA.SILVA@EXEMPLO.COM", (Guid?)null), (primeira.DesafioId, primeira.EmailNormalizado, primeira.PessoaId));
        Assert.False(segunda.Consumido);
        Assert.Equal(MotivoDaRecusa.JaConsumido, segunda.Motivo);

        // Estado lido por outra conexão, depois que os contextos usados foram descartados.
        var estado = (await ambiente.LerAsync(emissao.DesafioId))!;
        Assert.Equal(RelogioDeTeste.Inicio.AddMinutes(1), estado.ConsumidoEm);
        Assert.Null(estado.InvalidadoEm);
        Assert.Equal(0, estado.Tentativas);
    }

    [Fact]
    public async Task Codigo_errado_e_recusado_e_a_tentativa_persiste()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (emissao, codigo) = await ambiente.EmitirAsync(Email);

        var resultado = await ambiente.VerificarAsync(emissao.DesafioId, Email, Errado(codigo));

        Assert.Equal((false, MotivoDaRecusa.CodigoIncorreto, (int?)4), (resultado.Consumido, resultado.Motivo, resultado.TentativasRestantes));
        Assert.Equal(1, (await ambiente.LerAsync(emissao.DesafioId))!.Tentativas);
        Assert.True((await ambiente.VerificarAsync(emissao.DesafioId, Email, codigo)).Consumido);
    }

    [Fact]
    public async Task Cinco_tentativas_erradas_invalidam_o_desafio_e_o_codigo_correto_deixa_de_valer()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (emissao, codigo) = await ambiente.EmitirAsync(Email);

        var restantes = new List<int?>();
        for (var i = 0; i < 5; i++)
        {
            restantes.Add((await ambiente.VerificarAsync(emissao.DesafioId, Email, Errado(codigo))).TentativasRestantes);
        }

        Assert.Equal([4, 3, 2, 1, 0], restantes);
        var estado = (await ambiente.LerAsync(emissao.DesafioId))!;
        Assert.Equal(5, estado.Tentativas);
        Assert.NotNull(estado.InvalidadoEm);

        Assert.Equal(MotivoDaRecusa.Invalidado, (await ambiente.VerificarAsync(emissao.DesafioId, Email, codigo)).Motivo);
        Assert.Equal(5, (await ambiente.LerAsync(emissao.DesafioId))!.Tentativas);
    }

    [Fact]
    public async Task Formato_invalido_conta_como_tentativa()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (emissao, codigo) = await ambiente.EmitirAsync(Email);

        foreach (var invalido in new[] { "", codigo[..7], codigo + "0", " " + codigo[1..] })
        {
            Assert.Equal(MotivoDaRecusa.CodigoIncorreto, (await ambiente.VerificarAsync(emissao.DesafioId, Email, invalido)).Motivo);
        }

        Assert.Equal(4, (await ambiente.LerAsync(emissao.DesafioId))!.Tentativas);
    }

    [Fact]
    public async Task Codigo_vencido_e_recusado_mesmo_antes_da_limpeza()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (noLimite, codigoNoLimite) = await ambiente.EmitirAsync("ana@exemplo.com");
        var (vencido, codigoVencido) = await ambiente.EmitirAsync("bia@exemplo.com");

        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        Assert.True((await ambiente.VerificarAsync(noLimite.DesafioId, "ana@exemplo.com", codigoNoLimite)).Consumido);

        ambiente.Relogio.Avancar(TimeSpan.FromSeconds(1));
        Assert.Equal(MotivoDaRecusa.Expirado, (await ambiente.VerificarAsync(vencido.DesafioId, "bia@exemplo.com", codigoVencido)).Motivo);

        ambiente.Relogio.Avancar(TimeSpan.FromDays(1));
        Assert.Equal(MotivoDaRecusa.Expirado, (await ambiente.VerificarAsync(vencido.DesafioId, "bia@exemplo.com", codigoVencido)).Motivo);
        var estado = (await ambiente.LerAsync(vencido.DesafioId))!;
        Assert.True(estado.Aberto);
        Assert.Equal(0, estado.Tentativas);
    }

    [Fact]
    public async Task Codigo_de_outro_desafio_e_recusado()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (deAna, _) = await ambiente.EmitirAsync("ana@exemplo.com");
        var (deBia, codigoDeBia) = await ambiente.EmitirAsync("bia@exemplo.com");

        Assert.Equal(MotivoDaRecusa.CodigoIncorreto, (await ambiente.VerificarAsync(deAna.DesafioId, "ana@exemplo.com", codigoDeBia)).Motivo);
        Assert.Equal(MotivoDaRecusa.CodigoIncorreto, (await ambiente.VerificarAsync(deAna.DesafioId, "bia@exemplo.com", codigoDeBia)).Motivo);
        Assert.Equal(MotivoDaRecusa.NaoEncontrado, (await ambiente.VerificarAsync(Guid.CreateVersion7(), "bia@exemplo.com", codigoDeBia)).Motivo);
        Assert.True((await ambiente.VerificarAsync(deBia.DesafioId, "bia@exemplo.com", codigoDeBia)).Consumido);
    }

    [Fact]
    public async Task Codigo_de_outra_finalidade_e_recusado()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var pessoa = Guid.CreateVersion7();
        var (troca, codigo) = await ambiente.EmitirAsync(Email, FinalidadeDesafio.TrocaDeEmail, pessoa);

        var comoAcesso = await ambiente.VerificarAsync(troca.DesafioId, Email, codigo, FinalidadeDesafio.Acesso);
        var comoTroca = await ambiente.VerificarAsync(troca.DesafioId, Email, codigo, FinalidadeDesafio.TrocaDeEmail);

        Assert.Equal(MotivoDaRecusa.CodigoIncorreto, comoAcesso.Motivo);
        Assert.True(comoTroca.Consumido);
        Assert.Equal(pessoa, comoTroca.PessoaId);
    }

    [Fact]
    public async Task Codigo_correto_com_outro_email_e_recusado()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (emissao, codigo) = await ambiente.EmitirAsync(Email);

        Assert.Equal(MotivoDaRecusa.CodigoIncorreto, (await ambiente.VerificarAsync(emissao.DesafioId, "outra@exemplo.com", codigo)).Motivo);
        Assert.Equal(1, (await ambiente.LerAsync(emissao.DesafioId))!.Tentativas);
    }

    [Fact]
    public async Task Versao_de_chave_desconhecida_e_recusada_sem_consumir_tentativa()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (emissao, codigo) = await ambiente.EmitirAsync(Email);
        var outroChaveiro = new ChaveiroDeDesafios(2, new Dictionary<short, byte[]> { [2] = AmbienteDeDesafios.NovaChave() });

        var resultado = await ambiente.CriarVerificador(outroChaveiro).VerificarEConsumirEmTransacaoPropriaAsync(
            new PedidoDeVerificacao(emissao.DesafioId!.Value, FinalidadeDesafio.Acesso, Email, codigo), Ct);

        Assert.Equal(MotivoDaRecusa.ChaveIndisponivel, resultado.Motivo);
        var estado = (await ambiente.LerAsync(emissao.DesafioId))!;
        Assert.True(estado.Aberto);
        Assert.Equal(0, estado.Tentativas);
    }

    [Fact]
    public async Task Chave_diferente_com_a_mesma_versao_nao_confere()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (emissao, codigo) = await ambiente.EmitirAsync(Email);
        var trocada = new ChaveiroDeDesafios(1, new Dictionary<short, byte[]> { [1] = AmbienteDeDesafios.NovaChave() });

        var resultado = await ambiente.CriarVerificador(trocada).VerificarEConsumirEmTransacaoPropriaAsync(
            new PedidoDeVerificacao(emissao.DesafioId!.Value, FinalidadeDesafio.Acesso, Email, codigo), Ct);

        Assert.Equal(MotivoDaRecusa.CodigoIncorreto, resultado.Motivo);
    }

    [Fact]
    public async Task Versao_antiga_continua_verificavel_depois_da_rotacao()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (emissao, codigo) = await ambiente.EmitirAsync(Email);
        var rotacionado = new ChaveiroDeDesafios(2, new Dictionary<short, byte[]>
        {
            [1] = ambiente.Chave,
            [2] = AmbienteDeDesafios.NovaChave(),
        });

        var resultado = await ambiente.CriarVerificador(rotacionado).VerificarEConsumirEmTransacaoPropriaAsync(
            new PedidoDeVerificacao(emissao.DesafioId!.Value, FinalidadeDesafio.Acesso, Email, codigo), Ct);

        Assert.True(resultado.Consumido);
    }

    [Fact]
    public async Task Verificacao_exige_transacao_aberta_pelo_chamador()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (emissao, codigo) = await ambiente.EmitirAsync(Email);
        await using var contexto = banco.CriarContextoDeIdentidade();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ambiente.Verificador.VerificarEConsumirAsync(
            contexto, new PedidoDeVerificacao(emissao.DesafioId!.Value, FinalidadeDesafio.Acesso, Email, codigo), Ct));
        Assert.True((await ambiente.LerAsync(emissao.DesafioId))!.Aberto);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public async Task Verificacoes_simultaneas_em_conexoes_distintas_produzem_um_unico_sucesso(int quantidade)
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (emissao, codigo) = await ambiente.EmitirAsync(Email);
        var pedido = new PedidoDeVerificacao(emissao.DesafioId!.Value, FinalidadeDesafio.Acesso, Email, codigo);
        using var prontos = new CountdownEvent(quantidade);
        var largada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tarefas = Enumerable.Range(0, quantidade).Select(async _ =>
        {
            await using var contexto = banco.CriarContextoDeIdentidade();
            await using var transacao = await contexto.Database.BeginTransactionAsync(Ct);
            var pid = await contexto.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Ct);
            prontos.Signal();
            await largada.Task;

            var resultado = await ambiente.Verificador.VerificarEConsumirAsync(contexto, pedido, Ct);
            await transacao.CommitAsync(Ct);
            return (Pid: pid, Resultado: resultado);
        }).ToArray();

        prontos.Wait(Ct);
        largada.SetResult();
        var resultados = await Task.WhenAll(tarefas);

        Assert.Equal(quantidade, resultados.Select(r => r.Pid).Distinct().Count());
        Assert.Single(resultados, r => r.Resultado.Consumido);
        Assert.All(resultados.Where(r => !r.Resultado.Consumido), r => Assert.Equal(MotivoDaRecusa.JaConsumido, r.Resultado.Motivo));
        var estado = (await ambiente.LerAsync(emissao.DesafioId))!;
        Assert.NotNull(estado.ConsumidoEm);
        Assert.Equal(0, estado.Tentativas);
    }

    [Fact]
    public async Task Tentativas_erradas_simultaneas_sao_todas_contabilizadas()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (emissao, codigo) = await ambiente.EmitirAsync(Email);
        var largada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tarefas = Enumerable.Range(0, 8).Select(async _ =>
        {
            await largada.Task;
            return await ambiente.VerificarAsync(emissao.DesafioId, Email, Errado(codigo));
        }).ToArray();
        largada.SetResult();
        var resultados = await Task.WhenAll(tarefas);

        Assert.Equal(5, resultados.Count(r => r.Motivo == MotivoDaRecusa.CodigoIncorreto));
        Assert.Equal(3, resultados.Count(r => r.Motivo == MotivoDaRecusa.Invalidado));
        var estado = (await ambiente.LerAsync(emissao.DesafioId))!;
        Assert.Equal(5, estado.Tentativas);
        Assert.NotNull(estado.InvalidadoEm);
        Assert.Equal(MotivoDaRecusa.Invalidado, (await ambiente.VerificarAsync(emissao.DesafioId, Email, codigo)).Motivo);
    }

    [Fact]
    public async Task Falha_da_transacao_maior_desfaz_o_consumo()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        await using (var contexto = banco.CriarContextoDeIdentidade())
        {
            contexto.Users.Add(NovaConta(Email));
            await contexto.SaveChangesAsync(Ct);
        }

        var (emissao, codigo) = await ambiente.EmitirAsync(Email);
        var pedido = new PedidoDeVerificacao(emissao.DesafioId!.Value, FinalidadeDesafio.Acesso, Email, codigo);

        await using (var contexto = banco.CriarContextoDeIdentidade())
        await using (var transacao = await contexto.Database.BeginTransactionAsync(Ct))
        {
            Assert.True((await ambiente.Verificador.VerificarEConsumirAsync(contexto, pedido, Ct)).Consumido);

            // Operação seguinte da mesma transação falha: conta com e-mail já usado (EmailIndex).
            contexto.Users.Add(NovaConta(Email));
            await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Ct));
            await transacao.RollbackAsync(Ct);
        }

        var estado = (await ambiente.LerAsync(emissao.DesafioId))!;
        Assert.True(estado.Aberto);
        Assert.True((await ambiente.VerificarAsync(emissao.DesafioId, Email, codigo)).Consumido);
    }

    [Fact]
    public async Task Consumo_e_operacao_seguinte_confirmam_juntos()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (emissao, codigo) = await ambiente.EmitirAsync(Email);
        var conta = NovaConta(Email);

        await using (var contexto = banco.CriarContextoDeIdentidade())
        await using (var transacao = await contexto.Database.BeginTransactionAsync(Ct))
        {
            var resultado = await ambiente.Verificador.VerificarEConsumirAsync(
                contexto, new PedidoDeVerificacao(emissao.DesafioId!.Value, FinalidadeDesafio.Acesso, Email, codigo), Ct);
            Assert.True(resultado.Consumido);

            // Ainda não confirmado: outra conexão vê o desafio aberto.
            Assert.True((await ambiente.LerAsync(emissao.DesafioId))!.Aberto);

            contexto.Users.Add(conta);
            await contexto.SaveChangesAsync(Ct);
            await transacao.CommitAsync(Ct);
        }

        Assert.NotNull((await ambiente.LerAsync(emissao.DesafioId))!.ConsumidoEm);
        Assert.Equal(1, await banco.ContarAsync($"SELECT count(*) FROM identidade.\"AspNetUsers\" WHERE \"Id\" = '{conta.Id}'"));
    }

    [Fact]
    public async Task Tentativa_errada_dentro_da_transacao_do_chamador_so_conta_se_ele_confirmar()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (emissao, codigo) = await ambiente.EmitirAsync(Email);
        var errado = new PedidoDeVerificacao(emissao.DesafioId!.Value, FinalidadeDesafio.Acesso, Email, Errado(codigo));

        await using (var contexto = banco.CriarContextoDeIdentidade())
        await using (var transacao = await contexto.Database.BeginTransactionAsync(Ct))
        {
            Assert.Equal(MotivoDaRecusa.CodigoIncorreto, (await ambiente.Verificador.VerificarEConsumirAsync(contexto, errado, Ct)).Motivo);
            await transacao.RollbackAsync(Ct);
        }

        Assert.Equal(0, (await ambiente.LerAsync(emissao.DesafioId))!.Tentativas);

        await using (var contexto = banco.CriarContextoDeIdentidade())
        await using (var transacao = await contexto.Database.BeginTransactionAsync(Ct))
        {
            await ambiente.Verificador.VerificarEConsumirAsync(contexto, errado, Ct);
            await transacao.CommitAsync(Ct);
        }

        Assert.Equal(1, (await ambiente.LerAsync(emissao.DesafioId))!.Tentativas);
    }

    [Fact]
    public async Task Tentativas_erradas_nao_alteram_a_conta_existente()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var conta = NovaConta(Email);
        await using (var contexto = banco.CriarContextoDeIdentidade())
        {
            contexto.Users.Add(conta);
            await contexto.SaveChangesAsync(Ct);
        }

        var antes = await banco.ConsultarAsync("SELECT \"AccessFailedCount\", \"LockoutEnd\", \"ConcurrencyStamp\" FROM identidade.\"AspNetUsers\"");
        var (emissao, codigo) = await ambiente.EmitirAsync(Email);
        for (var i = 0; i < 5; i++)
        {
            await ambiente.VerificarAsync(emissao.DesafioId, Email, Errado(codigo));
        }

        Assert.Equal(antes, await banco.ConsultarAsync("SELECT \"AccessFailedCount\", \"LockoutEnd\", \"ConcurrencyStamp\" FROM identidade.\"AspNetUsers\""));
    }

    private static string Errado(string codigo) => codigo == "00000000" ? "00000001" : "00000000";
}
