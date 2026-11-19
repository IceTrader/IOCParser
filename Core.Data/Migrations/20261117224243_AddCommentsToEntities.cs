using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentsToEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "Whitelist",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "Documents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Comment",
                table: "Whitelist");

            migrationBuilder.DropColumn(
                name: "Comment",
                table: "Documents");
        }
    }
}
