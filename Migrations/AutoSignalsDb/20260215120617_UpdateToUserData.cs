using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class UpdateToUserData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                table: "UsersData",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailNotifications",
                table: "UsersData",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "UsersData");

            migrationBuilder.DropColumn(
                name: "EmailNotifications",
                table: "UsersData");
        }
    }
}
