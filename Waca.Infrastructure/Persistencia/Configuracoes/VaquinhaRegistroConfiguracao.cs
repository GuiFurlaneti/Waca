using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waca.Infrastructure.Persistencia.Entidades;

namespace Waca.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Mapeamento da tabela <c>vaquinhas</c>.
/// </summary>
/// <remarks>
/// O identificador vem da Application (nunca gerado pelo banco). O nome é <c>text</c> sem limite,
/// enquanto não houver regra de tamanho aprovada. Não há unicidade de nome ou organizador
/// nem relacionamento com tabelas legadas.
/// </remarks>
internal sealed class VaquinhaRegistroConfiguracao : IEntityTypeConfiguration<VaquinhaRegistro>
{
    public void Configure(EntityTypeBuilder<VaquinhaRegistro> builder)
    {
        builder.ToTable("vaquinhas");

        builder.HasKey(v => v.Id).HasName("pk_vaquinhas");

        builder.Property(v => v.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(v => v.Nome)
            .HasColumnName("nome")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(v => v.OrganizadorId)
            .HasColumnName("organizador_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(v => v.CriadaEm)
            .HasColumnName("criada_em")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(v => v.OrganizadorId).HasDatabaseName("ix_vaquinhas_organizador_id");
    }
}
