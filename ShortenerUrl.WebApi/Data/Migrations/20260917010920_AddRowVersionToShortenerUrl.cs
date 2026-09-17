using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortenerUrlApp.WebApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRowVersionToShortenerUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "ShortenerUrls",
                type: "xid",
                rowVersion: true,
                nullable: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "ShortenerUrls");
        }
    }
}
