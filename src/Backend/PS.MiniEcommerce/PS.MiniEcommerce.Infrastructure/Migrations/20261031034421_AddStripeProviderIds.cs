using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PS.MiniEcommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeProviderIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderRefundId",
                table: "Refunds",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_ProviderRefundId",
                table: "Refunds",
                column: "ProviderRefundId",
                unique: true,
                filter: "[ProviderRefundId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderPaymentId",
                table: "Payments",
                column: "ProviderPaymentId",
                unique: true,
                filter: "[Provider] = 'stripe'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Refunds_ProviderRefundId",
                table: "Refunds");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ProviderPaymentId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProviderRefundId",
                table: "Refunds");
        }
    }
}
