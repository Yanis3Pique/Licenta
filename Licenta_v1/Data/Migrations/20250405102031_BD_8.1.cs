using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licenta_v1.Data.Migrations
{
    /// <inheritdoc />
    public partial class BD_81 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OptimizationLocks");

            migrationBuilder.CreateTable(
                name: "OrderVehicleRestrictions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderVehicleRestrictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderVehicleRestrictions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderVehicleRestrictions_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderVehicleRestrictions_OrderId",
                table: "OrderVehicleRestrictions",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderVehicleRestrictions_VehicleId",
                table: "OrderVehicleRestrictions",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderVehicleRestrictions");

            migrationBuilder.CreateTable(
                name: "OptimizationLocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedById = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsGlobal = table.Column<bool>(type: "bit", nullable: false),
                    IsRunning = table.Column<bool>(type: "bit", nullable: false),
                    RegionId = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptimizationLocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OptimizationLocks_AspNetUsers_StartedById",
                        column: x => x.StartedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OptimizationLocks_StartedById",
                table: "OptimizationLocks",
                column: "StartedById");
        }
    }
}
