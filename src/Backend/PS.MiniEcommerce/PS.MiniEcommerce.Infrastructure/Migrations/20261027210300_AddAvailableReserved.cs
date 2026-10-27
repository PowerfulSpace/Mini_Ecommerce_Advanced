using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PS.MiniEcommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAvailableReserved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Available",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Reserved",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE Products SET Available = StockQuantity, Reserved = 0;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_StockQuantity_NonNegative",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StockQuantity",
                table: "Products");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Available_NonNegative",
                table: "Products",
                sql: "[Available] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Reserved_NonNegative",
                table: "Products",
                sql: "[Reserved] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StockQuantity",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE Products SET StockQuantity = Available;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Available_NonNegative",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Reserved_NonNegative",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Available",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Reserved",
                table: "Products");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_StockQuantity_NonNegative",
                table: "Products",
                sql: "[StockQuantity] >= 0");
        }
    }
}
