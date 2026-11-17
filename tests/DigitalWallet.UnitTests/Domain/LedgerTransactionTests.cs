using DigitalWallet.Api.Domain;

namespace DigitalWallet.UnitTests.Domain;

public sealed class LedgerTransactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    private static Wallet Customer(string currency = "TRY") => Wallet.Open("Test", currency, Now);
    private static Wallet System(string currency = "TRY") =>
        Wallet.CreateSystem(SystemWallets.ForCurrency(currency), currency, Now);

    private static Wallet FundedCustomer(decimal amount, string currency = "TRY")
    {
        var wallet = Customer(currency);
        LedgerTransaction.Deposit(System(currency), wallet, amount, Now);
        return wallet;
    }

    [Fact]
    public void Deposit_moves_money_from_system_wallet_and_balances_to_zero()
    {
        var system = System();
        var wallet = Customer();

        var transaction = LedgerTransaction.Deposit(system, wallet, 500m, Now);

        Assert.Equal(TransactionType.Deposit, transaction.Type);
        Assert.Equal(2, transaction.Entries.Count);
        Assert.Equal(0m, transaction.Entries.Sum(e => e.Amount));
        Assert.Equal(500m, wallet.Balance);
        Assert.Equal(-500m, system.Balance); // Sistem cüzdanı eksiye düşebilir: müşterilere olan borç.
    }

    [Fact]
    public void Transfer_writes_signed_entries_with_balance_after()
    {
        var from = FundedCustomer(100m);
        var to = Customer();

        var transaction = LedgerTransaction.Transfer(from, to, 30.25m, Now);

        var debit = Assert.Single(transaction.Entries, e => e.WalletId == from.Id);
        var credit = Assert.Single(transaction.Entries, e => e.WalletId == to.Id);
        Assert.Equal(-30.25m, debit.Amount);
        Assert.Equal(69.75m, debit.BalanceAfter);
        Assert.Equal(30.25m, credit.Amount);
        Assert.Equal(30.25m, credit.BalanceAfter);
        Assert.Equal(0m, transaction.Entries.Sum(e => e.Amount));
    }

    [Fact]
    public void Transfer_with_insufficient_funds_is_rejected_and_changes_nothing()
    {
        var from = FundedCustomer(10m);
        var to = Customer();

        var ex = Assert.Throws<DomainException>(() => LedgerTransaction.Transfer(from, to, 10.01m, Now));

        Assert.Equal(DomainErrors.InsufficientFunds, ex.Code);
        Assert.Equal(10m, from.Balance);
        Assert.Equal(0m, to.Balance);
    }

    [Fact]
    public void Transfer_of_exact_balance_leaves_zero()
    {
        var from = FundedCustomer(10m);
        LedgerTransaction.Transfer(from, Customer(), 10m, Now);
        Assert.Equal(0m, from.Balance);
    }

    [Fact]
    public void Transfer_to_same_wallet_is_rejected()
    {
        var wallet = FundedCustomer(10m);
        var ex = Assert.Throws<DomainException>(() => LedgerTransaction.Transfer(wallet, wallet, 1m, Now));
        Assert.Equal(DomainErrors.SameWallet, ex.Code);
    }

    [Fact]
    public void Transfer_between_currencies_is_rejected()
    {
        var ex = Assert.Throws<DomainException>(() =>
            LedgerTransaction.Transfer(FundedCustomer(10m, "TRY"), Customer("USD"), 1m, Now));
        Assert.Equal(DomainErrors.CurrencyMismatch, ex.Code);
    }

    [Fact]
    public void System_wallet_cannot_take_part_in_transfers_or_receive_deposits()
    {
        var transferEx = Assert.Throws<DomainException>(() => LedgerTransaction.Transfer(System(), Customer(), 1m, Now));
        var depositEx = Assert.Throws<DomainException>(() => LedgerTransaction.Deposit(System(), System(), 1m, Now));

        Assert.Equal(DomainErrors.SystemWalletNotAllowed, transferEx.Code);
        Assert.Equal(DomainErrors.SystemWalletNotAllowed, depositEx.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_amount_is_rejected(int amount)
    {
        var ex = Assert.Throws<DomainException>(() => LedgerTransaction.Deposit(System(), Customer(), amount, Now));
        Assert.Equal(DomainErrors.InvalidAmount, ex.Code);
    }
}
