using System.Collections.ObjectModel;
using BudgetForecaster.Core;
using BudgetForecaster.Core.Abstractions;
using BudgetForecaster.Core.Models;
using BudgetForecaster.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetForecaster;

public record CategoryRow(int Id, string Name, string TypeLabel, bool IsArchived)
{
    public bool IsActive => !IsArchived;
    public string Subtitle => IsArchived ? $"{TypeLabel} (archived)" : TypeLabel;
}

public partial class CategoriesViewModel : ObservableObject
{
    private readonly SqliteDatabase _db;
    private readonly ICategoryRepository _categories;

    public CategoriesViewModel(SqliteDatabase db, ICategoryRepository categories)
    {
        _db = db;
        _categories = categories;
    }

    public ObservableCollection<CategoryRow> Rows { get; } = new();
    public string[] TypeChoices { get; } = { "Expense", "Income" };

    [ObservableProperty] private string _nameText = "";
    [ObservableProperty] private string _selectedTypeName = "Expense";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _infoMessage = "";
    [ObservableProperty] private bool _showArchived;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenaming))]
    [NotifyPropertyChangedFor(nameof(IsNotRenaming))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private int? _renamingId;

    public bool IsRenaming => RenamingId.HasValue;
    public bool IsNotRenaming => !IsRenaming;
    public string SaveButtonText => IsRenaming ? "Save name" : "Add category";

    public async Task LoadAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db);
        await RefreshAsync();
    }

    partial void OnShowArchivedChanged(bool value)
    {
        _ = RefreshSafeAsync();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ClearMessages();

        try
        {
            if (RenamingId is int id)
            {
                await _categories.RenameAsync(id, NameText);
                InfoMessage = "Category renamed.";
            }
            else
            {
                var type = SelectedTypeName == "Income" ? CategoryType.Income : CategoryType.Expense;
                await _categories.AddAsync(new Category { Name = NameText, Type = type });
                InfoMessage = "Category added.";
            }
        }
        catch (CategoryValidationException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        ResetForm();
        await RefreshAsync();
    }

    [RelayCommand]
    private void StartRename(int id)
    {
        ClearMessages();

        var row = Rows.FirstOrDefault(r => r.Id == id);
        if (row is null) return;

        RenamingId = row.Id;
        NameText = row.Name;
        SelectedTypeName = row.TypeLabel;
    }

    [RelayCommand]
    private void CancelRename()
    {
        ClearMessages();
        ResetForm();
    }

    [RelayCommand]
    private async Task ArchiveAsync(int id)
    {
        ClearMessages();
        await _categories.ArchiveAsync(id);
        if (RenamingId == id) ResetForm();
        InfoMessage = "Archived. Past transactions and budgets are kept.";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RestoreAsync(int id)
    {
        ClearMessages();
        await _categories.RestoreAsync(id);
        InfoMessage = "Category restored.";
        await RefreshAsync();
    }

    private void ResetForm()
    {
        RenamingId = null;
        NameText = "";
        SelectedTypeName = "Expense";
    }

    private void ClearMessages()
    {
        ErrorMessage = "";
        InfoMessage = "";
    }

    private async Task RefreshSafeAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task RefreshAsync()
    {
        var all = await _categories.GetAllAsync(includeArchived: ShowArchived);

        Rows.Clear();
        foreach (var c in all)
        {
            Rows.Add(new CategoryRow(
                c.Id,
                c.Name,
                c.Type == CategoryType.Income ? "Income" : "Expense",
                c.IsArchived));
        }
    }
}
