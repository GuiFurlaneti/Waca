using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Waca.Infrastructure.Identidade.Desafios;
using Xunit;

namespace Waca.Infrastructure.Tests.Identidade.Desafios;

/// <summary>Geração do código, HMAC, chaves e opções, sem banco.</summary>
public class CodigoEChaveTests
{
    private static readonly Guid Desafio = Guid.CreateVersion7();

    [Fact]
    public void Codigo_gerado_tem_exatamente_oito_digitos_ascii()
    {
        for (var i = 0; i < 1000; i++)
        {
            var codigo = GeradorDeCodigo.Gerar(8);

            Assert.Equal(8, codigo.Length);
            Assert.All(codigo, c => Assert.True(char.IsAsciiDigit(c)));
            Assert.True(GeradorDeCodigo.TemFormatoValido(codigo, 8));
        }
    }

    [Theory]
    [InlineData(0, "00000000")]
    [InlineData(42, "00000042")]
    [InlineData(7_654_321, "07654321")]
    [InlineData(99_999_999, "99999999")]
    public void Formatacao_preserva_zeros_a_esquerda(int valor, string esperado)
    {
        Assert.Equal(esperado, GeradorDeCodigo.Formatar(valor, 8));
    }

    [Fact]
    public void Codigos_gerados_variam_e_podem_comecar_com_zero()
    {
        var codigos = Enumerable.Range(0, 2000).Select(_ => GeradorDeCodigo.Gerar(8)).ToArray();

        // Com 10^8 valores equiprováveis, ~10% começam com zero; a chance de nenhum em 2000 é desprezível.
        Assert.Contains(codigos, c => c[0] == '0');
        Assert.True(codigos.Distinct().Count() > 1990);
    }

    [Theory]
    [InlineData(-1, 8)]
    [InlineData(100_000_000, 8)]
    [InlineData(1, 5)]
    [InlineData(1, 10)]
    public void Formatacao_recusa_valores_ou_tamanhos_fora_do_intervalo(int valor, int digitos)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GeradorDeCodigo.Formatar(valor, digitos));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("1234567a")]
    [InlineData(" 1234567")]
    [InlineData("1234-678")]
    [InlineData("١٢٣٤٥٦٧٨")]
    public void Formato_invalido_e_reconhecido(string? codigo)
    {
        Assert.False(GeradorDeCodigo.TemFormatoValido(codigo, 8));
    }

    [Fact]
    public void Hmac_tem_32_bytes_e_e_deterministico()
    {
        var chave = AmbienteDeDesafios.NovaChave();

        var primeiro = ProtecaoDoCodigo.Calcular(chave, Desafio, FinalidadeDesafio.Acesso, "ANA@EXEMPLO.COM", "01234567");
        var segundo = ProtecaoDoCodigo.Calcular(chave, Desafio, FinalidadeDesafio.Acesso, "ANA@EXEMPLO.COM", "01234567");

        Assert.Equal(32, primeiro.Length);
        Assert.Equal(primeiro, segundo);
        Assert.True(ProtecaoDoCodigo.Confere(primeiro, segundo));
    }

    [Fact]
    public void Hmac_e_vinculado_ao_desafio_finalidade_email_codigo_e_chave()
    {
        var chave = AmbienteDeDesafios.NovaChave();
        var base_ = ProtecaoDoCodigo.Calcular(chave, Desafio, FinalidadeDesafio.Acesso, "ANA@EXEMPLO.COM", "01234567");

        byte[][] variacoes =
        [
            ProtecaoDoCodigo.Calcular(chave, Guid.CreateVersion7(), FinalidadeDesafio.Acesso, "ANA@EXEMPLO.COM", "01234567"),
            ProtecaoDoCodigo.Calcular(chave, Desafio, FinalidadeDesafio.TrocaDeEmail, "ANA@EXEMPLO.COM", "01234567"),
            ProtecaoDoCodigo.Calcular(chave, Desafio, FinalidadeDesafio.Acesso, "BIA@EXEMPLO.COM", "01234567"),
            ProtecaoDoCodigo.Calcular(chave, Desafio, FinalidadeDesafio.Acesso, "ANA@EXEMPLO.COM", "01234568"),
            ProtecaoDoCodigo.Calcular(AmbienteDeDesafios.NovaChave(), Desafio, FinalidadeDesafio.Acesso, "ANA@EXEMPLO.COM", "01234567"),
        ];

        Assert.All(variacoes, v => Assert.False(ProtecaoDoCodigo.Confere(base_, v)));
    }

    [Fact]
    public void Codificacao_dos_campos_nao_e_ambigua()
    {
        var chave = AmbienteDeDesafios.NovaChave();

        var a = ProtecaoDoCodigo.Calcular(chave, Desafio, FinalidadeDesafio.Acesso, "A@B.C1", "2345678");
        var b = ProtecaoDoCodigo.Calcular(chave, Desafio, FinalidadeDesafio.Acesso, "A@B.C", "12345678");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Hmac_nao_e_um_SHA256_simples_do_codigo()
    {
        var hmac = ProtecaoDoCodigo.Calcular(AmbienteDeDesafios.NovaChave(), Desafio, FinalidadeDesafio.Acesso, "ANA@EXEMPLO.COM", "01234567");

        Assert.NotEqual(SHA256.HashData(Encoding.UTF8.GetBytes("01234567")), hmac);
        Assert.NotEqual(SHA256.HashData(Encoding.UTF8.GetBytes(Desafio + "AcessoANA@EXEMPLO.COM01234567")), hmac);
    }

    [Fact]
    public void Comparacao_recusa_tamanho_diferente()
    {
        var hmac = ProtecaoDoCodigo.Calcular(AmbienteDeDesafios.NovaChave(), Desafio, FinalidadeDesafio.Acesso, "A@B.C", "01234567");

        Assert.False(ProtecaoDoCodigo.Confere(hmac[..31], hmac[..31]));
        Assert.False(ProtecaoDoCodigo.Confere([], []));
    }

    [Fact]
    public void Chave_valida_e_aceita_e_nao_aparece_em_ToString()
    {
        var base64 = Convert.ToBase64String(AmbienteDeDesafios.NovaChave());
        var opcoes = new OpcoesDaChaveDeDesafios { VersaoAtual = 1, Chaves = { [1] = base64 } };

        var chaveiro = ChaveiroDeDesafios.DeConfiguracao(opcoes);

        Assert.Equal(1, chaveiro.VersaoAtual);
        Assert.DoesNotContain(base64, chaveiro.ToString());
        Assert.DoesNotContain(base64, opcoes.ToString());
    }

    [Theory]
    [InlineData("sem-opcoes")]
    [InlineData("sem-chaves")]
    [InlineData("base64-invalido")]
    [InlineData("vazia")]
    [InlineData("curta")]
    [InlineData("longa")]
    [InlineData("trivial")]
    [InlineData("versao-atual-ausente")]
    [InlineData("versao-zero")]
    [InlineData("versao-negativa")]
    [InlineData("repetida")]
    public void Chave_ausente_ou_invalida_e_recusada(string caso)
    {
        var valida = Convert.ToBase64String(AmbienteDeDesafios.NovaChave());
        OpcoesDaChaveDeDesafios? opcoes = caso switch
        {
            "sem-opcoes" => null,
            "sem-chaves" => new() { VersaoAtual = 1 },
            "base64-invalido" => new() { VersaoAtual = 1, Chaves = { [1] = "não é base64!" } },
            "vazia" => new() { VersaoAtual = 1, Chaves = { [1] = "  " } },
            "curta" => new() { VersaoAtual = 1, Chaves = { [1] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(31)) } },
            "longa" => new() { VersaoAtual = 1, Chaves = { [1] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(65)) } },
            "trivial" => new() { VersaoAtual = 1, Chaves = { [1] = Convert.ToBase64String(new byte[32]) } },
            "versao-atual-ausente" => new() { VersaoAtual = 2, Chaves = { [1] = valida } },
            "versao-zero" => new() { VersaoAtual = 0, Chaves = { [0] = valida } },
            "versao-negativa" => new() { VersaoAtual = 1, Chaves = { [1] = valida, [-1] = Convert.ToBase64String(AmbienteDeDesafios.NovaChave()) } },
            "repetida" => new() { VersaoAtual = 1, Chaves = { [1] = valida, [2] = valida } },
            _ => throw new ArgumentOutOfRangeException(nameof(caso)),
        };

        Assert.ThrowsAny<ArgumentException>(() => ChaveiroDeDesafios.DeConfiguracao(opcoes));
    }

    [Fact]
    public void Opcoes_padrao_seguem_os_parametros_provisorios()
    {
        var opcoes = new OpcoesDeDesafios();

        opcoes.Validar();
        Assert.Equal(8, opcoes.DigitosDoCodigo);
        Assert.Equal(TimeSpan.FromMinutes(10), opcoes.Validade);
        Assert.Equal(5, opcoes.MaximoDeTentativas);
        Assert.Equal(TimeSpan.FromSeconds(60), opcoes.IntervaloMinimoEntreEmissoes);
        Assert.Equal((TimeSpan.FromMinutes(15), 3), (opcoes.JanelaCurta, opcoes.MaximoDeEmissoesNaJanelaCurta));
        Assert.Equal((TimeSpan.FromHours(24), 10), (opcoes.JanelaLonga, opcoes.MaximoDeEmissoesNaJanelaLonga));
        Assert.True(opcoes.Retencao >= opcoes.JanelaLonga);
    }

    [Theory]
    [InlineData("digitos-5")]
    [InlineData("digitos-10")]
    [InlineData("validade-curta")]
    [InlineData("validade-longa")]
    [InlineData("tentativas-0")]
    [InlineData("tentativas-11")]
    [InlineData("intervalo-negativo")]
    [InlineData("intervalo-maior-que-janela")]
    [InlineData("janela-curta-zero")]
    [InlineData("janela-longa-igual-a-curta")]
    [InlineData("janela-longa-acima-de-7-dias")]
    [InlineData("cota-curta-zero")]
    [InlineData("cota-longa-menor-que-curta")]
    [InlineData("retencao-menor-que-janela-longa")]
    public void Opcoes_invalidas_impedem_a_construcao_dos_componentes(string caso)
    {
        var opcoes = caso switch
        {
            "digitos-5" => new OpcoesDeDesafios { DigitosDoCodigo = 5 },
            "digitos-10" => new OpcoesDeDesafios { DigitosDoCodigo = 10 },
            "validade-curta" => new OpcoesDeDesafios { Validade = TimeSpan.FromSeconds(30) },
            "validade-longa" => new OpcoesDeDesafios { Validade = TimeSpan.FromHours(2) },
            "tentativas-0" => new OpcoesDeDesafios { MaximoDeTentativas = 0 },
            "tentativas-11" => new OpcoesDeDesafios { MaximoDeTentativas = 11 },
            "intervalo-negativo" => new OpcoesDeDesafios { IntervaloMinimoEntreEmissoes = TimeSpan.FromSeconds(-1) },
            "intervalo-maior-que-janela" => new OpcoesDeDesafios { IntervaloMinimoEntreEmissoes = TimeSpan.FromMinutes(16) },
            "janela-curta-zero" => new OpcoesDeDesafios { JanelaCurta = TimeSpan.Zero },
            "janela-longa-igual-a-curta" => new OpcoesDeDesafios { JanelaLonga = TimeSpan.FromMinutes(15) },
            "janela-longa-acima-de-7-dias" => new OpcoesDeDesafios { JanelaLonga = TimeSpan.FromDays(8), Retencao = TimeSpan.FromDays(30) },
            "cota-curta-zero" => new OpcoesDeDesafios { MaximoDeEmissoesNaJanelaCurta = 0 },
            "cota-longa-menor-que-curta" => new OpcoesDeDesafios { MaximoDeEmissoesNaJanelaLonga = 2 },
            "retencao-menor-que-janela-longa" => new OpcoesDeDesafios { Retencao = TimeSpan.FromHours(23) },
            _ => throw new ArgumentOutOfRangeException(nameof(caso)),
        };

        var chaveiro = new ChaveiroDeDesafios(1, new Dictionary<short, byte[]> { [1] = AmbienteDeDesafios.NovaChave() });
        var fabrica = new FabricaSemBanco();
        var normalizador = AmbienteDeDesafios.Normalizador;

        Assert.Throws<InvalidOperationException>(opcoes.Validar);
        Assert.Throws<InvalidOperationException>(() => new EmissorDeDesafios(
            fabrica, chaveiro, opcoes, normalizador, new CaixaDeEntregaEmMemoria(), TimeProvider.System, NullLogger<EmissorDeDesafios>.Instance));
        Assert.Throws<InvalidOperationException>(() => new VerificadorDeDesafios(
            fabrica, chaveiro, opcoes, normalizador, TimeProvider.System, NullLogger<VerificadorDeDesafios>.Instance));
        Assert.Throws<InvalidOperationException>(() => new LimpezaDeDesafios(fabrica, opcoes, TimeProvider.System));
    }

    [Fact]
    public void Pedido_de_verificacao_nao_expoe_o_codigo_em_ToString()
    {
        var pedido = new PedidoDeVerificacao(Desafio, FinalidadeDesafio.Acesso, "ana@exemplo.com", "01234567");

        Assert.DoesNotContain("01234567", pedido.ToString());
        Assert.DoesNotContain("ana@exemplo.com", pedido.ToString());
    }
}
