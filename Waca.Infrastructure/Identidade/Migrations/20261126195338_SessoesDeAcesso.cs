using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waca.Infrastructure.Identidade.Migrations
{
    /// <inheritdoc />
    public partial class SessoesDeAcesso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sessoes",
                schema: "identidade",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pessoa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_absoluta_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revogada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessoes", x => x.id);
                    table.CheckConstraint("ck_sessoes_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sessoes_revogacao", "revogada_em IS NULL OR revogada_em >= criada_em");
                    table.CheckConstraint("ck_sessoes_validade", "expira_absoluta_em > criada_em");
                    table.ForeignKey(
                        name: "fk_sessoes_pessoa",
                        column: x => x.pessoa_id,
                        principalSchema: "identidade",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sessoes_pessoa_id",
                schema: "identidade",
                table: "sessoes",
                column: "pessoa_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sessoes",
                schema: "identidade");
        }
    }
}
