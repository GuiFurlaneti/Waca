using Microsoft.EntityFrameworkCore.Design;

namespace Waca.Infrastructure.Identidade;

/// <summary>
/// Usada somente pelas ferramentas do EF Core (dotnet-ef) para gerar as migrations da identidade.
/// </summary>
/// <remarks>
/// Não contém connection string: gerar uma migration não acessa banco. Usa as mesmas opções do Identity e o
/// mesmo histórico (<c>identidade.__EFMigrationsHistory</c>) exigidos dos testes e da futura API.
/// </remarks>
public sealed class WacaIdentidadeDbContextFactory : IDesignTimeDbContextFactory<WacaIdentidadeDbContext>
{
    public WacaIdentidadeDbContext CreateDbContext(string[] args) =>
        new(WacaIdentidadeDbContext.CriarOpcoesForaDaAplicacao(connectionString: null));
}
