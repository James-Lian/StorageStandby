using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StorageStandby.Backend.Migrations
{
    /// <inheritdoc />
    public partial class MultiAccountProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_CloudTokens",
                table: "CloudTokens");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "CloudTokens");

            migrationBuilder.AddColumn<string>(
                name: "AccountId",
                table: "CloudTokens",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AccountName",
                table: "CloudTokens",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "CloudTokens",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_CloudTokens",
                table: "CloudTokens",
                columns: new[] { "ProviderName", "AccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_CloudTokens",
                table: "CloudTokens");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "CloudTokens");

            migrationBuilder.DropColumn(
                name: "AccountName",
                table: "CloudTokens");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "CloudTokens");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "CloudTokens",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0)
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_CloudTokens",
                table: "CloudTokens",
                column: "Id");
        }
    }
}
