using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class BookDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Hidden",
                table: "Books",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Books",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Year",
                table: "Books",
                type: "integer",
                nullable: true);

            // Fill in the sample books that already exist, without touching anything an admin set.
            foreach (var (title, year, language) in new[]
            {
                ("Pride and Prejudice", 1813, "English"),
                ("Frankenstein", 1818, "English"),
                ("Dracula", 1897, "English"),
                ("Crime and Punishment", 1866, "Russian"),
                ("Anna Karenina", 1878, "Russian"),
                ("Les Misérables", 1862, "French"),
                ("Around the World in 80 Days", 1872, "French"),
                ("Moby-Dick", 1851, "English"),
            })
            {
                migrationBuilder.Sql(
                    $"UPDATE \"Books\" SET \"Year\" = {year}, \"Language\" = '{language}' " +
                    $"WHERE \"Title\" = '{title}' AND \"Year\" IS NULL AND \"Language\" IS NULL;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Hidden",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Year",
                table: "Books");
        }
    }
}
