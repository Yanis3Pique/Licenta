using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licenta_v1.Data.Migrations
{
    /// <inheritdoc />
    public partial class BD_61 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ConsumptionEstimated",
                table: "Deliveries",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TimeTakenForDelivery",
                table: "Deliveries",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsumptionEstimated",
                table: "Deliveries");

            migrationBuilder.DropColumn(
                name: "TimeTakenForDelivery",
                table: "Deliveries");
        }
    }
}
