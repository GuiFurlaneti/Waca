using Waca.Application.Comum;
using Waca.Application.Vaquinhas;
using Waca.Infrastructure.Persistencia.Repositorios;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;

namespace Waca.Infrastructure.Tests.Persistencia;

/// <summary>
/// Casos de uso aprovados executados sobre o repositório real, com ator de teste explicitamente controlado.
/// </summary>
[Collection(ColecaoPostgres.Nome)]
public class CompatibilidadeApplicationTests(ServidorPostgres servidor)
{
    private static readonly Guid OrganizadorA = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000aa");
    private static readonly Guid OrganizadorB = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000bb");
    private static readonly Guid IdGerado = Guid.Parse("019a9b00-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset Agora = new(2026, 11, 25, 13, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Criar_pela_Application_e_listar_com_novo_contexto_preserva_os_dados()
    {
        await using var banco = await servidor.CriarBancoAsync();

        VaquinhaResumo criada;
        await using (var contexto = banco.CriarContexto())
        {
            var criar = new CriarVaquinha(
                new AtorFixo(OrganizadorA), new RepositorioVaquinhas(contexto), new GeradorFixo(IdGerado), new RelogioFixo(Agora));
            criada = await criar.ExecutarAsync(new CriarVaquinhaComando("  Viagem da galera  "), Ct);
        }

        IReadOnlyList<VaquinhaResumo> deA;
        IReadOnlyList<VaquinhaResumo> deB;
        await using (var contexto = banco.CriarContexto())
        {
            deA = await new ListarMinhasVaquinhas(new AtorFixo(OrganizadorA), new RepositorioVaquinhas(contexto)).ExecutarAsync(Ct);
        }

        await using (var contexto = banco.CriarContexto())
        {
            deB = await new ListarMinhasVaquinhas(new AtorFixo(OrganizadorB), new RepositorioVaquinhas(contexto)).ExecutarAsync(Ct);
        }

        Assert.Equal(new VaquinhaResumo(IdGerado, "Viagem da galera", Agora), criada);
        Assert.Equal([criada], deA);
        Assert.Empty(deB);
    }

    [Fact]
    public async Task Criacao_grava_somente_a_linha_da_vaquinha()
    {
        await using var banco = await servidor.CriarBancoAsync();

        await using (var contexto = banco.CriarContexto())
        {
            await new CriarVaquinha(
                    new AtorFixo(OrganizadorA), new RepositorioVaquinhas(contexto), new GeradorFixo(IdGerado), new RelogioFixo(Agora))
                .ExecutarAsync(new CriarVaquinhaComando("Viagem"), Ct);
        }

        var linha = Assert.Single(await banco.ConsultarAsync("SELECT id, nome, organizador_id FROM vaquinhas"));
        Assert.Equal(IdGerado, linha[0]);
        Assert.Equal("Viagem", linha[1]);
        Assert.Equal(OrganizadorA, linha[2]);
        Assert.Equal(1, await banco.ContarAsync(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'"));
    }

    [Fact]
    public async Task Identificador_repetido_chega_a_Application_como_falha_do_repositorio()
    {
        await using var banco = await servidor.CriarBancoAsync();

        await using (var contexto = banco.CriarContexto())
        {
            await new CriarVaquinha(
                    new AtorFixo(OrganizadorA), new RepositorioVaquinhas(contexto), new GeradorFixo(IdGerado), new RelogioFixo(Agora))
                .ExecutarAsync(new CriarVaquinhaComando("Primeira"), Ct);
        }

        await using (var contexto = banco.CriarContexto())
        {
            var criar = new CriarVaquinha(
                new AtorFixo(OrganizadorA), new RepositorioVaquinhas(contexto), new GeradorFixo(IdGerado), new RelogioFixo(Agora));

            await Assert.ThrowsAsync<FalhaNoRepositorioException>(
                () => criar.ExecutarAsync(new CriarVaquinhaComando("Segunda"), Ct));
        }

        Assert.Equal("Primeira", (string)Assert.Single(await banco.ConsultarAsync("SELECT nome FROM vaquinhas"))[0]!);
    }

    [Fact]
    public async Task Linha_invalida_chega_a_Application_como_falha_do_repositorio()
    {
        await using var banco = await servidor.CriarBancoAsync();
        await banco.ExecutarAsync(
            $"INSERT INTO vaquinhas (id, nome, organizador_id, criada_em) VALUES ('{IdGerado}', '   ', '{OrganizadorA}', now())");

        await using var contexto = banco.CriarContexto();
        var listar = new ListarMinhasVaquinhas(new AtorFixo(OrganizadorA), new RepositorioVaquinhas(contexto));

        var excecao = await Assert.ThrowsAsync<FalhaNoRepositorioException>(() => listar.ExecutarAsync(Ct));
        Assert.IsType<InvalidOperationException>(excecao.InnerException);
    }
}
