using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportJobDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                table: "import_jobs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "warnings",
                table: "import_jobs",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "failure_reason",
                table: "import_jobs");

            migrationBuilder.DropColumn(
                name: "warnings",
                table: "import_jobs");
        }
    }
}
