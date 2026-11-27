using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Waca.Infrastructure.Identidade.Sessoes;

/// <summary>Mapeamento de <c>identidade.sessoes</c> e de suas restrições.</summary>
internal sealed class SessaoConfiguracao : IEntityTypeConfiguration<SessaoRegistro>
{
    internal const string Tabela = "sessoes";

    public void Configure(EntityTypeBuilder<SessaoRegistro> builder)
    {
        builder.ToTable(Tabela, tabela =>
        {
            tabela.HasCheckConstraint("ck_sessoes_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
            tabela.HasCheckConstraint("ck_sessoes_validade", "expira_absoluta_em > criada_em");
            tabela.HasCheckConstraint("ck_sessoes_revogacao", "revogada_em IS NULL OR revogada_em >= criada_em");
        });

        builder.HasKey(s => s.Id).HasName("pk_sessoes");
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(s => s.PessoaId).HasColumnName("pessoa_id").IsRequired();
        builder.Property(s => s.CriadaEm).HasColumnName("criada_em").IsRequired();
        builder.Property(s => s.ExpiraAbsolutaEm).HasColumnName("expira_absoluta_em").IsRequired();
        builder.Property(s => s.RevogadaEm).HasColumnName("revogada_em");

        builder.HasOne<UsuarioWaca>()
            .WithMany()
            .HasForeignKey(s => s.PessoaId)
            .HasConstraintName("fk_sessoes_pessoa")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.PessoaId).HasDatabaseName("ix_sessoes_pessoa_id");
    }
}
