using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StorageStandby.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticSyncInterval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutomaticSyncIntervalMinutes",
                table: "SystemSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "AutomaticSyncIntervalMinutes",
                value: 60);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutomaticSyncIntervalMinutes",
                table: "SystemSettings");
        }
    }
}
