using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortenerUrlApp.WebApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShortenerUrls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LongUrl = table.Column<string>(type: "text", nullable: false),
                    ShortCode = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    CreateAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CountOfClick = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortenerUrls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShortenerUrls_ShortCode",
                table: "ShortenerUrls",
                column: "ShortCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShortenerUrls");
        }
    }
}
