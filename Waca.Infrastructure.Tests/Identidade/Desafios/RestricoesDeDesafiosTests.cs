using System.Security.Cryptography;
using Npgsql;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;
using static Waca.Infrastructure.Tests.Identidade.IdentidadeNoBancoDeTeste;

namespace Waca.Infrastructure.Tests.Identidade.Desafios;

/// <summary>Restrições do banco, exercitadas por SQL direto (sem passar pelos serviços).</summary>
[Collection(ColecaoPostgres.Nome)]
public class RestricoesDeDesafiosTests(ServidorPostgres servidor)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Linha_valida_e_aceita()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();

        await InserirAsync(banco, _ => { });

        Assert.Equal(1, await banco.ContarAsync("SELECT count(*) FROM identidade.desafios_acesso"));
    }

    [Theory]
    [InlineData("finalidade-invalida", "ck_desafios_acesso_finalidade")]
    [InlineData("finalidade-minuscula", "ck_desafios_acesso_finalidade")]
    [InlineData("hash-31-bytes", "ck_desafios_acesso_segredo")]
    [InlineData("hash-33-bytes", "ck_desafios_acesso_segredo")]
    [InlineData("tentativas-negativas", "ck_desafios_acesso_tentativas")]
    [InlineData("tentativas-acima-do-maximo", "ck_desafios_acesso_tentativas")]
    [InlineData("maximo-zero", "ck_desafios_acesso_tentativas")]
    [InlineData("maximo-11", "ck_desafios_acesso_tentativas")]
    [InlineData("esgotado-e-aberto", "ck_desafios_acesso_esgotado")]
    [InlineData("expira-igual-a-criacao", "ck_desafios_acesso_validade")]
    [InlineData("expira-antes-da-criacao", "ck_desafios_acesso_validade")]
    [InlineData("consumido-e-invalidado", "ck_desafios_acesso_estado_final")]
    [InlineData("consumido-apos-expirar", "ck_desafios_acesso_consumo")]
    [InlineData("consumido-antes-de-criar", "ck_desafios_acesso_consumo")]
    [InlineData("invalidado-antes-de-criar", "ck_desafios_acesso_invalidacao")]
    [InlineData("acesso-com-pessoa", "ck_desafios_acesso_pessoa")]
    [InlineData("troca-sem-pessoa", "ck_desafios_acesso_pessoa")]
    [InlineData("versao-zero", "ck_desafios_acesso_versao_chave")]
    [InlineData("id-vazio", "ck_desafios_acesso_id")]
    [InlineData("email-vazio", "ck_desafios_acesso_email")]
    public async Task Linha_inconsistente_e_recusada(string caso, string restricao)
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var criado = RelogioDeTeste.Inicio;

        var excecao = await Assert.ThrowsAsync<PostgresException>(() => InserirAsync(banco, v =>
        {
            switch (caso)
            {
                case "finalidade-invalida": v["finalidade"] = "Convite"; break;
                case "finalidade-minuscula": v["finalidade"] = "acesso"; break;
                case "hash-31-bytes": v["segredo_hash"] = RandomNumberGenerator.GetBytes(31); break;
                case "hash-33-bytes": v["segredo_hash"] = RandomNumberGenerator.GetBytes(33); break;
                case "tentativas-negativas": v["tentativas"] = -1; break;
                case "tentativas-acima-do-maximo": v["tentativas"] = 6; v["invalidado_em"] = criado; break;
                case "maximo-zero": v["max_tentativas"] = 0; v["invalidado_em"] = criado; break;
                case "maximo-11": v["max_tentativas"] = 11; break;
                case "esgotado-e-aberto": v["tentativas"] = 5; break;
                case "expira-igual-a-criacao": v["expira_em"] = criado; break;
                case "expira-antes-da-criacao": v["expira_em"] = criado.AddSeconds(-1); break;
                case "consumido-e-invalidado": v["consumido_em"] = criado.AddMinutes(1); v["invalidado_em"] = criado.AddMinutes(1); break;
                case "consumido-apos-expirar": v["consumido_em"] = criado.AddMinutes(10); break;
                case "consumido-antes-de-criar": v["consumido_em"] = criado.AddSeconds(-1); break;
                case "invalidado-antes-de-criar": v["invalidado_em"] = criado.AddSeconds(-1); break;
                case "acesso-com-pessoa": v["pessoa_id"] = Guid.CreateVersion7(); break;
                case "troca-sem-pessoa": v["finalidade"] = "TrocaDeEmail"; break;
                case "versao-zero": v["versao_chave"] = (short)0; break;
                case "id-vazio": v["id"] = Guid.Empty; break;
                case "email-vazio": v["email_normalizado"] = ""; break;
                default: throw new ArgumentOutOfRangeException(nameof(caso));
            }
        }));

        ExigirViolacao(excecao, PostgresErrorCodes.CheckViolation, restricao);
    }

    [Theory]
    [InlineData("finalidade")]
    [InlineData("email_normalizado")]
    [InlineData("segredo_hash")]
    [InlineData("versao_chave")]
    [InlineData("criado_em")]
    [InlineData("expira_em")]
    [InlineData("tentativas")]
    [InlineData("max_tentativas")]
    public async Task Colunas_obrigatorias_nao_aceitam_nulo(string coluna)
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();

        var excecao = await Assert.ThrowsAsync<PostgresException>(() => InserirAsync(banco, v => v[coluna] = null));

        ExigirViolacao(excecao, PostgresErrorCodes.NotNullViolation);
    }

    [Fact]
    public async Task Dois_desafios_abertos_para_o_mesmo_email_e_finalidade_sao_recusados()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        await InserirAsync(banco, _ => { });

        var excecao = await Assert.ThrowsAsync<PostgresException>(() => InserirAsync(banco, _ => { }));

        ExigirViolacao(excecao, PostgresErrorCodes.UniqueViolation, "ux_desafios_acesso_aberto");
    }

    [Fact]
    public async Task Desafios_encerrados_ou_de_outra_finalidade_nao_bloqueiam_um_novo_aberto()
    {
        await using var banco = await servidor.CriarBancoComIdentidadeAsync();
        var criado = RelogioDeTeste.Inicio;

        await InserirAsync(banco, v => v["invalidado_em"] = criado);
        await InserirAsync(banco, v => v["consumido_em"] = criado.AddMinutes(1));
        await InserirAsync(banco, v => { v["finalidade"] = "TrocaDeEmail"; v["pessoa_id"] = Guid.CreateVersion7(); });
        await InserirAsync(banco, _ => { });

        Assert.Equal(4, await banco.ContarAsync("SELECT count(*) FROM identidade.desafios_acesso"));
        Assert.Equal(2, await banco.ContarAsync("SELECT count(*) FROM identidade.desafios_acesso WHERE consumido_em IS NULL AND invalidado_em IS NULL"));
    }

    private static async Task InserirAsync(BancoDeTeste banco, Action<Dictionary<string, object?>> ajustar)
    {
        var criado = RelogioDeTeste.Inicio;
        var valores = new Dictionary<string, object?>
        {
            ["id"] = Guid.CreateVersion7(),
            ["finalidade"] = "Acesso",
            ["email_normalizado"] = "ANA@EXEMPLO.COM",
            ["pessoa_id"] = null,
            ["segredo_hash"] = RandomNumberGenerator.GetBytes(32),
            ["versao_chave"] = (short)1,
            ["criado_em"] = criado,
            ["expira_em"] = criado.AddMinutes(10),
            ["tentativas"] = 0,
            ["max_tentativas"] = 5,
            ["consumido_em"] = null,
            ["invalidado_em"] = null,
        };
        ajustar(valores);

        await using var conexao = new NpgsqlConnection(banco.ConnectionString);
        await conexao.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand(
            $"INSERT INTO identidade.desafios_acesso ({string.Join(", ", valores.Keys)}) VALUES ({string.Join(", ", valores.Keys.Select(k => "@" + k))})",
            conexao);
        foreach (var (coluna, valor) in valores)
        {
            comando.Parameters.Add(new NpgsqlParameter(coluna, TipoDa(coluna)) { Value = valor ?? DBNull.Value });
        }

        await comando.ExecuteNonQueryAsync(Ct);
    }

    private static NpgsqlTypes.NpgsqlDbType TipoDa(string coluna) => coluna switch
    {
        "id" or "pessoa_id" => NpgsqlTypes.NpgsqlDbType.Uuid,
        "finalidade" or "email_normalizado" => NpgsqlTypes.NpgsqlDbType.Varchar,
        "segredo_hash" => NpgsqlTypes.NpgsqlDbType.Bytea,
        "versao_chave" => NpgsqlTypes.NpgsqlDbType.Smallint,
        "tentativas" or "max_tentativas" => NpgsqlTypes.NpgsqlDbType.Integer,
        _ => NpgsqlTypes.NpgsqlDbType.TimestampTz,
    };
}
