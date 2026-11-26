using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Waca.Infrastructure.Identidade.Desafios;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;

namespace Waca.Infrastructure.Tests.Identidade.Desafios;

[Collection(ColecaoPostgres.Nome)]
public class EmissaoDeDesafiosTests(ServidorPostgres servidor)
{
    private const string Email = "Ana.Silva@Exemplo.com";
    private const string EmailNormalizado = "ANA.SILVA@EXEMPLO.COM";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Emite_desafio_de_oito_digitos_e_entrega_somente_apos_o_commit()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        long linhasVistasNaEntrega = -1;
        ambiente.Caixa.AoEntregar = async entrega =>
        {
            // Conexão independente: só enxerga dados já confirmados.
            await using var conexao = new NpgsqlConnection(banco.ConnectionString);
            await conexao.OpenAsync(Ct);
            await using var comando = new NpgsqlCommand("SELECT count(*) FROM identidade.desafios_acesso WHERE id = @id", conexao);
            comando.Parameters.AddWithValue("id", entrega.DesafioId);
            linhasVistasNaEntrega = (long)(await comando.ExecuteScalarAsync(Ct))!;
        };

        var (resultado, codigo) = await ambiente.EmitirAsync(Email);

        Assert.Equal(SituacaoDaEmissao.Emitido, resultado.Situacao);
        Assert.Equal(1, linhasVistasNaEntrega);
        Assert.True(GeradorDeCodigo.TemFormatoValido(codigo, 8));
        Assert.Equal(RelogioDeTeste.Inicio.AddMinutes(10), resultado.ExpiraEm);

        var entrega = Assert.Single(ambiente.Caixa.Entregas);
        Assert.Equal(resultado.DesafioId, entrega.DesafioId);
        Assert.Equal(Email, entrega.EmailDestino);
        Assert.Equal(FinalidadeDesafio.Acesso, entrega.Finalidade);
        Assert.DoesNotContain(codigo, entrega.ToString());

        var estado = (await ambiente.LerAsync(resultado.DesafioId))!;
        Assert.Equal(("Acesso", EmailNormalizado, (Guid?)null), (estado.Finalidade, estado.EmailNormalizado, estado.PessoaId));
        Assert.Equal((0, 5, (short)1), (estado.Tentativas, estado.MaxTentativas, estado.VersaoChave));
        Assert.Equal(RelogioDeTeste.Inicio, estado.CriadoEm);
        Assert.Equal(RelogioDeTeste.Inicio.AddMinutes(10), estado.ExpiraEm);
        Assert.True(estado.Aberto);
        Assert.Equal(7, resultado.DesafioId!.Value.Version);
    }

    [Fact]
    public async Task Codigo_nao_e_persistido_em_texto_puro_e_o_hash_e_o_HMAC_vinculado()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);

        var (resultado, codigo) = await ambiente.EmitirAsync(Email);
        var estado = (await ambiente.LerAsync(resultado.DesafioId))!;

        // Nenhuma coluna (inclusive a representação textual da linha inteira) contém o código.
        Assert.Equal(0, await ambiente.ContarDesafiosAsync($"desafios_acesso::text LIKE '%{codigo}%'"));
        Assert.Equal(32, estado.SegredoHash.Length);
        Assert.Equal(
            ProtecaoDoCodigo.Calcular(ambiente.Chave, resultado.DesafioId!.Value, FinalidadeDesafio.Acesso, EmailNormalizado, codigo),
            estado.SegredoHash);
        Assert.NotEqual(SHA256.HashData(Encoding.UTF8.GetBytes(codigo)), estado.SegredoHash);
        Assert.NotEqual(Encoding.UTF8.GetBytes(codigo), estado.SegredoHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ana@exemplo.com")]
    [InlineData("ana@exemplo.com ")]
    [InlineData("sem-arroba")]
    [InlineData("acesso-com-pessoa")]
    [InlineData("troca-sem-pessoa")]
    [InlineData("muito-longo")]
    public async Task Entrada_invalida_nao_grava_nem_entrega(string caso)
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var solicitacao = caso switch
        {
            "acesso-com-pessoa" => new SolicitacaoDeEmissao(FinalidadeDesafio.Acesso, "ana@exemplo.com", Guid.CreateVersion7()),
            "troca-sem-pessoa" => new SolicitacaoDeEmissao(FinalidadeDesafio.TrocaDeEmail, "ana@exemplo.com"),
            "muito-longo" => new SolicitacaoDeEmissao(FinalidadeDesafio.Acesso, new string('a', 250) + "@exemplo.com"),
            _ => new SolicitacaoDeEmissao(FinalidadeDesafio.Acesso, caso),
        };

        var resultado = await ambiente.Emissor.EmitirAsync(solicitacao, Ct);

        Assert.Equal(SituacaoDaEmissao.EntradaInvalida, resultado.Situacao);
        Assert.Null(resultado.DesafioId);
        Assert.Equal(0, await ambiente.ContarDesafiosAsync());
        Assert.Empty(ambiente.Caixa.Tentativas);
    }

    [Fact]
    public async Task Novo_desafio_invalida_o_anterior_que_nao_volta_a_valer()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);

        var (primeiro, codigoAntigo) = await ambiente.EmitirAsync(Email);
        ambiente.Relogio.Avancar(TimeSpan.FromSeconds(60));
        var (segundo, codigoNovo) = await ambiente.EmitirAsync("ana.silva@EXEMPLO.COM");

        Assert.Equal(SituacaoDaEmissao.Emitido, segundo.Situacao);
        var antigo = (await ambiente.LerAsync(primeiro.DesafioId))!;
        Assert.Equal(RelogioDeTeste.Inicio.AddSeconds(60), antigo.InvalidadoEm);
        Assert.Null(antigo.ConsumidoEm);
        Assert.Equal(1, await ambiente.ContarDesafiosAsync("consumido_em IS NULL AND invalidado_em IS NULL"));

        Assert.Equal(MotivoDaRecusa.Invalidado, (await ambiente.VerificarAsync(primeiro.DesafioId, Email, codigoAntigo)).Motivo);
        Assert.True((await ambiente.VerificarAsync(segundo.DesafioId, Email, codigoNovo)).Consumido);
    }

    [Fact]
    public async Task Intervalo_minimo_de_60_segundos_e_respeitado()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        await ambiente.EmitirAsync(Email);

        ambiente.Relogio.Avancar(TimeSpan.FromSeconds(59));
        var cedo = await ambiente.EmitirAsync(Email);
        ambiente.Relogio.Avancar(TimeSpan.FromSeconds(1));
        var noLimite = await ambiente.EmitirAsync(Email);

        Assert.Equal((SituacaoDaEmissao.LimiteAtingido, LimiteDeEmissao.IntervaloMinimo), (cedo.Resultado.Situacao, cedo.Resultado.Limite));
        Assert.Null(cedo.Resultado.DesafioId);
        Assert.Equal(SituacaoDaEmissao.Emitido, noLimite.Resultado.Situacao);
        Assert.Equal(2, await ambiente.ContarDesafiosAsync());
        Assert.Equal(2, ambiente.Caixa.Entregas.Count);
    }

    [Fact]
    public async Task Janela_de_15_minutos_limita_a_tres_emissoes()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(SituacaoDaEmissao.Emitido, (await ambiente.EmitirAsync(Email)).Resultado.Situacao);
            ambiente.Relogio.Avancar(TimeSpan.FromSeconds(61));
        }

        var quarta = await ambiente.EmitirAsync(Email);
        Assert.Equal((SituacaoDaEmissao.LimiteAtingido, LimiteDeEmissao.JanelaCurta), (quarta.Resultado.Situacao, quarta.Resultado.Limite));

        // A primeira emissão (instante zero) sai da janela; restam duas dentro dela.
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(3 * 61) + TimeSpan.FromSeconds(1));
        Assert.Equal(SituacaoDaEmissao.Emitido, (await ambiente.EmitirAsync(Email)).Resultado.Situacao);
        Assert.Equal(4, await ambiente.ContarDesafiosAsync());
    }

    [Fact]
    public async Task Janela_de_24_horas_limita_a_dez_emissoes()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(SituacaoDaEmissao.Emitido, (await ambiente.EmitirAsync(Email)).Resultado.Situacao);
            ambiente.Relogio.Avancar(TimeSpan.FromMinutes(6));
        }

        var decimaPrimeira = await ambiente.EmitirAsync(Email);
        Assert.Equal((SituacaoDaEmissao.LimiteAtingido, LimiteDeEmissao.JanelaLonga), (decimaPrimeira.Resultado.Situacao, decimaPrimeira.Resultado.Limite));

        // Agora = 60 min. Ao passar 24 h da primeira emissão, ela sai da janela longa.
        ambiente.Relogio.Avancar(TimeSpan.FromHours(23) + TimeSpan.FromSeconds(1));
        Assert.Equal(SituacaoDaEmissao.Emitido, (await ambiente.EmitirAsync(Email)).Resultado.Situacao);
        Assert.Equal(11, await ambiente.ContarDesafiosAsync());
    }

    [Fact]
    public async Task Emissoes_simultaneas_para_o_mesmo_email_produzem_um_unico_desafio()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var largada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tarefas = Enumerable.Range(0, 10).Select(async i =>
        {
            await largada.Task;
            return await ambiente.Emissor.EmitirAsync(
                new SolicitacaoDeEmissao(FinalidadeDesafio.Acesso, i % 2 == 0 ? Email : "ANA.silva@exemplo.COM"), Ct);
        }).ToArray();
        largada.SetResult();
        var resultados = await Task.WhenAll(tarefas);

        Assert.Single(resultados, r => r.Situacao == SituacaoDaEmissao.Emitido);
        Assert.All(
            resultados.Where(r => r.Situacao != SituacaoDaEmissao.Emitido),
            r => Assert.Equal((SituacaoDaEmissao.LimiteAtingido, (LimiteDeEmissao?)LimiteDeEmissao.IntervaloMinimo), (r.Situacao, r.Limite)));
        Assert.Equal(1, await ambiente.ContarDesafiosAsync());
        Assert.Single(ambiente.Caixa.Entregas);
    }

    [Fact]
    public async Task Cotas_sao_respeitadas_sob_concorrencia()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco, new OpcoesDeDesafios { IntervaloMinimoEntreEmissoes = TimeSpan.Zero });
        var largada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tarefas = Enumerable.Range(0, 12).Select(async _ =>
        {
            await largada.Task;
            return await ambiente.Emissor.EmitirAsync(new SolicitacaoDeEmissao(FinalidadeDesafio.Acesso, Email), Ct);
        }).ToArray();
        largada.SetResult();
        var resultados = await Task.WhenAll(tarefas);

        Assert.Equal(3, resultados.Count(r => r.Situacao == SituacaoDaEmissao.Emitido));
        Assert.Equal(9, resultados.Count(r => r.Situacao == SituacaoDaEmissao.LimiteAtingido && r.Limite == LimiteDeEmissao.JanelaCurta));
        Assert.Equal(3, await ambiente.ContarDesafiosAsync());
        Assert.Equal(1, await ambiente.ContarDesafiosAsync("consumido_em IS NULL AND invalidado_em IS NULL"));
    }

    [Fact]
    public async Task Emails_e_finalidades_diferentes_nao_compartilham_cota()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);

        var a = await ambiente.EmitirAsync("ana@exemplo.com");
        var b = await ambiente.EmitirAsync("bia@exemplo.com");
        var troca = await ambiente.EmitirAsync("ana@exemplo.com", FinalidadeDesafio.TrocaDeEmail, Guid.CreateVersion7());

        Assert.All(new[] { a, b, troca }, r => Assert.Equal(SituacaoDaEmissao.Emitido, r.Resultado.Situacao));
        Assert.Equal(3, await ambiente.ContarDesafiosAsync("consumido_em IS NULL AND invalidado_em IS NULL"));
    }

    [Fact]
    public async Task Falha_na_entrega_invalida_o_desafio_consome_cota_e_nao_vaza_o_codigo_no_log()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        ambiente.Caixa.AoEntregar = entrega => throw new InvalidOperationException($"provedor recusou {entrega.EmailDestino} {entrega.Codigo}");

        var (resultado, codigo) = await ambiente.EmitirAsync(Email);

        Assert.Equal(SituacaoDaEmissao.EntregaFalhou, resultado.Situacao);
        Assert.Null(resultado.ExpiraEm);
        Assert.Empty(ambiente.Caixa.Entregas);
        var estado = (await ambiente.LerAsync(resultado.DesafioId))!;
        Assert.NotNull(estado.InvalidadoEm);
        Assert.Equal(MotivoDaRecusa.Invalidado, (await ambiente.VerificarAsync(resultado.DesafioId, Email, codigo)).Motivo);

        ambiente.Caixa.AoEntregar = null;
        Assert.Equal(LimiteDeEmissao.IntervaloMinimo, (await ambiente.EmitirAsync(Email)).Resultado.Limite);

        Assert.NotEmpty(ambiente.Log.Linhas);
        Assert.DoesNotContain(ambiente.Log.Linhas, l => l.Contains(codigo, StringComparison.Ordinal));
        Assert.DoesNotContain(ambiente.Log.Linhas, l => l.Contains("provedor recusou", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Logs_de_emissao_e_verificacao_nao_contem_codigo_nem_endereco()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);

        var (resultado, codigo) = await ambiente.EmitirAsync(Email);
        await ambiente.VerificarAsync(resultado.DesafioId, Email, codigo == "00000000" ? "00000001" : "00000000");
        await ambiente.VerificarAsync(resultado.DesafioId, Email, codigo);
        await ambiente.VerificarAsync(resultado.DesafioId, Email, codigo);

        Assert.True(ambiente.Log.Linhas.Count >= 4);
        foreach (var proibido in new[] { codigo, Email, EmailNormalizado })
        {
            Assert.DoesNotContain(ambiente.Log.Linhas, l => l.Contains(proibido, StringComparison.OrdinalIgnoreCase));
        }
    }
}
