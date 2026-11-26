using Microsoft.AspNetCore.Identity;
using Waca.Infrastructure.Identidade;
using Xunit;

namespace Waca.Infrastructure.Tests.Identidade;

/// <summary>Regras do modelo de conta e das opções do Identity, sem banco.</summary>
public class UsuarioWacaTests
{
    [Fact]
    public void Conta_nova_guarda_o_identificador_e_o_email_sem_normalizar_nem_confirmar()
    {
        var id = Guid.CreateVersion7();

        var conta = new UsuarioWaca(id, "Ana.Silva@Exemplo.com", EstadoConta.Ativa);

        Assert.Equal(id, conta.Id);
        Assert.Equal("Ana.Silva@Exemplo.com", conta.Email);
        Assert.Null(conta.NormalizedEmail);
        Assert.False(conta.EmailConfirmed);
        Assert.Equal(EstadoConta.Ativa, conta.Estado);
        Assert.Null(conta.PasswordHash);
    }

    [Fact]
    public void Nome_de_usuario_deriva_do_identificador_e_nao_do_email()
    {
        var id = Guid.CreateVersion7();

        var conta = new UsuarioWaca(id, "bruno@exemplo.com", EstadoConta.Ativa);

        Assert.Equal(id.ToString("N"), conta.UserName);
        Assert.DoesNotContain("bruno", conta.UserName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Identificador_vazio_e_recusado()
    {
        var excecao = Assert.Throws<ArgumentException>(() => new UsuarioWaca(Guid.Empty, "a@exemplo.com", EstadoConta.Ativa));

        Assert.Equal("id", excecao.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Email_em_branco_e_recusado(string? email)
    {
        Assert.ThrowsAny<ArgumentException>(() => new UsuarioWaca(Guid.CreateVersion7(), email!, EstadoConta.Ativa));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Estado_inexistente_e_recusado(int estado)
    {
        var excecao = Assert.Throws<ArgumentException>(
            () => new UsuarioWaca(Guid.CreateVersion7(), "a@exemplo.com", (EstadoConta)estado));

        Assert.Equal("estado", excecao.ParamName);
    }

    [Fact]
    public void Estado_padrao_nao_corresponde_a_nenhum_estado_valido()
    {
        Assert.False(Enum.IsDefined(default(EstadoConta)));
        Assert.Equal([EstadoConta.Ativa, EstadoConta.Desativada], Enum.GetValues<EstadoConta>());
    }

    [Theory]
    [InlineData(EstadoConta.Ativa, true, true)]
    [InlineData(EstadoConta.Ativa, false, false)]
    [InlineData(EstadoConta.Desativada, true, false)]
    [InlineData(EstadoConta.Desativada, false, false)]
    public void Acesso_exige_conta_ativa_e_email_confirmado(EstadoConta estado, bool emailConfirmado, bool esperado)
    {
        var conta = new UsuarioWaca(Guid.CreateVersion7(), "a@exemplo.com", estado) { EmailConfirmed = emailConfirmado };

        Assert.Equal(esperado, conta.PermiteAcesso());
    }

    [Fact]
    public void Estado_fora_do_enum_nunca_permite_acesso()
    {
        var conta = new UsuarioWaca(Guid.CreateVersion7(), "a@exemplo.com", EstadoConta.Ativa) { EmailConfirmed = true };

        conta.Estado = default;

        Assert.False(conta.PermiteAcesso());
    }

    [Fact]
    public void Opcoes_do_Waca_exigem_email_unico_e_confirmado_e_fixam_o_schema()
    {
        var opcoes = new IdentityOptions();

        OpcoesDaIdentidade.Configurar(opcoes);

        Assert.True(opcoes.User.RequireUniqueEmail);
        Assert.True(opcoes.SignIn.RequireConfirmedEmail);
        Assert.Equal(IdentitySchemaVersions.Version2, opcoes.Stores.SchemaVersion);
        Assert.Equal(128, opcoes.Stores.MaxLengthForKeys);
        Assert.False(opcoes.Stores.ProtectPersonalData);
        OpcoesDaIdentidade.ExigirArmazenamentoCompativel(opcoes.Stores);
    }

    [Fact]
    public void Opcoes_padrao_do_Identity_nao_sao_aceitas_pelo_modelo()
    {
        var padrao = new IdentityOptions();

        Assert.False(padrao.User.RequireUniqueEmail);
        Assert.Throws<InvalidOperationException>(() => OpcoesDaIdentidade.ExigirArmazenamentoCompativel(padrao.Stores));
        Assert.Throws<InvalidOperationException>(() => OpcoesDaIdentidade.ExigirArmazenamentoCompativel(null));
    }

    [Fact]
    public void Opcoes_de_armazenamento_divergentes_sao_recusadas()
    {
        Assert.Throws<InvalidOperationException>(() => OpcoesDaIdentidade.ExigirArmazenamentoCompativel(Aprovadas(o => o.SchemaVersion = IdentitySchemaVersions.Version3)));
        Assert.Throws<InvalidOperationException>(() => OpcoesDaIdentidade.ExigirArmazenamentoCompativel(Aprovadas(o => o.SchemaVersion = IdentitySchemaVersions.Version1)));
        Assert.Throws<InvalidOperationException>(() => OpcoesDaIdentidade.ExigirArmazenamentoCompativel(Aprovadas(o => o.MaxLengthForKeys = 256)));
        Assert.Throws<InvalidOperationException>(() => OpcoesDaIdentidade.ExigirArmazenamentoCompativel(Aprovadas(o => o.ProtectPersonalData = true)));
    }

    [Fact]
    public void Normalizador_oficial_do_Identity_iguala_maiusculas_e_minusculas()
    {
        var normalizador = new UpperInvariantLookupNormalizer();

        Assert.Equal(normalizador.NormalizeEmail("Ana.Silva@Exemplo.com"), normalizador.NormalizeEmail("ana.silva@EXEMPLO.COM"));
        Assert.NotEqual(normalizador.NormalizeEmail("ana@exemplo.com"), normalizador.NormalizeEmail("ana@exemplo.com.br"));
    }

    private static StoreOptions Aprovadas(Action<StoreOptions> alterar)
    {
        var opcoes = new IdentityOptions();
        OpcoesDaIdentidade.Configurar(opcoes);
        alterar(opcoes.Stores);
        return opcoes.Stores;
    }
}
