using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waca.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class CriacaoInicialVaquinhas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vaquinhas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    organizador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vaquinhas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vaquinhas_organizador_id",
                table: "vaquinhas",
                column: "organizador_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vaquinhas");
        }
    }
}
