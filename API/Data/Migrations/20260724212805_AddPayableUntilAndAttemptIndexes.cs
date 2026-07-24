using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayableUntilAndAttemptIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_PaymentAttempts_PaymentId",
                table: "PaymentAttempts",
                newName: "IX_PaymentAttempts_PaymentId_Completed");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PayableUntil",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_PaymentId_Pending",
                table: "PaymentAttempts",
                column: "PaymentId",
                unique: true,
                filter: "\"Status\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentAttempts_PaymentId_Pending",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "PayableUntil",
                table: "Payments");

            migrationBuilder.RenameIndex(
                name: "IX_PaymentAttempts_PaymentId_Completed",
                table: "PaymentAttempts",
                newName: "IX_PaymentAttempts_PaymentId");
        }
    }
}
