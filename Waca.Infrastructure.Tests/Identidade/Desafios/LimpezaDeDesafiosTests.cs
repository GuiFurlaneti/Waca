using Waca.Infrastructure.Identidade.Desafios;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;

namespace Waca.Infrastructure.Tests.Identidade.Desafios;

[Collection(ColecaoPostgres.Nome)]
public class LimpezaDeDesafiosTests(ServidorPostgres servidor)
{
    private const string Email = "ana@exemplo.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Remove_somente_desafios_expirados_ha_mais_que_a_retencao_e_e_idempotente()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (antigo, _) = await ambiente.EmitirAsync(Email);

        // Expirou há exatamente a retenção: ainda fica.
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(10) + TimeSpan.FromDays(7));
        var (recente, _) = await ambiente.EmitirAsync("bia@exemplo.com");
        Assert.Equal(0, await ambiente.Limpeza.RemoverAntigosAsync(Ct));

        ambiente.Relogio.Avancar(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await ambiente.Limpeza.RemoverAntigosAsync(Ct));
        Assert.Equal(0, await ambiente.Limpeza.RemoverAntigosAsync(Ct));

        Assert.Null(await ambiente.LerAsync(antigo.DesafioId));
        Assert.NotNull(await ambiente.LerAsync(recente.DesafioId));
    }

    [Fact]
    public async Task Limpeza_nao_devolve_cota_dentro_das_janelas()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        for (var i = 0; i < 3; i++)
        {
            await ambiente.EmitirAsync(Email);
            ambiente.Relogio.Avancar(TimeSpan.FromSeconds(61));
        }

        Assert.Equal(0, await ambiente.Limpeza.RemoverAntigosAsync(Ct));
        Assert.Equal(LimiteDeEmissao.JanelaCurta, (await ambiente.EmitirAsync(Email)).Resultado.Limite);

        // No fim da janela longa, nada que ainda conte para a cota pode ter sido removido.
        ambiente.Relogio.Avancar(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(3 * 61) - TimeSpan.FromSeconds(1));
        Assert.Equal(0, await ambiente.Limpeza.RemoverAntigosAsync(Ct));
        Assert.Equal(3, await ambiente.ContarDesafiosAsync());
    }

    [Fact]
    public async Task Limpeza_nao_reabre_desafio_consumido_nem_expirado()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        var (consumido, codigoConsumido) = await ambiente.EmitirAsync(Email);
        var (expirado, codigoExpirado) = await ambiente.EmitirAsync("bia@exemplo.com");
        Assert.True((await ambiente.VerificarAsync(consumido.DesafioId, Email, codigoConsumido)).Consumido);

        ambiente.Relogio.Avancar(TimeSpan.FromDays(8));
        Assert.Equal(2, await ambiente.Limpeza.RemoverAntigosAsync(Ct));

        Assert.Equal(MotivoDaRecusa.NaoEncontrado, (await ambiente.VerificarAsync(consumido.DesafioId, Email, codigoConsumido)).Motivo);
        Assert.Equal(MotivoDaRecusa.NaoEncontrado, (await ambiente.VerificarAsync(expirado.DesafioId, "bia@exemplo.com", codigoExpirado)).Motivo);
        Assert.Equal(0, await ambiente.ContarDesafiosAsync());
    }

    [Fact]
    public async Task Limpeza_nao_afeta_usuarios_nem_outras_tabelas()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var ambiente = new AmbienteDeDesafios(banco);
        await using (var contexto = banco.CriarContextoDeIdentidade())
        {
            contexto.Users.Add(IdentidadeNoBancoDeTeste.NovaConta(Email));
            await contexto.SaveChangesAsync(Ct);
        }

        await ambiente.EmitirAsync(Email);
        ambiente.Relogio.Avancar(TimeSpan.FromDays(30));
        await ambiente.Limpeza.RemoverAntigosAsync(Ct);

        Assert.Equal(1, await banco.ContarAsync("SELECT count(*) FROM identidade.\"AspNetUsers\""));
        Assert.Equal(0, await banco.ContarAsync("SELECT count(*) FROM public.vaquinhas"));
    }
}
