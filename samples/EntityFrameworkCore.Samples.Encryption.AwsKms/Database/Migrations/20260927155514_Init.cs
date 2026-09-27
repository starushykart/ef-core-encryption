using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EntityFrameworkCore.Samples.Encryption.AwsKms.Database.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "EncryptedWrappedPasswords",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedFluent = table.Column<string>(type: "text", nullable: false),
                    EncryptedAttribute = table.Column<string>(type: "text", nullable: false),
                    Original = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncryptedWrappedPasswords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__EncryptionKeys",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    WrappingKeyId = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    WrappedKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___EncryptionKeys", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EncryptedWrappedPasswords",
                schema: "public");

            migrationBuilder.DropTable(
                name: "__EncryptionKeys",
                schema: "public");
        }
    }
}
