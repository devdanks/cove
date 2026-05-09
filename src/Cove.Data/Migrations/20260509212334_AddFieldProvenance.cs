using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "field_provenance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    FieldKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ValueJson = table.Column<string>(type: "jsonb", nullable: true),
                    SourceKey = table.Column<string>(type: "text", nullable: false),
                    SourceRunId = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    ModelKey = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    Confidence = table.Column<float>(type: "real", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_provenance", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_field_provenance_HostType_HostId",
                table: "field_provenance",
                columns: new[] { "HostType", "HostId" });

            migrationBuilder.CreateIndex(
                name: "IX_field_provenance_HostType_HostId_FieldKey",
                table: "field_provenance",
                columns: new[] { "HostType", "HostId", "FieldKey" });

            migrationBuilder.CreateIndex(
                name: "IX_field_provenance_HostType_HostId_FieldKey_SourceKey_SourceR~",
                table: "field_provenance",
                columns: new[] { "HostType", "HostId", "FieldKey", "SourceKey", "SourceRunId", "ModelKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_field_provenance_SourceKey",
                table: "field_provenance",
                column: "SourceKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "field_provenance");
        }
    }
}
