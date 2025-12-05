using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class klineFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KLineAssetPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    High = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Low = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Close = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Volume = table.Column<decimal>(type: "decimal(28,8)", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KLineAssetPrices", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KLineAssetPrices");
        }
    }
}
