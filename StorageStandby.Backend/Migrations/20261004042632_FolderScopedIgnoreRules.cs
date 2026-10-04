using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StorageStandby.Backend.Migrations
{
    /// <inheritdoc />
    public partial class FolderScopedIgnoreRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GlobalIgnoreRules",
                table: "SystemSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GlobalIgnoreRules",
                table: "SystemSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "GlobalIgnoreRules",
                value: "*C:/");
        }
    }
}
