using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentTimeoutScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PaymentTimeoutTokenId",
                table: "OrderState",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RefundRequestedAt",
                table: "OrderState",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RefundedAt",
                table: "OrderState",
                type: "timestamp with time zone",
                nullable: true);

            // Quartz AdoJobStore tabloları ("quartz" şeması); bkz. Persistence/Sql/quartz_tables_postgres.sql
            migrationBuilder.Sql(QuartzSchema.CreateScript());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP SCHEMA IF EXISTS {QuartzSchema.Name} CASCADE;");

            migrationBuilder.DropColumn(
                name: "PaymentTimeoutTokenId",
                table: "OrderState");

            migrationBuilder.DropColumn(
                name: "RefundRequestedAt",
                table: "OrderState");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                table: "OrderState");
        }
    }
}
