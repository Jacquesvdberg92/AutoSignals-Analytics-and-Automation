using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class AddDcaBotColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoRestart",
                table: "Bots",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AverageEntryPrice",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseOrderSizeUsd",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CooldownMinutes",
                table: "Bots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CooldownUntil",
                table: "Bots",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentSafetyOrderCount",
                table: "Bots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsIsolated",
                table: "Bots",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Leverage",
                table: "Bots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxSafetyOrders",
                table: "Bots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SafetyOrderPriceDeviation",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SafetyOrderSizeUsd",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SafetyOrderStepScale",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SafetyOrderVolumeScale",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StoplossPercent",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TakeProfitPercent",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalInvested",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoRestart",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "AverageEntryPrice",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "BaseOrderSizeUsd",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "CooldownMinutes",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "CooldownUntil",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "CurrentSafetyOrderCount",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "IsIsolated",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "Leverage",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "MaxSafetyOrders",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "SafetyOrderPriceDeviation",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "SafetyOrderSizeUsd",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "SafetyOrderStepScale",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "SafetyOrderVolumeScale",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "StoplossPercent",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "TakeProfitPercent",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "TotalInvested",
                table: "Bots");
        }
    }
}
