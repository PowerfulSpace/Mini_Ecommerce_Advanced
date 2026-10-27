using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PS.MiniEcommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderStatusesAndPaymentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Status_Valid",
                table: "Orders");

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Status_Valid",
                table: "Orders",
                sql: "[Status] IN ('Created', 'Paid', 'Shipped', 'Delivered', 'Cancelled', 'Returned')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Status_Valid",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "Orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Status_Valid",
                table: "Orders",
                sql: "[Status] IN ('Created', 'Cancelled')");
        }
    }
}
