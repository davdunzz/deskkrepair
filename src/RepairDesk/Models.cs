using System.IO;
namespace RepairDesk;

public sealed class RepairRecord
{
    public int Id { get; set; }
    public string PracticeNumber { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? AppointmentAt { get; set; }
    public string EmployeeCode { get; set; } = "";
    public string Status { get; set; } = "DA FARE";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public string Color { get; set; } = "";
    public string Imei { get; set; } = "";
    public string RepairDescription { get; set; } = "";
    public List<string> RepairTypes { get; set; } = [];
    public List<string> Accessories { get; set; } = [];
    public List<string> DeviceConditions { get; set; } = [];
    public string ConditionNotes { get; set; } = "";
    public List<UsedPart> UsedParts { get; set; } = [];
    public string DisplayName => $"{FirstName} {LastName}".Trim();
    public string Device => $"{Brand} {Model}".Trim();
    public string AppointmentText => AppointmentAt?.ToString("dd/MM/yyyy HH:mm") ?? "—";
    public string CalendarText => AppointmentAt is null ? "" : $"{AppointmentAt:HH:mm}  {DisplayName} — {Device}  •  {Status}  •  Operatore: {EmployeeCode}";
    public string DashboardAppointmentText => AppointmentAt is null ? "" : $"{AppointmentAt:dd/MM/yyyy HH:mm}  •  {DisplayName}  •  {Device}  •  {Status}";
}

public sealed class TechnicalManual
{
    public int Id { get; set; }
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public string Title { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public string FileName => Path.GetFileName(FilePath);
}

public sealed class InventoryItem
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Altro";
    public int Quantity { get; set; }
    public string StockStatus => Quantity <= 0 ? "ESAURITO" : Quantity <= 2 ? "SCORTA BASSA" : "DISPONIBILE";
}

public sealed class UsedPart
{
    public int PartId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string Display => $"{Code} — {Name}  × {Quantity}";
}

public sealed class ShopSettings
{
    public string ShopName { get; set; } = "Centro Assistenza";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string VatNumber { get; set; } = "";
}
