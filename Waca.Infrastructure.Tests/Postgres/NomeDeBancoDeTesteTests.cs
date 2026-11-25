using Xunit;

namespace Waca.Infrastructure.Tests.Postgres;

/// <summary>Validação dos nomes de bancos de teste. Não usa Docker nem banco.</summary>
public class NomeDeBancoDeTesteTests
{
    private const string Hex32 = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Nomes_gerados_seguem_o_padrao_e_sao_distintos()
    {
        var nomes = Enumerable.Range(0, 1_000).Select(_ => NomeDeBancoDeTeste.Gerar()).ToArray();

        Assert.All(nomes, nome =>
        {
            Assert.Matches("^waca_teste_[0-9a-f]{32}$", nome);
            Assert.True(NomeDeBancoDeTeste.EhValido(nome));
        });
        Assert.Equal(nomes.Length, nomes.Distinct().Count());
    }

    [Fact]
    public void Nome_no_padrao_e_aceito()
    {
        Assert.Equal("waca_teste_" + Hex32, NomeDeBancoDeTeste.Validar("waca_teste_" + Hex32));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("postgres")]
    [InlineData("POSTGRES")]
    [InlineData("template0")]
    [InlineData("template1")]
    [InlineData("waca_teste_")]
    [InlineData("waca_teste_0123456789abcdef0123456789abcde")]
    [InlineData("waca_teste_0123456789abcdef0123456789abcdef0")]
    [InlineData("waca_teste_0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("waca_teste_0123456789abcdef0123456789abcdeg")]
    [InlineData("WACA_TESTE_0123456789abcdef0123456789abcdef")]
    [InlineData("outro_0123456789abcdef0123456789abcdef")]
    [InlineData(" waca_teste_0123456789abcdef0123456789abcdef")]
    [InlineData("waca_teste_0123456789abcdef0123456789abcdef ")]
    [InlineData("waca_teste_0123456789abcdef0123456789abcdef\n")]
    [InlineData("waca_teste_0123456789abcdef0123456789abcdef\r\n")]
    [InlineData("waca_teste_0123456789abcdef0123456789abcdef\"; DROP DATABASE postgres; --")]
    [InlineData("waca_teste_0123456789abcdef0123456789abcd\"x")]
    [InlineData("waca_teste_0123456789abcdef0123456789abcd١٢")]
    public void Nomes_fora_do_padrao_sao_rejeitados(string? nome)
    {
        Assert.False(NomeDeBancoDeTeste.EhValido(nome));
        Assert.Throws<ArgumentException>(() => NomeDeBancoDeTeste.Validar(nome));
        Assert.ThrowsAny<ArgumentException>(() => NomeDeBancoDeTeste.ComandoCriar(nome!));
        Assert.ThrowsAny<ArgumentException>(() => NomeDeBancoDeTeste.ComandoExcluir(nome!));
    }

    [Fact]
    public void Comandos_usam_somente_o_identificador_validado_e_delimitado()
    {
        var nome = "waca_teste_" + Hex32;

        Assert.Equal($"CREATE DATABASE \"{nome}\"", NomeDeBancoDeTeste.ComandoCriar(nome));
        Assert.Equal($"DROP DATABASE IF EXISTS \"{nome}\" WITH (FORCE)", NomeDeBancoDeTeste.ComandoExcluir(nome));
    }
}
