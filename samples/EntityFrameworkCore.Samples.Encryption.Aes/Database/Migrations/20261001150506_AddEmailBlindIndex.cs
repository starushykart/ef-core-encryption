using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EntityFrameworkCore.Samples.Encryption.Aes.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailBlindIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Email_Index",
                table: "Customers",
                type: "bytea",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Email_Index",
                table: "Customers",
                column: "Email_Index");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_Email_Index",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Email_Index",
                table: "Customers");
        }
    }
}
