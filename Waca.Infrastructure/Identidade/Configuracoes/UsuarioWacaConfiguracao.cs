using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Waca.Infrastructure.Identidade.Configuracoes;

/// <summary>
/// Ajustes do Waca sobre o mapeamento padrão do Identity para <see cref="UsuarioWaca"/>.
/// </summary>
/// <remarks>
/// Aplicada depois do mapeamento do Identity. O índice <c>EmailIndex</c> é o mesmo criado pelo Identity,
/// tornado único; nenhum índice paralelo é criado. <c>UserNameIndex</c> permanece como o Identity define.
/// </remarks>
internal sealed class UsuarioWacaConfiguracao : IEntityTypeConfiguration<UsuarioWaca>
{
    internal const string Tabela = "AspNetUsers";
    internal const string IndiceDeEmail = "EmailIndex";
    internal const string RestricaoDeEstado = "CK_AspNetUsers_Estado";
    internal const string RestricaoDeIdNaoVazio = "CK_AspNetUsers_Id_NaoVazio";
    internal const string RestricaoDeEmailNaoVazio = "CK_AspNetUsers_Email_NaoVazio";
    internal const int ComprimentoMaximoDeEmail = 256;
    internal const int ComprimentoMaximoDeEstado = 20;

    public void Configure(EntityTypeBuilder<UsuarioWaca> builder)
    {
        builder.ToTable(Tabela, tabela =>
        {
            tabela.HasCheckConstraint(RestricaoDeEstado, "\"Estado\" IN ('Ativa', 'Desativada')");
            tabela.HasCheckConstraint(RestricaoDeIdNaoVazio, "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            tabela.HasCheckConstraint(RestricaoDeEmailNaoVazio, "\"Email\" <> '' AND \"NormalizedEmail\" <> ''");
        });

        // O identificador vem de quem cria a conta (Guid v7 previsto); o banco e o EF nunca o geram.
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.Email).IsRequired().HasMaxLength(ComprimentoMaximoDeEmail);
        builder.Property(u => u.NormalizedEmail).IsRequired().HasMaxLength(ComprimentoMaximoDeEmail);
        builder.HasIndex(u => u.NormalizedEmail).HasDatabaseName(IndiceDeEmail).IsUnique();

        builder.Property(u => u.Estado)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(ComprimentoMaximoDeEstado);
    }
}
