using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Docker.DotNet.Models;
using Xunit;

namespace Waca.Infrastructure.Tests.Postgres;

/// <summary>
/// Salvaguardas do servidor de testes que podem ser verificadas sem Docker: ausência de servidor alternativo,
/// posse dos bancos e regras de rede. A eficácia real da publicação em loopback só é observável com Docker.
/// </summary>
public class ServidorPostgresSegurancaTests
{
    private const string NomeValido = "waca_teste_0123456789abcdef0123456789abcdef";

    // ---------- Sem servidor alternativo ----------

    [Fact]
    public void Codigo_de_teste_nao_le_variaveis_de_ambiente()
    {
        var membros = ReferenciasDeMembro(typeof(ServidorPostgres).Assembly);

        Assert.DoesNotContain(membros, m => m.Tipo == "System.Environment" && m.Nome.StartsWith("GetEnvironmentVariable", StringComparison.Ordinal));
    }

    [Fact]
    public void Variavel_da_conexao_alternativa_nao_existe_mais()
    {
        var nomeAntigo = string.Concat("WACA_TESTES_", "POSTGRES");

        Assert.DoesNotContain(TextosLiterais(typeof(ServidorPostgres).Assembly), t => t.Contains(nomeAntigo, StringComparison.Ordinal));
        Assert.Null(typeof(ServidorPostgres).GetField("VariavelDeAmbiente", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        Assert.Null(typeof(ServidorPostgres).GetProperty("Origem"));
    }

    // ---------- Posse dos bancos ----------

    [Fact]
    public void Banco_nao_registrado_pela_execucao_nao_pode_ser_representado()
    {
        var servidor = new ServidorPostgres();

        Assert.Throws<InvalidOperationException>(() => new BancoDeTeste(servidor, NomeValido));
        Assert.Throws<InvalidOperationException>(() => servidor.ConnectionStringDo(NomeValido));
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("template0")]
    [InlineData("template1")]
    [InlineData("waca_teste_x")]
    public void Banco_administrativo_ou_invalido_nao_pode_ser_representado(string nome)
    {
        var servidor = new ServidorPostgres();

        Assert.Throws<ArgumentException>(() => new BancoDeTeste(servidor, nome));
        Assert.Throws<ArgumentException>(() => servidor.ConnectionStringDo(nome));
    }

    [Fact]
    public async Task Servidor_recusa_remover_banco_que_nao_criou()
    {
        var servidor = new ServidorPostgres();

        await Assert.ThrowsAsync<InvalidOperationException>(() => servidor.RemoverBancoAsync(NomeValido));
        await Assert.ThrowsAsync<ArgumentException>(() => servidor.RemoverBancoAsync("postgres"));
    }

    [Fact]
    public void BancoDeTeste_nao_tem_construtor_publico()
    {
        Assert.Empty(typeof(BancoDeTeste).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    // ---------- Configuração de portas antes da criação ----------

    [Fact]
    public void Restricao_ao_loopback_falha_sem_configuracao_de_portas()
    {
        Assert.Throws<InvalidOperationException>(
            () => ServidorPostgres.RestringirPublicacaoAoLoopback(new CreateContainerParameters()));
        Assert.Throws<InvalidOperationException>(
            () => ServidorPostgres.RestringirPublicacaoAoLoopback(new CreateContainerParameters { HostConfig = new HostConfig() }));
        Assert.Throws<InvalidOperationException>(
            () => ServidorPostgres.RestringirPublicacaoAoLoopback(Parametros(("8080/tcp", [new PortBinding { HostPort = "" }]))));
        Assert.Throws<InvalidOperationException>(
            () => ServidorPostgres.RestringirPublicacaoAoLoopback(Parametros(("5432/tcp", []))));
    }

    [Fact]
    public void Restricao_ao_loopback_recusa_porta_de_host_fixa()
    {
        Assert.Throws<InvalidOperationException>(
            () => ServidorPostgres.RestringirPublicacaoAoLoopback(Parametros(("5432/tcp", [new PortBinding { HostPort = "5432" }]))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    public void Restricao_ao_loopback_publica_todas_as_portas_dinamicas_em_127_0_0_1(string? portaDoHost)
    {
        var postgres = new PortBinding { HostIP = "0.0.0.0", HostPort = portaDoHost! };
        var outra = new PortBinding { HostPort = portaDoHost! };
        var parametros = Parametros(("5432/tcp", [postgres]), ("8080/tcp", [outra]));

        ServidorPostgres.RestringirPublicacaoAoLoopback(parametros);

        Assert.Equal("127.0.0.1", postgres.HostIP);
        Assert.Equal("127.0.0.1", outra.HostIP);
        Assert.Equal(portaDoHost, postgres.HostPort);
    }

    // ---------- Docker local ----------

    [Theory]
    [InlineData("unix:///var/run/docker.sock")]
    [InlineData("npipe://./pipe/docker_engine")]
    [InlineData("tcp://localhost:2375")]
    [InlineData("tcp://127.0.0.1:2375")]
    [InlineData("tcp://[::1]:2375")]
    public void Docker_local_e_aceito(string endpoint)
    {
        ServidorPostgres.ExigirDockerLocal(new Uri(endpoint));
    }

    [Theory]
    [InlineData("tcp://10.0.0.5:2376")]
    [InlineData("tcp://docker.exemplo.com:2376")]
    [InlineData("https://192.168.1.20:2376")]
    [InlineData("ssh://usuario@servidor")]
    public void Docker_remoto_e_recusado(string endpoint)
    {
        Assert.Throws<InvalidOperationException>(() => ServidorPostgres.ExigirDockerLocal(new Uri(endpoint)));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Host_do_conteiner_em_loopback_e_aceito(string host)
    {
        ServidorPostgres.ExigirHostLoopback(host);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("10.0.0.5")]
    [InlineData("docker")]
    [InlineData("0.0.0.0")]
    public void Host_do_conteiner_fora_do_loopback_e_recusado(string? host)
    {
        Assert.Throws<InvalidOperationException>(() => ServidorPostgres.ExigirHostLoopback(host));
    }

    // ---------- Portas efetivamente publicadas (conferência após iniciar) ----------

    [Fact]
    public void Publicacao_somente_em_127_0_0_1_e_aceita()
    {
        ServidorPostgres.ExigirPublicacaoSomenteNoLoopback(Portas(("5432/tcp", [new PortBinding { HostIP = "127.0.0.1", HostPort = "49153" }])));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("")]
    [InlineData("::")]
    [InlineData("192.168.0.10")]
    public void Publicacao_fora_do_loopback_e_recusada(string ip)
    {
        Assert.Throws<InvalidOperationException>(
            () => ServidorPostgres.ExigirPublicacaoSomenteNoLoopback(Portas(("5432/tcp", [new PortBinding { HostIP = ip, HostPort = "49153" }]))));
        Assert.Throws<InvalidOperationException>(
            () => ServidorPostgres.ExigirPublicacaoSomenteNoLoopback(Portas((
                "5432/tcp",
                [new PortBinding { HostIP = "127.0.0.1", HostPort = "49153" }, new PortBinding { HostIP = ip, HostPort = "49153" }]))));
    }

    [Fact]
    public void Porta_nao_publicada_e_recusada()
    {
        Assert.Throws<InvalidOperationException>(() => ServidorPostgres.ExigirPublicacaoSomenteNoLoopback(null));
        Assert.Throws<InvalidOperationException>(() => ServidorPostgres.ExigirPublicacaoSomenteNoLoopback(Portas()));
        Assert.Throws<InvalidOperationException>(() => ServidorPostgres.ExigirPublicacaoSomenteNoLoopback(Portas(("5432/tcp", []))));
    }

    // ---------- Auxiliares ----------

    private static CreateContainerParameters Parametros(params (string Porta, IList<PortBinding> Vinculos)[] portas) =>
        new() { HostConfig = new HostConfig { PortBindings = Portas(portas) } };

    private static Dictionary<string, IList<PortBinding>> Portas(params (string Porta, IList<PortBinding> Vinculos)[] portas) =>
        portas.ToDictionary(p => p.Porta, p => p.Vinculos);

    private static IReadOnlyList<(string Tipo, string Nome)> ReferenciasDeMembro(Assembly assembly)
    {
        using var arquivo = File.OpenRead(assembly.Location);
        using var pe = new PEReader(arquivo);
        var metadados = pe.GetMetadataReader();

        var resultado = new List<(string, string)>();
        foreach (var handle in metadados.MemberReferences)
        {
            var membro = metadados.GetMemberReference(handle);
            if (membro.Parent.Kind == HandleKind.TypeReference)
            {
                var tipo = metadados.GetTypeReference((TypeReferenceHandle)membro.Parent);
                resultado.Add(($"{metadados.GetString(tipo.Namespace)}.{metadados.GetString(tipo.Name)}", metadados.GetString(membro.Name)));
            }
        }

        return resultado;
    }

    private static IReadOnlyList<string> TextosLiterais(Assembly assembly)
    {
        using var arquivo = File.OpenRead(assembly.Location);
        using var pe = new PEReader(arquivo);
        var metadados = pe.GetMetadataReader();

        var textos = new List<string>();
        var handle = MetadataTokens.UserStringHandle(1);
        while (!handle.IsNil)
        {
            textos.Add(metadados.GetUserString(handle));
            handle = metadados.GetNextHandle(handle);
        }

        return textos;
    }
}
