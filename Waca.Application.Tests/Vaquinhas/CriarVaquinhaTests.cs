using Waca.Application.Comum;
using Waca.Application.Vaquinhas;
using Xunit;

namespace Waca.Application.Tests.Vaquinhas;

public class CriarVaquinhaTests
{
    private static readonly Guid Organizador = Guid.Parse("0a9b8c7d-6e5f-4a3b-8c2d-1e0f9a8b7c6d");
    private static readonly Guid IdGerado = Guid.Parse("019a8f3e-1c2d-7a4b-8c5d-6e7f8a9b0c1d");
    private static readonly Guid OutroIdGerado = Guid.Parse("019a8f3e-1c2d-7a4b-8c5d-6e7f8a9b0c1e");
    private static readonly DateTimeOffset Agora = new(2026, 11, 25, 10, 15, 0, TimeSpan.Zero);

    // ---------- Criação válida ----------

    [Fact]
    public async Task Ator_valido_cria_vaquinha_com_organizador_id_e_instante_fornecidos_pelas_abstracoes()
    {
        var (casoDeUso, repositorio, _, _, _) = Montar(Organizador);

        var resultado = await casoDeUso.ExecutarAsync(new CriarVaquinhaComando("Viagem da galera"), CancellationToken.None);

        Assert.Equal(new VaquinhaResumo(IdGerado, "Viagem da galera", Agora), resultado);
        var gravada = Assert.Single(repositorio.Gravadas);
        Assert.Equal(IdGerado, gravada.Id);
        Assert.Equal("Viagem da galera", gravada.Nome);
        Assert.Equal(Organizador, gravada.OrganizadorId);
        Assert.Equal(Agora, gravada.CriadaEm);
    }

    [Fact]
    public async Task Nome_e_normalizado_pelo_dominio()
    {
        var (casoDeUso, repositorio, _, _, _) = Montar(Organizador);

        var resultado = await casoDeUso.ExecutarAsync(new CriarVaquinhaComando("  Viagem  da galera \t"), CancellationToken.None);

        Assert.Equal("Viagem  da galera", resultado.Nome);
        Assert.Equal("Viagem  da galera", Assert.Single(repositorio.Gravadas).Nome);
    }

    [Fact]
    public async Task Instante_do_relogio_e_gravado_em_UTC()
    {
        var instante = new DateTimeOffset(2026, 11, 25, 7, 15, 0, TimeSpan.FromHours(-3));
        var (casoDeUso, repositorio, _, _, _) = Montar(Organizador, agora: instante);

        var resultado = await casoDeUso.ExecutarAsync(new CriarVaquinhaComando("Viagem"), CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, resultado.CriadaEm.Offset);
        Assert.Equal(instante.UtcDateTime, resultado.CriadaEm.UtcDateTime);
        Assert.Equal(resultado.CriadaEm, Assert.Single(repositorio.Gravadas).CriadaEm);
    }

    [Fact]
    public async Task Cada_criacao_usa_um_novo_identificador_do_gerador()
    {
        var (casoDeUso, repositorio, gerador, _, _) = Montar(Organizador, ids: [IdGerado, OutroIdGerado]);

        var primeira = await casoDeUso.ExecutarAsync(new CriarVaquinhaComando("Viagem"), CancellationToken.None);
        var segunda = await casoDeUso.ExecutarAsync(new CriarVaquinhaComando("Viagem"), CancellationToken.None);

        Assert.Equal(IdGerado, primeira.Id);
        Assert.Equal(OutroIdGerado, segunda.Id);
        Assert.Equal(2, gerador.Chamadas);
        Assert.Equal(2, repositorio.Gravadas.Count);
    }

    [Fact]
    public async Task Criacao_grava_somente_a_vaquinha_sem_participacao_do_organizador()
    {
        var (casoDeUso, repositorio, _, _, _) = Montar(Organizador);

        await casoDeUso.ExecutarAsync(new CriarVaquinhaComando("Viagem"), CancellationToken.None);

        Assert.Equal(1, repositorio.ChamadasAdicionar);
        Assert.Single(repositorio.Gravadas);
        Assert.Empty(repositorio.OrganizadoresConsultados);
    }

    [Fact]
    public async Task Token_de_cancelamento_e_repassado_ao_repositorio()
    {
        var (casoDeUso, repositorio, _, _, _) = Montar(Organizador);
        using var fonte = new CancellationTokenSource();

        await casoDeUso.ExecutarAsync(new CriarVaquinhaComando("Viagem"), fonte.Token);

        Assert.Equal(fonte.Token, repositorio.UltimoToken);
    }

    // ---------- Entrada inválida ----------

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData(" ")]
    public async Task Nome_invalido_e_rejeitado_sem_acionar_o_repositorio(string nome)
    {
        var (casoDeUso, repositorio, _, _, _) = Montar(Organizador);

        var excecao = await Assert.ThrowsAsync<EntradaInvalidaException>(
            () => casoDeUso.ExecutarAsync(new CriarVaquinhaComando(nome), CancellationToken.None));

        Assert.Equal("Nome", excecao.Campo);
        Assert.IsType<ArgumentException>(excecao.InnerException);
        Assert.Equal(0, repositorio.ChamadasAdicionar);
        Assert.Empty(repositorio.Gravadas);
    }

    [Fact]
    public async Task Nome_nulo_e_rejeitado_sem_acionar_o_repositorio()
    {
        var (casoDeUso, repositorio, _, _, _) = Montar(Organizador);

        var excecao = await Assert.ThrowsAsync<EntradaInvalidaException>(
            () => casoDeUso.ExecutarAsync(new CriarVaquinhaComando(null!), CancellationToken.None));

        Assert.Equal("Nome", excecao.Campo);
        Assert.IsType<ArgumentNullException>(excecao.InnerException);
        Assert.Equal(0, repositorio.ChamadasAdicionar);
    }

    [Fact]
    public async Task Defeito_do_gerador_ou_do_relogio_nao_e_reportado_como_entrada_invalida()
    {
        var (comIdVazio, repositorio1, _, _, _) = Montar(Organizador, ids: [Guid.Empty]);
        var (semInstante, repositorio2, _, _, _) = Montar(Organizador, agora: default(DateTimeOffset));

        var erroDoId = await Assert.ThrowsAsync<ArgumentException>(
            () => comIdVazio.ExecutarAsync(new CriarVaquinhaComando("Viagem"), CancellationToken.None));
        var erroDoInstante = await Assert.ThrowsAsync<ArgumentException>(
            () => semInstante.ExecutarAsync(new CriarVaquinhaComando("Viagem"), CancellationToken.None));

        Assert.Equal("id", erroDoId.ParamName);
        Assert.Equal("criadaEm", erroDoInstante.ParamName);
        Assert.Equal(0, repositorio1.ChamadasAdicionar);
        Assert.Equal(0, repositorio2.ChamadasAdicionar);
    }

    [Fact]
    public async Task Comando_nulo_e_rejeitado()
    {
        var (casoDeUso, repositorio, _, _, _) = Montar(Organizador);

        await Assert.ThrowsAsync<ArgumentNullException>(() => casoDeUso.ExecutarAsync(null!, CancellationToken.None));

        Assert.Equal(0, repositorio.ChamadasAdicionar);
    }

    // ---------- Ator ----------

    [Fact]
    public async Task Ator_ausente_e_rejeitado_antes_de_qualquer_outra_etapa()
    {
        var (casoDeUso, repositorio, gerador, relogio, ator) = Montar(null);

        await Assert.ThrowsAsync<AtorNaoIdentificadoException>(
            () => casoDeUso.ExecutarAsync(new CriarVaquinhaComando("Viagem"), CancellationToken.None));

        Assert.Equal(1, ator.Leituras);
        Assert.Equal(0, gerador.Chamadas);
        Assert.Equal(0, relogio.Leituras);
        Assert.Equal(0, repositorio.ChamadasAdicionar);
    }

    [Fact]
    public async Task Ator_com_Guid_vazio_e_rejeitado_antes_de_qualquer_outra_etapa()
    {
        var (casoDeUso, repositorio, gerador, relogio, _) = Montar(Guid.Empty);

        await Assert.ThrowsAsync<AtorNaoIdentificadoException>(
            () => casoDeUso.ExecutarAsync(new CriarVaquinhaComando("Viagem"), CancellationToken.None));

        Assert.Equal(0, gerador.Chamadas);
        Assert.Equal(0, relogio.Leituras);
        Assert.Equal(0, repositorio.ChamadasAdicionar);
    }

    [Fact]
    public async Task Ator_ausente_tem_precedencia_sobre_nome_invalido()
    {
        var (casoDeUso, _, _, _, _) = Montar(null);

        await Assert.ThrowsAsync<AtorNaoIdentificadoException>(
            () => casoDeUso.ExecutarAsync(new CriarVaquinhaComando(" "), CancellationToken.None));
    }

    // ---------- Repositório ----------

    [Fact]
    public async Task Resultado_so_e_devolvido_depois_da_confirmacao_do_repositorio()
    {
        var (casoDeUso, repositorio, _, _, _) = Montar(Organizador);
        repositorio.Confirmacao = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tarefa = casoDeUso.ExecutarAsync(new CriarVaquinhaComando("Viagem"), CancellationToken.None);

        Assert.False(tarefa.IsCompleted);
        Assert.Empty(repositorio.Gravadas);

        repositorio.Confirmacao.SetResult();
        var resultado = await tarefa;

        Assert.Equal(IdGerado, resultado.Id);
        Assert.Single(repositorio.Gravadas);
    }

    [Fact]
    public async Task Falha_de_gravacao_nao_produz_resultado()
    {
        var (casoDeUso, repositorio, _, _, _) = Montar(Organizador);
        var falha = new IOException("banco indisponível");
        repositorio.FalhaAoAdicionar = falha;

        var excecao = await Assert.ThrowsAsync<FalhaNoRepositorioException>(
            () => casoDeUso.ExecutarAsync(new CriarVaquinhaComando("Viagem"), CancellationToken.None));

        Assert.Same(falha, excecao.InnerException);
        Assert.Empty(repositorio.Gravadas);
    }

    [Fact]
    public async Task Falha_depois_de_espera_nao_produz_resultado()
    {
        var (casoDeUso, repositorio, _, _, _) = Montar(Organizador);
        repositorio.Confirmacao = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var falha = new TimeoutException("sem confirmação");

        var tarefa = casoDeUso.ExecutarAsync(new CriarVaquinhaComando("Viagem"), CancellationToken.None);
        Assert.False(tarefa.IsCompleted);
        repositorio.Confirmacao.SetException(falha);

        var excecao = await Assert.ThrowsAsync<FalhaNoRepositorioException>(() => tarefa);
        Assert.Same(falha, excecao.InnerException);
    }

    [Fact]
    public async Task Falha_sincrona_do_repositorio_nao_produz_resultado()
    {
        var (casoDeUso, repositorio, _, _, _) = Montar(Organizador);
        var falha = new InvalidOperationException("conexão fechada");
        repositorio.FalhaSincronaAoAdicionar = falha;

        var excecao = await Assert.ThrowsAsync<FalhaNoRepositorioException>(
            () => casoDeUso.ExecutarAsync(new CriarVaquinhaComando("Viagem"), CancellationToken.None));

        Assert.Same(falha, excecao.InnerException);
    }

    [Fact]
    public async Task Cancelamento_do_repositorio_nao_e_convertido_em_falha()
    {
        var (casoDeUso, repositorio, _, _, _) = Montar(Organizador);
        repositorio.FalhaAoAdicionar = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => casoDeUso.ExecutarAsync(new CriarVaquinhaComando("Viagem"), CancellationToken.None));
    }

    // ---------- Dependências ----------

    [Fact]
    public void Construtor_exige_todas_as_dependencias()
    {
        var ator = new AtorFixo(Organizador);
        var repositorio = new RepositorioFalso();
        var gerador = new GeradorFixo(IdGerado);
        var relogio = new RelogioFixo(Agora);

        Assert.Throws<ArgumentNullException>(() => new CriarVaquinha(null!, repositorio, gerador, relogio));
        Assert.Throws<ArgumentNullException>(() => new CriarVaquinha(ator, null!, gerador, relogio));
        Assert.Throws<ArgumentNullException>(() => new CriarVaquinha(ator, repositorio, null!, relogio));
        Assert.Throws<ArgumentNullException>(() => new CriarVaquinha(ator, repositorio, gerador, null!));
    }

    // ---------- Auxiliares ----------

    private static (CriarVaquinha CasoDeUso, RepositorioFalso Repositorio, GeradorFixo Gerador, RelogioFixo Relogio, AtorFixo Ator) Montar(
        Guid? ator, Guid[]? ids = null, DateTimeOffset? agora = null)
    {
        var atorFixo = new AtorFixo(ator);
        var repositorio = new RepositorioFalso();
        var gerador = new GeradorFixo(ids ?? [IdGerado]);
        var relogio = new RelogioFixo(agora ?? Agora);
        return (new CriarVaquinha(atorFixo, repositorio, gerador, relogio), repositorio, gerador, relogio, atorFixo);
    }
}
