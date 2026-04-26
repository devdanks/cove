using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScrapeAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scrape_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScraperId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: true),
                    InputKind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InputJson = table.Column<string>(type: "text", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: true),
                    EntitySnapshotJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppliedByUser = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scrape_attempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_scrape_attempts_CreatedAt",
                table: "scrape_attempts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_scrape_attempts_EntityType_EntityId_CreatedAt",
                table: "scrape_attempts",
                columns: new[] { "EntityType", "EntityId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_scrape_attempts_Status",
                table: "scrape_attempts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scrape_attempts");
        }
    }
}
