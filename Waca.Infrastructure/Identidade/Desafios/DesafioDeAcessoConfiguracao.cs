using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Waca.Infrastructure.Identidade.Desafios;

/// <summary>Mapeamento de <c>identidade.desafios_acesso</c> e de suas restrições de integridade.</summary>
internal sealed class DesafioDeAcessoConfiguracao : IEntityTypeConfiguration<DesafioDeAcessoRegistro>
{
    internal const string Tabela = "desafios_acesso";
    internal const string IndiceDeDesafioAberto = "ux_desafios_acesso_aberto";
    internal const string IndiceDeCota = "ix_desafios_acesso_cota";
    internal const string IndiceDeExpiracao = "ix_desafios_acesso_expira_em";

    public void Configure(EntityTypeBuilder<DesafioDeAcessoRegistro> builder)
    {
        builder.ToTable(Tabela, tabela =>
        {
            tabela.HasCheckConstraint("ck_desafios_acesso_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
            tabela.HasCheckConstraint("ck_desafios_acesso_finalidade", "finalidade IN ('Acesso', 'TrocaDeEmail')");
            tabela.HasCheckConstraint(
                "ck_desafios_acesso_pessoa",
                "(finalidade <> 'Acesso' OR pessoa_id IS NULL) AND (finalidade <> 'TrocaDeEmail' OR (pessoa_id IS NOT NULL AND pessoa_id <> '00000000-0000-0000-0000-000000000000'::uuid))");
            tabela.HasCheckConstraint("ck_desafios_acesso_email", "email_normalizado <> ''");
            tabela.HasCheckConstraint("ck_desafios_acesso_segredo", $"octet_length(segredo_hash) = {ProtecaoDoCodigo.TamanhoDoSegredo}");
            tabela.HasCheckConstraint("ck_desafios_acesso_versao_chave", "versao_chave > 0");
            tabela.HasCheckConstraint("ck_desafios_acesso_validade", "expira_em > criado_em");
            tabela.HasCheckConstraint(
                "ck_desafios_acesso_tentativas",
                $"max_tentativas BETWEEN 1 AND {OpcoesDeDesafios.MaximoDeTentativasPermitido} AND tentativas BETWEEN 0 AND max_tentativas");
            tabela.HasCheckConstraint("ck_desafios_acesso_estado_final", "consumido_em IS NULL OR invalidado_em IS NULL");
            tabela.HasCheckConstraint("ck_desafios_acesso_consumo", "consumido_em IS NULL OR (consumido_em >= criado_em AND consumido_em < expira_em)");
            tabela.HasCheckConstraint("ck_desafios_acesso_invalidacao", "invalidado_em IS NULL OR invalidado_em >= criado_em");
            tabela.HasCheckConstraint(
                "ck_desafios_acesso_esgotado",
                "tentativas < max_tentativas OR consumido_em IS NOT NULL OR invalidado_em IS NOT NULL");
        });

        builder.HasKey(d => d.Id).HasName("pk_desafios_acesso");
        builder.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(d => d.Finalidade).HasColumnName("finalidade").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(d => d.EmailNormalizado).HasColumnName("email_normalizado").HasMaxLength(256).IsRequired();
        builder.Property(d => d.PessoaId).HasColumnName("pessoa_id");
        builder.Property(d => d.SegredoHash).HasColumnName("segredo_hash").HasColumnType("bytea").IsRequired();
        builder.Property(d => d.VersaoChave).HasColumnName("versao_chave").IsRequired();
        builder.Property(d => d.CriadoEm).HasColumnName("criado_em").IsRequired();
        builder.Property(d => d.ExpiraEm).HasColumnName("expira_em").IsRequired();
        builder.Property(d => d.Tentativas).HasColumnName("tentativas").IsRequired();
        builder.Property(d => d.MaxTentativas).HasColumnName("max_tentativas").IsRequired();
        builder.Property(d => d.ConsumidoEm).HasColumnName("consumido_em");
        builder.Property(d => d.InvalidadoEm).HasColumnName("invalidado_em");

        // No máximo um desafio aberto por e-mail e finalidade: proteção adicional à serialização da emissão.
        builder.HasIndex(d => new { d.EmailNormalizado, d.Finalidade })
            .HasDatabaseName(IndiceDeDesafioAberto)
            .IsUnique()
            .HasFilter("consumido_em IS NULL AND invalidado_em IS NULL");

        // Consultas de cota (último envio e contagem por janela).
        builder.HasIndex(d => new { d.EmailNormalizado, d.Finalidade, d.CriadoEm }).HasDatabaseName(IndiceDeCota);

        // Limpeza por expiração.
        builder.HasIndex(d => d.ExpiraEm).HasDatabaseName(IndiceDeExpiracao);
    }
}
