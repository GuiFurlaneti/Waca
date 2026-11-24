using System.Globalization;
using Waca.Domain.ObjetosDeValor;
using Xunit;

namespace Waca.Domain.Tests.ObjetosDeValor;

public class ValorMonetarioTests
{
    public static TheoryData<decimal> ValoresValidos => new()
    {
        0m,
        0.01m,
        10.50m,
        10.500m, // zeros à direita não são fração efetiva de centavo
    };

    [Theory]
    [MemberData(nameof(ValoresValidos))]
    public void Criar_aceita_valores_com_precisao_de_centavos(decimal valor)
    {
        var monetario = ValorMonetario.Criar(valor);

        Assert.Equal(valor, monetario.Valor);
    }

    public static TheoryData<decimal> ValoresComFracaoDeCentavo => new()
    {
        10.005m,
        0.001m,
    };

    [Theory]
    [MemberData(nameof(ValoresComFracaoDeCentavo))]
    public void Criar_rejeita_fracao_de_centavo_sem_arredondar(decimal valor)
    {
        var excecao = Assert.Throws<ArgumentException>(() => ValorMonetario.Criar(valor));

        Assert.Equal("valor", excecao.ParamName);
    }

    public static TheoryData<decimal> ValoresNegativos => new()
    {
        -0.01m,
        -10.50m,
    };

    [Theory]
    [MemberData(nameof(ValoresNegativos))]
    public void Criar_rejeita_valor_negativo(decimal valor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ValorMonetario.Criar(valor));
    }

    public static TheoryData<decimal> ValoresSemResolucaoDeCentavos => new()
    {
        decimal.MaxValue,
        1_000_000_000_000_000_000_000_000_000m,
    };

    [Theory]
    [MemberData(nameof(ValoresSemResolucaoDeCentavos))]
    public void Criar_rejeita_valor_acima_do_maximo_representavel(decimal valor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ValorMonetario.Criar(valor));
    }

    [Fact]
    public void Valores_numericamente_equivalentes_sao_iguais()
    {
        var a = ValorMonetario.Criar(10.5m);
        var b = ValorMonetario.Criar(10.50m);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Valores_diferentes_nao_sao_iguais()
    {
        var a = ValorMonetario.Criar(10.50m);
        var b = ValorMonetario.Criar(10.51m);

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void Somar_produz_resultado_exato()
    {
        var soma = ValorMonetario.Criar(0.10m).Somar(ValorMonetario.Criar(0.20m));

        Assert.Equal(0.30m, soma.Valor);
        Assert.Equal(ValorMonetario.Criar(10.51m), ValorMonetario.Criar(10.50m) + ValorMonetario.Criar(0.01m));
    }

    [Fact]
    public void Somar_no_limite_representavel_e_exato()
    {
        var maximo = ValorMonetario.MaximoRepresentavel;
        var umCentavoAbaixo = ValorMonetario.Criar(maximo.Valor - 0.01m);

        Assert.Equal(maximo, umCentavoAbaixo + ValorMonetario.Criar(0.01m));
        Assert.Equal(maximo, maximo + ValorMonetario.Zero);
    }

    [Fact]
    public void Somar_que_excede_o_maximo_lanca_overflow_sem_alterar_parcelas()
    {
        var maximo = ValorMonetario.MaximoRepresentavel;
        var centavo = ValorMonetario.Criar(0.01m);

        Assert.Throws<OverflowException>(() => maximo.Somar(centavo));
        Assert.Throws<OverflowException>(() => maximo + maximo);

        Assert.Equal(792_281_625_142_643_375_935_439_503.35m, maximo.Valor);
        Assert.Equal(0.01m, centavo.Valor);
    }

    [Fact]
    public void Valor_padrao_da_estrutura_e_zero_valido()
    {
        var padrao = default(ValorMonetario);

        Assert.Equal(0m, padrao.Valor);
        Assert.Equal(ValorMonetario.Criar(0m), padrao);
        Assert.Equal(ValorMonetario.Zero, new ValorMonetario());
        Assert.Equal(ValorMonetario.Criar(5.25m), padrao + ValorMonetario.Criar(5.25m));
    }

    [Fact]
    public void ToString_nao_depende_da_cultura_atual()
    {
        var culturaOriginal = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");

            Assert.Equal("10.50", ValorMonetario.Criar(10.50m).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = culturaOriginal;
        }
    }
}
