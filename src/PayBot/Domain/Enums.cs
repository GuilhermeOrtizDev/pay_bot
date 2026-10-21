namespace PayBot.Domain;

public enum Direction
{
    Income = 1,
    Expense = 2
}

public enum PaymentMethod
{
    Pix = 1,
    CreditCard = 2,
    DebitCard = 3,
    CashWithdrawal = 4,
    Fixed = 5,
    Other = 6
}

public enum CardKind
{
    Credit = 1,
    Debit = 2
}

public enum CardBilling
{
    Installments = 1,
    InFull = 2,
    Fixed = 3
}

public enum InstallmentAmountKind
{
    Installment = 1,
    Total = 2
}

public enum MenuStep
{
    Main = 0,
    ExpenseMenu = 1,
    AwaitingAmount = 2,
    AwaitingInstallments = 3,
    AwaitingCardChoice = 4,
    AwaitingNewCardName = 5,
    AwaitingIncomeAmount = 6,
    AwaitingIncomeDescription = 7,
    SummaryMenu = 8,
    AwaitingDateRange = 9,
    AwaitingCreditCharge = 10,
    AwaitingDebitCharge = 11,
    AwaitingExpenseDescription = 12,
    RemoveMenu = 13,
    AwaitingDeleteDateRange = 14,
    AwaitingDeleteChoice = 15,
    AwaitingSummaryOutput = 16,
    AwaitingChargeMonth = 17,
    AwaitingIncomeMonth = 18,
    Paused = 19,
    AwaitingBulkFix = 20,
    AwaitingBulkEntry = 21,
    ExampleMenu = 22,
    AiMode = 23,
    AiAwaitingConfirm = 24,
    AiAwaitingDeleteChoice = 25,
    AiAwaitingDeleteConfirm = 26,
    AdminMenu = 27,
    AdminAwaitingTenantName = 28,
    AdminAwaitingGroupJid = 29,
    AdminAwaitingLinkGroup = 30,
    AdminAwaitingLinkTenant = 31,
    AwaitingInstallmentAmountKind = 32
}
