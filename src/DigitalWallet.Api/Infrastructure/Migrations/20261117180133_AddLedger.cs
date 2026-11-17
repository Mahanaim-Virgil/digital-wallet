using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DigitalWallet.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "type",
                table: "wallets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Elle değiştirildi: var olan cüzdanlar müşteri cüzdanıdır (EF'in ürettiği varsayılan "" idi).
                defaultValue: "Customer");

            migrationBuilder.CreateTable(
                name: "ledger_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_transactions", x => x.id);
                    table.CheckConstraint("ck_ledger_transactions_amount_positive", "amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_amount_non_zero", "amount <> 0");
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledger_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "ledger_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_wallets_wallet_id",
                        column: x => x.wallet_id,
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "wallets",
                columns: new[] { "id", "balance", "created_at_utc", "currency", "owner_name", "type" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000840"), 0m, new DateTimeOffset(new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "USD", "Sistem: dış kaynak (USD)", "System" },
                    { new Guid("00000000-0000-0000-0000-000000000949"), 0m, new DateTimeOffset(new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TRY", "Sistem: dış kaynak (TRY)", "System" },
                    { new Guid("00000000-0000-0000-0000-000000000978"), 0m, new DateTimeOffset(new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "EUR", "Sistem: dış kaynak (EUR)", "System" }
                });

            migrationBuilder.CreateIndex(
                name: "ux_wallets_system_currency",
                table: "wallets",
                column: "currency",
                unique: true,
                filter: "type = 'System'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wallets_customer_balance_non_negative",
                table: "wallets",
                sql: "type = 'System' OR balance >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_transaction_id",
                table: "ledger_entries",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_wallet_id_created_at_utc",
                table: "ledger_entries",
                columns: new[] { "wallet_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "ledger_transactions");

            migrationBuilder.DropIndex(
                name: "ux_wallets_system_currency",
                table: "wallets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wallets_customer_balance_non_negative",
                table: "wallets");

            migrationBuilder.DeleteData(
                table: "wallets",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000840"));

            migrationBuilder.DeleteData(
                table: "wallets",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000949"));

            migrationBuilder.DeleteData(
                table: "wallets",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000978"));

            migrationBuilder.DropColumn(
                name: "type",
                table: "wallets");
        }
    }
}
