using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using RenOff.Core;
using RenOff.Data;

namespace RenOff.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly LocalSqliteStore _store;
    private readonly Dictionary<Guid, DispatcherTimer> _saveTimers = new();
    private bool _isInitializingSettings;
    private string _quickAddText = "";
    private RenOffItemViewModel? _selectedItem;
    private int _pendingCount;
    private DateTimeOffset _lastListViewedAtUtc = DateTimeOffset.UtcNow;
    private bool _reminderEnabled;
    private DateTime? _reminderDate;
    private string _reminderTimeText = "09:00";
    private Guid? _selectedReminderId;
    private DateTimeOffset? _selectedReminderCreatedAtUtc;
    private string _statusMessage = "";
    private string _currentLocalTimeText = "";
    private readonly DispatcherTimer _clockTimer;
    private string _uiLanguage = "it";
    private string _uiTheme = "light";
    private string _uiStyle = "modern";
    private string _appLockTimeout = "never";
    private bool _closeToTrayEnabled = true;
    private bool _nudgeEnabled = true;
    private int _nudgeIntervalHours = 4;
    private string _headerStatusMessage = "";
    private DispatcherTimer? _headerStatusTimer;
    private bool _bulkSelectAll;

    public MainViewModel()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RenOff",
            "renoff.db");
        _store = new LocalSqliteStore(dbPath);

        _isInitializingSettings = true;
        UiLanguage = _store.GetSetting("ui.language") ?? "it";
        UiTheme = _store.GetSetting("ui.theme") ?? "light";
        UiStyle = _store.GetSetting("ui.style") ?? "modern";
        CloseToTrayEnabled = (_store.GetSetting("ui.closeToTray") ?? "1") == "1";
        NudgeEnabled = (_store.GetSetting("nudge.enabled") ?? "1") == "1";
        if (int.TryParse(_store.GetSetting("nudge.intervalHours") ?? "4", out var hours) && hours > 0)
        {
            NudgeIntervalHours = hours;
        }
        AppLockTimeout = NormalizeAppLockTimeout(_store.GetSetting("applock.timeout") ?? AppLockService.GetTimeoutSetting());
        _isInitializingSettings = false;

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _clockTimer.Tick += (_, _) => CurrentLocalTimeText = DateTime.Now.ToString("HH:mm:ss");
        CurrentLocalTimeText = DateTime.Now.ToString("HH:mm:ss");
        _clockTimer.Start();

        AddCommand = new RelayCommand(Add, () => !string.IsNullOrWhiteSpace(QuickAddText));
        DeleteCommand = new RelayCommand(DeleteSelected, () => SelectedItem is not null);
        DeleteAllCommand = new RelayCommand(DeleteAll, () => Items.Count > 0);
        SaveReminderCommand = new RelayCommand(SaveReminder, CanSaveReminder);
        ClearReminderCommand = new RelayCommand(ClearReminder, () => SelectedItem is not null && SelectedReminderId is not null);
        SnoozeSelectedReminderCommand = new RelayCommand(() => SnoozeSelectedReminder(TimeSpan.FromMinutes(10)), () => SelectedItem is not null && SelectedReminderId is not null);
        DismissSelectedReminderCommand = new RelayCommand(DismissSelectedReminder, () => SelectedItem is not null && SelectedReminderId is not null);
        CompleteSelectedCommand = new RelayCommand(CompleteSelected, CanCompleteSelected);
        DeleteSelectedCommand = new RelayCommand(DeleteSelectedBulk, CanDeleteSelected);
        ExportCommand = new RelayCommand(ExportBackup, () => Items.Count > 0);
        ImportCommand = new RelayCommand(ImportBackup);
        ChangeLockPasswordCommand = new RelayCommand(() =>
        {
            if (PromptSetLockPassword())
            {
                ShowHeaderStatus("AppLockPasswordSet");
            }
        });

        Items.CollectionChanged += (_, _) =>
        {
            RecalculatePendingCount();
            ((RelayCommand)DeleteAllCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ExportCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CompleteSelectedCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        };

        var loaded = _store.GetAll();
        foreach (var item in loaded)
        {
            AddToCollection(item, save: false, insertOnTop: false);
        }

        if (Items.Count == 0)
        {
            AddToCollection(new RenOffItem
            {
                Type = RenOffItemType.Todo,
                Title = "Prova: aggiungi un to-do",
                Body = "Scrivi nella barra in alto e premi Invio.",
            }, save: true, insertOnTop: false);
            AddToCollection(new RenOffItem
            {
                Type = RenOffItemType.Note,
                Title = "Prova: nota rapida",
                Body = "Seleziona un elemento e modifica titolo/testo a destra.",
            }, save: true, insertOnTop: false);
        }

        SelectedItem = Items.Count == 0 ? null : Items[0];
        LoadReminderForSelection();
        RecalculatePendingCount();
    }

    public ObservableCollection<RenOffItemViewModel> Items { get; } = new();

    public int PendingCount
    {
        get => _pendingCount;
        private set
        {
            if (value == _pendingCount) return;
            _pendingCount = value;
            OnPropertyChanged();
        }
    }

    public DateTimeOffset LastListViewedAtUtc
    {
        get => _lastListViewedAtUtc;
        private set
        {
            if (value == _lastListViewedAtUtc) return;
            _lastListViewedAtUtc = value;
            OnPropertyChanged();
        }
    }

    public string QuickAddText
    {
        get => _quickAddText;
        set
        {
            if (value == _quickAddText) return;
            _quickAddText = value;
            OnPropertyChanged();
            ((RelayCommand)AddCommand).RaiseCanExecuteChanged();
        }
    }

    public RenOffItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(value, _selectedItem)) return;
            if (_selectedItem is not null)
            {
                FlushItemSave(_selectedItem.Id);
            }
            _selectedItem = value;
            OnPropertyChanged();
            ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SaveReminderCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ClearReminderCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SnoozeSelectedReminderCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DismissSelectedReminderCommand).RaiseCanExecuteChanged();
            LoadReminderForSelection();
        }
    }

    public ICommand AddCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DeleteAllCommand { get; }
    public ICommand SaveReminderCommand { get; }
    public ICommand ClearReminderCommand { get; }
    public ICommand SnoozeSelectedReminderCommand { get; }
    public ICommand DismissSelectedReminderCommand { get; }
    public ICommand CompleteSelectedCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ChangeLockPasswordCommand { get; }

    public string UiLanguage
    {
        get => _uiLanguage;
        set
        {
            value ??= "it";
            if (value == _uiLanguage) return;
            _uiLanguage = value;
            OnPropertyChanged();

            if (_isInitializingSettings) return;
            _store.SetSetting("ui.language", value);
            App.ApplyLanguage(value);
        }
    }

    public string UiTheme
    {
        get => _uiTheme;
        set
        {
            value ??= "light";
            if (value == _uiTheme) return;
            _uiTheme = value;
            OnPropertyChanged();

            if (_isInitializingSettings) return;
            _store.SetSetting("ui.theme", value);
            App.ApplyTheme(value);
        }
    }

    public string UiStyle
    {
        get => _uiStyle;
        set
        {
            value ??= "modern";
            if (value == _uiStyle) return;
            _uiStyle = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsThemeSelectionEnabled));

            if (_isInitializingSettings) return;
            _store.SetSetting("ui.style", value);
            App.ApplyStyle(value);
            App.ApplyTheme(UiTheme);
            App.RecreateMainWindow();
        }
    }

    public bool IsThemeSelectionEnabled => _uiStyle.Equals("modern", StringComparison.OrdinalIgnoreCase);

    public string AppLockTimeout
    {
        get => _appLockTimeout;
        set
        {
            value = NormalizeAppLockTimeout(value);
            if (value == _appLockTimeout) return;
            var previousValue = _appLockTimeout;

            if (!_isInitializingSettings && value != "never" && !AppLockService.HasPasswordConfigured())
            {
                if (!PromptSetLockPassword())
                {
                    _appLockTimeout = previousValue;
                    OnPropertyChanged();
                    return;
                }
            }

            _appLockTimeout = value;
            if (_isInitializingSettings)
            {
                OnPropertyChanged();
                return;
            }

            AppLockService.SetTimeoutSetting(value);
            _store.SetSetting("applock.timeout", value);
            _appLockTimeout = NormalizeAppLockTimeout(_store.GetSetting("applock.timeout") ?? value);
            OnPropertyChanged();
        }
    }

    public void RefreshAppLockSettings()
    {
        _isInitializingSettings = true;
        AppLockTimeout = NormalizeAppLockTimeout(_store.GetSetting("applock.timeout") ?? AppLockService.GetTimeoutSetting());
        _isInitializingSettings = false;
    }

    private bool PromptSetLockPassword()
    {
        var dialog = new PassphraseDialog(
            GetResourceString("AppLockSetPasswordPrompt", "Imposta una password per il blocco app."),
            requireConfirmation: true,
            showPlainOption: false,
            confirmButtonText: GetResourceString("AppLockSetPasswordButton", "Imposta"),
            titleText: GetResourceString("ChangeLockPassword", "Imposta/cambia password blocco"))
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };

        if (dialog.ShowDialog() != true || dialog.Outcome != PassphraseDialogResult.Encrypted) return false;

        var recoveryCode = AppLockService.SetPasswordAndGenerateRecoveryCode(dialog.Passphrase);

        var recoveryDialog = new RecoveryCodeDialog(recoveryCode)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        recoveryDialog.ShowDialog();

        return true;
    }

    private static string NormalizeAppLockTimeout(string? value)
    {
        return (value ?? "never").Trim().ToLowerInvariant() switch
        {
            "30m" => "30m",
            "1h" => "1h",
            _ => "never",
        };
    }

    public bool CloseToTrayEnabled
    {
        get => _closeToTrayEnabled;
        set
        {
            if (value == _closeToTrayEnabled) return;
            _closeToTrayEnabled = value;
            OnPropertyChanged();

            if (_isInitializingSettings) return;
            _store.SetSetting("ui.closeToTray", value ? "1" : "0");
        }
    }

    public bool NudgeEnabled
    {
        get => _nudgeEnabled;
        set
        {
            if (value == _nudgeEnabled) return;
            _nudgeEnabled = value;
            OnPropertyChanged();

            if (_isInitializingSettings) return;
            _store.SetSetting("nudge.enabled", value ? "1" : "0");
        }
    }

    public int NudgeIntervalHours
    {
        get => _nudgeIntervalHours;
        set
        {
            if (value == _nudgeIntervalHours) return;
            _nudgeIntervalHours = value;
            OnPropertyChanged();

            if (_isInitializingSettings) return;
            _store.SetSetting("nudge.intervalHours", value.ToString());
        }
    }

    public bool BulkSelectAll
    {
        get => _bulkSelectAll;
        set
        {
            if (value == _bulkSelectAll) return;
            _bulkSelectAll = value;
            OnPropertyChanged();

            foreach (var item in Items)
            {
                item.IsBulkSelected = value;
            }

            ((RelayCommand)CompleteSelectedCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            value ??= "";
            if (value == _statusMessage) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string CurrentLocalTimeText
    {
        get => _currentLocalTimeText;
        private set
        {
            value ??= "";
            if (value == _currentLocalTimeText) return;
            _currentLocalTimeText = value;
            OnPropertyChanged();
        }
    }

    public string HeaderStatusMessage
    {
        get => _headerStatusMessage;
        private set
        {
            value ??= "";
            if (value == _headerStatusMessage) return;
            _headerStatusMessage = value;
            OnPropertyChanged();
        }
    }

    private void Add()
    {
        var title = QuickAddText.Trim();
        if (title.Length == 0)
        {
            ShowHeaderStatus("NothingToAdd");
            return;
        }

        var vm = AddToCollection(new RenOffItem { Type = RenOffItemType.Todo, Title = title, Body = "" }, save: true, insertOnTop: true);
        SelectedItem = vm;
        QuickAddText = "";
        ShowHeaderStatus("AddedItem");
    }

    private void ShowHeaderStatus(string resourceKey)
    {
        var text = System.Windows.Application.Current?.TryFindResource(resourceKey) as string;
        if (string.IsNullOrWhiteSpace(text)) return;

        HeaderStatusMessage = text;
        _headerStatusTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1.5),
        };
        _headerStatusTimer.Stop();
        _headerStatusTimer.Tick -= OnHeaderStatusTimerTick;
        _headerStatusTimer.Tick += OnHeaderStatusTimerTick;
        _headerStatusTimer.Start();
    }

    private void OnHeaderStatusTimerTick(object? sender, EventArgs e)
    {
        _headerStatusTimer?.Stop();
        HeaderStatusMessage = "";
    }

    private void DeleteSelected()
    {
        var selected = SelectedItem;
        if (selected is null) return;

        var index = Items.IndexOf(selected);
        if (index < 0) return;

        Items.RemoveAt(index);
        FlushItemSave(selected.Id);
        if (_saveTimers.Remove(selected.Id, out var timer))
        {
            timer.Stop();
        }
        _store.Delete(selected.Id);
        NormalizeSortOrdersAndPersist();
        SelectedItem = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
    }

    private void DeleteAll()
    {
        var title = System.Windows.Application.Current?.TryFindResource("ConfirmDeleteAllTitle") as string ?? "RenOff";
        var text = System.Windows.Application.Current?.TryFindResource("ConfirmDeleteAllText") as string ?? "Eliminare tutto?";
        var result = System.Windows.MessageBox.Show(text, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        foreach (var item in Items.ToList())
        {
            FlushItemSave(item.Id);
            if (_saveTimers.Remove(item.Id, out var timer))
            {
                timer.Stop();
            }
            _store.Delete(item.Id);
        }

        Items.Clear();
        SelectedItem = null;
        BulkSelectAll = false;
        ShowHeaderStatus("DeletedAll");
    }

    private bool CanCompleteSelected()
        => Items.Any(i => i.IsBulkSelected && i.IsTodo && !i.IsDone);

    private bool CanDeleteSelected()
        => Items.Any(i => i.IsBulkSelected);

    private void CompleteSelected()
    {
        var changed = false;
        foreach (var item in Items)
        {
            if (!item.IsBulkSelected) continue;
            if (!item.IsTodo) continue;
            if (item.IsDone) continue;
            item.IsDone = true;
            changed = true;
        }

        if (changed)
        {
            ShowHeaderStatus("CompletedSelected");
        }
    }

    private void DeleteSelectedBulk()
    {
        var toDelete = Items.Where(i => i.IsBulkSelected).ToList();
        if (toDelete.Count == 0) return;

        foreach (var item in toDelete)
        {
            var index = Items.IndexOf(item);
            if (index >= 0)
            {
                Items.RemoveAt(index);
            }
            FlushItemSave(item.Id);
            if (_saveTimers.Remove(item.Id, out var timer))
            {
                timer.Stop();
            }
            _store.Delete(item.Id);
        }

        NormalizeSortOrdersAndPersist();
        BulkSelectAll = false;
        if (SelectedItem is not null && !Items.Contains(SelectedItem))
        {
            SelectedItem = Items.Count == 0 ? null : Items[0];
        }
        ShowHeaderStatus("DeletedSelected");
    }

    private RenOffItemViewModel AddToCollection(RenOffItem model, bool save, bool insertOnTop)
    {
        if (save)
        {
            if (insertOnTop)
            {
                model.SortOrder = Items.Count == 0 ? 0 : Items.Min(i => i.SortOrder) - 1;
            }
            else
            {
                model.SortOrder = Items.Count == 0 ? 0 : Items.Max(i => i.SortOrder) + 1;
            }
        }

        var vm = new RenOffItemViewModel(model);
        vm.Changed += OnItemChanged;
        vm.BulkSelectionChanged += OnItemBulkSelectionChanged;
        if (insertOnTop)
        {
            Items.Insert(0, vm);
        }
        else
        {
            Items.Add(vm);
        }

        if (save)
        {
            _store.Upsert(vm.ToModelSnapshot());
            NormalizeSortOrdersAndPersist();
        }

        return vm;
    }

    private void OnItemBulkSelectionChanged(object? sender, EventArgs e)
    {
        ((RelayCommand)CompleteSelectedCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
    }

    private void NormalizeSortOrdersAndPersist()
    {
        var updates = new List<(Guid ItemId, int SortOrder)>(Items.Count);
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            if (item.SortOrder == i) continue;
            item.SortOrder = i;
            updates.Add((item.Id, i));
        }

        _store.UpdateSortOrders(updates);
    }

    public void MoveItem(RenOffItemViewModel item, int newIndex)
    {
        var oldIndex = Items.IndexOf(item);
        if (oldIndex < 0) return;
        if (newIndex < 0) newIndex = 0;
        if (newIndex >= Items.Count) newIndex = Items.Count - 1;
        if (oldIndex == newIndex) return;

        Items.Move(oldIndex, newIndex);
        NormalizeSortOrdersAndPersist();
    }

    private sealed class BackupPayload
    {
        public int Version { get; init; } = 1;
        public DateTimeOffset ExportedAtUtc { get; init; } = DateTimeOffset.UtcNow;
        public List<RenOffItem> Items { get; init; } = new();
        public List<Reminder> Reminders { get; init; } = new();
    }

    private void ExportBackup()
    {
        var passphraseDialog = new PassphraseDialog(
            GetResourceString("ExportEncryptPrompt", "Proteggi il backup con una password (opzionale)."),
            requireConfirmation: true,
            showPlainOption: true,
            confirmButtonText: GetResourceString("ExportEncryptedButton", "Esporta cifrato"),
            titleText: GetResourceString("ExportTitle", "Esporta backup"))
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };

        if (passphraseDialog.ShowDialog() != true) return;
        if (passphraseDialog.Outcome == PassphraseDialogResult.Cancelled) return;

        var encrypt = passphraseDialog.Outcome == PassphraseDialogResult.Encrypted;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = GetResourceString("ExportTitle", "Esporta"),
            Filter = encrypt
                ? "RenOff backup cifrato (*.renoff.enc)|*.renoff.enc"
                : "RenOff backup (*.renoff.json)|*.renoff.json|JSON (*.json)|*.json",
            DefaultExt = encrypt ? ".renoff.enc" : ".renoff.json",
            FileName = $"renoff-backup-{DateTime.Now:yyyyMMdd}.{(encrypt ? "renoff.enc" : "renoff.json")}",
        };

        if (dialog.ShowDialog() != true) return;

        var reminders = new List<Reminder>();
        foreach (var item in Items)
        {
            var reminder = _store.GetReminderForItem(item.Id);
            if (reminder is not null)
            {
                reminders.Add(reminder);
            }
        }

        var payload = new BackupPayload
        {
            Items = Items.Select(i => i.ToModelSnapshot()).ToList(),
            Reminders = reminders,
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        if (encrypt)
        {
            var encrypted = BackupEncryption.Encrypt(json, passphraseDialog.Passphrase);
            File.WriteAllBytes(dialog.FileName, encrypted);
        }
        else
        {
            File.WriteAllText(dialog.FileName, json);
        }

        ShowHeaderStatus("BackupExported");
        App.ShowTrayBalloon("RenOff", GetResourceString("BackupExported", "Esportato"));
    }

    private void ImportBackup()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = GetResourceString("ImportTitle", "Importa"),
            Filter = "RenOff backup (*.renoff.json;*.renoff.enc;*.json)|*.renoff.json;*.renoff.enc;*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true) return;

        var fileBytes = File.ReadAllBytes(dialog.FileName);
        string? json;

        if (BackupEncryption.IsEncrypted(fileBytes))
        {
            json = DecryptBackupWithPrompt(fileBytes);
            if (json is null) return;
        }
        else
        {
            json = Encoding.UTF8.GetString(fileBytes);
        }

        var payload = JsonSerializer.Deserialize<BackupPayload>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (payload is null || payload.Items.Count == 0)
        {
            System.Windows.MessageBox.Show(
                GetResourceString("ImportInvalid", "Backup non valido."),
                "RenOff",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var promptTitle = GetResourceString("ImportModeTitle", "Importazione");
        var promptText = GetResourceString("ImportModeText", "Vuoi sostituire tutto (Sì) o unire ai dati esistenti (No)?");
        var mode = System.Windows.MessageBox.Show(promptText, promptTitle, System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Question);
        if (mode == System.Windows.MessageBoxResult.Cancel) return;

        if (mode == System.Windows.MessageBoxResult.Yes)
        {
            foreach (var item in Items.ToList())
            {
                FlushItemSave(item.Id);
                if (_saveTimers.Remove(item.Id, out var timer))
                {
                    timer.Stop();
                }
                _store.Delete(item.Id);
            }

            Items.Clear();
            SelectedItem = null;
            BulkSelectAll = false;
        }

        foreach (var item in payload.Items)
        {
            _store.Upsert(item);
        }

        foreach (var reminder in payload.Reminders)
        {
            _store.UpsertReminder(reminder);
        }

        ReloadFromStore();
        ShowHeaderStatus("BackupImported");
        App.ShowTrayBalloon("RenOff", GetResourceString("BackupImported", "Importato"));
    }

    private string? DecryptBackupWithPrompt(byte[] fileBytes)
    {
        while (true)
        {
            var dialog = new PassphraseDialog(
                GetResourceString("ImportDecryptPrompt", "Questo backup è protetto da password. Inseriscila per continuare."),
                requireConfirmation: false,
                showPlainOption: false,
                confirmButtonText: GetResourceString("UnlockButton", "Sblocca"),
                titleText: GetResourceString("ImportTitle", "Importa backup"))
            {
                Owner = System.Windows.Application.Current?.MainWindow,
            };

            if (dialog.ShowDialog() != true || dialog.Outcome == PassphraseDialogResult.Cancelled) return null;

            try
            {
                return BackupEncryption.Decrypt(fileBytes, dialog.Passphrase);
            }
            catch (InvalidDataException)
            {
                System.Windows.MessageBox.Show(
                    GetResourceString("WrongPassphrase", "Password errata o file corrotto."),
                    "RenOff",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
    }

    private static string GetResourceString(string key, string fallback)
        => System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;

    private void ReloadFromStore()
    {
        foreach (var item in Items.ToList())
        {
            FlushItemSave(item.Id);
            if (_saveTimers.Remove(item.Id, out var timer))
            {
                timer.Stop();
            }
        }

        Items.Clear();

        var loaded = _store.GetAll();
        foreach (var item in loaded)
        {
            AddToCollection(item, save: false, insertOnTop: false);
        }

        BulkSelectAll = false;
        SelectedItem = Items.Count == 0 ? null : Items[0];
        LoadReminderForSelection();
        RecalculatePendingCount();
    }

    private void OnItemChanged(object? sender, EventArgs e)
    {
        if (sender is not RenOffItemViewModel vm) return;
        ScheduleItemSave(vm);
        RecalculatePendingCount();
    }

    private void ScheduleItemSave(RenOffItemViewModel vm)
    {
        if (!_saveTimers.TryGetValue(vm.Id, out var timer))
        {
            timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(350),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _store.Upsert(vm.ToModelSnapshot());
            };
            _saveTimers[vm.Id] = timer;
        }

        timer.Stop();
        timer.Start();
    }

    private void FlushItemSave(Guid itemId)
    {
        if (_saveTimers.TryGetValue(itemId, out var timer) && timer.IsEnabled)
        {
            timer.Stop();
            var vm = FindItem(itemId);
            if (vm is not null)
            {
                _store.Upsert(vm.ToModelSnapshot());
            }
        }
    }

    private RenOffItemViewModel? FindItem(Guid itemId)
    {
        foreach (var item in Items)
        {
            if (item.Id == itemId) return item;
        }
        return null;
    }

    public void MarkListViewed()
    {
        LastListViewedAtUtc = DateTimeOffset.UtcNow;
    }

    public bool ReminderEnabled
    {
        get => _reminderEnabled;
        set
        {
            if (value == _reminderEnabled) return;
            _reminderEnabled = value;
            OnPropertyChanged();
            ((RelayCommand)SaveReminderCommand).RaiseCanExecuteChanged();
        }
    }

    public DateTime? ReminderDate
    {
        get => _reminderDate;
        set
        {
            if (value == _reminderDate) return;
            _reminderDate = value;
            OnPropertyChanged();
            ((RelayCommand)SaveReminderCommand).RaiseCanExecuteChanged();
        }
    }

    public string ReminderTimeText
    {
        get => _reminderTimeText;
        set
        {
            value ??= "";
            if (value == _reminderTimeText) return;
            _reminderTimeText = value;
            OnPropertyChanged();
            ((RelayCommand)SaveReminderCommand).RaiseCanExecuteChanged();
        }
    }

    public Guid? SelectedReminderId
    {
        get => _selectedReminderId;
        private set
        {
            if (value == _selectedReminderId) return;
            _selectedReminderId = value;
            OnPropertyChanged();
            ((RelayCommand)ClearReminderCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SnoozeSelectedReminderCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DismissSelectedReminderCommand).RaiseCanExecuteChanged();
        }
    }

    public IReadOnlyList<ReminderNotification> GetDueReminders(DateTimeOffset nowUtc, int limit = 1)
        => _store.GetDueReminders(nowUtc, limit);

    public void MarkReminderFired(Guid reminderId)
        => _store.MarkReminderFired(reminderId);

    public void SnoozeReminder(Guid reminderId, TimeSpan duration)
        => _store.SnoozeReminder(reminderId, DateTimeOffset.UtcNow.Add(duration));

    public void DismissReminder(Guid reminderId)
        => _store.DismissReminder(reminderId);

    private bool CanSaveReminder()
    {
        if (SelectedItem is null) return false;
        if (!ReminderEnabled) return false;
        if (ReminderDate is null) return false;
        return TryGetReminderScheduledAtUtc(out _);
    }

    private void SaveReminder()
    {
        if (SelectedItem is null) return;
        if (!TryGetReminderScheduledAtUtc(out var scheduledAtUtc)) return;

        var now = DateTimeOffset.UtcNow;
        var id = SelectedReminderId ?? Guid.NewGuid();
        var createdAt = _selectedReminderCreatedAtUtc ?? now;

        _store.UpsertReminder(new Reminder
        {
            Id = id,
            ItemId = SelectedItem.Id,
            ScheduledAtUtc = scheduledAtUtc,
            SnoozedUntilUtc = null,
            Status = ReminderStatus.Scheduled,
            LastFiredAtUtc = null,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = now,
        });

        SelectedReminderId = id;
        _selectedReminderCreatedAtUtc = createdAt;
        var local = scheduledAtUtc.ToLocalTime();
        var template = System.Windows.Application.Current?.TryFindResource("ReminderSavedTemplate") as string ?? "Salvato: {0:dd/MM HH:mm}";
        StatusMessage = string.Format(template, local);
        ShowHeaderStatus("ReminderSaved");
        App.ShowTrayBalloon("RenOff", StatusMessage);
    }

    private void ClearReminder()
    {
        if (SelectedReminderId is null) return;
        _store.DismissReminder(SelectedReminderId.Value);
        SelectedReminderId = null;
        _selectedReminderCreatedAtUtc = null;
        ReminderEnabled = false;
        StatusMessage = "Reminder disattivato";
    }

    private void SnoozeSelectedReminder(TimeSpan duration)
    {
        if (SelectedReminderId is null) return;
        SnoozeReminder(SelectedReminderId.Value, duration);
        StatusMessage = $"Snooze: +{(int)duration.TotalMinutes} min";
    }

    private void DismissSelectedReminder()
    {
        if (SelectedReminderId is null) return;
        DismissReminder(SelectedReminderId.Value);
        SelectedReminderId = null;
        _selectedReminderCreatedAtUtc = null;
        ReminderEnabled = false;
        StatusMessage = "Reminder confermato";
    }

    private void LoadReminderForSelection()
    {
        var selected = SelectedItem;
        if (selected is null)
        {
            ReminderEnabled = false;
            ReminderDate = null;
            ReminderTimeText = "09:00";
            SelectedReminderId = null;
            _selectedReminderCreatedAtUtc = null;
            StatusMessage = "";
            return;
        }

        var reminder = _store.GetReminderForItem(selected.Id);
        if (reminder is null)
        {
            ReminderEnabled = false;
            ReminderDate = DateTime.Today;
            ReminderTimeText = DateTime.Now.AddMinutes(30).ToString("HH:mm");
            SelectedReminderId = null;
            _selectedReminderCreatedAtUtc = null;
            StatusMessage = "";
            return;
        }

        SelectedReminderId = reminder.Id;
        _selectedReminderCreatedAtUtc = reminder.CreatedAtUtc;
        var local = reminder.EffectiveAtUtc.ToLocalTime();
        ReminderEnabled = true;
        ReminderDate = local.Date;
        ReminderTimeText = local.ToString("HH:mm");
        StatusMessage = $"Reminder: {local:dd/MM HH:mm}";
    }

    private bool TryGetReminderScheduledAtUtc(out DateTimeOffset scheduledAtUtc)
    {
        scheduledAtUtc = default;
        if (ReminderDate is null) return false;
        if (!TimeSpan.TryParse(ReminderTimeText, out var timeOfDay)) return false;

        var date = ReminderDate.Value.Date;
        var local = new DateTime(date.Year, date.Month, date.Day, timeOfDay.Hours, timeOfDay.Minutes, 0, DateTimeKind.Local);
        scheduledAtUtc = new DateTimeOffset(local).ToUniversalTime();
        return true;
    }

    private void RecalculatePendingCount()
    {
        var count = 0;
        foreach (var item in Items)
        {
            if (item.IsTodo && !item.IsDone) count++;
        }

        PendingCount = count;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class RenOffItemViewModel : INotifyPropertyChanged
{
    private readonly RenOffItem _model;
    private bool _isBulkSelected;

    public RenOffItemViewModel(RenOffItem model)
    {
        _model = model;
    }

    public Guid Id => _model.Id;

    public int SortOrder
    {
        get => _model.SortOrder;
        set
        {
            if (value == _model.SortOrder) return;
            _model.SortOrder = value;
            OnPropertyChanged();
        }
    }

    public bool IsBulkSelected
    {
        get => _isBulkSelected;
        set
        {
            if (value == _isBulkSelected) return;
            _isBulkSelected = value;
            OnPropertyChanged();
            BulkSelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public RenOffItemType Type
    {
        get => _model.Type;
        set
        {
            if (value == _model.Type) return;
            _model.Type = value;
            Touch();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTodo));
        }
    }

    public bool IsTodo
    {
        get => Type == RenOffItemType.Todo;
        set
        {
            Type = value ? RenOffItemType.Todo : RenOffItemType.Note;
            if (!value) IsDone = false;
        }
    }

    public string Title
    {
        get => _model.Title;
        set
        {
            value ??= "";
            if (value == _model.Title) return;
            _model.Title = value;
            Touch();
            OnPropertyChanged();
        }
    }

    public string Body
    {
        get => _model.Body;
        set
        {
            value ??= "";
            if (value == _model.Body) return;
            _model.Body = value;
            Touch();
            OnPropertyChanged();
        }
    }

    public bool IsDone
    {
        get => _model.IsDone;
        set
        {
            if (value == _model.IsDone) return;
            _model.IsDone = value;
            Touch();
            OnPropertyChanged();
        }
    }

    public DateTimeOffset CreatedAt => _model.CreatedAt;
    public DateTimeOffset UpdatedAt => _model.UpdatedAt;

    private void Touch()
    {
        _model.UpdatedAt = DateTimeOffset.UtcNow;
        OnPropertyChanged(nameof(UpdatedAt));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public RenOffItem ToModelSnapshot()
        => new()
        {
            Id = _model.Id,
            SortOrder = _model.SortOrder,
            Type = _model.Type,
            Title = _model.Title,
            Body = _model.Body,
            IsDone = _model.IsDone,
            CreatedAt = _model.CreatedAt,
            UpdatedAt = _model.UpdatedAt,
        };

    public event EventHandler? Changed;
    public event EventHandler? BulkSelectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool> _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute ?? (() => true);
    }

    public bool CanExecute(object? parameter) => _canExecute();
    public void Execute(object? parameter) => _execute();

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
