using System.Reflection;
using Waca.Application.Comum;
using Waca.Application.Vaquinhas;
using Waca.Domain.Vaquinhas;
using Xunit;

namespace Waca.Application.Tests.Vaquinhas;

public class ListarMinhasVaquinhasTests
{
    private static readonly Guid OrganizadorA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid OrganizadorB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset Criacao = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly Vaquinha ViagemDeA = Nova("10000000-0000-7000-8000-00000000000a", "Viagem", OrganizadorA, 0);
    private static readonly Vaquinha FormaturaDeA = Nova("20000000-0000-7000-8000-00000000000a", "Formatura", OrganizadorA, 1);
    private static readonly Vaquinha ChurrascoDeB = Nova("30000000-0000-7000-8000-00000000000b", "Churrasco", OrganizadorB, 2);

    // ---------- Isolamento entre organizadores ----------

    [Fact]
    public async Task Organizador_A_recebe_somente_as_proprias_vaquinhas()
    {
        var repositorio = RepositorioCom(ViagemDeA, ChurrascoDeB, FormaturaDeA);

        var resultado = await Listar(OrganizadorA, repositorio);

        Assert.Equal(
            [
                new VaquinhaResumo(ViagemDeA.Id, "Viagem", ViagemDeA.CriadaEm),
                new VaquinhaResumo(FormaturaDeA.Id, "Formatura", FormaturaDeA.CriadaEm),
            ],
            resultado);
        Assert.Equal([OrganizadorA], repositorio.OrganizadoresConsultados);
    }

    [Fact]
    public async Task Organizador_B_nao_recebe_vaquinhas_de_A()
    {
        var repositorio = RepositorioCom(ViagemDeA, ChurrascoDeB, FormaturaDeA);

        var resultado = await Listar(OrganizadorB, repositorio);

        Assert.Equal([new VaquinhaResumo(ChurrascoDeB.Id, "Churrasco", ChurrascoDeB.CriadaEm)], resultado);
        Assert.Equal([OrganizadorB], repositorio.OrganizadoresConsultados);
    }

    [Fact]
    public async Task Vaquinhas_de_outro_organizador_devolvidas_por_repositorio_defeituoso_sao_descartadas()
    {
        var repositorio = RepositorioCom(ViagemDeA, ChurrascoDeB, FormaturaDeA);
        repositorio.RespostaDaListagem = _ => [ViagemDeA, ChurrascoDeB, FormaturaDeA];

        var resultado = await Listar(OrganizadorA, repositorio);

        Assert.Equal([ViagemDeA.Id, FormaturaDeA.Id], resultado.Select(v => v.Id));
        Assert.DoesNotContain(resultado, v => v.Id == ChurrascoDeB.Id);
    }

    [Fact]
    public async Task Repositorio_defeituoso_que_so_devolve_dados_de_outro_organizador_resulta_em_lista_vazia()
    {
        var repositorio = new RepositorioFalso { RespostaDaListagem = _ => [ChurrascoDeB] };

        var resultado = await Listar(OrganizadorA, repositorio);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task Itens_nulos_devolvidos_pelo_repositorio_sao_ignorados()
    {
        var repositorio = new RepositorioFalso { RespostaDaListagem = _ => [null!, ViagemDeA] };

        var resultado = await Listar(OrganizadorA, repositorio);

        Assert.Equal([ViagemDeA.Id], resultado.Select(v => v.Id));
    }

    [Fact]
    public async Task Criacao_e_consulta_respeitam_o_ator_de_cada_chamada()
    {
        var repositorio = new RepositorioFalso();
        var gerador = new GeradorFixo(ViagemDeA.Id);
        var criar = new CriarVaquinha(new AtorFixo(OrganizadorA), repositorio, gerador, new RelogioFixo(Criacao));

        var criada = await criar.ExecutarAsync(new CriarVaquinhaComando("Viagem"), CancellationToken.None);
        var deA = await Listar(OrganizadorA, repositorio);
        var deB = await Listar(OrganizadorB, repositorio);

        Assert.Equal([criada], deA);
        Assert.Empty(deB);
    }

    // ---------- Resultado ----------

    [Fact]
    public async Task Sem_vaquinhas_devolve_lista_vazia()
    {
        var repositorio = RepositorioCom(ChurrascoDeB);

        var resultado = await Listar(OrganizadorA, repositorio);

        Assert.NotNull(resultado);
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task Ordem_devolvida_pelo_repositorio_e_preservada()
    {
        var repositorio = new RepositorioFalso { RespostaDaListagem = _ => [FormaturaDeA, ViagemDeA] };

        var resultado = await Listar(OrganizadorA, repositorio);

        Assert.Equal([FormaturaDeA.Id, ViagemDeA.Id], resultado.Select(v => v.Id));
    }

    [Fact]
    public void Resposta_contem_apenas_id_nome_e_instante_de_criacao()
    {
        var propriedades = typeof(VaquinhaResumo)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (p.Name, p.PropertyType))
            .OrderBy(p => p.Name)
            .ToArray();

        Assert.Equal(
            [("CriadaEm", typeof(DateTimeOffset)), ("Id", typeof(Guid)), ("Nome", typeof(string))],
            propriedades);
    }

    [Fact]
    public void Consulta_nao_recebe_parametro_para_escolher_o_organizador()
    {
        var parametros = typeof(ListarMinhasVaquinhas)
            .GetMethod(nameof(ListarMinhasVaquinhas.ExecutarAsync))!
            .GetParameters()
            .Select(p => p.ParameterType);

        Assert.Equal([typeof(CancellationToken)], parametros);
    }

    // ---------- Ator ----------

    [Fact]
    public async Task Ator_ausente_nao_consulta_o_repositorio()
    {
        var repositorio = RepositorioCom(ViagemDeA);

        await Assert.ThrowsAsync<AtorNaoIdentificadoException>(() => Listar(null, repositorio));

        Assert.Empty(repositorio.OrganizadoresConsultados);
    }

    [Fact]
    public async Task Ator_com_Guid_vazio_nao_consulta_o_repositorio()
    {
        var repositorio = RepositorioCom(ViagemDeA);

        await Assert.ThrowsAsync<AtorNaoIdentificadoException>(() => Listar(Guid.Empty, repositorio));

        Assert.Empty(repositorio.OrganizadoresConsultados);
    }

    // ---------- Repositório ----------

    [Fact]
    public async Task Falha_na_consulta_nao_produz_resultado()
    {
        var falha = new IOException("banco indisponível");
        var repositorio = new RepositorioFalso { FalhaAoListar = falha };

        var excecao = await Assert.ThrowsAsync<FalhaNoRepositorioException>(() => Listar(OrganizadorA, repositorio));

        Assert.Same(falha, excecao.InnerException);
    }

    [Fact]
    public async Task Resultado_nulo_do_repositorio_e_tratado_como_falha()
    {
        var repositorio = new RepositorioFalso { RespostaDaListagem = _ => null! };

        await Assert.ThrowsAsync<FalhaNoRepositorioException>(() => Listar(OrganizadorA, repositorio));
    }

    [Fact]
    public async Task Cancelamento_da_consulta_nao_e_convertido_em_falha()
    {
        var repositorio = new RepositorioFalso { FalhaAoListar = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() => Listar(OrganizadorA, repositorio));
    }

    [Fact]
    public async Task Token_de_cancelamento_e_repassado_ao_repositorio()
    {
        var repositorio = RepositorioCom(ViagemDeA);
        using var fonte = new CancellationTokenSource();

        await new ListarMinhasVaquinhas(new AtorFixo(OrganizadorA), repositorio).ExecutarAsync(fonte.Token);

        Assert.Equal(fonte.Token, repositorio.UltimoToken);
    }

    [Fact]
    public void Construtor_exige_todas_as_dependencias()
    {
        Assert.Throws<ArgumentNullException>(() => new ListarMinhasVaquinhas(null!, new RepositorioFalso()));
        Assert.Throws<ArgumentNullException>(() => new ListarMinhasVaquinhas(new AtorFixo(OrganizadorA), null!));
    }

    // ---------- Auxiliares ----------

    private static Task<IReadOnlyList<VaquinhaResumo>> Listar(Guid? ator, RepositorioFalso repositorio) =>
        new ListarMinhasVaquinhas(new AtorFixo(ator), repositorio).ExecutarAsync(CancellationToken.None);

    private static RepositorioFalso RepositorioCom(params Vaquinha[] vaquinhas)
    {
        var repositorio = new RepositorioFalso();
        repositorio.Gravadas.AddRange(vaquinhas);
        return repositorio;
    }

    private static Vaquinha Nova(string id, string nome, Guid organizador, int minutos) =>
        new(Guid.Parse(id), nome, organizador, Criacao.AddMinutes(minutos));
}
