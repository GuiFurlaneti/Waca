using Waca.Application.Comum;
using Xunit;

namespace Waca.Application.Tests.Vaquinhas;

public class GeradorDeIdentificadoresV7Tests
{
    [Fact]
    public void Gera_identificadores_versao_7_validos_e_distintos()
    {
        var gerador = new GeradorDeIdentificadoresV7();

        var ids = Enumerable.Range(0, 1_000).Select(_ => gerador.NovoId()).ToArray();

        Assert.All(ids, id =>
        {
            Assert.NotEqual(Guid.Empty, id);
            Assert.Equal(7, id.Version);
        });
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }
}
