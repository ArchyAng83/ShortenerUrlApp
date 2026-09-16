using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortenerUrlApp.WebApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAliasAndExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ShortCode",
                table: "ShortenerUrls",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "ShortenerUrls",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCustomAlias",
                table: "ShortenerUrls",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxClicks",
                table: "ShortenerUrls",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClickEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShortenerUrlId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClickedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    Referrer = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClickEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClickEvents_ShortenerUrls_ShortenerUrlId",
                        column: x => x.ShortenerUrlId,
                        principalTable: "ShortenerUrls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShortenerUrls_ExpiresAt",
                table: "ShortenerUrls",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ClickEvents_ShortenerUrlId_ClickedAt",
                table: "ClickEvents",
                columns: new[] { "ShortenerUrlId", "ClickedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClickEvents");

            migrationBuilder.DropIndex(
                name: "IX_ShortenerUrls_ExpiresAt",
                table: "ShortenerUrls");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "ShortenerUrls");

            migrationBuilder.DropColumn(
                name: "IsCustomAlias",
                table: "ShortenerUrls");

            migrationBuilder.DropColumn(
                name: "MaxClicks",
                table: "ShortenerUrls");

            migrationBuilder.AlterColumn<string>(
                name: "ShortCode",
                table: "ShortenerUrls",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }
    }
}
