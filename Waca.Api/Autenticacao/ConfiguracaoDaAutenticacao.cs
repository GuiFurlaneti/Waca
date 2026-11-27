using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Waca.Application.Comum;
using Waca.Infrastructure.Identidade;
using Waca.Infrastructure.Identidade.Desafios;
using Waca.Infrastructure.Identidade.Sessoes;

namespace Waca.Api.Autenticacao;

/// <summary>Limites por IP da API de autenticação (complementares às cotas persistidas por e-mail).</summary>
public sealed class OpcoesDeLimitePorIp
{
    public int PermissoesPorJanela { get; init; } = 10;

    public TimeSpan Janela { get; init; } = TimeSpan.FromMinutes(1);
}

/// <summary>Proxies reversos confiáveis. Sem configuração, cabeçalhos <c>X-Forwarded-*</c> são ignorados.</summary>
public sealed class OpcoesDeProxy
{
    public string[] ProxiesConfiaveis { get; init; } = [];

    public string[] RedesConfiaveis { get; init; } = [];
}

/// <summary>Certificado que protege as chaves do Data Protection em repouso (obrigatório fora de Development).</summary>
public sealed class OpcoesDeProtecaoDeChaves
{
    public string? CaminhoDoCertificado { get; init; }

    public string? SenhaDoCertificado { get; init; }

    /// <summary>Não expõe a senha.</summary>
    public override string ToString() => nameof(OpcoesDeProtecaoDeChaves);
}

/// <summary>Composição da autenticação do Waca na API.</summary>
public static class ConfiguracaoDaAutenticacao
{
    public const string SecaoDaChaveDeDesafios = "Waca:Desafios:Chave";
    public const string SecaoDeParametrosDosDesafios = "Waca:Desafios:Parametros";
    public const string SecaoDeSessao = "Waca:Sessao";
    public const string SecaoDeLimitePorIp = "Waca:LimitePorIp";
    public const string SecaoDeProxy = "Waca:Proxy";
    public const string SecaoDeProtecaoDeChaves = "Waca:DataProtection";
    public const string ChaveDoDiretorioDeEmails = "Waca:Desenvolvimento:DiretorioDeEmails";
    public const string ConnectionStringDaIdentidade = "Identidade";
    public const string NomeDoCookieDeSessao = "__Host-waca-sessao";

    /// <exception cref="InvalidOperationException">Configuração ausente ou insegura para o ambiente.</exception>
    public static WebApplicationBuilder AdicionarAutenticacaoWaca(this WebApplicationBuilder builder)
    {
        var configuracao = builder.Configuration;
        var ambiente = builder.Environment;
        var servicos = builder.Services;

        var protecaoDeChaves = configuracao.GetSection(SecaoDeProtecaoDeChaves).Get<OpcoesDeProtecaoDeChaves>() ?? new();
        var certificado = ValidarInicializacao(configuracao, ambiente, protecaoDeChaves);

        var connectionString = configuracao.GetConnectionString(ConnectionStringDaIdentidade);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"A connection string '{ConnectionStringDaIdentidade}' é obrigatória e deve vir de configuração segura.");
        }

        var chaveiro = ChaveiroDeDesafios.DeConfiguracao(configuracao.GetSection(SecaoDaChaveDeDesafios).Get<OpcoesDaChaveDeDesafios>());
        var opcoesDeDesafios = configuracao.GetSection(SecaoDeParametrosDosDesafios).Get<OpcoesDeDesafios>() ?? new();
        opcoesDeDesafios.Validar();
        var opcoesDeSessao = configuracao.GetSection(SecaoDeSessao).Get<OpcoesDeSessao>() ?? new();
        opcoesDeSessao.Validar();
        var limitePorIp = configuracao.GetSection(SecaoDeLimitePorIp).Get<OpcoesDeLimitePorIp>() ?? new();
        if (limitePorIp.PermissoesPorJanela < 1 || limitePorIp.Janela <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Configuração de limite por IP inválida.");
        }

        servicos.TryAddSingleton(TimeProvider.System);

        // Um único contexto por requisição (o mesmo do UserManager); a fábrica atende o emissor de desafios.
        servicos.AddDbContextFactory<WacaIdentidadeDbContext>(opcoes => WacaIdentidadeDbContext.UsarPostgres(opcoes, connectionString));
        servicos.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<WacaIdentidadeDbContext>>().CreateDbContext());

        servicos.AddIdentityCore<UsuarioWaca>(OpcoesDaIdentidade.Configurar)
            .AddEntityFrameworkStores<WacaIdentidadeDbContext>()
            .AddSignInManager();

        servicos.Configure<SecurityStampValidatorOptions>(opcoes =>
            opcoes.OnRefreshingPrincipal = contexto =>
            {
                if (contexto.CurrentPrincipal is not null && contexto.NewPrincipal is not null)
                {
                    ClaimsDaSessao.Preservar(contexto.CurrentPrincipal, contexto.NewPrincipal);
                }

                return Task.CompletedTask;
            });

        servicos.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        servicos.ConfigureApplicationCookie(opcoes =>
        {
            opcoes.Cookie.Name = NomeDoCookieDeSessao;
            opcoes.Cookie.HttpOnly = true;
            opcoes.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            opcoes.Cookie.SameSite = SameSiteMode.Strict;
            opcoes.Cookie.Path = "/";
            opcoes.Cookie.Domain = null;
            opcoes.ExpireTimeSpan = opcoesDeSessao.Inatividade;
            opcoes.SlidingExpiration = true;
            opcoes.EventsType = typeof(ValidadorDeSessao);
        });
        servicos.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Validate(o => o.EventsType == typeof(ValidadorDeSessao), "O cookie de sessão precisa do ValidadorDeSessao.")
            .ValidateOnStart();

        servicos.AddAuthorization(opcoes =>
            opcoes.FallbackPolicy = new AuthorizationPolicyBuilder(IdentityConstants.ApplicationScheme)
                .RequireAuthenticatedUser()
                .Build());

        servicos.AdicionarProtecaoContraCsrf();

        var protecao = servicos.AddDataProtection()
            .SetApplicationName("Waca")
            .PersistKeysToDbContext<WacaIdentidadeDbContext>();
        if (certificado is not null)
        {
            protecao.ProtectKeysWithCertificate(certificado);
        }

        servicos.AddRateLimiter(opcoes =>
        {
            opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opcoes.AddPolicy(EndpointsDeAutenticacao.PoliticaDeLimite, contexto =>
                RateLimitPartition.GetFixedWindowLimiter(
                    contexto.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limitePorIp.PermissoesPorJanela,
                        Window = limitePorIp.Janela,
                        QueueLimit = 0,
                    }));
        });

        ConfigurarProxy(servicos, configuracao.GetSection(SecaoDeProxy).Get<OpcoesDeProxy>() ?? new());

        servicos.AddSingleton(chaveiro);
        servicos.AddSingleton(opcoesDeDesafios);
        servicos.AddSingleton(opcoesDeSessao);
        servicos.AddSingleton<SessoesPersistidas>();
        servicos.AddScoped<EmissorDeDesafios>();
        servicos.AddScoped<VerificadorDeDesafios>();
        servicos.AddScoped<ConfirmacaoDeAcesso>();
        servicos.AddScoped<ValidadorDeSessao>();
        servicos.AddScoped<IAtorAtual, AtorAtualHttp>();
        servicos.AddHttpContextAccessor();

        if (ambiente.IsDevelopment())
        {
            var diretorio = configuracao[ChaveDoDiretorioDeEmails] ?? EntregaEmArquivoParaDesenvolvimento.DiretorioPadrao;
            servicos.AddSingleton<IEntregaDeCodigoDeAcesso>(sp => new EntregaEmArquivoParaDesenvolvimento(sp.GetRequiredService<IHostEnvironment>(), diretorio));
        }

        return builder;
    }

    /// <summary>Pipeline: proxy confiável, HTTPS, limites, autenticação, CSRF, autorização e endpoints.</summary>
    public static WebApplication UsarAutenticacaoWaca(this WebApplication app)
    {
        if (app.Services.GetService<Microsoft.Extensions.Options.IOptions<ForwardedHeadersOptions>>()?.Value.ForwardedHeaders is { } cabecalhos
            && cabecalhos != ForwardedHeaders.None)
        {
            app.UseForwardedHeaders();
        }

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UsarProtecaoContraCsrf();
        app.UseAuthorization();
        app.MapearAutenticacao();
        return app;
    }

    /// <summary>
    /// Fora de Development exige <c>AllowedHosts</c> explícito, certificado de proteção das chaves e provedor de e-mail
    /// aprovado. Reúne todos os problemas em uma única exceção. Devolve o certificado carregado, se houver.
    /// </summary>
    /// <exception cref="InvalidOperationException">Configuração insegura.</exception>
    public static X509Certificate2? ValidarInicializacao(IConfiguration configuracao, IHostEnvironment ambiente, OpcoesDeProtecaoDeChaves protecaoDeChaves)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(ambiente);
        ArgumentNullException.ThrowIfNull(protecaoDeChaves);

        if (ambiente.IsDevelopment())
        {
            return null;
        }

        var problemas = new List<string>();

        var hosts = configuracao["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(hosts) || hosts.Split(';').Any(h => h.Trim() == "*"))
        {
            problemas.Add("AllowedHosts deve listar explicitamente os hosts da aplicação.");
        }

        X509Certificate2? certificado = null;
        if (string.IsNullOrWhiteSpace(protecaoDeChaves.CaminhoDoCertificado))
        {
            problemas.Add("As chaves do Data Protection precisam de proteção em repouso: configure o certificado.");
        }
        else
        {
            try
            {
                certificado = X509CertificateLoader.LoadPkcs12FromFile(protecaoDeChaves.CaminhoDoCertificado, protecaoDeChaves.SenhaDoCertificado);
                if (!certificado.HasPrivateKey)
                {
                    problemas.Add("O certificado de proteção das chaves precisa ter chave privada.");
                }
            }
            catch (Exception falha) when (falha is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
            {
                problemas.Add("O certificado de proteção das chaves não pôde ser carregado.");
            }
        }

        problemas.Add("Nenhum provedor de entrega de códigos por e-mail foi aprovado para este ambiente.");

        throw new InvalidOperationException(
            $"Configuração insegura para o ambiente {ambiente.EnvironmentName}:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", problemas)}");
    }

    private static void ConfigurarProxy(IServiceCollection servicos, OpcoesDeProxy proxy)
    {
        if (proxy.ProxiesConfiaveis.Length == 0 && proxy.RedesConfiaveis.Length == 0)
        {
            return;
        }

        var enderecos = proxy.ProxiesConfiaveis.Select(IPAddress.Parse).ToArray();
        var redes = proxy.RedesConfiaveis.Select(System.Net.IPNetwork.Parse).ToArray();

        servicos.Configure<ForwardedHeadersOptions>(opcoes =>
        {
            opcoes.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            opcoes.ForwardLimit = 1;
            opcoes.KnownProxies.Clear();
            opcoes.KnownIPNetworks.Clear();
            foreach (var endereco in enderecos)
            {
                opcoes.KnownProxies.Add(endereco);
            }

            foreach (var rede in redes)
            {
                opcoes.KnownIPNetworks.Add(rede);
            }
        });
    }
}
