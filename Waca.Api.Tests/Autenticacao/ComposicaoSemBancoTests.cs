using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Waca.Api.Autenticacao;
using Waca.Infrastructure.Identidade.Desafios;
using Xunit;

namespace Waca.Api.Tests.Autenticacao;

/// <summary>Regras da composição que não dependem de banco.</summary>
public class ComposicaoSemBancoTests
{
    [Fact]
    public void Ator_atual_sem_sessao_validada_nao_existe_mesmo_com_claims_autenticadas()
    {
        var contexto = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()), new Claim("sid", Guid.CreateVersion7().ToString())],
                "Identity.Application")),
        };
        var ator = new AtorAtualHttp(new HttpContextAccessor { HttpContext = contexto });

        Assert.True(contexto.User.Identity!.IsAuthenticated);
        Assert.Null(ator.PessoaId);
    }

    [Fact]
    public void Ator_atual_le_somente_a_sessao_validada()
    {
        var pessoa = Guid.CreateVersion7();
        var contexto = new DefaultHttpContext();
        contexto.Features.Set<ISessaoValidada>(new SessaoValidadaDeTeste(pessoa, Guid.CreateVersion7(), "a@exemplo.com"));
        contexto.Request.QueryString = new QueryString($"?pessoaId={Guid.CreateVersion7()}");
        contexto.Request.Headers["X-User-Id"] = Guid.CreateVersion7().ToString();

        Assert.Equal(pessoa, new AtorAtualHttp(new HttpContextAccessor { HttpContext = contexto }).PessoaId);
        // HttpContextAccessor guarda o contexto em AsyncLocal compartilhado: um contexto novo, sem sessão validada.
        Assert.Null(new AtorAtualHttp(new HttpContextAccessor { HttpContext = new DefaultHttpContext() }).PessoaId);
    }

    [Fact]
    public void Renovacao_do_principal_preserva_sid_e_amr_sem_duplicar()
    {
        var atual = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sid", "s1"), new Claim("amr", "otp_email"), new Claim("outra", "x")], "Identity.Application"));
        var novo = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "p")], "Identity.Application"));

        ClaimsDaSessao.Preservar(atual, novo);
        ClaimsDaSessao.Preservar(atual, novo);

        Assert.Equal("s1", Assert.Single(novo.FindAll("sid")).Value);
        Assert.Equal("otp_email", Assert.Single(novo.FindAll("amr")).Value);
        Assert.Null(novo.FindFirst("outra"));
    }

    [Fact]
    public void Pedidos_e_resultados_nao_expoem_email_nem_codigo_em_ToString()
    {
        var textos = new[]
        {
            new PedidoDeCodigo("ana@exemplo.com").ToString(),
            new PedidoDeConfirmacao(Guid.CreateVersion7(), "ana@exemplo.com", "01234567").ToString(),
            new SolicitacaoDeEmissao(FinalidadeDesafio.Acesso, "ana@exemplo.com").ToString(),
            new ResultadoDaVerificacao(true, null, Guid.CreateVersion7(), "ANA@EXEMPLO.COM", null, null).ToString(),
            new Waca.Infrastructure.Identidade.Sessoes.EstadoDaSessao(
                Guid.CreateVersion7(), Guid.CreateVersion7(), "ana@exemplo.com", true,
                Waca.Infrastructure.Identidade.EstadoConta.Ativa, "stamp-secreto", DateTimeOffset.UtcNow, null).ToString(),
        };

        Assert.All(textos, t =>
        {
            Assert.DoesNotContain("ana@exemplo.com", t, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("01234567", t, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Entrega_em_arquivo_recusa_ambiente_diferente_de_Development()
    {
        foreach (var ambiente in new[] { Environments.Production, Environments.Staging, "Testing" })
        {
            Assert.Throws<InvalidOperationException>(() => new EntregaEmArquivoParaDesenvolvimento(new AmbienteDeTeste(ambiente), Path.GetTempPath()));
        }
    }

    [Fact]
    public void Diretorio_padrao_de_emails_fica_fora_do_repositorio()
    {
        var repositorio = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        Assert.False(EntregaEmArquivoParaDesenvolvimento.DiretorioPadrao.StartsWith(repositorio, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Producao_sem_configuracao_segura_e_recusada_com_todos_os_problemas()
    {
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "*" }).Build();

        var falha = Assert.Throws<InvalidOperationException>(() => ConfiguracaoDaAutenticacao.ValidarInicializacao(
            configuracao, new AmbienteDeTeste(Environments.Production), new OpcoesDeProtecaoDeChaves()));

        Assert.Contains("AllowedHosts", falha.Message, StringComparison.Ordinal);
        Assert.Contains("Data Protection", falha.Message, StringComparison.Ordinal);
        Assert.Contains("provedor de entrega", falha.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Producao_com_hosts_e_certificado_validos_ainda_exige_provedor_de_email()
    {
        var pasta = Directory.CreateTempSubdirectory("waca-teste-");
        try
        {
            using var rsa = RSA.Create(2048);
            var pedido = new CertificateRequest("CN=waca-teste", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificado = pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var senha = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            var caminho = Path.Combine(pasta.FullName, "protecao.pfx");
            File.WriteAllBytes(caminho, certificado.Export(X509ContentType.Pfx, senha));

            var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "waca.exemplo" }).Build();

            var falha = Assert.Throws<InvalidOperationException>(() => ConfiguracaoDaAutenticacao.ValidarInicializacao(
                configuracao,
                new AmbienteDeTeste(Environments.Production),
                new OpcoesDeProtecaoDeChaves { CaminhoDoCertificado = caminho, SenhaDoCertificado = senha }));

            Assert.DoesNotContain("AllowedHosts", falha.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Data Protection", falha.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("certificado", falha.Message, StringComparison.Ordinal);
            Assert.Contains("provedor de entrega", falha.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(senha, falha.Message, StringComparison.Ordinal);
        }
        finally
        {
            pasta.Delete(recursive: true);
        }
    }

    [Fact]
    public void Development_nao_exige_as_configuracoes_de_producao()
    {
        var configuracao = new ConfigurationBuilder().Build();

        Assert.Null(ConfiguracaoDaAutenticacao.ValidarInicializacao(configuracao, new AmbienteDeTeste(Environments.Development), new OpcoesDeProtecaoDeChaves()));
    }

    [Fact]
    public void A_API_em_producao_nao_inicia_sem_configuracao_segura()
    {
        using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment(Environments.Production));

        var falha = Assert.ThrowsAny<Exception>(() => fabrica.Services);

        Assert.Contains("Configuração insegura", (falha.InnerException ?? falha).Message + falha.Message, StringComparison.Ordinal);
    }

    private sealed record SessaoValidadaDeTeste(Guid PessoaId, Guid SessaoId, string Email) : ISessaoValidada
    {
        public override string ToString() => nameof(SessaoValidadaDeTeste);
    }

    private sealed class AmbienteDeTeste(string nome) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nome;

        public string ApplicationName { get; set; } = "Waca.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
