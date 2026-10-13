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
    AwaitingDeleteChoice = 15
}
