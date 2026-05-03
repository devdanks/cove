using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceSuggestionDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "face_suggestion_decisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FaceId = table.Column<int>(type: "integer", nullable: false),
                    PerformerId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_face_suggestion_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_face_suggestion_decisions_faces_FaceId",
                        column: x => x.FaceId,
                        principalTable: "faces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_face_suggestion_decisions_performers_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "performers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_face_suggestion_decisions_FaceId_PerformerId_UserId",
                table: "face_suggestion_decisions",
                columns: new[] { "FaceId", "PerformerId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_face_suggestion_decisions_FaceId_UserId",
                table: "face_suggestion_decisions",
                columns: new[] { "FaceId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_face_suggestion_decisions_PerformerId",
                table: "face_suggestion_decisions",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "IX_face_suggestion_decisions_UserId_Decision",
                table: "face_suggestion_decisions",
                columns: new[] { "UserId", "Decision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "face_suggestion_decisions");
        }
    }
}
