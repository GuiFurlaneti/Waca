using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waca.Infrastructure.Identidade.Migrations
{
    /// <inheritdoc />
    public partial class DesafiosDeAcesso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "desafios_acesso",
                schema: "identidade",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    finalidade = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    email_normalizado = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    pessoa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    segredo_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    versao_chave = table.Column<short>(type: "smallint", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    max_tentativas = table.Column<int>(type: "integer", nullable: false),
                    consumido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invalidado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_desafios_acesso", x => x.id);
                    table.CheckConstraint("ck_desafios_acesso_consumo", "consumido_em IS NULL OR (consumido_em >= criado_em AND consumido_em < expira_em)");
                    table.CheckConstraint("ck_desafios_acesso_email", "email_normalizado <> ''");
                    table.CheckConstraint("ck_desafios_acesso_esgotado", "tentativas < max_tentativas OR consumido_em IS NOT NULL OR invalidado_em IS NOT NULL");
                    table.CheckConstraint("ck_desafios_acesso_estado_final", "consumido_em IS NULL OR invalidado_em IS NULL");
                    table.CheckConstraint("ck_desafios_acesso_finalidade", "finalidade IN ('Acesso', 'TrocaDeEmail')");
                    table.CheckConstraint("ck_desafios_acesso_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_desafios_acesso_invalidacao", "invalidado_em IS NULL OR invalidado_em >= criado_em");
                    table.CheckConstraint("ck_desafios_acesso_pessoa", "(finalidade <> 'Acesso' OR pessoa_id IS NULL) AND (finalidade <> 'TrocaDeEmail' OR (pessoa_id IS NOT NULL AND pessoa_id <> '00000000-0000-0000-0000-000000000000'::uuid))");
                    table.CheckConstraint("ck_desafios_acesso_segredo", "octet_length(segredo_hash) = 32");
                    table.CheckConstraint("ck_desafios_acesso_tentativas", "max_tentativas BETWEEN 1 AND 10 AND tentativas BETWEEN 0 AND max_tentativas");
                    table.CheckConstraint("ck_desafios_acesso_validade", "expira_em > criado_em");
                    table.CheckConstraint("ck_desafios_acesso_versao_chave", "versao_chave > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_desafios_acesso_cota",
                schema: "identidade",
                table: "desafios_acesso",
                columns: new[] { "email_normalizado", "finalidade", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_desafios_acesso_expira_em",
                schema: "identidade",
                table: "desafios_acesso",
                column: "expira_em");

            migrationBuilder.CreateIndex(
                name: "ux_desafios_acesso_aberto",
                schema: "identidade",
                table: "desafios_acesso",
                columns: new[] { "email_normalizado", "finalidade" },
                unique: true,
                filter: "consumido_em IS NULL AND invalidado_em IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "desafios_acesso",
                schema: "identidade");
        }
    }
}
