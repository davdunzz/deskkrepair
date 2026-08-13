using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RepairDesk;

public partial class MainWindow : Window
{
    private RepairRecord? _editingRepair;
    private InventoryItem? _editingInventory;
    private readonly ObservableCollection<UsedPart> _usedParts = [];

    public MainWindow()
    {
        InitializeComponent();
        UsedPartsList.ItemsSource = _usedParts;
        ReloadCatalog();
        LoadSettings();
        LoadStorageSettings();
        RefreshArchive();
        RefreshInventory();
        AppointmentsCalendar.SelectedDate = DateTime.Today;
        RefreshCalendar();
        SelectSection(0);
    }

    private void SelectSection(int index)
    {
        Button[] navigationButtons =
        [
            NavNewButton,
            NavInventoryButton,
            NavArchiveButton,
            NavCalendarButton,
            NavCatalogButton,
            NavSettingsButton
        ];

        foreach (var button in navigationButtons)
            button.ClearValue(BackgroundProperty);

        navigationButtons[index].Background = new SolidColorBrush(Color.FromRgb(0x58, 0x56, 0xE8));
        MainTabs.SelectedIndex = index;
    }

    private void NavNewRepair_Click(object sender, RoutedEventArgs e) => SelectSection(0);
    private void NavInventory_Click(object sender, RoutedEventArgs e) => SelectSection(1);
    private void NavArchive_Click(object sender, RoutedEventArgs e) => SelectSection(2);
    private void NavCalendar_Click(object sender, RoutedEventArgs e) => SelectSection(3);
    private void NavCatalog_Click(object sender, RoutedEventArgs e) => SelectSection(4);
    private void NavSettings_Click(object sender, RoutedEventArgs e) => SelectSection(5);

    private void ReloadCatalog()
    {
        var brands = Database.GetBrands();
        var current = BrandBox.Text;
        BrandBox.ItemsSource = brands;
        CatalogBrandBox.ItemsSource = brands;
        BrandBox.Text = current;
    }

    private void BrandBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BrandBox.SelectedItem is string brand) LoadModels(brand);
    }

    private void BrandBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => LoadModels(BrandBox.Text);

    private void LoadModels(string brand)
    {
        var current = ModelBox.Text;
        ModelBox.ItemsSource = Database.GetModels(brand.Trim());
        ModelBox.Text = current;
    }

    private void SaveAndGenerate_Click(object sender, RoutedEventArgs e)
    {
        SaveForm(true);
    }

    private void SaveOnly_Click(object sender, RoutedEventArgs e) => SaveForm(false);

    private void SaveForm(bool generatePdf)
    {
        if (!ValidateForm() || !TryGetAppointment(out var appointment)) return;
        try
        {
            Database.AddModel(BrandBox.Text, ModelBox.Text);
            var repair = BuildRecord(appointment);
            var wasEditing = _editingRepair is not null;
            if (wasEditing) Database.UpdateRepair(repair); else repair.Id = Database.SaveRepair(repair);
            string? path = generatePdf ? PdfService.Generate(repair, Database.LoadSettings()) : null;
            RefreshArchive();
            RefreshInventory();
            RefreshCalendar();
            ReloadCatalog();
            var message = wasEditing ? "Riparazione modificata e salvata." : "Riparazione salvata nell'archivio.";
            if (path is not null) message += $"\n\nPDF creato in:\n{path}";
            MessageBox.Show(message, "Operazione completata", MessageBoxButton.OK, MessageBoxImage.Information);
            if (path is not null) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            ClearForm();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Non è stato possibile salvare la scheda.\n\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ValidateForm()
    {
        if (!string.IsNullOrWhiteSpace(FirstNameBox.Text) && !string.IsNullOrWhiteSpace(LastNameBox.Text) &&
            !string.IsNullOrWhiteSpace(PhoneBox.Text) && !string.IsNullOrWhiteSpace(BrandBox.Text) &&
            !string.IsNullOrWhiteSpace(ModelBox.Text) && !string.IsNullOrWhiteSpace(RepairDescriptionBox.Text)) return true;
        MessageBox.Show("Compila tutti i campi contrassegnati con *.", "Dati mancanti", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private bool TryGetAppointment(out DateTime? appointment)
    {
        appointment = null;
        if (AppointmentDatePicker.SelectedDate is null)
        {
            if (!string.IsNullOrWhiteSpace(AppointmentTimeBox.Text)) { MessageBox.Show("Se inserisci l'ora devi scegliere anche la data."); return false; }
            return true;
        }
        if (!TimeSpan.TryParse(AppointmentTimeBox.Text.Trim(), out var time) || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
        { MessageBox.Show("Inserisci un orario valido, per esempio 14:30.", "Orario non valido", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        appointment = AppointmentDatePicker.SelectedDate.Value.Date.Add(time);
        return true;
    }

    private RepairRecord BuildRecord(DateTime? appointment) => new()
    {
        Id = _editingRepair?.Id ?? 0, PracticeNumber = _editingRepair?.PracticeNumber ?? Database.NextPracticeNumber(), CreatedAt = _editingRepair?.CreatedAt ?? DateTime.Now,
        AppointmentAt = appointment, EmployeeCode = EmployeeCodeBox.Text.Trim(), FirstName = FirstNameBox.Text.Trim(), LastName = LastNameBox.Text.Trim(), Phone = PhoneBox.Text.Trim(), Email = EmailBox.Text.Trim(),
        Brand = BrandBox.Text.Trim(), Model = ModelBox.Text.Trim(), Color = ColorBox.Text.Trim(), Imei = ImeiBox.Text.Trim(), RepairDescription = RepairDescriptionBox.Text.Trim(),
        RepairTypes = Checked(RepairTypesPanel), Accessories = Checked(AccessoriesPanel), DeviceConditions = Checked(ConditionsPanel), ConditionNotes = ConditionNotesBox.Text.Trim(), UsedParts = _usedParts.Select(x => new UsedPart { PartId=x.PartId,Code=x.Code,Name=x.Name,Quantity=x.Quantity }).ToList()
    };

    private static List<string> Checked(Panel panel) => panel.Children.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => x.Content?.ToString() ?? "").Where(x => x.Length > 0).ToList();

    private void ClearForm_Click(object sender, RoutedEventArgs e) => ClearForm();

    private void ClearForm()
    {
        foreach (var box in new[] { FirstNameBox, LastNameBox, PhoneBox, EmailBox, ColorBox, ImeiBox, RepairDescriptionBox, ConditionNotesBox, EmployeeCodeBox, PartCodeBox }) box.Clear();
        BrandBox.Text = ""; ModelBox.Text = "";
        AppointmentDatePicker.SelectedDate = null; AppointmentTimeBox.Clear();
        PartQuantityBox.Text = "1"; _usedParts.Clear();
        foreach (var panel in new[] { RepairTypesPanel, AccessoriesPanel, ConditionsPanel }) foreach (var check in panel.Children.OfType<CheckBox>()) check.IsChecked = false;
        _editingRepair = null; SaveOnlyButton.Content = "SALVA NELL'ARCHIVIO";
        FirstNameBox.Focus();
    }

    private void ClearAppointment_Click(object sender, RoutedEventArgs e) { AppointmentDatePicker.SelectedDate = null; AppointmentTimeBox.Clear(); }

    private void RefreshArchive() => ArchiveGrid.ItemsSource = Database.SearchRepairs(SearchBox?.Text ?? "");
    private void RefreshArchive_Click(object sender, RoutedEventArgs e) => RefreshArchive();
    private void Search_Click(object sender, RoutedEventArgs e) => RefreshArchive();
    private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) RefreshArchive(); }

    private void RegeneratePdf_Click(object sender, RoutedEventArgs e)
    {
        if (ArchiveGrid.SelectedItem is not RepairRecord repair) { MessageBox.Show("Seleziona prima una riparazione dall'archivio."); return; }
        try
        {
            var path = PdfService.Generate(repair, Database.LoadSettings());
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Errore PDF", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void GenerateAppointmentList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var repairs = Database.GetAllRepairsForList();
            if (repairs.Count == 0) { MessageBox.Show("Nell'archivio non ci sono riparazioni."); return; }
            var path = PdfService.GenerateRepairList(repairs, Database.LoadSettings());
            MessageBox.Show($"Lista di {repairs.Count} riparazioni creata in:\n{path}", "PDF creato", MessageBoxButton.OK, MessageBoxImage.Information);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Errore PDF", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void EditRepair_Click(object sender, RoutedEventArgs e)
    {
        if (ArchiveGrid.SelectedItem is not RepairRecord repair) { MessageBox.Show("Seleziona prima una riparazione."); return; }
        LoadRepairIntoForm(repair);
    }

    private void LoadRepairIntoForm(RepairRecord repair)
    {
        _editingRepair = repair;
        FirstNameBox.Text = repair.FirstName; LastNameBox.Text = repair.LastName; PhoneBox.Text = repair.Phone; EmailBox.Text = repair.Email;
        BrandBox.Text = repair.Brand; LoadModels(repair.Brand); ModelBox.Text = repair.Model; ColorBox.Text = repair.Color; ImeiBox.Text = repair.Imei;
        RepairDescriptionBox.Text = repair.RepairDescription; ConditionNotesBox.Text = repair.ConditionNotes;
        SetChecks(RepairTypesPanel, repair.RepairTypes); SetChecks(AccessoriesPanel, repair.Accessories); SetChecks(ConditionsPanel, repair.DeviceConditions);
        AppointmentDatePicker.SelectedDate = repair.AppointmentAt?.Date; AppointmentTimeBox.Text = repair.AppointmentAt?.ToString("HH:mm") ?? "";
        EmployeeCodeBox.Text = repair.EmployeeCode; _usedParts.Clear(); foreach (var part in repair.UsedParts) _usedParts.Add(new UsedPart { PartId=part.PartId,Code=part.Code,Name=part.Name,Quantity=part.Quantity });
        SaveOnlyButton.Content = "SALVA MODIFICHE"; MainTabs.SelectedIndex = 0; FirstNameBox.Focus();
    }

    private static void SetChecks(Panel panel, List<string> values)
    { foreach (var check in panel.Children.OfType<CheckBox>()) check.IsChecked = values.Contains(check.Content?.ToString() ?? ""); }

    private void DeleteRepair_Click(object sender, RoutedEventArgs e)
    {
        if (ArchiveGrid.SelectedItem is not RepairRecord repair) { MessageBox.Show("Seleziona prima una riparazione."); return; }
        if (MessageBox.Show($"Eliminare definitivamente la pratica {repair.PracticeNumber} di {repair.DisplayName}?", "Conferma eliminazione", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Database.DeleteRepair(repair.Id); RefreshArchive(); RefreshCalendar(); RefreshInventory();
    }

    private void AppointmentsCalendar_SelectedDatesChanged(object? sender, SelectionChangedEventArgs e) => RefreshCalendar();

    private void RefreshCalendar()
    {
        if (AppointmentsCalendar is null || AppointmentsList is null) return;
        var day = AppointmentsCalendar.SelectedDate ?? DateTime.Today;
        SelectedDayTitle.Text = day.ToString("dddd d MMMM yyyy", new System.Globalization.CultureInfo("it-IT"));
        AppointmentsList.ItemsSource = Database.GetAppointments(day);
    }

    private void AppointmentsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AppointmentsList.SelectedItem is not RepairRecord repair) return;
        var dialog = new AppointmentDialog(repair.AppointmentAt ?? DateTime.Now) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Database.UpdateAppointment(repair.Id, dialog.Appointment); RefreshArchive(); RefreshCalendar();
    }

    private void AddUsedPart_Click(object sender, RoutedEventArgs e)
    {
        var part = Database.FindPartByCode(PartCodeBox.Text);
        if (part is null) { MessageBox.Show("Codice ricambio non trovato nel magazzino."); return; }
        if (!int.TryParse(PartQuantityBox.Text, out var qty) || qty <= 0) { MessageBox.Show("Inserisci una quantità valida."); return; }
        var already = _usedParts.FirstOrDefault(x => x.PartId == part.Id); var total = qty + (already?.Quantity ?? 0);
        var available = part.Quantity + (_editingRepair?.UsedParts.FirstOrDefault(x => x.PartId == part.Id)?.Quantity ?? 0);
        if (total > available) { MessageBox.Show($"Giacenza insufficiente. Disponibilità massima: {available}."); return; }
        if (already is not null) { var index=_usedParts.IndexOf(already); _usedParts[index]=new UsedPart{PartId=part.Id,Code=part.Code,Name=part.Name,Quantity=total}; }
        else _usedParts.Add(new UsedPart { PartId=part.Id, Code=part.Code, Name=part.Name, Quantity=qty });
        PartCodeBox.Clear(); PartQuantityBox.Text="1"; PartCodeBox.Focus();
    }

    private void RemoveUsedPart_Click(object sender, RoutedEventArgs e)
    { if (UsedPartsList.SelectedItem is UsedPart part) _usedParts.Remove(part); else MessageBox.Show("Seleziona un ricambio dalla lista."); }

    private void RefreshInventory() => InventoryGrid.ItemsSource = Database.GetInventory(InventorySearchBox?.Text ?? "");
    private void SearchInventory_Click(object sender, RoutedEventArgs e) => RefreshInventory();
    private void InventorySearchBox_KeyDown(object sender, KeyEventArgs e) { if(e.Key==Key.Enter) RefreshInventory(); }
    private void InventoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LoadInventoryForEdit();
    private void EditInventory_Click(object sender, RoutedEventArgs e) => LoadInventoryForEdit();
    private void LoadInventoryForEdit()
    {
        if(InventoryGrid.SelectedItem is not InventoryItem item){MessageBox.Show("Seleziona un ricambio.");return;}
        _editingInventory=item; InventoryCodeBox.Text=item.Code; InventoryNameBox.Text=item.Name; InventoryCategoryBox.Text=item.Category; InventoryQuantityBox.Text=item.Quantity.ToString(); SaveInventoryButton.Content="SALVA MODIFICHE";
    }
    private void ClearInventory_Click(object sender, RoutedEventArgs e) => ClearInventoryForm();
    private void ClearInventoryForm(){_editingInventory=null;InventoryCodeBox.Clear();InventoryNameBox.Clear();InventoryCategoryBox.Text="Altro";InventoryQuantityBox.Text="0";SaveInventoryButton.Content="SALVA RICAMBIO";}
    private void SaveInventory_Click(object sender, RoutedEventArgs e)
    {
        if(string.IsNullOrWhiteSpace(InventoryCodeBox.Text)||string.IsNullOrWhiteSpace(InventoryNameBox.Text)||!int.TryParse(InventoryQuantityBox.Text,out var qty)||qty<0){MessageBox.Show("Inserisci codice, nome e una quantità valida.");return;}
        try{Database.SaveInventoryItem(new InventoryItem{Id=_editingInventory?.Id??0,Code=InventoryCodeBox.Text,Name=InventoryNameBox.Text,Category=string.IsNullOrWhiteSpace(InventoryCategoryBox.Text)?"Altro":InventoryCategoryBox.Text,Quantity=qty});ClearInventoryForm();RefreshInventory();}
        catch(Exception ex){MessageBox.Show($"Impossibile salvare il ricambio. Il codice deve essere univoco.\n\n{ex.Message}");}
    }
    private void DeleteInventory_Click(object sender, RoutedEventArgs e)
    {
        if(InventoryGrid.SelectedItem is not InventoryItem item){MessageBox.Show("Seleziona un ricambio.");return;}
        if(MessageBox.Show($"Eliminare {item.Code} — {item.Name}?","Conferma",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        try{Database.DeleteInventoryItem(item.Id);RefreshInventory();}catch(Exception ex){MessageBox.Show(ex.Message);}
    }

    private void AddBrand_Click(object sender, RoutedEventArgs e)
    {
        Database.AddBrand(NewBrandBox.Text); NewBrandBox.Clear(); ReloadCatalog(); MessageBox.Show("Marca aggiunta.");
    }

    private void AddModel_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CatalogBrandBox.Text) || string.IsNullOrWhiteSpace(NewModelBox.Text)) { MessageBox.Show("Indica marca e modello."); return; }
        Database.AddModel(CatalogBrandBox.Text, NewModelBox.Text); NewModelBox.Clear(); ReloadCatalog(); MessageBox.Show("Modello aggiunto.");
    }

    private void LoadSettings()
    {
        var s = Database.LoadSettings();
        ShopNameBox.Text = s.ShopName; ShopAddressBox.Text = s.Address; ShopPhoneBox.Text = s.Phone; ShopEmailBox.Text = s.Email; ShopVatBox.Text = s.VatNumber;
    }

    private void LoadStorageSettings()
    {
        var options = StorageConfig.Load();
        StorageModeBox.SelectedIndex = options.Mode.Equals("Portable", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        PdfFolderBox.Text = options.CustomPdfFolder;
        UpdateStoragePreview();
    }

    private StorageOptions ReadStorageOptions() => new()
    {
        Mode = StorageModeBox.SelectedItem is ComboBoxItem item && item.Tag?.ToString() == "Portable" ? "Portable" : "PC",
        CustomPdfFolder = PdfFolderBox.Text.Trim()
    };

    private void UpdateStoragePreview()
    {
        if (StorageModeBox is null || StoragePreviewText is null) return;
        var options = ReadStorageOptions();
        StoragePreviewText.Text = $"Archivio: {Path.Combine(StorageConfig.GetDataFolder(options), "repairdesk.db")}\nPDF: {StorageConfig.GetPdfFolder(options)}";
    }

    private void StorageModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateStoragePreview();

    private void ChoosePdfFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Scegli dove salvare i PDF di RepairDesk", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        PdfFolderBox.Text = dialog.FolderName; UpdateStoragePreview();
    }

    private void DefaultPdfFolder_Click(object sender, RoutedEventArgs e) { PdfFolderBox.Clear(); UpdateStoragePreview(); }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        Database.SaveSettings(new ShopSettings { ShopName = ShopNameBox.Text.Trim(), Address = ShopAddressBox.Text.Trim(), Phone = ShopPhoneBox.Text.Trim(), Email = ShopEmailBox.Text.Trim(), VatNumber = ShopVatBox.Text.Trim() });
        try
        {
            Database.SwitchStorage(ReadStorageOptions());
            RefreshArchive(); RefreshCalendar(); UpdateStoragePreview();
            MessageBox.Show("Impostazioni salvate. L'archivio è stato copiato nella posizione scelta.", "Operazione completata", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show($"Non è stato possibile cambiare la posizione dei dati.\n\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
