using Microsoft.EntityFrameworkCore;
using Waca.Infrastructure.Persistencia.Configuracoes;
using Waca.Infrastructure.Persistencia.Entidades;

namespace Waca.Infrastructure.Persistencia;

/// <summary>
/// Contexto do EF Core para o PostgreSQL do Waca.
/// </summary>
/// <remarks>
/// <para>
/// Deve ser usado com tempo de vida curto (um por requisição ou operação), nunca como singleton.
/// O EF Core abre a conexão só durante cada comando e a devolve ao pool em seguida.
/// </para>
/// <para>
/// A connection string não fica no código: quem registra o contexto deve fornecê-la por configuração
/// segura (variável de ambiente ou cofre de segredos). O esquema é criado apenas por migrations;
/// nada é aplicado automaticamente na inicialização.
/// </para>
/// </remarks>
public sealed class WacaDbContext(DbContextOptions<WacaDbContext> options) : DbContext(options)
{
    internal DbSet<VaquinhaRegistro> Vaquinhas => Set<VaquinhaRegistro>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new VaquinhaRegistroConfiguracao());
    }
}
