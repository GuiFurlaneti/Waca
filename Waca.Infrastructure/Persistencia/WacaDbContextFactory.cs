using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Waca.Infrastructure.Persistencia;

/// <summary>
/// Usada somente pelas ferramentas do EF Core (dotnet-ef) para gerar migrations.
/// </summary>
/// <remarks>
/// Não contém connection string: gerar uma migration não acessa banco. Para aplicar migrations,
/// a connection string deve ser informada explicitamente à ferramenta (<c>--connection</c>).
/// </remarks>
public sealed class WacaDbContextFactory : IDesignTimeDbContextFactory<WacaDbContext>
{
    public WacaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<WacaDbContext>().UseNpgsql().Options);
}
