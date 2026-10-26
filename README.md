# Runway

A cross-platform personal finance app (Windows + Android) built with .NET MAUI and SQLite.

Plan a monthly budget, record what you actually spend, see the variance per category,
and forecast how long your savings will last over 1, 5 and 10 years.

## Features
- Add and delete income and expense transactions
- Monthly budgets per category, with "copy last month"
- Budget statement: budgeted vs actual vs variance, with income and expense sections
- Forecast with saved assumptions, inflation-adjusted values and a runway warning
- Fully offline: your data stays on your device

## Tech
.NET 10, .NET MAUI, C#, SQLite (Microsoft.Data.Sqlite), xUnit

## Status
v0.3: transactions, budgets, statement and forecast are working.
Planned for v1.0: edit transactions, manage categories, charts, CSV export, app icon.

## Project structure
- `BudgetForecaster.Core`: models and business rules (statement, forecast)
- `BudgetForecaster.Data`: SQLite repositories
- `BudgetForecaster.Tests`: unit and integration tests
- `BudgetForecaster`: the MAUI app
