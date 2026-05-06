using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameLikesAndUserScopedTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "SceneOHistory",
                newName: "SceneLikeHistory");

            migrationBuilder.RenameColumn(
                name: "OCount",
                table: "user_entity_affinities",
                newName: "LikeCount");

            migrationBuilder.RenameColumn(
                name: "OCounter",
                table: "scenes",
                newName: "LikeCounter");

            migrationBuilder.RenameColumn(
                name: "OCounter",
                table: "images",
                newName: "LikeCounter");

            migrationBuilder.RenameIndex(
                name: "IX_SceneOHistory_SceneId",
                table: "SceneLikeHistory",
                newName: "IX_SceneLikeHistory_SceneId");

            migrationBuilder.Sql("ALTER TABLE \"SceneLikeHistory\" RENAME CONSTRAINT \"PK_SceneOHistory\" TO \"PK_SceneLikeHistory\";");
            migrationBuilder.Sql("ALTER TABLE \"SceneLikeHistory\" RENAME CONSTRAINT \"FK_SceneOHistory_scenes_SceneId\" TO \"FK_SceneLikeHistory_scenes_SceneId\";");

            migrationBuilder.AddColumn<int>(
                name: "DerivedLikeCount",
                table: "user_entity_affinities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PageVisitCount",
                table: "user_entity_affinities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "DerivedLikeAwarded",
                table: "PlaybackSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DerivedLikeCount",
                table: "user_entity_affinities");

            migrationBuilder.DropColumn(
                name: "PageVisitCount",
                table: "user_entity_affinities");

            migrationBuilder.DropColumn(
                name: "DerivedLikeAwarded",
                table: "PlaybackSessions");

            migrationBuilder.RenameColumn(
                name: "LikeCount",
                table: "user_entity_affinities",
                newName: "OCount");

            migrationBuilder.RenameColumn(
                name: "LikeCounter",
                table: "scenes",
                newName: "OCounter");

            migrationBuilder.RenameColumn(
                name: "LikeCounter",
                table: "images",
                newName: "OCounter");

            migrationBuilder.Sql("ALTER TABLE \"SceneLikeHistory\" RENAME CONSTRAINT \"PK_SceneLikeHistory\" TO \"PK_SceneOHistory\";");
            migrationBuilder.Sql("ALTER TABLE \"SceneLikeHistory\" RENAME CONSTRAINT \"FK_SceneLikeHistory_scenes_SceneId\" TO \"FK_SceneOHistory_scenes_SceneId\";");

            migrationBuilder.RenameIndex(
                name: "IX_SceneLikeHistory_SceneId",
                table: "SceneLikeHistory",
                newName: "IX_SceneOHistory_SceneId");

            migrationBuilder.RenameTable(
                name: "SceneLikeHistory",
                newName: "SceneOHistory");
        }
    }
}
