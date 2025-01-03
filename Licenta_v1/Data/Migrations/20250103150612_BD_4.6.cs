using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licenta_v1.Data.Migrations
{
    /// <inheritdoc />
    public partial class BD_46 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastBatteryCheckDate",
                table: "Vehicles",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<double>(
                name: "LastBatteryCheckKM",
                table: "Vehicles",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "LastBrakePadChangeKM",
                table: "Vehicles",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastCoolantCheckDate",
                table: "Vehicles",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<double>(
                name: "LastCoolantCheckKM",
                table: "Vehicles",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEngineServiceDate",
                table: "Vehicles",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<double>(
                name: "LastEngineServiceKM",
                table: "Vehicles",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastGeneralInspectionDate",
                table: "Vehicles",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<double>(
                name: "LastGeneralInspectionKM",
                table: "Vehicles",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSuspensionServiceDate",
                table: "Vehicles",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<double>(
                name: "LastSuspensionServiceKM",
                table: "Vehicles",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastTireChangeDate",
                table: "Vehicles",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<double>(
                name: "LastTireChangeKM",
                table: "Vehicles",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AlterColumn<int>(
                name: "MaintenanceType",
                table: "Maintenances",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastBatteryCheckDate",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastBatteryCheckKM",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastBrakePadChangeKM",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastCoolantCheckDate",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastCoolantCheckKM",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastEngineServiceDate",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastEngineServiceKM",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastGeneralInspectionDate",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastGeneralInspectionKM",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastSuspensionServiceDate",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastSuspensionServiceKM",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastTireChangeDate",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastTireChangeKM",
                table: "Vehicles");

            migrationBuilder.AlterColumn<string>(
                name: "MaintenanceType",
                table: "Maintenances",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
