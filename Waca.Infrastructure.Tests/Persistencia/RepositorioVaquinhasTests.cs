using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Waca.Domain.Vaquinhas;
using Waca.Infrastructure.Persistencia;
using Waca.Infrastructure.Persistencia.Repositorios;
using Waca.Infrastructure.Tests.Postgres;
using Xunit;

namespace Waca.Infrastructure.Tests.Persistencia;

[Collection(ColecaoPostgres.Nome)]
public class RepositorioVaquinhasTests(ServidorPostgres servidor)
{
    private static readonly Guid OrganizadorA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid OrganizadorB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid OrganizadorC = Guid.Parse("cccccccc-0000-4000-8000-000000000003");
    private static readonly DateTimeOffset Criacao = new(2026, 11, 25, 12, 30, 15, 123, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- Persistência ----------

    [Fact]
    public async Task Vaquinha_gravada_e_recuperada_por_outro_contexto_com_os_mesmos_dados()
    {
        await using var banco = await servidor.CriarBancoAsync();
        var original = Nova("019a9a00-0000-7000-8000-000000000001", "Viagem da galera", OrganizadorA,
            new DateTimeOffset(2026, 11, 25, 9, 30, 15, 123, TimeSpan.FromHours(-3)).AddTicks(4560));

        await using (var contexto = banco.CriarContexto())
        {
            await new RepositorioVaquinhas(contexto).AdicionarAsync(original, Ct);
        }

        await using var outroContexto = banco.CriarContexto();
        var recuperada = Assert.Single(await new RepositorioVaquinhas(outroContexto).ListarPorOrganizadorAsync(OrganizadorA, Ct));

        Assert.Equal(original.Id, recuperada.Id);
        Assert.Equal(original.Nome, recuperada.Nome);
        Assert.Equal(original.OrganizadorId, recuperada.OrganizadorId);
        Assert.Equal(original.CriadaEm, recuperada.CriadaEm);
        Assert.Equal(original.CriadaEm.UtcTicks, recuperada.CriadaEm.UtcTicks);
        Assert.Equal(TimeSpan.Zero, recuperada.CriadaEm.Offset);
    }

    [Fact]
    public async Task Instante_e_gravado_em_UTC_no_banco()
    {
        await using var banco = await servidor.CriarBancoAsync();
        var vaquinha = Nova("019a9a00-0000-7000-8000-000000000002", "Viagem", OrganizadorA, Criacao);
        await Adicionar(banco, vaquinha);

        var linha = Assert.Single(await banco.ConsultarAsync(
            $"SELECT to_char(criada_em AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS.MS') FROM vaquinhas WHERE id = '{vaquinha.Id}'"));

        Assert.Equal("2026-11-25 12:30:15.123", linha[0]);
    }

    [Theory]
    [InlineData("Viagem à Bahia – 2026 🌴")]
    [InlineData("日本旅行の積立")]
    [InlineData("Presente da Zoë & do João (çãõ)")]
    [InlineData("Açãó com acento combinado")]
    public async Task Nomes_Unicode_sao_preservados(string nome)
    {
        await using var banco = await servidor.CriarBancoAsync();
        var vaquinha = Nova("019a9a00-0000-7000-8000-000000000003", nome, OrganizadorA, Criacao);
        await Adicionar(banco, vaquinha);

        var recuperada = Assert.Single(await Listar(banco, OrganizadorA));

        Assert.Equal(nome, recuperada.Nome);
        Assert.Equal(nome.Length, recuperada.Nome.Length);
    }

    [Fact]
    public async Task Nome_longo_e_aceito_porque_a_coluna_e_text()
    {
        await using var banco = await servidor.CriarBancoAsync();
        var nome = new string('a', 20_000);
        await Adicionar(banco, Nova("019a9a00-0000-7000-8000-000000000004", nome, OrganizadorA, Criacao));

        Assert.Equal(nome, Assert.Single(await Listar(banco, OrganizadorA)).Nome);
    }

    [Fact]
    public async Task Nomes_iguais_sao_aceitos_em_vaquinhas_diferentes_do_mesmo_organizador()
    {
        await using var banco = await servidor.CriarBancoAsync();
        await Adicionar(banco, Nova("019a9a00-0000-7000-8000-000000000005", "Viagem", OrganizadorA, Criacao));
        await Adicionar(banco, Nova("019a9a00-0000-7000-8000-000000000006", "Viagem", OrganizadorA, Criacao));

        var recuperadas = await Listar(banco, OrganizadorA);

        Assert.Equal(2, recuperadas.Count);
        Assert.All(recuperadas, v => Assert.Equal("Viagem", v.Nome));
    }

    [Fact]
    public async Task Identificador_repetido_e_rejeitado_pela_chave_primaria_e_nada_muda()
    {
        await using var banco = await servidor.CriarBancoAsync();
        var original = Nova("019a9a00-0000-7000-8000-000000000007", "Original", OrganizadorA, Criacao);
        var repetida = Nova("019a9a00-0000-7000-8000-000000000007", "Outra", OrganizadorB, Criacao.AddDays(1));
        await Adicionar(banco, original);

        await using var contexto = banco.CriarContexto();
        var repositorio = new RepositorioVaquinhas(contexto);
        var excecao = await Assert.ThrowsAsync<DbUpdateException>(() => repositorio.AdicionarAsync(repetida, Ct));

        var postgres = Assert.IsType<PostgresException>(excecao.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("pk_vaquinhas", postgres.ConstraintName);
        Assert.Equal(1, await banco.ContarAsync("SELECT count(*) FROM vaquinhas"));
        Assert.Equal("Original", Assert.Single(await Listar(banco, OrganizadorA)).Nome);
        Assert.Empty(await Listar(banco, OrganizadorB));

        // O registro rejeitado não fica pendente no contexto: um novo SaveChanges não tenta inseri-lo de novo.
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Equal(0, await contexto.SaveChangesAsync(Ct));
    }

    // ---------- Isolamento ----------

    [Fact]
    public async Task Consulta_devolve_somente_as_vaquinhas_do_organizador_informado()
    {
        await using var banco = await servidor.CriarBancoAsync();
        var a1 = Nova("019a9a00-0000-7000-8000-0000000000a1", "Viagem", OrganizadorA, Criacao);
        var a2 = Nova("019a9a00-0000-7000-8000-0000000000a2", "Formatura", OrganizadorA, Criacao.AddMinutes(1));
        var b1 = Nova("019a9a00-0000-7000-8000-0000000000b1", "Churrasco", OrganizadorB, Criacao.AddMinutes(2));
        foreach (var vaquinha in new[] { a1, b1, a2 })
        {
            await Adicionar(banco, vaquinha);
        }

        var deA = await Listar(banco, OrganizadorA);
        var deB = await Listar(banco, OrganizadorB);
        var deC = await Listar(banco, OrganizadorC);

        Assert.Equal(new[] { a1.Id, a2.Id }.Order(), deA.Select(v => v.Id).Order());
        Assert.All(deA, v => Assert.Equal(OrganizadorA, v.OrganizadorId));
        Assert.Equal([b1.Id], deB.Select(v => v.Id));
        Assert.Empty(deC);
    }

    [Fact]
    public async Task Filtro_por_organizador_e_aplicado_na_consulta_SQL()
    {
        await using var banco = await servidor.CriarBancoAsync();
        await Adicionar(banco, Nova("019a9a00-0000-7000-8000-0000000000c1", "Viagem", OrganizadorA, Criacao));
        await Adicionar(banco, Nova("019a9a00-0000-7000-8000-0000000000c2", "Churrasco", OrganizadorB, Criacao));
        var captura = new ComandosCapturados();

        await using (var contexto = banco.CriarContexto(captura))
        {
            await new RepositorioVaquinhas(contexto).ListarPorOrganizadorAsync(OrganizadorA, Ct);
        }

        var sql = Assert.Single(captura.Comandos);
        Assert.Contains("FROM vaquinhas", sql);
        Assert.Matches(@"WHERE\s+v\.organizador_id\s*=\s*@", sql);
    }

    // ---------- Falhas e integridade ----------

    [Fact]
    public async Task Falha_de_gravacao_por_banco_inexistente_e_propagada()
    {
        await using var banco = await servidor.CriarBancoAsync();
        var conexaoInvalida = new NpgsqlConnectionStringBuilder(banco.ConnectionString) { Database = banco.Nome + "_inexistente" };
        await using var contexto = new WacaDbContext(
            new DbContextOptionsBuilder<WacaDbContext>().UseNpgsql(conexaoInvalida.ConnectionString).Options);

        var excecao = await Assert.ThrowsAnyAsync<Exception>(
            () => new RepositorioVaquinhas(contexto).AdicionarAsync(Nova("019a9a00-0000-7000-8000-0000000000d1", "Viagem", OrganizadorA, Criacao), Ct));

        var postgres = Assert.IsType<PostgresException>(PrimeiraPostgresException(excecao));
        Assert.Equal(PostgresErrorCodes.InvalidCatalogName, postgres.SqlState);
        Assert.Equal(0, await banco.ContarAsync("SELECT count(*) FROM vaquinhas"));
    }

    [Theory]
    [InlineData("''", "id-normal")]
    [InlineData("'   '", "id-normal")]
    [InlineData("' Viagem'", "id-normal")]
    [InlineData("'Viagem '", "id-normal")]
    [InlineData("'Viagem'", "id-vazio")]
    [InlineData("'Viagem'", "instante-padrao")]
    public async Task Linha_invalida_no_banco_nao_vira_vaquinha_valida(string nomeSql, string caso)
    {
        await using var banco = await servidor.CriarBancoAsync();
        var id = caso == "id-vazio" ? Guid.Empty : Guid.Parse("019a9a00-0000-7000-8000-0000000000e1");
        var instante = caso == "instante-padrao" ? "'0001-01-01 00:00:00+00'" : "'2026-11-25 12:00:00+00'";
        await banco.ExecutarAsync(
            $"INSERT INTO vaquinhas (id, nome, organizador_id, criada_em) VALUES ('{id}', {nomeSql}, '{OrganizadorA}', {instante})");

        await using var contexto = banco.CriarContexto();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new RepositorioVaquinhas(contexto).ListarPorOrganizadorAsync(OrganizadorA, Ct));
    }

    [Fact]
    public async Task Linha_com_organizador_vazio_nao_vira_vaquinha_valida()
    {
        await using var banco = await servidor.CriarBancoAsync();
        await banco.ExecutarAsync(
            $"INSERT INTO vaquinhas (id, nome, organizador_id, criada_em) VALUES ('019a9a00-0000-7000-8000-0000000000e2', 'Viagem', '{Guid.Empty}', now())");

        await using var contexto = banco.CriarContexto();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new RepositorioVaquinhas(contexto).ListarPorOrganizadorAsync(Guid.Empty, Ct));
    }

    [Fact]
    public async Task Cancelamento_interrompe_a_gravacao_sem_gravar()
    {
        await using var banco = await servidor.CriarBancoAsync();
        await using var contexto = banco.CriarContexto();
        using var fonte = new CancellationTokenSource();
        await fonte.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new RepositorioVaquinhas(contexto).AdicionarAsync(Nova("019a9a00-0000-7000-8000-0000000000f1", "Viagem", OrganizadorA, Criacao), fonte.Token));

        Assert.Equal(0, await banco.ContarAsync("SELECT count(*) FROM vaquinhas"));
        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Cancelamento_interrompe_a_consulta()
    {
        await using var banco = await servidor.CriarBancoAsync();
        await using var contexto = banco.CriarContexto();
        using var fonte = new CancellationTokenSource();
        await fonte.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new RepositorioVaquinhas(contexto).ListarPorOrganizadorAsync(OrganizadorA, fonte.Token));
    }

    [Fact]
    public async Task Conexao_e_fechada_apos_cada_operacao_e_contexto_descartado_nao_e_reutilizavel()
    {
        await using var banco = await servidor.CriarBancoAsync();
        var contexto = banco.CriarContexto();
        var repositorio = new RepositorioVaquinhas(contexto);

        await repositorio.AdicionarAsync(Nova("019a9a00-0000-7000-8000-0000000000f2", "Viagem", OrganizadorA, Criacao), Ct);
        Assert.Equal(ConnectionState.Closed, contexto.Database.GetDbConnection().State);

        await repositorio.ListarPorOrganizadorAsync(OrganizadorA, Ct);
        Assert.Equal(ConnectionState.Closed, contexto.Database.GetDbConnection().State);

        await contexto.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => repositorio.ListarPorOrganizadorAsync(OrganizadorA, Ct));
    }

    [Fact]
    public async Task Repositorio_exige_contexto_e_vaquinha()
    {
        await using var banco = await servidor.CriarBancoAsync();
        await using var contexto = banco.CriarContexto();

        Assert.Throws<ArgumentNullException>(() => new RepositorioVaquinhas(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new RepositorioVaquinhas(contexto).AdicionarAsync(null!, Ct));
    }

    // ---------- Auxiliares ----------

    private static Vaquinha Nova(string id, string nome, Guid organizador, DateTimeOffset criadaEm) =>
        new(Guid.Parse(id), nome, organizador, criadaEm);

    private static async Task Adicionar(BancoDeTeste banco, Vaquinha vaquinha)
    {
        await using var contexto = banco.CriarContexto();
        await new RepositorioVaquinhas(contexto).AdicionarAsync(vaquinha, Ct);
    }

    private static async Task<IReadOnlyList<Vaquinha>> Listar(BancoDeTeste banco, Guid organizador)
    {
        await using var contexto = banco.CriarContexto();
        return await new RepositorioVaquinhas(contexto).ListarPorOrganizadorAsync(organizador, Ct);
    }

    private static PostgresException? PrimeiraPostgresException(Exception? excecao)
    {
        for (; excecao is not null; excecao = excecao.InnerException)
        {
            if (excecao is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }
}
